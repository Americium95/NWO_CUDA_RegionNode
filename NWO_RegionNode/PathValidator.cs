using System;
using System.Text;
using System.Security.Cryptography;
using System.Numerics;

namespace NWO_RegionNode.PathValidation
{
    public class PathValidator
    {
        public const int MAX_PATH_POLYGONS = 4096;
        public const int MAX_GEOMETRY_SAMPLES = 64;

        private readonly IAuthoritativeNavMeshProvider _provider;
        public string AuthoritativeVersion => _provider.GetVersion();
        public bool TryGetCentroid(int polygonIndex, out double x, out double y)
        {
            return _provider.TryGetCentroid(polygonIndex, out x, out y);
        }
        public bool ContainsPoint(int polygonIndex, double x, double y)
        {
            return _provider.ContainsPoint(polygonIndex, x, y);
        }

        public PathValidator(IAuthoritativeNavMeshProvider provider)
        {
            _provider = provider ?? new DefaultNavMeshProvider();
        }

        public PathValidationResult Validate(PathPolygonIndexRequest req)
        {
            var res = new PathValidationResult
            {
                PlanSequence = req.RequestId,
                ServerPlanId = GenerateServerPlanId(),
                SampledSegments = 0,
                TotalSegments = 0
            };

            string providerVer = _provider.GetVersion();
            if (req.PathPolygonCount < 2 || req.PathPolygonCount > MAX_PATH_POLYGONS)
            {
                res.Decision = (byte)PathDecision.Rejected;
                res.ReasonCode = (ushort)PathReasonCode.PLAN_INVALID;
                return res;
            }

            if (!_provider.HasPolygon(req.StartPolygonIndex) || !_provider.HasPolygon(req.DestinationPolygonIndex))
            {
                res.Decision = (byte)PathDecision.Rejected;
                res.ReasonCode = (ushort)PathReasonCode.PLAN_INVALID;
                return res;
            }

            int[] path = req.PathPolygonIndices;
            if (path == null || path.Length != req.PathPolygonCount)
            {
                res.Decision = (byte)PathDecision.Rejected;
                res.ReasonCode = (ushort)PathReasonCode.INVALID_REQUEST;
                return res;
            }

            for (int i = 0; i < path.Length; i++)
            {
                if (!_provider.HasPolygon(path[i]) || !_provider.IsOnNavMesh(path[i]))
                {
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)PathReasonCode.PLAN_INVALID;
                    return res;
                }
            }

            // Corridor connectivity is structural, so validate every transition independently
            // of the bounded random geometry sample below.
            for (int i = 0; i + 1 < path.Length; i++)
            {
                bool connected = _provider.CanMoveBetween(path[i], path[i + 1]);
                if (!connected && req.ProtocolVersion == 2)
                    connected = _provider.CanMoveAcrossDateline(path[i], path[i + 1]);
                if (path[i] == path[i + 1] || !connected)
                {
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)PathReasonCode.PATH_DISCONTINUITY;
                    return res;
                }
            }

            if (path[0] != req.StartPolygonIndex || path[path.Length - 1] != req.DestinationPolygonIndex)
            {
                res.Decision = (byte)PathDecision.Rejected;
                res.ReasonCode = (ushort)PathReasonCode.PLAN_INVALID;
                return res;
            }

            if (req.Visits != null && !ValidateVisits(req.Visits, path, res))
            {
                return res;
            }

            if (req.ProtocolVersion == 2)
            {
                Vector2[] points = req.Waypoints;
                if (points == null || points.Length < 2 || points.Length > PathPolygonDecoder.V2_MAX_WAYPOINT_COUNT
                    || !string.Equals(req.NavMeshVersion, providerVer, StringComparison.Ordinal))
                {
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)PathReasonCode.INVALID_REQUEST;
                    return res;
                }
                for (int i = 0; i < points.Length; i++)
                    if (!float.IsFinite(points[i].X) || !float.IsFinite(points[i].Y))
                    {
                        res.Decision = (byte)PathDecision.Rejected;
                        res.ReasonCode = (ushort)PathReasonCode.INVALID_REQUEST;
                        return res;
                    }

                res.TotalSegments = (uint)(points.Length - 1);
                // V2 validates the actual global route. The corridor remains structural
                // metadata: approved LOS shortcuts may leave its polygons.
                if (!_provider.ValidateWaypointRoute(req.StartPolygonIndex, req.DestinationPolygonIndex,
                    points, out PathReasonCode routeReason))
                {
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)(routeReason == PathReasonCode.VALID
                        ? PathReasonCode.OFF_MESH_SAMPLE : routeReason);
                    return res;
                }
                res.SampledSegments = res.TotalSegments;
                res.Decision = (byte)PathDecision.Accepted;
                res.ReasonCode = (ushort)PathReasonCode.VALID;
                return res;
            }

            uint totalSeg = (uint)(path.Length - 1);
            res.TotalSegments = totalSeg;
            if (totalSeg > 0)
            {
                int samples = (int)Math.Min((uint)MAX_GEOMETRY_SAMPLES, totalSeg);
                if (!ProbeSampledSegments(path, samples, out uint sampled, out PathReasonCode geomReason))
                {
                    res.SampledSegments = sampled;
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)geomReason;
                    return res;
                }
                res.SampledSegments = sampled;
            }

            res.Decision = (byte)PathDecision.Accepted;
            res.ReasonCode = (ushort)PathReasonCode.VALID;
            return res;
        }

        private bool ValidateVisits(System.Collections.Generic.List<VisitEntry> visits, int[] path, PathValidationResult res)
        {
            int cursor = 0;
            for (int i = 0; i < visits.Count; i++)
            {
                VisitEntry v = visits[i];
                if (v.Order != i)
                {
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)PathReasonCode.VISIT_ORDER_MISMATCH;
                    return false;
                }

                if (!_provider.HasPolygon(v.PolygonIndex))
                {
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)PathReasonCode.VISIT_UNREACHED;
                    return false;
                }

                bool reached = false;
                while (cursor < path.Length)
                {
                    if (path[cursor] == v.PolygonIndex)
                    {
                        cursor++;
                        reached = true;
                        break;
                    }
                    cursor++;
                }

                if (!reached)
                {
                    res.Decision = (byte)PathDecision.Rejected;
                    res.ReasonCode = (ushort)PathReasonCode.VISIT_ORDER_MISMATCH;
                    return false;
                }
            }
            return true;
        }

        private bool ProbeSampledSegments(int[] path, int sampleCount, out uint sampled, out PathReasonCode reason)
        {
            reason = PathReasonCode.VALID;
            sampled = 0;

            int segmentCount = path.Length - 1;
            var order = new int[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                order[i] = i;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                int j = i + RandomNumberGenerator.GetInt32(segmentCount - i);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                int segment = order[i];
                int from = path[segment];
                int to = path[segment + 1];
                sampled++;
                if (!_provider.CanMoveBetween(from, to))
                {
                    reason = PathReasonCode.PATH_DISCONTINUITY;
                    return false;
                }
                if (!_provider.ValidateGeometry(new int[] { from, to }, out _, out _, out PathReasonCode providerReason))
                {
                    reason = providerReason == PathReasonCode.VALID
                        ? PathReasonCode.FORBIDDEN_REGION
                        : providerReason;
                    return false;
                }
            }

            return true;
        }

        private string GenerateServerPlanId()
        {
            byte[] buf = new byte[12];
            RandomNumberGenerator.Fill(buf);
            StringBuilder sb = new StringBuilder(16);
            for (int i = 0; i < 12; i++) sb.Append(buf[i].ToString("x2"));
            return "sp1_" + sb.ToString();
        }
    }
}
