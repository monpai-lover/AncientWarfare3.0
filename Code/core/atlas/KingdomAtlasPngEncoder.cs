using System;
using System.IO;
using System.IO.Compression;

namespace AncientWarfare3.core.atlas
{
    internal static class KingdomAtlasPngEncoder
    {
        internal static byte[] Encode(KingdomAtlasRaster pRaster)
        {
            if (pRaster == null) throw new ArgumentNullException(nameof(pRaster));
            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                WriteChunk(output, "IHDR", BuildHeader(pRaster.Width, pRaster.Height));
                using (var raw = new MemoryStream())
                {
                    int stride = pRaster.Width * 4;
                    for (int y = pRaster.Height - 1; y >= 0; y--)
                    {
                        raw.WriteByte(0);
                        raw.Write(pRaster.Rgba, y * stride, stride);
                    }
                    byte[] compressed;
                    byte[] rawBytes = raw.ToArray();
                    using (var packed = new MemoryStream())
                    {
                        using (var deflate = new DeflateStream(packed, CompressionLevel.Fastest, true))
                        {
                            raw.Position = 0;
                            raw.CopyTo(deflate);
                        }
                        byte[] deflated = packed.ToArray();
                        compressed = new byte[deflated.Length + 6];
                        compressed[0] = 0x78; compressed[1] = 0x9c;
                        Buffer.BlockCopy(deflated, 0, compressed, 2, deflated.Length);
                        uint adler = Adler32(rawBytes);
                        int offset = deflated.Length + 2;
                        compressed[offset] = (byte)(adler >> 24);
                        compressed[offset + 1] = (byte)(adler >> 16);
                        compressed[offset + 2] = (byte)(adler >> 8);
                        compressed[offset + 3] = (byte)adler;
                    }
                    WriteChunk(output, "IDAT", compressed);
                }
                WriteChunk(output, "IEND", Array.Empty<byte>());
                return output.ToArray();
            }
        }

        private static byte[] BuildHeader(int pWidth, int pHeight)
        {
            return new[] { (byte)(pWidth >> 24), (byte)(pWidth >> 16), (byte)(pWidth >> 8), (byte)pWidth,
                (byte)(pHeight >> 24), (byte)(pHeight >> 16), (byte)(pHeight >> 8), (byte)pHeight,
                (byte)8, (byte)6, (byte)0, (byte)0, (byte)0 };
        }

        private static void WriteChunk(Stream pStream, string pName, byte[] pData)
        {
            byte[] name = System.Text.Encoding.ASCII.GetBytes(pName);
            WriteUInt(pStream, (uint)(pData?.Length ?? 0));
            pStream.Write(name, 0, 4);
            if (pData != null && pData.Length > 0) pStream.Write(pData, 0, pData.Length);
            uint crc = 0xffffffffu;
            for (int i = 0; i < name.Length; i++) crc = Crc(crc, name[i]);
            if (pData != null) for (int i = 0; i < pData.Length; i++) crc = Crc(crc, pData[i]);
            WriteUInt(pStream, crc ^ 0xffffffffu);
        }

        private static void WriteUInt(Stream pStream, uint pValue)
        {
            pStream.WriteByte((byte)(pValue >> 24)); pStream.WriteByte((byte)(pValue >> 16));
            pStream.WriteByte((byte)(pValue >> 8)); pStream.WriteByte((byte)pValue);
        }

        private static uint Crc(uint pCrc, byte pValue)
        {
            pCrc ^= pValue;
            for (int i = 0; i < 8; i++) pCrc = (pCrc & 1) != 0 ? (pCrc >> 1) ^ 0xedb88320u : pCrc >> 1;
            return pCrc;
        }

        private static uint Adler32(byte[] pData)
        {
            uint a = 1, b = 0;
            for (int i = 0; i < pData.Length; i++)
            {
                a = (a + pData[i]) % 65521u;
                b = (b + a) % 65521u;
            }
            return (b << 16) | a;
        }
    }
}
