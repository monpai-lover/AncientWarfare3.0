using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.atlas
{
    internal static class KingdomAtlasRasterizer
    {
        private static readonly KingdomAtlasColor Water =
            new KingdomAtlasColor(24, 58, 105);
        private static readonly KingdomAtlasColor Land =
            new KingdomAtlasColor(54, 58, 56);
        private static readonly KingdomAtlasColor Boundary =
            new KingdomAtlasColor(245, 245, 225);

        internal static KingdomAtlasRaster Render(KingdomAtlasNode pNode,
            int pResolution)
        {
            if (pNode?.Event == null) throw new ArgumentNullException(nameof(pNode));
            int resolution = Math.Max(64, Math.Min(8192, pResolution));
            IReadOnlyList<KingdomAtlasZoneCell> cells = pNode.VisibleZones ??
                Array.Empty<KingdomAtlasZoneCell>();
            if (cells.Count == 0)
                return new KingdomAtlasRaster(resolution, resolution,
                    Solid(resolution, resolution, Water));

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            var owners = pNode.CityOwners ?? new Dictionary<long, long>();
            var colors = new Dictionary<long, KingdomAtlasColor>();
            if (KingdomAtlasRules.TryParseColor(pNode.Event.OldKingdomColor, out KingdomAtlasColor oldColor))
                colors[pNode.Event.OldKingdomId] = oldColor;
            if (KingdomAtlasRules.TryParseColor(pNode.Event.NewKingdomColor, out KingdomAtlasColor newColor))
                colors[pNode.Event.NewKingdomId] = newColor;
            var coordinateOwners = new Dictionary<long, long>();
            for (int index = 0; index < cells.Count; index++)
            {
                KingdomAtlasZoneCell cell = cells[index];
                minX = Math.Min(minX, cell.X); maxX = Math.Max(maxX, cell.X);
                minY = Math.Min(minY, cell.Y); maxY = Math.Max(maxY, cell.Y);
                if (!owners.TryGetValue(cell.CityId, out long owner)) owner = -1L;
                coordinateOwners[Key(cell.X, cell.Y)] = owner;
            }
            int worldWidth = Math.Max(1, maxX - minX + 1);
            int worldHeight = Math.Max(1, maxY - minY + 1);
            var pixels = Solid(resolution, resolution, Water);
            for (int index = 0; index < cells.Count; index++)
            {
                KingdomAtlasZoneCell cell = cells[index];
                if (!owners.TryGetValue(cell.CityId, out long owner)) owner = -1L;
                KingdomAtlasColor color = cell.Water ? Water : Land;
                if (!cell.Water && colors.TryGetValue(owner, out KingdomAtlasColor ownerColor))
                    color = ownerColor;
                int left = Scale(cell.X - minX, worldWidth, resolution);
                int right = Scale(cell.X - minX + 1, worldWidth, resolution) - 1;
                int top = Scale(maxY - cell.Y, worldHeight, resolution);
                int bottom = Scale(maxY - cell.Y + 1, worldHeight, resolution) - 1;
                FillRect(pixels, resolution, resolution, left, top, right, bottom, color);
                if (!cell.Water && IsBoundary(coordinateOwners, cell.X, cell.Y, owner))
                    DrawRect(pixels, resolution, resolution, left, top, right, bottom, Boundary);
            }
            DrawLabels(pixels, resolution, resolution, pNode, minX, maxX, minY, maxY);
            return new KingdomAtlasRaster(resolution, resolution, pixels);
        }

        internal static List<KingdomAtlasLabel> BuildLabels(KingdomAtlasNode pNode,
            int pResolution)
        {
            var result = new List<KingdomAtlasLabel>();
            if (pNode?.Event == null) return result;
            AddLabel(result, pNode.Event.OldKingdomId, pNode.Event.OldKingdomName,
                pNode.Event.OldKingdomColor, pNode.VisibleZones, pNode.CityOwners,
                pResolution);
            AddLabel(result, pNode.Event.NewKingdomId, pNode.Event.NewKingdomName,
                pNode.Event.NewKingdomColor, pNode.VisibleZones, pNode.CityOwners,
                pResolution);
            return result;
        }

        private static void AddLabel(List<KingdomAtlasLabel> pResult, long pKingdomId,
            string pName, string pColorText, IReadOnlyList<KingdomAtlasZoneCell> pCells,
            IReadOnlyDictionary<long, long> pOwners, int pResolution)
        {
            if (pKingdomId < 0L || string.IsNullOrWhiteSpace(pName) || pCells == null) return;
            var points = new List<KingdomAtlasZoneCell>();
            for (int i = 0; i < pCells.Count; i++)
            {
                KingdomAtlasZoneCell cell = pCells[i];
                if (!pOwners.TryGetValue(cell.CityId, out long owner) || owner != pKingdomId || cell.Water) continue;
                points.Add(cell);
            }
            if (points.Count == 0) return;
            long sumX = 0, sumY = 0;
            for (int i = 0; i < points.Count; i++) { sumX += points[i].X; sumY += points[i].Y; }
            if (!KingdomAtlasRules.TryParseColor(pColorText, out KingdomAtlasColor color)) color = Boundary;
            pResult.Add(new KingdomAtlasLabel { KingdomId = pKingdomId, Text = pName,
                Color = color, X = (int)Math.Round(sumX / (double)points.Count),
                Y = (int)Math.Round(sumY / (double)points.Count), Size = Math.Max(8f, Math.Min(22f, (float)Math.Sqrt(points.Count))) });
        }

        private static bool IsBoundary(Dictionary<long, long> pOwners, int pX, int pY, long pOwner)
        {
            return OwnerAt(pOwners, pX - 1, pY, pOwner) || OwnerAt(pOwners, pX + 1, pY, pOwner) ||
                   OwnerAt(pOwners, pX, pY - 1, pOwner) || OwnerAt(pOwners, pX, pY + 1, pOwner);
        }

        private static bool OwnerAt(Dictionary<long, long> pOwners, int pX, int pY, long pOwner)
        {
            return pOwners.TryGetValue(Key(pX, pY), out long owner) && owner != pOwner;
        }

        private static long Key(int pX, int pY) => ((long)pX << 32) ^ (uint)pY;

        private static int Scale(int pValue, int pExtent, int pResolution)
        {
            return Math.Max(0, Math.Min(pResolution - 1,
                (int)Math.Floor(pValue * (double)pResolution / pExtent)));
        }

        private static byte[] Solid(int pWidth, int pHeight, KingdomAtlasColor pColor)
        {
            var result = new byte[pWidth * pHeight * 4];
            for (int i = 0; i < pWidth * pHeight; i++)
            {
                int o = i * 4; result[o] = pColor.Red; result[o + 1] = pColor.Green;
                result[o + 2] = pColor.Blue; result[o + 3] = pColor.Alpha;
            }
            return result;
        }

        private static void FillRect(byte[] pPixels, int pWidth, int pHeight,
            int pLeft, int pTop, int pRight, int pBottom, KingdomAtlasColor pColor)
        {
            int left = Math.Max(0, Math.Min(pWidth - 1, Math.Min(pLeft, pRight)));
            int right = Math.Max(0, Math.Min(pWidth - 1, Math.Max(pLeft, pRight)));
            int top = Math.Max(0, Math.Min(pHeight - 1, Math.Min(pTop, pBottom)));
            int bottom = Math.Max(0, Math.Min(pHeight - 1, Math.Max(pTop, pBottom)));
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++) Set(pPixels, pWidth, x, y, pColor);
        }

        private static void DrawRect(byte[] pPixels, int pWidth, int pHeight,
            int pLeft, int pTop, int pRight, int pBottom, KingdomAtlasColor pColor)
        {
            for (int x = pLeft; x <= pRight; x++) { Set(pPixels, pWidth, x, pTop, pColor); Set(pPixels, pWidth, x, pBottom, pColor); }
            for (int y = pTop; y <= pBottom; y++) { Set(pPixels, pWidth, pLeft, y, pColor); Set(pPixels, pWidth, pRight, y, pColor); }
        }

        private static void Set(byte[] pPixels, int pWidth, int pX, int pY, KingdomAtlasColor pColor)
        {
            if (pX < 0 || pY < 0 || pX >= pWidth || pY >= pWidth) return;
            int offset = (pY * pWidth + pX) * 4;
            pPixels[offset] = pColor.Red; pPixels[offset + 1] = pColor.Green;
            pPixels[offset + 2] = pColor.Blue; pPixels[offset + 3] = pColor.Alpha;
        }

        private static void DrawLabels(byte[] pPixels, int pWidth, int pHeight,
            KingdomAtlasNode pNode, int pMinX, int pMaxX, int pMinY, int pMaxY)
        {
            List<KingdomAtlasLabel> labels = BuildLabels(pNode, pWidth);
            for (int i = 0; i < labels.Count; i++)
            {
                KingdomAtlasLabel label = labels[i];
                int x = Scale(label.X - pMinX, Math.Max(1, pMaxX - pMinX + 1), pWidth);
                int y = Scale(label.Y - pMinY, Math.Max(1, pMaxY - pMinY + 1), pHeight);
                DrawText(pPixels, pWidth, pHeight, label.Text, x, pHeight - y,
                    label.Color);
            }
        }

        private static void DrawText(byte[] pPixels, int pWidth, int pHeight,
            string pText, int pX, int pY, KingdomAtlasColor pColor)
        {
            if (string.IsNullOrEmpty(pText)) return;
            int cursor = pX - Math.Min(120, pText.Length * 4);
            for (int i = 0; i < pText.Length && i < 32; i++)
            {
                char ch = char.ToUpperInvariant(pText[i]);
                string[] glyph = Glyph(ch);
                for (int gy = 0; gy < glyph.Length; gy++)
                    for (int gx = 0; gx < glyph[gy].Length; gx++)
                        if (glyph[gy][gx] == '#')
                        {
                            Set(pPixels, pWidth, cursor + gx * 2, pY - gy * 2, new KingdomAtlasColor(0, 0, 0, 255));
                            Set(pPixels, pWidth, cursor + gx * 2 + 1, pY - gy * 2 - 1, pColor);
                        }
                cursor += 12;
            }
        }

        private static string[] Glyph(char pChar)
        {
            switch (pChar)
            {
                case 'A': return new[] { ".#.", "#.#", "###", "#.#", "#.#" };
                case 'B': return new[] { "##.", "#.#", "##.", "#.#", "##." };
                case 'C': return new[] { ".##", "#..", "#..", "#..", ".##" };
                case 'D': return new[] { "##.", "#.#", "#.#", "#.#", "##." };
                case 'E': return new[] { "###", "#..", "##.", "#..", "###" };
                case 'F': return new[] { "###", "#..", "##.", "#..", "#.." };
                case 'G': return new[] { ".##", "#..", "#.#", "#.#", ".##" };
                case 'H': return new[] { "#.#", "#.#", "###", "#.#", "#.#" };
                case 'I': return new[] { "###", ".#.", ".#.", ".#.", "###" };
                case 'J': return new[] { "..#", "..#", "..#", "#.#", ".#." };
                case 'K': return new[] { "#.#", "##.", "#..", "##.", "#.#" };
                case 'L': return new[] { "#..", "#..", "#..", "#..", "###" };
                case 'M': return new[] { "#.#", "###", "###", "#.#", "#.#" };
                case 'N': return new[] { "#.#", "###", "###", "###", "#.#" };
                case 'O': return new[] { ".#.", "#.#", "#.#", "#.#", ".#." };
                case 'P': return new[] { "##.", "#.#", "##.", "#..", "#.." };
                case 'Q': return new[] { ".#.", "#.#", "#.#", ".##", "..#" };
                case 'R': return new[] { "##.", "#.#", "##.", "#.#", "#.#" };
                case 'S': return new[] { ".##", "#..", ".#.", "..#", "##." };
                case 'T': return new[] { "###", ".#.", ".#.", ".#.", ".#." };
                case 'U': return new[] { "#.#", "#.#", "#.#", "#.#", ".#." };
                case 'V': return new[] { "#.#", "#.#", "#.#", "#.#", ".#." };
                case 'W': return new[] { "#.#", "#.#", "###", "###", "#.#" };
                case 'X': return new[] { "#.#", "#.#", ".#.", "#.#", "#.#" };
                case 'Y': return new[] { "#.#", "#.#", ".#.", ".#.", ".#." };
                case 'Z': return new[] { "###", "..#", ".#.", "#..", "###" };
                case ' ': return new[] { "...", "...", "...", "...", "..." };
                default: return new[] { "###", "#.#", ".#.", "#.#", "###" };
            }
        }
    }
}
