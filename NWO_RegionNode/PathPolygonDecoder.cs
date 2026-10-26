using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using DotNetty.Buffers;

namespace NWO_RegionNode.PathValidation
{
    public static class PathPolygonDecoder
    {
        public const int V1_MAX_LENGTH = 16408;
        public const int V2_MAX_CORRIDOR_COUNT = 4096;
        public const int V2_MAX_WAYPOINT_COUNT = 4096;
        public const int V2_MAX_LENGTH = 49224;
        public const float V2_NAV_SCALE = 1280f;
        public const float V2_MAP_SIZE = 2048f;
        public const uint V2_COORDINATE_FLAGS = 1;

        public static bool TryDecode(IByteBuffer buffer, int startIndex, out PathPolygonIndexRequest req, out PathReasonCode error)
        {
            req = new PathPolygonIndexRequest();
            error = PathReasonCode.INVALID_REQUEST;
            try
            {
                if (buffer == null || startIndex < buffer.ReaderIndex
                    || buffer.ReadableBytes < startIndex - buffer.ReaderIndex + 12) return false;

                int p = startIndex;
                req.Opcode = unchecked((byte)buffer.GetByte(p++));
                req.Subtype = unchecked((byte)buffer.GetByte(p++));
                req.ProtocolVersion = buffer.GetUnsignedShortLE(p); p += 2;
                req.TotalPacketLength = buffer.GetUnsignedIntLE(p); p += 4;
                req.RequestId = buffer.GetUnsignedIntLE(p); p += 4;
                if (req.Opcode != 6 || req.Subtype != 0) return false;

                if (req.ProtocolVersion == 1)
                    return TryDecodeV1(buffer, startIndex, p, req, out error);
                if (req.ProtocolVersion == 2)
                    return TryDecodeV2(buffer, startIndex, p, req, out error);
                return false;
            }
            catch
            {
                error = PathReasonCode.INVALID_REQUEST;
                return false;
            }
        }

        private static bool TryDecodeV1(IByteBuffer buffer, int startIndex, int p, PathPolygonIndexRequest req, out PathReasonCode error)
        {
            error = PathReasonCode.INVALID_REQUEST;
            if (req.TotalPacketLength < 32 || req.TotalPacketLength > V1_MAX_LENGTH
                || (ulong)startIndex + req.TotalPacketLength != (ulong)buffer.WriterIndex) return false;
            int end = checked(startIndex + (int)req.TotalPacketLength);
            if (p + 12 > end) return false;
            req.UserId = buffer.GetUnsignedIntLE(p); p += 4;
            req.ObjectId = buffer.GetUnsignedIntLE(p); p += 4;
            req.PathPolygonCount = buffer.GetUnsignedIntLE(p); p += 4;
            if (req.PathPolygonCount < 2 || req.PathPolygonCount > PathValidator.MAX_PATH_POLYGONS
                || (ulong)p + (ulong)req.PathPolygonCount * 4 != (ulong)end) return false;
            req.PathPolygonIndices = new int[(int)req.PathPolygonCount];
            for (int i = 0; i < req.PathPolygonIndices.Length; i++, p += 4)
                req.PathPolygonIndices[i] = buffer.GetIntLE(p);
            req.StartPolygonIndex = req.PathPolygonIndices[0];
            req.DestinationPolygonIndex = req.PathPolygonIndices[^1];
            req.PlanSequence = req.RequestId;
            req.NavMeshVersion = null;
            error = PathReasonCode.VALID;
            return true;
        }

        private static bool TryDecodeV2(IByteBuffer buffer, int startIndex, int p, PathPolygonIndexRequest req, out PathReasonCode error)
        {
            error = PathReasonCode.INVALID_REQUEST;
            if (req.TotalPacketLength < 72 + 2 * 4 + 2 * 8 || req.TotalPacketLength > V2_MAX_LENGTH
                || (ulong)startIndex + req.TotalPacketLength != (ulong)buffer.WriterIndex) return false;
            int end = checked(startIndex + (int)req.TotalPacketLength);
            if (p + 60 > end) return false;

            req.UserId = buffer.GetUnsignedIntLE(p); p += 4;
            req.ObjectId = buffer.GetUnsignedIntLE(p); p += 4;
            req.GeometryHash = new byte[32]; buffer.GetBytes(p, req.GeometryHash, 0, 32); p += 32;
            req.NavMeshVersion = Convert.ToHexString(req.GeometryHash).ToLowerInvariant();
            if (!string.Equals(req.NavMeshVersion, AuthoritativeNavMeshProvider.TrustedVersion, StringComparison.Ordinal)) return false;
            req.NavScale = BitConverter.Int32BitsToSingle(buffer.GetIntLE(p)); p += 4;
            req.MapSize = BitConverter.Int32BitsToSingle(buffer.GetIntLE(p)); p += 4;
            req.CoordinateFlags = buffer.GetUnsignedIntLE(p); p += 4;
            if (!float.IsFinite(req.NavScale) || req.NavScale != V2_NAV_SCALE
                || !float.IsFinite(req.MapSize) || req.MapSize != V2_MAP_SIZE
                || req.CoordinateFlags != V2_COORDINATE_FLAGS) return false;

            req.PathPolygonCount = buffer.GetUnsignedIntLE(p); p += 4;
            if (req.PathPolygonCount < 2 || req.PathPolygonCount > V2_MAX_CORRIDOR_COUNT
                || (ulong)p + (ulong)req.PathPolygonCount * 4 + 4 > (ulong)end) return false;
            req.PathPolygonIndices = new int[(int)req.PathPolygonCount];
            for (int i = 0; i < req.PathPolygonIndices.Length; i++, p += 4)
                req.PathPolygonIndices[i] = buffer.GetIntLE(p);
            req.StartPolygonIndex = req.PathPolygonIndices[0];
            req.DestinationPolygonIndex = req.PathPolygonIndices[^1];

            uint waypointCount = buffer.GetUnsignedIntLE(p); p += 4;
            if (waypointCount < 2 || waypointCount > V2_MAX_WAYPOINT_COUNT
                || (ulong)p + (ulong)waypointCount * 8 != (ulong)end) return false;
            req.Waypoints = new Vector2[(int)waypointCount];
            for (int i = 0; i < req.Waypoints.Length; i++, p += 8)
            {
                float x = BitConverter.Int32BitsToSingle(buffer.GetIntLE(p));
                float y = BitConverter.Int32BitsToSingle(buffer.GetIntLE(p + 4));
                if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
                req.Waypoints[i] = new Vector2(x, y);
            }

            // The client serializes X on the shortest periodic copy. Reject ambiguous
            // multi-period jumps rather than silently choosing another interpretation.
            for (int i = 1; i < req.Waypoints.Length; i++)
            {
                float dx = MathF.Abs(req.Waypoints[i].X - req.Waypoints[i - 1].X);
                float dy = MathF.Abs(req.Waypoints[i].Y - req.Waypoints[i - 1].Y);
                if (dx > req.MapSize * 0.5f || dy > req.MapSize) return false;
            }

            req.PlanSequence = req.RequestId;
            using (var sha = SHA256.Create())
            {
                byte[] bytes = new byte[checked((int)req.TotalPacketLength - 20)];
                buffer.GetBytes(startIndex + 20, bytes, 0, bytes.Length);
                req.GeometryHash = sha.ComputeHash(bytes);
            }
            error = PathReasonCode.VALID;
            return true;
        }

        public static byte[] EncodeResponse(PathValidationResult res, uint requestId, string authoritativeVersion = null,
            ushort protocolVersion = 1, byte[] geometryHash = null)
        {
            byte[] version = Encoding.UTF8.GetBytes(authoritativeVersion ?? string.Empty);
            byte[] serverPlanIdBytes = res.ServerPlanId == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(res.ServerPlanId);
            bool v2 = protocolVersion == 2;
            if (protocolVersion != 1 && !v2) throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            if (version.Length > ushort.MaxValue || serverPlanIdBytes.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(authoritativeVersion), "Response string exceeds uint16 length prefix.");
            byte[] digest = v2 && res.Decision == (byte)PathDecision.Accepted && geometryHash?.Length == 32
                ? (byte[])geometryHash.Clone() : new byte[32];
            int total = 12 + 8 + 1 + 2 + 2 + version.Length + 4 + 4 + 2 + serverPlanIdBytes.Length + (v2 ? 32 : 0);
            byte[] pkt = new byte[total]; int p = 0;
            pkt[p++] = 6; pkt[p++] = 1; WriteUInt16LE(pkt, p, protocolVersion); p += 2;
            WriteUInt32LE(pkt, p, (uint)total); p += 4; WriteUInt32LE(pkt, p, requestId); p += 4;
            WriteUInt64LE(pkt, p, requestId); p += 8; pkt[p++] = res.Decision;
            WriteUInt16LE(pkt, p, res.ReasonCode); p += 2; WriteUInt16LE(pkt, p, (ushort)version.Length); p += 2;
            Array.Copy(version, 0, pkt, p, version.Length); p += version.Length;
            WriteUInt32LE(pkt, p, res.SampledSegments); p += 4; WriteUInt32LE(pkt, p, res.TotalSegments);
            p += 4; WriteUInt16LE(pkt, p, (ushort)serverPlanIdBytes.Length); p += 2;
            Array.Copy(serverPlanIdBytes, 0, pkt, p, serverPlanIdBytes.Length); p += serverPlanIdBytes.Length;
            if (v2) Array.Copy(digest, 0, pkt, p, digest.Length);
            return pkt;
        }

        private static void WriteUInt16LE(byte[] b, int i, ushort v) { b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); }
        private static void WriteUInt32LE(byte[] b, int i, uint v) { for (int j = 0; j < 4; j++) b[i + j] = (byte)(v >> (8 * j)); }
        private static void WriteUInt64LE(byte[] b, int i, ulong v) { for (int j = 0; j < 8; j++) b[i + j] = (byte)(v >> (8 * j)); }
    }
}
