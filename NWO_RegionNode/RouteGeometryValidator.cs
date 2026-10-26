using System;
using System.Collections.Generic;
using System.Numerics;
using NWO_RegionNode.PathValidation;

namespace NWO_RegionNode.PathValidation
{
    /// <summary>Proves waypoint edges against the complete, periodic authoritative polygon union.</summary>
    internal sealed class RouteGeometryValidator
    {
        private const float MapSize = 2048f;
        private const int BandCount = 256;
        private const int MaxBandsPerPolygon = 16;
        private const int MaxWaypoints = 4096;
        private const float MaxSegmentXSpan = MapSize * 0.5f;
        private const float MaxSegmentYSpan = MapSize;
        private const float MinWaypointX = -MapSize;
        private const float MaxWaypointX = MapSize * 2f;

        private readonly Polygon[] _polygons;
        private readonly List<int>[] _bands;
        private readonly List<int> _widePolygons;
        private readonly float _minY;
        private readonly float _bandHeight;
        private readonly AuthoritativeLandMask _landMask;

        internal RouteGeometryValidator(int[][] loops, (double X, double Y)[] vertices, string trustedLandMaskPath = null)
        {
            if (loops == null) throw new ArgumentNullException(nameof(loops));
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            _polygons = new Polygon[loops.Length];
            float globalMinY = float.PositiveInfinity, globalMaxY = float.NegativeInfinity;
            for (int id = 0; id < loops.Length; id++)
            {
                int[] loop = loops[id];
                if (loop == null) throw new ArgumentException("A validated polygon loop is null.", nameof(loops));
                var points = new Vector2[loop.Length];
                float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
                float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
                for (int i = 0; i < loop.Length; i++)
                {
                    int vertex = loop[i];
                    if ((uint)vertex >= (uint)vertices.Length) throw new ArgumentException("A polygon references an invalid vertex.", nameof(loops));
                    // Unity Vector2 stores each coordinate as IEEE-754 float.
                    points[i] = new Vector2((float)vertices[vertex].X, (float)vertices[vertex].Y);
                    minX = MathF.Min(minX, points[i].X); maxX = MathF.Max(maxX, points[i].X);
                    minY = MathF.Min(minY, points[i].Y); maxY = MathF.Max(maxY, points[i].Y);
                }
                // Unity Rect(min, max-min) exposes xMax/yMax as xMin + width/height.
                float width = maxX - minX, height = maxY - minY;
                var bounds = new Bounds(minX, minY, minX + width, minY + height);
                _polygons[id] = new Polygon(points, bounds);
                globalMinY = MathF.Min(globalMinY, bounds.MinY); globalMaxY = MathF.Max(globalMaxY, bounds.MaxY);
            }

            _minY = float.IsPositiveInfinity(globalMinY) ? 0f : globalMinY;
            float range = float.IsPositiveInfinity(globalMinY) ? 1f : globalMaxY - globalMinY;
            _bandHeight = range > 0f ? range / BandCount : 1f;
            _bands = new List<int>[BandCount];
            for (int i = 0; i < BandCount; i++) _bands[i] = new List<int>();
            _widePolygons = new List<int>();
            for (int id = 0; id < _polygons.Length; id++)
            {
                Bounds b = _polygons[id].Bounds;
                int first = BandFor(b.MinY), last = BandFor(b.MaxY);
                if (last - first + 1 > MaxBandsPerPolygon) _widePolygons.Add(id);
                else for (int band = first; band <= last; band++) _bands[band].Add(id);
            }
            _landMask = AuthoritativeLandMask.TryLoad(trustedLandMaskPath);
        }

        internal bool ValidateWaypointRoute(int startPolygon, int goalPolygon, Vector2[] points, out PathReasonCode reason)
        {
            reason = PathReasonCode.OFF_MESH_SAMPLE;
            if (points == null || points.Length < 2 || points.Length > MaxWaypoints ||
                (uint)startPolygon >= (uint)_polygons.Length || (uint)goalPolygon >= (uint)_polygons.Length ||
                _polygons[startPolygon].Points.Length < 3 || _polygons[goalPolygon].Points.Length < 3 ||
                _landMask == null)
                return false;

            for (int i = 0; i < points.Length; i++)
                if (!Finite(points[i].X) || !Finite(points[i].Y) || points[i].X < MinWaypointX ||
                    points[i].X > MaxWaypointX || points[i].Y < 0f || points[i].Y > MapSize) return false;
            if (points[0].X < 0f || points[0].X >= MapSize) return false;
            if (!ContainsPeriodic(startPolygon, points[0]) || !ContainsPeriodic(goalPolygon, points[points.Length - 1])) return false;

            for (int i = 0; i + 1 < points.Length; i++)
            {
                Vector2 a = points[i], b = points[i + 1], d = b - a;
                if (MathF.Abs(d.X) > MaxSegmentXSpan || MathF.Abs(d.Y) > MaxSegmentYSpan) return false;
                if (!SegmentCovered(a, b, d)) return false;
                if (_landMask.SegmentCrossesLand(a, b))
                {
                    reason = PathReasonCode.FORBIDDEN_REGION;
                    return false;
                }
            }
            reason = PathReasonCode.VALID;
            return true;
        }

        private bool SegmentCovered(Vector2 a, Vector2 b, Vector2 d)
        {
            if (!ContainsUnion(a) || !ContainsUnion(b)) return false;
            if (d.X == 0f && d.Y == 0f) return true;
            float minX = MathF.Min(a.X, b.X), maxX = MathF.Max(a.X, b.X);
            float minY = MathF.Min(a.Y, b.Y), maxY = MathF.Max(a.Y, b.Y);
            // The native client tests edge intersections against the segment's a + d endpoint,
            // while its polygon-copy candidate range remains based on the supplied a/b AABB.
            float lineEndX = a.X + d.X, lineEndY = a.Y + d.Y;
            float edgeMinX = MathF.Min(a.X, lineEndX), edgeMaxX = MathF.Max(a.X, lineEndX);
            float edgeMinY = MathF.Min(a.Y, lineEndY), edgeMaxY = MathF.Max(a.Y, lineEndY);
            int firstBand = BandFor(minY), lastBand = BandFor(maxY);
            var candidateIds = new List<int>();
            var seen = new HashSet<int>();
            for (int band = firstBand; band <= lastBand; band++)
                AddBandCandidates(_bands[band], minY, maxY, minX, maxX, candidateIds, seen);
            AddBandCandidates(_widePolygons, minY, maxY, minX, maxX, candidateIds, seen);
            if (candidateIds.Count == 0) return false;

            var spans = new List<Interval>();
            for (int c = 0; c < candidateIds.Count; c++)
            {
                Polygon polygon = _polygons[candidateIds[c]];
                Bounds box = polygon.Bounds;
                float segMinX = MathF.Min(a.X, b.X), segMaxX = MathF.Max(a.X, b.X);
                int firstCopy = CeilToInt((segMinX - box.MaxX) / MapSize);
                int lastCopy = FloorToInt((segMaxX - box.MinX) / MapSize);
                for (int copy = firstCopy; copy <= lastCopy; copy++)
                {
                    float offset = copy * MapSize;
                    var breaks = new List<float>(polygon.Points.Length + 2) { 0f, 1f };
                    for (int i = 0; i < polygon.Points.Length; i++)
                    {
                        Vector2 p0 = new Vector2(polygon.Points[i].X + offset, polygon.Points[i].Y);
                        Vector2 p1 = new Vector2(polygon.Points[(i + 1) % polygon.Points.Length].X + offset,
                            polygon.Points[(i + 1) % polygon.Points.Length].Y);
                        if (MathF.Max(p0.X, p1.X) < edgeMinX || MathF.Min(p0.X, p1.X) > edgeMaxX ||
                            MathF.Max(p0.Y, p1.Y) < edgeMinY || MathF.Min(p0.Y, p1.Y) > edgeMaxY) continue;
                        Vector2 edge = p1 - p0;
                        float den = Cross(d, edge);
                        if (den == 0f)
                        {
                            AddBreak(breaks, Project(a, d, p0));
                            AddBreak(breaks, Project(a, d, p1));
                        }
                        else AddBreak(breaks, Cross(p0 - a, edge) / den);
                    }
                    breaks.Sort();
                    for (int k = 0; k + 1 < breaks.Count; k++)
                    {
                        float t0 = breaks[k], t1 = breaks[k + 1];
                        if (t1 <= t0) continue;
                        float t = (t0 + t1) * 0.5f;
                        Vector2 midpoint = a + d * t;
                        if (Inside(polygon, offset, midpoint.X, midpoint.Y)) spans.Add(new Interval(t0, t1));
                    }
                }
            }
            if (spans.Count == 0) return false;
            spans.Sort((x, y) => x.Min.CompareTo(y.Min));
            float reach = 0f;
            for (int i = 0; i < spans.Count; i++)
            {
                Interval span = spans[i];
                if (span.Max <= reach) continue;
                if (span.Min > reach) return false;
                reach = span.Max;
                if (reach >= 1f) return true;
            }
            return reach >= 1f;
        }

        private void AddBandCandidates(List<int> source, float minY, float maxY, float minX, float maxX,
            List<int> result, HashSet<int> seen)
        {
            for (int i = 0; i < source.Count; i++)
            {
                int id = source[i];
                if (seen.Contains(id)) continue;
                Bounds b = _polygons[id].Bounds;
                if (b.MaxY < minY || b.MinY > maxY) continue;
                int firstCopy = CeilToInt((minX - b.MaxX) / MapSize);
                int lastCopy = FloorToInt((maxX - b.MinX) / MapSize);
                if (firstCopy > lastCopy) continue;
                if (seen.Add(id)) result.Add(id);
            }
        }

        private bool ContainsPeriodic(int polygonId, Vector2 point)
        {
            Polygon polygon = _polygons[polygonId];
            Bounds box = polygon.Bounds;
            float center = (box.MinX + box.MaxX) * 0.5f;
            int nearest = RoundToInt((point.X - center) / MapSize);
            for (int copy = nearest - 1; copy <= nearest + 1; copy++)
                if (Inside(polygon, copy * MapSize, point.X, point.Y)) return true;
            return false;
        }

        private bool ContainsUnion(Vector2 point)
        {
            int band = BandFor(point.Y);
            for (int phase = 0; phase < 2; phase++)
            {
                List<int> ids = phase == 0 ? _bands[band] : _widePolygons;
                for (int i = 0; i < ids.Count; i++)
                    if (ContainsPeriodic(ids[i], point)) return true;
            }
            return false;
        }

        private bool Inside(Polygon polygon, float offset, float x, float y)
        {
            Bounds bounds = polygon.Bounds;
            if (x < bounds.MinX + offset || x > bounds.MaxX + offset || y < bounds.MinY || y > bounds.MaxY) return false;
            bool inside = false;
            Vector2 p = new Vector2(x, y);
            for (int i = 0, j = polygon.Points.Length - 1; i < polygon.Points.Length; j = i++)
            {
                Vector2 a = new Vector2(polygon.Points[j].X + offset, polygon.Points[j].Y);
                Vector2 b = new Vector2(polygon.Points[i].X + offset, polygon.Points[i].Y);
                Vector2 e = b - a;
                float len2 = (float)((double)e.X * e.X + (double)e.Y * e.Y);
                if (len2 > 0f)
                {
                    double u = (((double)p.X - a.X) * e.X + ((double)p.Y - a.Y) * e.Y) / len2;
                    if (u >= 0d && u <= 1d && Cross(e, p - a) == 0f) return true;
                }
                if ((a.Y > p.Y) != (b.Y > p.Y) &&
                    (double)a.X + ((double)p.Y - a.Y) / ((double)b.Y - a.Y) * e.X > p.X) inside = !inside;
            }
            return inside;
        }

        private int BandFor(float y)
        {
            int band = FloorToInt((y - _minY) / _bandHeight);
            return band < 0 ? 0 : band >= BandCount ? BandCount - 1 : band;
        }

        private static void AddBreak(List<float> values, float value)
        { if (value > 0f && value < 1f) values.Add(value); }
        private static float Cross(Vector2 a, Vector2 b) => (float)((double)a.X * b.Y - (double)a.Y * b.X);
        private static float Project(Vector2 a, Vector2 d, Vector2 p)
        {
            float len2 = (float)((double)d.X * d.X + (double)d.Y * d.Y);
            if (len2 <= 0f) return 0f;
            return (float)((((double)p.X - a.X) * d.X + ((double)p.Y - a.Y) * d.Y) / len2);
        }
        private static int FloorToInt(float value) => (int)MathF.Floor(value);
        private static int CeilToInt(float value) => (int)MathF.Ceiling(value);
        private static int RoundToInt(float value) => (int)MathF.Round(value, MidpointRounding.ToEven);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class Polygon
        {
            internal readonly Vector2[] Points;
            internal readonly Bounds Bounds;
            internal Polygon(Vector2[] points, Bounds bounds) { Points = points; Bounds = bounds; }
        }
        private readonly struct Bounds
        {
            internal readonly float MinX, MinY, MaxX, MaxY;
            internal Bounds(float minX, float minY, float maxX, float maxY)
            { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        }
        private readonly struct Interval
        {
            internal readonly float Min, Max;
            internal Interval(float min, float max) { Min = min; Max = max; }
        }
    }
}
