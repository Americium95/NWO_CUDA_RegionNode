using System;
using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;

namespace NWO_RegionNode.PathValidation
{
    public sealed class PathFrameDecoder : ChannelHandlerAdapter
    {
        public const byte PATH_OPCODE = 0x06;
        public const byte PATH_REQUEST_SUBTYPE = 0x00;
        public const byte AUTOPILOT_TOGGLE_SUBTYPE = 0x02;
        public const int LENGTH_FIELD_END = 8;
        public const int MIN_FRAME_LENGTH = 12;
        public const int MAX_FRAME_LENGTH = 1048576;
        public const int INITIAL_PENDING_CAPACITY = 4096;
        public const int AUTOPILOT_TOGGLE_REQUEST_LENGTH = 21;
        public const int MANUAL_MOVEMENT_LENGTH = 11;

        private byte[] _pending = new byte[INITIAL_PENDING_CAPACITY];
        private int _pendingCount;

        public int PendingBytes => _pendingCount;
        public int PendingCapacity => _pending.Length;

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var buffer = message as IByteBuffer;
            if (buffer == null || buffer.ReadableBytes <= 0)
            {
                ReferenceCountUtil.SafeRelease(message);
                return;
            }

            // A 0x06 frame is already in flight. Every following byte belongs to that
            // frame until the advertised length is satisfied, so it is accumulated
            // unconditionally. Any non-0x06 byte seen by Drain is an unsupported
            // interleave and terminates the channel (see Drain).
            if (_pendingCount > 0)
            {
                if (!CanAppend(buffer, context)) return;
                Append(buffer);
                ReferenceCountUtil.SafeRelease(buffer);

                Drain(context);
                return;
            }

            // No frame in flight. A read that starts with 0x06 begins a 0x06 stream.
            if (unchecked((byte)buffer.GetByte(buffer.ReaderIndex)) == PATH_OPCODE)
            {
                if (!CanAppend(buffer, context)) return;
                Append(buffer);
                ReferenceCountUtil.SafeRelease(buffer);

                Drain(context);
                return;
            }

            // The read starts with a legacy 0x02/0x04 packet. Legacy packets carry no
            // length field, so this decoder cannot tell where that packet ends and
            // therefore cannot split a trailing coalesced 0x06 frame off it without
            // risking an offset misread of the legacy payload.
            //
            // Rather than forwarding the whole buffer and silently discarding the
            // trailing 0x06 request, an unframeable mix is rejected deterministically:
            // the channel is closed so nothing is lost quietly.
            if (ContainsPathFrameHeader(buffer))
            {
                Console.WriteLine("[path] legacy packet coalesced with 0x06 frame, channel closed");
                ReferenceCountUtil.SafeRelease(buffer);
                context.CloseAsync();
                return;
            }

            // Pure legacy read: forward the original buffer untouched.
            context.FireChannelRead(buffer);
        }

        private void Append(IByteBuffer buffer)
        {
            int readable = buffer.ReadableBytes;
            EnsureCapacity(_pendingCount + readable);
            buffer.GetBytes(buffer.ReaderIndex, _pending, _pendingCount, readable);
            _pendingCount += readable;
        }

        private bool CanAppend(IByteBuffer buffer, IChannelHandlerContext context)
        {
            int readable = buffer.ReadableBytes;
            // Check using subtraction to avoid integer overflow. Pending storage is
            // fixed-capacity at MAX_FRAME_LENGTH and input is rejected before copying.
            if (readable > MAX_FRAME_LENGTH - _pendingCount)
            {
                Console.WriteLine("[path] 0x06 read exceeds pending frame capacity, channel closed");
                _pendingCount = 0;
                ReferenceCountUtil.SafeRelease(buffer);
                context.CloseAsync();
                return false;
            }

            int combined = _pendingCount + readable;
            // Once enough header bytes are present, reject impossible advertised
            // lengths before retaining the rest of this read.
            if (combined >= LENGTH_FIELD_END && _pendingCount < LENGTH_FIELD_END)
            {
                int headerOffset = _pendingCount == 0 ? buffer.ReaderIndex : 0;
                uint advertised = ReadLengthAcrossBuffers(buffer, headerOffset);
                if (advertised < MIN_FRAME_LENGTH || advertised > MAX_FRAME_LENGTH)
                {
                    Console.WriteLine($"[path] invalid 0x06 frame length {advertised}, channel closed");
                    _pendingCount = 0;
                    ReferenceCountUtil.SafeRelease(buffer);
                    context.CloseAsync();
                    return false;
                }
            }
            return true;
        }

        private uint ReadLengthAcrossBuffers(IByteBuffer buffer, int bufferOffset)
        {
            byte At(int index) => index < _pendingCount
                ? _pending[index]
                : buffer.GetByte(bufferOffset + index - _pendingCount);
            return (uint)(unchecked((byte)At(4))
                | (unchecked((byte)At(5)) << 8)
                | (unchecked((byte)At(6)) << 16)
                | (unchecked((byte)At(7)) << 24));
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _pending.Length)
            {
                return;
            }
            int capacity = _pending.Length;
            while (capacity < required)
            {
                capacity = Math.Min(MAX_FRAME_LENGTH, capacity * 2);
            }
            Array.Resize(ref _pending, capacity);
        }

        private void Drain(IChannelHandlerContext context)
        {
            int offset = 0;
            while (_pendingCount - offset > 0)
            {
                // A 0x06 frame was declared in flight but this byte is not 0x06:
                // legacy bytes were interleaved into the middle of a frame. The
                // declared length can no longer be trusted, so no safe split exists.
                // Fail closed instead of guessing and losing a frame.
                if (_pending[offset] != PATH_OPCODE)
                {
                    Console.WriteLine("[path] legacy bytes interleaved into 0x06 frame, channel closed");
                    _pendingCount = 0;
                    context.CloseAsync();
                    return;
                }

                if (_pendingCount - offset < 2)
                {
                    break;
                }

                byte subtype = _pending[offset + 1];
                if (subtype != PATH_REQUEST_SUBTYPE && subtype != AUTOPILOT_TOGGLE_SUBTYPE)
                {
                    Console.WriteLine("[path] unsupported or invalid 0x06 subtype frame, channel closed");
                    _pendingCount = 0;
                    context.CloseAsync();
                    return;
                }

                if (_pendingCount - offset < LENGTH_FIELD_END)
                {
                    break;
                }

                uint total = ReadLength(_pending, offset);
                if (subtype == AUTOPILOT_TOGGLE_SUBTYPE && total != AUTOPILOT_TOGGLE_REQUEST_LENGTH)
                {
                    Console.WriteLine("[path] invalid autopilot toggle length, channel closed");
                    _pendingCount = 0; context.CloseAsync(); return;
                }
                if (total < MIN_FRAME_LENGTH || total > MAX_FRAME_LENGTH)
                {
                    Console.WriteLine($"[path] invalid 0x06 frame length {total}, channel closed");
                    _pendingCount = 0;
                    context.CloseAsync();
                    return;
                }

                // A frame can be at most MAX_FRAME_LENGTH; the preflight above keeps
                // aggregate pending storage within that same limit.

                if (_pendingCount - offset < (int)total)
                {
                    break;
                }

                var frame = new byte[total];
                Buffer.BlockCopy(_pending, offset, frame, 0, (int)total);
                offset += (int)total;
                context.FireChannelRead(Unpooled.CopiedBuffer(frame));
                // OFF is commonly written immediately before legacy manual input.
                // Split only this known fixed-size frame; leave a partial tail buffered.
                if (subtype == AUTOPILOT_TOGGLE_SUBTYPE && _pendingCount - offset >= 2
                    && _pending[offset] == 0x04 && _pending[offset + 1] == 0x02)
                {
                    if (_pendingCount - offset < MANUAL_MOVEMENT_LENGTH) break;
                    var manual = new byte[MANUAL_MOVEMENT_LENGTH];
                    Buffer.BlockCopy(_pending, offset, manual, 0, MANUAL_MOVEMENT_LENGTH);
                    offset += MANUAL_MOVEMENT_LENGTH;
                    context.FireChannelRead(Unpooled.CopiedBuffer(manual));
                }
            }

            Compact(offset);
        }

        // Detects a complete 0x06/0x00 header (opcode, subtype and a plausible
        // totalPacketLength) anywhere in a legacy-first read. A hit means the read
        // mixes a legacy packet with a path request and cannot be framed safely.
        private static bool ContainsPathFrameHeader(IByteBuffer buffer)
        {
            int start = buffer.ReaderIndex;
            int readable = buffer.ReadableBytes;
            for (int i = 0; i + LENGTH_FIELD_END <= readable; i++)
            {
                int pos = start + i;
                if (unchecked((byte)buffer.GetByte(pos)) != PATH_OPCODE
                    || unchecked((byte)buffer.GetByte(pos + 1)) != PATH_REQUEST_SUBTYPE)
                {
                    continue;
                }

                uint total = (uint)(unchecked((byte)buffer.GetByte(pos + 4))
                    | (unchecked((byte)buffer.GetByte(pos + 5)) << 8)
                    | (unchecked((byte)buffer.GetByte(pos + 6)) << 16)
                    | (unchecked((byte)buffer.GetByte(pos + 7)) << 24));

                if (total >= MIN_FRAME_LENGTH && total <= MAX_FRAME_LENGTH)
                {
                    return true;
                }
            }

            return false;
        }

        private static uint ReadLength(byte[] source, int offset)
        {
            return (uint)(source[offset + 4]
                | (source[offset + 5] << 8)
                | (source[offset + 6] << 16)
                | (source[offset + 7] << 24));
        }

        private void Compact(int consumed)
        {
            if (consumed <= 0)
            {
                return;
            }
            int remaining = _pendingCount - consumed;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_pending, consumed, _pending, 0, remaining);
            }
            _pendingCount = remaining;
        }
    }
}
