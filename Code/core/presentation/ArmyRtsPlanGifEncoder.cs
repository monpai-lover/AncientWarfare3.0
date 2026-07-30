using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AncientWarfare3.core.presentation
{
    public static class ArmyRtsPlanGifEncoder
    {
        private const int MinimumCodeSize = 8;
        private const int LiteralRunLength = 250;

        public static byte[] Encode(
            IReadOnlyList<ArmyRtsPlanGifFrame> pFrames,
            int pDelayCentiseconds =
                ArmyRtsPlanRules.DefaultFrameDelayCentiseconds,
            Func<bool> pCancellationRequested = null)
        {
            ThrowIfCancelled(pCancellationRequested);
            if (pFrames == null || pFrames.Count == 0)
                throw new ArgumentException("At least one frame is required.",
                    nameof(pFrames));
            int width = pFrames[0].Raster.Width;
            int height = pFrames[0].Raster.Height;
            if (width > ushort.MaxValue || height > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pFrames),
                    "GIF dimensions must fit unsigned 16-bit values.");
            for (int i = 1; i < pFrames.Count; i++)
                if (pFrames[i].Raster.Width != width ||
                    pFrames[i].Raster.Height != height)
                    throw new ArgumentException(
                        "All GIF frames must have identical dimensions.",
                        nameof(pFrames));

            using var output = new MemoryStream();
            WriteAscii(output, "GIF89a");
            WriteUInt16(output, width);
            WriteUInt16(output, height);
            output.WriteByte(0xf7);
            output.WriteByte(0);
            output.WriteByte(0);
            ThrowIfCancelled(pCancellationRequested);
            WritePalette(output);
            WriteLoopExtension(output);
            int delay = Math.Max(1, Math.Min(ushort.MaxValue,
                pDelayCentiseconds));
            for (int i = 0; i < pFrames.Count; i++)
            {
                ThrowIfCancelled(pCancellationRequested);
                WriteFrame(output, pFrames[i].Raster, delay,
                    pCancellationRequested);
            }
            ThrowIfCancelled(pCancellationRequested);
            output.WriteByte(0x3b);
            return output.ToArray();
        }

        private static void WritePalette(Stream pOutput)
        {
            IReadOnlyList<ArmyRtsPlanColor> colors =
                ArmyRtsPlanPalette.Colors;
            for (int i = 0; i < colors.Count; i++)
            {
                pOutput.WriteByte(colors[i].Red);
                pOutput.WriteByte(colors[i].Green);
                pOutput.WriteByte(colors[i].Blue);
            }
        }

        private static void WriteLoopExtension(Stream pOutput)
        {
            pOutput.WriteByte(0x21);
            pOutput.WriteByte(0xff);
            pOutput.WriteByte(0x0b);
            WriteAscii(pOutput, "NETSCAPE2.0");
            pOutput.WriteByte(0x03);
            pOutput.WriteByte(0x01);
            WriteUInt16(pOutput, 0);
            pOutput.WriteByte(0);
        }

        private static void WriteFrame(Stream pOutput,
            ArmyRtsPlanIndexedRaster pRaster, int pDelay,
            Func<bool> pCancellationRequested)
        {
            pOutput.WriteByte(0x21);
            pOutput.WriteByte(0xf9);
            pOutput.WriteByte(0x04);
            pOutput.WriteByte(0x00);
            WriteUInt16(pOutput, pDelay);
            pOutput.WriteByte(0x00);
            pOutput.WriteByte(0x00);

            pOutput.WriteByte(0x2c);
            WriteUInt16(pOutput, 0);
            WriteUInt16(pOutput, 0);
            WriteUInt16(pOutput, pRaster.Width);
            WriteUInt16(pOutput, pRaster.Height);
            pOutput.WriteByte(0x00);
            pOutput.WriteByte(MinimumCodeSize);
            ThrowIfCancelled(pCancellationRequested);
            byte[] compressed = EncodeLiterals(pRaster.Pixels,
                pCancellationRequested);
            int offset = 0;
            while (offset < compressed.Length)
            {
                ThrowIfCancelled(pCancellationRequested);
                int count = Math.Min(255, compressed.Length - offset);
                pOutput.WriteByte((byte)count);
                pOutput.Write(compressed, offset, count);
                offset += count;
            }
            pOutput.WriteByte(0x00);
        }

        private static byte[] EncodeLiterals(byte[] pPixels,
            Func<bool> pCancellationRequested)
        {
            const int clearCode = 1 << MinimumCodeSize;
            const int endCode = clearCode + 1;
            var bytes = new List<byte>(pPixels.Length +
                                       pPixels.Length / 8 + 16);
            int bitBuffer = 0;
            int bitCount = 0;
            WriteCode(bytes, clearCode, ref bitBuffer, ref bitCount);
            int run = 0;
            for (int i = 0; i < pPixels.Length; i++)
            {
                if ((i & 4095) == 0)
                    ThrowIfCancelled(pCancellationRequested);
                if (run == LiteralRunLength)
                {
                    WriteCode(bytes, clearCode, ref bitBuffer, ref bitCount);
                    run = 0;
                }
                WriteCode(bytes, pPixels[i], ref bitBuffer, ref bitCount);
                run++;
            }
            WriteCode(bytes, endCode, ref bitBuffer, ref bitCount);
            if (bitCount > 0) bytes.Add((byte)bitBuffer);
            return bytes.ToArray();
        }

        private static void ThrowIfCancelled(
            Func<bool> pCancellationRequested)
        {
            if (pCancellationRequested != null &&
                pCancellationRequested())
                throw new OperationCanceledException(
                    "RTS plan GIF encoding exceeded its shutdown budget.");
        }

        private static void WriteCode(List<byte> pBytes, int pCode,
            ref int pBitBuffer, ref int pBitCount)
        {
            pBitBuffer |= pCode << pBitCount;
            pBitCount += MinimumCodeSize + 1;
            while (pBitCount >= 8)
            {
                pBytes.Add((byte)pBitBuffer);
                pBitBuffer >>= 8;
                pBitCount -= 8;
            }
        }

        private static void WriteUInt16(Stream pOutput, int pValue)
        {
            pOutput.WriteByte((byte)pValue);
            pOutput.WriteByte((byte)(pValue >> 8));
        }

        private static void WriteAscii(Stream pOutput, string pValue)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(pValue);
            pOutput.Write(bytes, 0, bytes.Length);
        }
    }
}
