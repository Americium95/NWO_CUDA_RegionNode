using System;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using NWO_RegionNode.PathValidation;

namespace NWO_RegionNode
{
    public class PathPacketHandler
    {
        private readonly PathValidator _validator;

        public PathPacketHandler() : this(null)
        {
        }

        public PathPacketHandler(IAuthoritativeNavMeshProvider provider)
        {
            _validator = new PathValidator(provider);
        }

        public void HandlePacket(IChannelHandlerContext context, IByteBuffer buffer,
            Func<uint, uint, int[], bool> routeAccepted = null,
            Func<PathPolygonIndexRequest, bool> routeAcceptedV2 = null)
        {
            if (context == null || buffer == null)
            {
                return;
            }

            if (buffer.ReadableBytes < PathFrameDecoder.MIN_FRAME_LENGTH)
            {
                // Preserve correlation whenever the fixed requestId field is present.
                if (buffer.ReadableBytes >= 12
                    && unchecked((byte)buffer.GetByte(buffer.ReaderIndex)) == PathFrameDecoder.PATH_OPCODE
                    && unchecked((byte)buffer.GetByte(buffer.ReaderIndex + 1)) == PathFrameDecoder.PATH_REQUEST_SUBTYPE)
                    Respond(context, buffer.GetUnsignedIntLE(buffer.ReaderIndex + 8), PathReasonCode.INVALID_REQUEST);
                return;
            }

            int idx = buffer.ReaderIndex;
            byte opcode = unchecked((byte)buffer.GetByte(idx));
            byte subtype = unchecked((byte)buffer.GetByte(idx + 1));

            if (opcode != PathFrameDecoder.PATH_OPCODE || subtype != PathFrameDecoder.PATH_REQUEST_SUBTYPE)
            {
                return;
            }

            uint requestId = buffer.GetUnsignedIntLE(idx + 8);
            ushort protocolVersion = buffer.ReadableBytes >= 4 ? buffer.GetUnsignedShortLE(idx + 2) : (ushort)1;
            ushort responseVersion = protocolVersion == 2 ? (ushort)2 : (ushort)1;
            uint total = buffer.GetUnsignedIntLE(idx + 4);
            if (total < PathFrameDecoder.MIN_FRAME_LENGTH || total > PathFrameDecoder.MAX_FRAME_LENGTH)
            {
                Respond(context, requestId, PathReasonCode.INVALID_REQUEST, responseVersion);
                return;
            }

            if (buffer.ReadableBytes != (int)total)
            {
                Respond(context, requestId, PathReasonCode.INVALID_REQUEST, responseVersion);
                return;
            }

            if (!PathPolygonDecoder.TryDecode(buffer, idx, out PathPolygonIndexRequest req, out PathReasonCode err))
            {
                Respond(context, requestId, err, responseVersion);
                return;
            }

            PathValidationResult result = _validator.Validate(req);
            if (result.Decision == (byte)PathDecision.Accepted)
            {
                bool stored = req.ProtocolVersion == 2
                    ? routeAcceptedV2 != null && routeAcceptedV2(req)
                    : routeAccepted == null || routeAccepted(req.UserId, req.ObjectId, req.PathPolygonIndices);
                if (!stored)
                    result = new PathValidationResult { Decision = (byte)PathDecision.Rejected,
                        ReasonCode = (ushort)PathReasonCode.INVALID_REQUEST, PlanSequence = req.RequestId,
                        TotalSegments = req.ProtocolVersion == 2 ? (uint)(req.Waypoints.Length - 1) : req.PathPolygonCount - 1 };
            }
            Respond(context, req.RequestId, (PathReasonCode)result.ReasonCode, result,
                _validator.AuthoritativeVersion, req.ProtocolVersion, req.GeometryHash);
        }

        private void Respond(IChannelHandlerContext context, uint requestId, PathReasonCode reason, ushort protocolVersion = 1)
        {
            Respond(context, requestId, reason, null, _validator.AuthoritativeVersion, protocolVersion);
        }

        private static void Respond(IChannelHandlerContext context, uint requestId, PathReasonCode reason,
            PathValidationResult validated, string version = null, ushort protocolVersion = 1, byte[] geometryHash = null)
        {
            PathValidationResult res = validated ?? new PathValidationResult
            {
                PlanSequence = 0,
                Decision = (byte)PathDecision.Rejected,
                ReasonCode = (ushort)reason,
                ServerPlanId = null,
                SampledSegments = 0,
                TotalSegments = 0
            };

            byte[] resp = PathPolygonDecoder.EncodeResponse(res, requestId, version, protocolVersion, geometryHash);
            context.WriteAndFlushAsync(Unpooled.CopiedBuffer(resp));
        }
    }
}
