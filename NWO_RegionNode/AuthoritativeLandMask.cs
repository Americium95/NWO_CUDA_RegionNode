using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Numerics;

namespace NWO_RegionNode.PathValidation
{
    /// <summary>Trusted NLM2 packed chart used for the client-compatible dense land rejection lattice.</summary>
    internal sealed class AuthoritativeLandMask
    {
        private const string TrustedSha256 = "ab4f6a9c22c86a028f1b8851f7ad51e4248b1e8ff391cef30839559bc30f97d8";
        private const int HeaderBytes = 24;
        private const int SourceChartSize = 8192;
        private const int CropTop = 260;
        private const int Width = 8192;
        private const int Height = 5380;
        private const int NavMapSize = 2048;
        private const double MapSize = 2048.0;
        private readonly byte[] _bits;

        private AuthoritativeLandMask(byte[] bits) { _bits = bits; }

        internal static AuthoritativeLandMask TryLoad(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                byte[] compressed = File.ReadAllBytes(path);
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(compressed)).Replace("-", "").ToLowerInvariant();
                    if (!string.Equals(actual, TrustedSha256, StringComparison.Ordinal)) return null;
                }

                byte[] packed;
                using (var input = new MemoryStream(compressed, false))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    packed = output.ToArray();
                }
                if (packed.Length < HeaderBytes || packed[0] != (byte)'N' || packed[1] != (byte)'L' ||
                    packed[2] != (byte)'M' || packed[3] != (byte)'2') return null;
                int width = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(4, 4));
                int height = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(8, 4));
                int crop = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(12, 4));
                int navMapSize = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(16, 4));
                int reserved = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(20, 4));
                long bitCount = (long)width * height;
                if (width != Width || height != Height || crop != CropTop || navMapSize != NavMapSize || reserved != 0 ||
                    bitCount > int.MaxValue * 8L || packed.Length != HeaderBytes + (bitCount + 7L) / 8L) return null;
                var bits = new byte[packed.Length - HeaderBytes];
                Buffer.BlockCopy(packed, HeaderBytes, bits, 0, bits.Length);
                return new AuthoritativeLandMask(bits);
            }
            catch (Exception)
            {
                // A missing, corrupt, or unsupported trusted chart closes V2 route validation.
                return null;
            }
        }

        internal bool SegmentCrossesLand(Vector2 startNav, Vector2 endNav)
        {
            float dx = endNav.X - startNav.X;
            if (dx > (float)MapSize * 0.5f) dx -= (float)MapSize;
            else if (dx < -(float)MapSize * 0.5f) dx += (float)MapSize;
            float dy = endNav.Y - startNav.Y;
            float pixelsPerNavUnit = SourceChartSize / (float)MapSize;
            float pixelX = dx * pixelsPerNavUnit, pixelY = dy * pixelsPerNavUnit;
            // Match the managed client Mono profile: accumulate squared float components in double,
            // cast the sum back to float, then take the float square root.
            float pixelLength = MathF.Sqrt((float)((double)pixelX * pixelX + (double)pixelY * pixelY));
            int steps = Math.Max(1, (int)MathF.Ceiling(pixelLength * 2f));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                // Keep interpolation's scalar intermediates in double as on the managed client.
                var point = new Vector2((float)((double)startNav.X + (double)dx * t),
                    (float)((double)startNav.Y + (double)dy * t));
                if (!TryGetBit(point, out bool land) || land) return true;
            }
            return false;
        }

        internal bool TryGetBit(Vector2 nav, out bool land)
        {
            // Preserve the client's double remainder/normalization before narrowing to Vector2 precision.
            if (!float.IsFinite(nav.X) || !float.IsFinite(nav.Y)) { land = false; return false; }
            double wrappedX = nav.X % MapSize;
            if (wrappedX < 0) wrappedX += MapSize;
            int x = (int)Math.Floor(wrappedX / MapSize * SourceChartSize);
            int sourceY = (int)Math.Floor((double)nav.Y / MapSize * SourceChartSize);
            int y = sourceY - CropTop;
            if ((uint)x >= Width || (uint)y >= Height)
            {
                land = false;
                return false;
            }
            int index = y * Width + x;
            land = (_bits[index >> 3] & (1 << (index & 7))) != 0;
            return true;
        }
    }
}
