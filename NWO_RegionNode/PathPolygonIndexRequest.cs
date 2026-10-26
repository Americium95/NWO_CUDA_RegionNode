using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using DotNetty.Buffers;

namespace NWO_RegionNode.PathValidation
{
    public class PathPolygonIndexRequest
    {
        public byte Opcode;
        public byte Subtype;
        public ushort ProtocolVersion;
        public uint TotalPacketLength;
        public uint RequestId;
        public uint UserId;
        public uint ObjectId;
        public float NavScale;
        public float MapSize;
        public uint CoordinateFlags;
        public byte[] GeometryHash;
        public Vector2[] Waypoints;
        public ulong PlanSequence;
        public ushort NavMeshVersionByteLength;
        public string NavMeshVersion;
        public int StartPolygonIndex;
        public int DestinationPolygonIndex;
        public ushort VisitCount;
        public List<VisitEntry> Visits;
        public uint PathPolygonCount;
        public int[] PathPolygonIndices;
        public ulong ClientComputedAtUtcMs;
    }
    public class VisitEntry
    {
        public ushort VisitIdByteLength;
        public string VisitId;
        public int PolygonIndex;
        public ushort Order;
    }
    public enum PathDecision
    {
        Accepted = 0,
        Rejected = 1,
        Limited = 2,
        ResyncRequired = 3
    }
    public enum PathReasonCode
    {
        VALID = 0,
        INVALID_REQUEST = 1,
        RATE_LIMITED = 2,
        STALE_SEQUENCE = 3,
        START_MISMATCH = 4,
        RESYNC_REQUIRED = 5,
        PLAN_INVALID = 6,
        PATH_DISCONTINUITY = 7,
        OFF_MESH_SAMPLE = 8,
        FORBIDDEN_REGION = 9,
        VISIT_UNREACHED = 10,
        VISIT_ORDER_MISMATCH = 11
    }
    public class PathValidationResult
    {
        public byte Decision;
        public ushort ReasonCode;
        public ulong PlanSequence;
        public string ServerPlanId;
        public uint SampledSegments;
        public uint TotalSegments;
    }
    public interface IAuthoritativeNavMeshProvider
    {
        string GetVersion();
        bool TryGetCentroid(int polygonIndex, out double x, out double y) { x = y = 0; return false; }
        bool HasPolygon(int polygonIndex);
        bool ContainsPoint(int polygonIndex, double x, double y)
        {
            return TryGetCentroid(polygonIndex, out double cx, out double cy)
                && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= 4.0;
        }
        bool CanMoveBetween(int fromPoly, int toPoly);
        bool CanMoveAcrossDateline(int fromPoly, int toPoly) => false;
        bool IsOnNavMesh(int polygonIndex);
        bool ValidateGeometry(int[] pathPolys, out uint sampledSegments, out uint totalSegments, out PathReasonCode reason);
        bool ValidateWaypointRoute(int startPolygon, int goalPolygon, Vector2[] points, out PathReasonCode reason)
        {
            reason = PathReasonCode.OFF_MESH_SAMPLE;
            return false;
        }
    }
    public class DefaultNavMeshProvider : IAuthoritativeNavMeshProvider
    {
        public const string STUB_VERSION = "stub-0";

        private readonly string _version;

        public DefaultNavMeshProvider() : this(STUB_VERSION)
        {
        }

        public DefaultNavMeshProvider(string version)
        {
            _version = string.IsNullOrEmpty(version) ? STUB_VERSION : version;
        }

        public string GetVersion() => _version;
        public bool HasPolygon(int polygonIndex) => false;
        public bool CanMoveBetween(int fromPoly, int toPoly) => false;
        public bool IsOnNavMesh(int polygonIndex) => false;
        public bool ValidateGeometry(int[] pathPolys, out uint sampledSegments, out uint totalSegments, out PathReasonCode reason)
        {
            sampledSegments = 0;
            totalSegments = pathPolys != null ? Math.Max(0, (uint)pathPolys.Length - 1) : 0;
            reason = PathReasonCode.FORBIDDEN_REGION;
            return false;
        }
    }
}
