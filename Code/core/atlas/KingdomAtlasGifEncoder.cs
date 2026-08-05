using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AncientWarfare3.core.atlas
{
    internal static class KingdomAtlasGifEncoder
    {
        private const int CodeSize = 8;
        internal static byte[] Encode(IReadOnlyList<KingdomAtlasRaster> pFrames,
            Func<bool> pCancelled = null)
        {
            if (pFrames == null || pFrames.Count == 0) throw new ArgumentException("At least one frame is required.");
            int width = pFrames[0].Width, height = pFrames[0].Height;
            using (var output = new MemoryStream())
            {
                WriteAscii(output, "GIF89a"); Write16(output, width); Write16(output, height);
                output.WriteByte(0xf7); output.WriteByte(0); output.WriteByte(0); WritePalette(output); WriteLoop(output);
                for (int i = 0; i < pFrames.Count; i++)
                {
                    if (pCancelled != null && pCancelled()) throw new OperationCanceledException();
                    KingdomAtlasRaster frame = pFrames[i];
                    if (frame.Width != width || frame.Height != height) throw new ArgumentException("GIF frame dimensions differ.");
                    output.WriteByte(0x21); output.WriteByte(0xf9); output.WriteByte(4); output.WriteByte(0);
                    Write16(output, 8); output.WriteByte(0); output.WriteByte(0);
                    output.WriteByte(0x2c); Write16(output, 0); Write16(output, 0); Write16(output, width); Write16(output, height); output.WriteByte(0); output.WriteByte(CodeSize);
                    byte[] indexed = Index(frame); byte[] packed = Pack(indexed);
                    int offset = 0; while (offset < packed.Length) { int count = Math.Min(255, packed.Length - offset); output.WriteByte((byte)count); output.Write(packed, offset, count); offset += count; }
                    output.WriteByte(0);
                }
                output.WriteByte(0x3b); return output.ToArray();
            }
        }

        private static byte[] Index(KingdomAtlasRaster pRaster)
        {
            var pixels = new byte[pRaster.Width * pRaster.Height];
            for (int i = 0; i < pixels.Length; i++) { int o = i * 4; pixels[i] = (byte)(((pRaster.Rgba[o] >> 5) << 5) | ((pRaster.Rgba[o + 1] >> 5) << 2) | (pRaster.Rgba[o + 2] >> 6)); }
            return pixels;
        }

        private static byte[] Pack(byte[] pPixels)
        {
            const int clear = 1 << CodeSize, end = clear + 1;
            var bytes = new List<byte>(pPixels.Length + 32); int buffer = 0, bits = 0;
            Action<int> write = code => { buffer |= code << bits; bits += CodeSize + 1; while (bits >= 8) { bytes.Add((byte)buffer); buffer >>= 8; bits -= 8; } };
            write(clear); int run = 0;
            for (int i = 0; i < pPixels.Length; i++) { if (run == 250) { write(clear); run = 0; } write(pPixels[i]); run++; }
            write(end); if (bits > 0) bytes.Add((byte)buffer); return bytes.ToArray();
        }

        private static void WritePalette(Stream pOutput)
        {
            for (int i = 0; i < 256; i++) { int r = i >> 5 & 7, g = i >> 2 & 7, b = i & 3; pOutput.WriteByte((byte)Math.Round(r * 255d / 7d)); pOutput.WriteByte((byte)Math.Round(g * 255d / 7d)); pOutput.WriteByte((byte)Math.Round(b * 255d / 3d)); }
        }
        private static void WriteLoop(Stream pOutput) { pOutput.WriteByte(0x21); pOutput.WriteByte(0xff); pOutput.WriteByte(0x0b); WriteAscii(pOutput, "NETSCAPE2.0"); pOutput.WriteByte(3); pOutput.WriteByte(1); Write16(pOutput, 0); pOutput.WriteByte(0); }
        private static void Write16(Stream pOutput, int pValue) { pOutput.WriteByte((byte)pValue); pOutput.WriteByte((byte)(pValue >> 8)); }
        private static void WriteAscii(Stream pOutput, string pValue) { byte[] bytes = Encoding.ASCII.GetBytes(pValue); pOutput.Write(bytes, 0, bytes.Length); }
    }
}
