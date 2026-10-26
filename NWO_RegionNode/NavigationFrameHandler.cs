using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using NWO_RegionNode.PathValidation;

namespace NWO_RegionNode
{
    /// <summary>Dispatches framed navigation messages before legacy packets reach EchoServerHandler.</summary>
    public sealed class NavigationFrameHandler : ChannelHandlerAdapter
    {
        public const int ToggleRequestLength = 21;
        public const int ToggleAckLength = 22;
        private readonly PathPacketHandler _pathHandler;
        private readonly AutopilotController _autopilot;

        public NavigationFrameHandler(IAuthoritativeNavMeshProvider provider, AutopilotController autopilot)
        {
            _pathHandler = new PathPacketHandler(provider);
            _autopilot = autopilot ?? throw new ArgumentNullException(nameof(autopilot));
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            if (message is not IByteBuffer buffer || buffer.ReadableBytes < 2)
            {
                context.FireChannelRead(message);
                return;
            }

            byte opcode = unchecked((byte)buffer.GetByte(buffer.ReaderIndex));
            byte subtype = unchecked((byte)buffer.GetByte(buffer.ReaderIndex + 1));
            if (opcode == 0x06 && subtype == 0x00)
            {
                try
                {
                    _pathHandler.HandlePacket(context, buffer, (userId, objectId, polygons) =>
                        _autopilot.StoreValidatedRoute(userId, objectId, polygons, context),
                        request => _autopilot.StoreValidatedRoute(request, context));
                }
                finally { ReferenceCountUtil.SafeRelease(buffer); }
                return;
            }

            if (opcode == 0x06 && subtype == 0x02)
            {
                try { HandleToggle(context, buffer); }
                finally { ReferenceCountUtil.SafeRelease(buffer); }
                return;
            }

            if (opcode == 0x04 && subtype == 0x02 && buffer.ReadableBytes == PathFrameDecoder.MANUAL_MOVEMENT_LENGTH)
            {
                int start = buffer.ReaderIndex;
                uint shipId = buffer.GetUnsignedIntLE(start + 2);
                _autopilot.Pause(shipId);
            }
            context.FireChannelRead(buffer);
        }

        private void HandleToggle(IChannelHandlerContext context, IByteBuffer request)
        {
            int start = request.ReaderIndex;
            uint requestId = request.ReadableBytes >= 12 ? request.GetUnsignedIntLE(start + 8) : 0;
            uint userId = request.ReadableBytes >= 16 ? request.GetUnsignedIntLE(start + 12) : 0;
            uint objectId = request.ReadableBytes >= 20 ? request.GetUnsignedIntLE(start + 16) : 0;
            bool valid = request.ReadableBytes == ToggleRequestLength
                && request.GetUnsignedIntLE(start + 4) == ToggleRequestLength
                && request.GetUnsignedShortLE(start + 2) == 1
                && (unchecked((byte)request.GetByte(start + 20)) == 0 || unchecked((byte)request.GetByte(start + 20)) == 1);
            bool accepted = valid && _autopilot.Toggle(userId, objectId, requestId, unchecked((byte)request.GetByte(start + 20)) == 1, context);

            byte[] ack = new byte[ToggleAckLength];
            ack[0] = 0x06; ack[1] = 0x03; ack[2] = 1; ack[3] = 0;
            WriteUInt32LE(ack, 4, ToggleAckLength);
            WriteUInt32LE(ack, 8, requestId);
            WriteUInt32LE(ack, 12, userId);
            WriteUInt32LE(ack, 16, objectId);
            ack[20] = accepted ? (byte)1 : (byte)0;
            ack[21] = accepted ? (byte)0 : (byte)1;
            context.WriteAndFlushAsync(Unpooled.CopiedBuffer(ack));
        }

        public override void ChannelReadComplete(IChannelHandlerContext context)
        {
            context.FireChannelReadComplete();
        }

        private static void WriteUInt32LE(byte[] bytes, int offset, uint value)
        {
            for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i));
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            _autopilot.CancelChannel(context);
            context.FireChannelInactive();
        }
    }
}
