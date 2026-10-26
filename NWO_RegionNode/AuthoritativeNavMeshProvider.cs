using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NWO_RegionNode.PathValidation
{
    /// <summary>Immutable, hash-pinned view of the server's packaged polygon mesh.</summary>
    public sealed class AuthoritativeNavMeshProvider : IAuthoritativeNavMeshProvider
    {
        public const string TrustedSha256 = "6a34a46619003a0f166aca904a350e1900050b339b2f592c7b18a7e423373cec";
        public const string TrustedVersion = TrustedSha256;
        public const int ExpectedPolygonCount = 352668;
        public const int ExpectedPortalCount = 404031;
        private readonly int[][] _polygons;
        private readonly (double X, double Y)[] _vertices;
        private readonly Dictionary<long, (int A, int B)> _portals;
        private readonly HashSet<long> _datelinePairs;
        private readonly RouteGeometryValidator _routeGeometry;

        private AuthoritativeNavMeshProvider(int[][] polygons, (double X, double Y)[] vertices,
            Dictionary<long, (int A, int B)> portals, string trustedLandMaskPath)
        {
            _polygons = polygons;
            _vertices = vertices;
            _portals = portals;
            _datelinePairs = BuildDatelinePairs(polygons, vertices);
            _routeGeometry = new RouteGeometryValidator(polygons, vertices, trustedLandMaskPath);
        }

        public static AuthoritativeNavMeshProvider Load(string path)
        {
            string meshPath = Path.GetFullPath(path);
            string maskPath = Path.Combine(Path.GetDirectoryName(meshPath), "server-landmask.bytes");
            byte[] bytes = File.ReadAllBytes(path);
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(hash, TrustedSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Navmesh SHA-256 does not match the trusted asset.");
            }

            using JsonDocument doc = JsonDocument.Parse(bytes);
            JsonElement root = doc.RootElement;
            if (GetString(root, "format") != "navmesh-merged-convex")
                throw new InvalidDataException("Unexpected navmesh format.");
            JsonElement stats = root.GetProperty("stats");
            if (stats.GetProperty("polygons").GetInt32() != ExpectedPolygonCount
                || stats.GetProperty("portals").GetInt32() != ExpectedPortalCount)
                throw new InvalidDataException("Navmesh counts do not match the trusted asset.");

            JsonElement sourcePolygons = root.GetProperty("polygons");
            JsonElement sourcePortals = root.GetProperty("portals");
            JsonElement sourceVertices = root.GetProperty("vertices");
            if (sourcePolygons.GetArrayLength() != ExpectedPolygonCount
                || sourcePortals.GetArrayLength() != ExpectedPortalCount)
                throw new InvalidDataException("Navmesh arrays do not match trusted counts.");

            var polygons = new int[ExpectedPolygonCount][];
            int pi = 0;
            foreach (JsonElement entry in sourcePolygons.EnumerateArray())
            {
                JsonElement arr = Unwrap(entry);
                if (arr.GetArrayLength() < 4) throw new InvalidDataException("Malformed polygon vertex loop.");
                var vertices = new int[arr.GetArrayLength() - 1];
                int n = 0;
                foreach (JsonElement value in arr.EnumerateArray())
                {
                    int id = value.GetInt32();
                    if (n++ == 0) continue; // tile index
                    if ((uint)id >= (uint)sourceVertices.GetArrayLength())
                        throw new InvalidDataException("Polygon has an invalid vertex reference.");
                    vertices[n - 2] = id;
                }
                polygons[pi++] = vertices;
            }

            var vertexCoords = new (double X, double Y)[sourceVertices.GetArrayLength()];
            int vi = 0;
            foreach (JsonElement v in sourceVertices.EnumerateArray())
            {
                if (v.GetArrayLength() != 2) throw new InvalidDataException("Malformed vertex coordinate.");
                vertexCoords[vi++] = (v[0].GetDouble(), v[1].GetDouble());
            }
            var portals = new Dictionary<long, (int A, int B)>();
            foreach (JsonElement entry in sourcePortals.EnumerateArray())
            {
                JsonElement arr = Unwrap(entry);
                if (arr.GetArrayLength() != 4) throw new InvalidDataException("Malformed portal record.");
                int a = arr[0].GetInt32(), b = arr[1].GetInt32();
                int va = arr[2].GetInt32(), vb = arr[3].GetInt32();
                if ((uint)a >= ExpectedPolygonCount || (uint)b >= ExpectedPolygonCount || a == b
                    || (uint)va >= (uint)vertexCoords.Length
                    || (uint)vb >= (uint)vertexCoords.Length || va == vb
                    || !HasBoundaryVertex(polygons[a], va) || !HasBoundaryVertex(polygons[a], vb)
                    || !HasBoundaryVertex(polygons[b], va) || !HasBoundaryVertex(polygons[b], vb))
                    throw new InvalidDataException("Portal references invalid geometry.");
                portals[Pair(a, b)] = (va, vb);
            }
            return new AuthoritativeNavMeshProvider(polygons, vertexCoords, portals,
                File.Exists(maskPath) ? maskPath : null);
        }

        public string GetVersion() => TrustedVersion;
        public bool ValidateWaypointRoute(int startPolygon, int goalPolygon, System.Numerics.Vector2[] points, out PathReasonCode reason)
            => _routeGeometry.ValidateWaypointRoute(startPolygon, goalPolygon, points, out reason);
        public bool HasPolygon(int polygonIndex) => (uint)polygonIndex < (uint)_polygons.Length;
        public bool TryGetCentroid(int polygonIndex, out double x, out double y)
        {
            x = y = 0;
            if (!HasPolygon(polygonIndex)) return false;
            int[] loop = _polygons[polygonIndex];
            for (int i = 0; i < loop.Length; i++) { x += _vertices[loop[i]].X; y += _vertices[loop[i]].Y; }
            x /= loop.Length; y /= loop.Length;
            return true;
        }
        public bool IsOnNavMesh(int polygonIndex) => HasPolygon(polygonIndex) && _polygons[polygonIndex].Length >= 3;
        public bool ContainsPoint(int polygonIndex, double x, double y)
        {
            if (!IsOnNavMesh(polygonIndex) || double.IsNaN(x) || double.IsNaN(y)
                || double.IsInfinity(x) || double.IsInfinity(y)) return false;
            int[] loop = _polygons[polygonIndex];
            bool inside = false;
            for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
            {
                var a = _vertices[loop[j]];
                var b = _vertices[loop[i]];
                double cross = (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);
                double dot = (x - a.X) * (x - b.X) + (y - a.Y) * (y - b.Y);
                if (Math.Abs(cross) <= 1e-8 && dot <= 1e-8) return true;
                if ((a.Y > y) != (b.Y > y)
                    && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }
        public bool CanMoveBetween(int fromPoly, int toPoly) => HasPolygon(fromPoly) && HasPolygon(toPoly)
            && fromPoly != toPoly && _portals.ContainsKey(Pair(fromPoly, toPoly));
        public bool CanMoveAcrossDateline(int fromPoly, int toPoly) => HasPolygon(fromPoly) && HasPolygon(toPoly)
            && fromPoly != toPoly && _datelinePairs.Contains(Pair(fromPoly, toPoly));

        private static HashSet<long> BuildDatelinePairs(int[][] polygons, (double X, double Y)[] vertices)
        {
            var left = new List<(int Polygon, double MinY, double MaxY)>();
            var right = new List<(int Polygon, double MinY, double MaxY)>();
            for (int p = 0; p < polygons.Length; p++)
            {
                int[] loop = polygons[p];
                for (int i = 0; i < loop.Length; i++)
                {
                    var a = vertices[loop[i]];
                    var b = vertices[loop[(i + 1) % loop.Length]];
                    if (a.X <= 0.5 && b.X <= 0.5)
                        left.Add((p, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                    else if (a.X >= 2047.5 && b.X >= 2047.5)
                        right.Add((p, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                }
            }
            var pairs = new HashSet<long>();
            foreach (var l in left)
                foreach (var r in right)
                    if (l.Polygon != r.Polygon && Math.Min(l.MaxY, r.MaxY) > Math.Max(l.MinY, r.MinY) + 1.0)
                        pairs.Add(Pair(l.Polygon, r.Polygon));
            return pairs;
        }

        // For an adjacent polygon transition, sample the midpoint of its shared portal gate and
        // confirm it remains within the convex polygon union. The request carries polygon IDs only.
        public bool ValidateGeometry(int[] pathPolys, out uint sampledSegments, out uint totalSegments, out PathReasonCode reason)
        {
            totalSegments = pathPolys == null || pathPolys.Length < 2 ? 0u : (uint)(pathPolys.Length - 1);
            sampledSegments = 0;
            reason = PathReasonCode.VALID;
            if (pathPolys == null || pathPolys.Length < 2) { reason = PathReasonCode.PLAN_INVALID; return false; }
            for (int i = 0; i + 1 < pathPolys.Length; i++)
            {
                int a = pathPolys[i], b = pathPolys[i + 1];
                if (!CanMoveBetween(a, b)) { reason = PathReasonCode.PATH_DISCONTINUITY; return false; }
                if (!TryGetGate(a, b, out var gate) || !IsInside(a, gate) && !IsInside(b, gate))
                { reason = PathReasonCode.OFF_MESH_SAMPLE; return false; }
                sampledSegments++;
            }
            return true;
        }

        private static long Pair(int a, int b) => ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
        private bool TryGetGate(int a, int b, out (double X, double Y) gate)
        {
            gate = default;
            // The transition gate is the midpoint of the shared edge. Store edge endpoints by
            // deriving their common vertices from the two convex loops.
            int first = -1, second = -1;
            if (_portals.TryGetValue(Pair(a, b), out var portal)) { first = portal.A; second = portal.B; }
            if (first < 0 || second < 0) return false;
            gate = ((_vertices[first].X + _vertices[second].X) * 0.5, (_vertices[first].Y + _vertices[second].Y) * 0.5);
            return true;
        }
        private bool IsInside(int poly, (double X, double Y) p)
        {
            // Convexity is part of the pinned mesh format; sign changes indicate outside.
            double sign = 0;
            int[] loop = _polygons[poly];
            for (int i = 0; i < loop.Length; i++)
            {
                var a = _vertices[loop[i]]; var b = _vertices[loop[(i + 1) % loop.Length]];
                double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (Math.Abs(cross) < 1e-8) continue;
                if (sign == 0) sign = Math.Sign(cross); else if (Math.Sign(cross) != sign) return false;
            }
            return true;
        }
        private static bool HasBoundaryVertex(int[] loop, int vertex)
        {
            for (int i = 0; i < loop.Length; i++) if (loop[i] == vertex) return true;
            return false;
        }
        private static JsonElement Unwrap(JsonElement e) => e.ValueKind == JsonValueKind.Object ? e.GetProperty("value") : e;
        private static string GetString(JsonElement root, string name) => root.GetProperty(name).GetString() ?? string.Empty;
    }
}
