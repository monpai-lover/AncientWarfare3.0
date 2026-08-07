using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.presentation;

namespace AncientWarfare3.core.atlas
{
    /// <summary>
    /// Resumable node-at-a-time atlas generation.  The UI advances this
    /// session from a coroutine so each completed node can be painted and
    /// the window can remain responsive.
    /// </summary>
    internal sealed class KingdomAtlasGenerationSession
    {
        private readonly long _kingdomId;
        private readonly int _resolution;
        private readonly bool _gif;
        private readonly string _saveDirectory;
        private readonly string _outputDirectory;
        private readonly string _manifestPath;
        private readonly List<KingdomAtlasNode> _nodes;
        private readonly HashSet<long> _completed;
        private readonly ArmyRtsPlanWorldTerrainSnapshot _terrain;
        private readonly List<KingdomAtlasRaster> _gifFrames =
            new List<KingdomAtlasRaster>();
        private readonly KingdomAtlasGenerationResult _result;
        private int _index;
        private bool _finished;

        private KingdomAtlasGenerationSession(
            KingdomAtlasGenerationResult pResult)
        {
            _result = pResult;
            _nodes = new List<KingdomAtlasNode>();
            _completed = new HashSet<long>();
            _finished = true;
        }

        private KingdomAtlasGenerationSession(long pKingdomId,
            int pResolution, bool pGif, string pSaveDirectory,
            string pOutputDirectory, string pManifestPath,
            List<KingdomAtlasNode> pNodes, HashSet<long> pCompleted,
            ArmyRtsPlanWorldTerrainSnapshot pTerrain)
        {
            _kingdomId = pKingdomId;
            _resolution = pResolution;
            _gif = pGif;
            _saveDirectory = pSaveDirectory;
            _outputDirectory = pOutputDirectory;
            _manifestPath = pManifestPath;
            _nodes = pNodes ?? new List<KingdomAtlasNode>();
            _completed = pCompleted ?? new HashSet<long>();
            _terrain = pTerrain;
            _result = new KingdomAtlasGenerationResult
            {
                OutputDirectory = _outputDirectory
            };
        }

        internal KingdomAtlasGenerationResult Result => _result;

        internal bool IsComplete => _finished;

        internal static KingdomAtlasGenerationSession Create(long pKingdomId,
            int pResolution, bool pGif,
            ArmyRtsPlanWorldTerrainSnapshot pTerrain = null)
        {
            var invalid = new KingdomAtlasGenerationResult();
            if (!KingdomAtlasRules.IsReliableResolution(pResolution,
                    pResolution))
            {
                invalid.Error = "Unsupported atlas resolution.";
                return new KingdomAtlasGenerationSession(invalid);
            }
            if (!AW3SaveDirectoryRegistry.TryGet(out string saveDirectory))
            {
                invalid.Error = "Save the world before generating a kingdom atlas.";
                return new KingdomAtlasGenerationSession(invalid);
            }

            List<KingdomAtlasNode> nodes;
            try
            {
                nodes = KingdomAtlasHistoryService.BuildNodes(pKingdomId);
            }
            catch (Exception error)
            {
                invalid.Error = error.Message;
                return new KingdomAtlasGenerationSession(invalid);
            }
            if (!KingdomAtlasHistoryService.HasReliableColours(nodes))
            {
                invalid.Error = "Historical kingdom colours are incomplete; atlas generation was refused.";
                return new KingdomAtlasGenerationSession(invalid);
            }

            ArmyRtsPlanWorldTerrainSnapshot terrain = pTerrain;
            try
            {
                if (terrain == null)
                    terrain = KingdomAtlasLiveTerrainService.Capture(
                        Math.Max(768, pResolution));
                for (int index = 0; index < nodes.Count; index++)
                    KingdomAtlasLiveTerrainService.AttachNodeGeometry(
                        nodes[index], terrain);
            }
            catch (Exception error)
            {
                invalid.Error = error.Message;
                return new KingdomAtlasGenerationSession(invalid);
            }

            try
            {
                string output = KingdomAtlasArtifactWriter.GetOutputDirectory(
                    saveDirectory);
                Directory.CreateDirectory(output);
                string manifestPath = Path.Combine(output, "kingdom_" +
                    pKingdomId.ToString(CultureInfo.InvariantCulture) + "_" +
                    pResolution.ToString(CultureInfo.InvariantCulture) + "_" +
                    KingdomAtlasArtifactWriter.GeometryVersion + ".manifest");
                HashSet<long> completed =
                    KingdomAtlasArtifactWriter.ReadCompletedEvents(
                        manifestPath, saveDirectory, pResolution);
                return new KingdomAtlasGenerationSession(pKingdomId,
                    pResolution, pGif, saveDirectory, output, manifestPath,
                    nodes, completed, terrain);
            }
            catch (Exception error)
            {
                invalid.Error = error.Message;
                return new KingdomAtlasGenerationSession(invalid);
            }
        }

        internal bool MoveNext(Func<bool> pCancellationRequested,
            out KingdomAtlasProgress pProgress)
        {
            pProgress = default(KingdomAtlasProgress);
            if (_finished) return false;
            if (pCancellationRequested != null && pCancellationRequested())
            {
                _result.Error = "Atlas generation cancelled.";
                _finished = true;
                return false;
            }
            if (_index >= _nodes.Count)
            {
                FinishGif();
                _result.OutputDirectory = _outputDirectory;
                _finished = true;
                return false;
            }

            KingdomAtlasNode node = _nodes[_index];
            string pngPath = KingdomAtlasArtifactWriter.BuildPngPath(
                _saveDirectory, _kingdomId, _resolution, _index,
                node.Event.EventId);
            bool hasOutput = File.Exists(pngPath);
            if (_completed.Contains(node.Event.EventId) && hasOutput)
            {
                _result.NodesSkipped++;
                if (_gif)
                    _gifFrames.Add(RenderForExport(node));
                pProgress = new KingdomAtlasProgress(_index + 1,
                    _nodes.Count, "skipped", node.Event.EventId);
                _index++;
                return true;
            }

            KingdomAtlasRaster raster = RenderForExport(node);
            KingdomAtlasArtifactWriter.WriteAtomically(pngPath,
                KingdomAtlasPngEncoder.Encode(raster));
            if (_gif) _gifFrames.Add(raster);
            _completed.Add(node.Event.EventId);
            KingdomAtlasArtifactWriter.WriteManifest(_manifestPath,
                _saveDirectory, _resolution, _completed, _nodes.Count);
            _result.NodesGenerated++;
            pProgress = new KingdomAtlasProgress(_index + 1, _nodes.Count,
                "png", node.Event.EventId);
            _index++;
            return true;
        }

        private KingdomAtlasRaster RenderForExport(KingdomAtlasNode pNode)
        {
            KingdomAtlasRaster raster = KingdomAtlasLiveTerrainService.Render(
                pNode, _resolution, _terrain);
            Func<KingdomAtlasNode, KingdomAtlasRaster, KingdomAtlasRaster>
                labelRenderer = KingdomAtlasRasterizer.ExternalLabelRenderer;
            if (labelRenderer == null) return raster;
            try
            {
                return labelRenderer(pNode, raster) ?? raster;
            }
            catch
            {
                return raster;
            }
        }

        internal void Cancel()
        {
            if (_finished) return;
            _result.Error = "Atlas generation cancelled.";
            _finished = true;
        }

        private void FinishGif()
        {
            if (!_gif || _gifFrames.Count == 0) return;
            byte[] gif = KingdomAtlasGifEncoder.Encode(_gifFrames);
            string gifPath = Path.Combine(
                _outputDirectory, "kingdom_" +
                _kingdomId.ToString(CultureInfo.InvariantCulture) + "_" +
                _resolution.ToString(CultureInfo.InvariantCulture) + ".gif");
            KingdomAtlasArtifactWriter.WriteAtomically(gifPath, gif);
            _result.GifPath = gifPath;
        }
    }
}
