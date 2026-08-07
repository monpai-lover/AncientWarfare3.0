using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.presentation;

namespace AncientWarfare3.core.atlas
{
    internal sealed class KingdomAtlasGenerationResult
    {
        public int NodesGenerated { get; set; }
        public int NodesSkipped { get; set; }
        public string OutputDirectory { get; set; } = "";
        public string GifPath { get; set; } = "";
        public string Error { get; set; } = "";
        public bool Success => string.IsNullOrEmpty(Error);
    }

    internal static class KingdomAtlasArtifactWriter
    {
        internal const string GeometryVersion =
            "historical-city-chronicle-v14-territory-scaled-labels";

        internal static KingdomAtlasGenerationResult Generate(long pKingdomId,
            int pResolution, bool pGif, Action<KingdomAtlasProgress> pProgress = null,
            Func<bool> pCancellationRequested = null,
            ArmyRtsPlanWorldTerrainSnapshot pTerrain = null)
        {
            KingdomAtlasGenerationSession session = Begin(pKingdomId,
                pResolution, pGif, pTerrain);
            KingdomAtlasProgress progress;
            while (session.MoveNext(pCancellationRequested, out progress))
                pProgress?.Invoke(progress);
            return session.Result;
        }

        internal static KingdomAtlasGenerationSession Begin(long pKingdomId,
            int pResolution, bool pGif,
            ArmyRtsPlanWorldTerrainSnapshot pTerrain = null)
        {
            return KingdomAtlasGenerationSession.Create(pKingdomId,
                pResolution, pGif, pTerrain);
        }

        internal static string GetOutputDirectory(string pSaveDirectory)
        {
            return Path.Combine(pSaveDirectory ?? "", "aw3_kingdom_atlas",
                GeometryVersion);
        }

        internal static string BuildPngPath(string pSaveDirectory,
            long pKingdomId, int pResolution, int pNodeIndex, long pEventId)
        {
            return Path.Combine(GetOutputDirectory(pSaveDirectory),
                KingdomAtlasRules.BuildOutputStem(pKingdomId, pResolution,
                    pNodeIndex, pEventId) + ".png");
        }

        internal static string BuildPreviewPngPath(string pSaveDirectory,
            long pKingdomId, int pResolution, int pNodeIndex, long pEventId,
            int pFontIndex = 0)
        {
            return Path.Combine(GetOutputDirectory(pSaveDirectory),
                KingdomAtlasRules.BuildPreviewCacheRelativePath(pKingdomId,
                    pResolution, pNodeIndex, pEventId, pFontIndex));
        }

        internal static bool TryLoadCachedPng(long pKingdomId,
            int pResolution, int pNodeIndex, long pEventId,
            out byte[] pPng)
        {
            pPng = null;
            if (!AW3SaveDirectoryRegistry.TryGet(out string saveDirectory))
                return false;
            try
            {
                string path = BuildPngPath(saveDirectory, pKingdomId,
                    pResolution, pNodeIndex, pEventId);
                if (!File.Exists(path)) return false;
                pPng = File.ReadAllBytes(path);
                return pPng.Length > 0;
            }
            catch
            {
                pPng = null;
                return false;
            }
        }

        internal static bool TryLoadCachedPreviewPng(long pKingdomId,
            int pResolution, int pNodeIndex, long pEventId,
            out byte[] pPng, int pFontIndex = 0)
        {
            pPng = null;
            if (!AW3SaveDirectoryRegistry.TryGet(out string saveDirectory))
                return false;
            try
            {
                string path = BuildPreviewPngPath(saveDirectory, pKingdomId,
                    pResolution, pNodeIndex, pEventId, pFontIndex);
                if (!File.Exists(path)) return false;
                pPng = File.ReadAllBytes(path);
                return pPng.Length > 0;
            }
            catch
            {
                pPng = null;
                return false;
            }
        }

        internal static void CachePreviewPng(long pKingdomId,
            int pResolution, int pNodeIndex, long pEventId,
            KingdomAtlasRaster pRaster, int pFontIndex = 0)
        {
            if (pRaster == null || !AW3SaveDirectoryRegistry.TryGet(
                    out string saveDirectory)) return;
            try
            {
                string path = BuildPreviewPngPath(saveDirectory, pKingdomId,
                    pResolution, pNodeIndex, pEventId, pFontIndex);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
                WriteAtomically(path, KingdomAtlasPngEncoder.Encode(pRaster));
            }
            catch { }
        }

        internal static HashSet<long> ReadCompletedEvents(string pPath,
            string pSaveDirectory, int pResolution)
        {
            var result = new HashSet<long>();
            try
            {
                if (!File.Exists(pPath)) return result;
                string[] lines = File.ReadAllLines(pPath);
                bool metadataMatches = false;
                for (int index = 0; index < lines.Length; index++)
                {
                    string line = lines[index]?.Trim() ?? "";
                    if (line.StartsWith("save_identity=", StringComparison.Ordinal))
                        metadataMatches = string.Equals(line.Substring(14),
                            Path.GetFullPath(pSaveDirectory),
                            StringComparison.OrdinalIgnoreCase);
                }
                if (!metadataMatches) return result;
                for (int index = 0; index < lines.Length; index++)
                {
                    string line = lines[index]?.Trim() ?? "";
                    if (line.StartsWith("completed_key=", StringComparison.Ordinal))
                    {
                        string key = line.Substring(14);
                        string[] parts = key.Split(':');
                        if (parts.Length == 3 && parts[1] ==
                            pResolution.ToString(CultureInfo.InvariantCulture) &&
                            parts[2] == GeometryVersion &&
                            long.TryParse(parts[0], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out long eventId))
                            result.Add(eventId);
                    }
                }
            }
            catch { }
            return result;
        }

        internal static void WriteManifest(string pPath, string pSaveDirectory,
            int pResolution, HashSet<long> pCompleted, int pTotal)
        {
            string temp = pPath + ".tmp." + Guid.NewGuid().ToString("N");
            var text = new StringBuilder(256 + pCompleted.Count * 32);
            text.AppendLine("format=aw3_kingdom_atlas_manifest_v2");
            text.AppendLine("save_identity=" + Path.GetFullPath(pSaveDirectory));
            text.AppendLine("resolution=" + pResolution.ToString(
                CultureInfo.InvariantCulture));
            text.AppendLine("geometry_version=" + GeometryVersion);
            text.AppendLine("total_nodes=" + pTotal.ToString(
                CultureInfo.InvariantCulture));
            text.AppendLine("completed_nodes=" + pCompleted.Count.ToString(
                CultureInfo.InvariantCulture));
            var ordered = new List<long>(pCompleted);
            ordered.Sort();
            for (int index = 0; index < ordered.Count; index++)
                text.AppendLine("completed_key=" +
                    KingdomAtlasRules.BuildGenerationKey(ordered[index],
                        pResolution, GeometryVersion));
            File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
            if (File.Exists(pPath)) File.Replace(temp, pPath, null);
            else File.Move(temp, pPath);
        }

        internal static void WriteAtomically(string pPath, byte[] pBytes)
        {
            string temp = pPath + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temp, pBytes);
            if (File.Exists(pPath)) File.Replace(temp, pPath, null);
            else File.Move(temp, pPath);
        }
    }
}
