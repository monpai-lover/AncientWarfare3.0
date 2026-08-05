using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.atlas
{
    internal sealed class KingdomAtlasGenerationResult
    {
        public int NodesGenerated { get; set; }
        public int NodesSkipped { get; set; }
        public string OutputDirectory { get; set; } = "";
        public string Error { get; set; } = "";
        public bool Success => string.IsNullOrEmpty(Error);
    }

    internal static class KingdomAtlasArtifactWriter
    {
        internal static KingdomAtlasGenerationResult Generate(long pKingdomId,
            int pResolution, bool pGif, Action<KingdomAtlasProgress> pProgress = null,
            Func<bool> pCancellationRequested = null)
        {
            var result = new KingdomAtlasGenerationResult();
            if (!KingdomAtlasRules.IsReliableResolution(pResolution, pResolution))
            {
                result.Error = "Unsupported atlas resolution.";
                return result;
            }
            if (!AW3SaveDirectoryRegistry.TryGet(out string saveDirectory))
            {
                result.Error = "Save the world before generating a kingdom atlas.";
                return result;
            }
            List<KingdomAtlasNode> nodes;
            try { nodes = KingdomAtlasHistoryService.BuildNodes(pKingdomId); }
            catch (Exception error) { result.Error = error.Message; return result; }
            if (!KingdomAtlasHistoryService.HasReliableColours(nodes))
            {
                result.Error = "Historical kingdom colours are incomplete; atlas generation was refused.";
                return result;
            }
            string output = Path.Combine(saveDirectory, "aw3_kingdom_atlas");
            Directory.CreateDirectory(output);
            string manifestPath = Path.Combine(output, "kingdom_" +
                pKingdomId.ToString(CultureInfo.InvariantCulture) + "_" +
                pResolution.ToString(CultureInfo.InvariantCulture) + ".manifest");
            long cursor = ReadCursor(manifestPath);
            var gifFrames = new List<KingdomAtlasRaster>();
            int total = nodes.Count;
            for (int index = 0; index < nodes.Count; index++)
            {
                if (pCancellationRequested != null && pCancellationRequested())
                {
                    result.Error = "Atlas generation cancelled.";
                    return result;
                }
                KingdomAtlasNode node = nodes[index];
                if (node.Event.EventId <= cursor)
                {
                    result.NodesSkipped++;
                    if (pGif) gifFrames.Add(KingdomAtlasRasterizer.Render(node, pResolution));
                    pProgress?.Invoke(new KingdomAtlasProgress(index + 1, total,
                        "skipped", node.Event.EventId));
                    continue;
                }
                KingdomAtlasRaster raster = KingdomAtlasRasterizer.Render(node, pResolution);
                byte[] png = KingdomAtlasPngEncoder.Encode(raster);
                string stem = KingdomAtlasRules.BuildOutputStem(pKingdomId,
                    pResolution, index, node.Event.EventId);
                string pngPath = Path.Combine(output, stem + ".png");
                WriteAtomically(pngPath, png);
                if (pGif)
                    gifFrames.Add(raster);
                WriteCursor(manifestPath, node.Event.EventId);
                result.NodesGenerated++;
                pProgress?.Invoke(new KingdomAtlasProgress(index + 1, total,
                    "png", node.Event.EventId));
            }
            if (pGif && gifFrames.Count > 0)
            {
                byte[] gif = KingdomAtlasGifEncoder.Encode(gifFrames);
                WriteAtomically(Path.Combine(output, "kingdom_" +
                    pKingdomId.ToString(CultureInfo.InvariantCulture) + "_" +
                    pResolution.ToString(CultureInfo.InvariantCulture) + ".gif"), gif);
            }
            result.OutputDirectory = output;
            return result;
        }

        private static long ReadCursor(string pPath)
        {
            try
            {
                if (!File.Exists(pPath)) return -1L;
                string line = File.ReadAllText(pPath).Trim();
                return long.TryParse(line, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long value) ? value : -1L;
            }
            catch { return -1L; }
        }

        private static void WriteCursor(string pPath, long pEventId)
        {
            string temp = pPath + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, pEventId.ToString(CultureInfo.InvariantCulture),
                new UTF8Encoding(false));
            if (File.Exists(pPath)) File.Replace(temp, pPath, null);
            else File.Move(temp, pPath);
        }

        private static void WriteAtomically(string pPath, byte[] pBytes)
        {
            string temp = pPath + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temp, pBytes);
            if (File.Exists(pPath)) File.Replace(temp, pPath, null);
            else File.Move(temp, pPath);
        }
    }
}
