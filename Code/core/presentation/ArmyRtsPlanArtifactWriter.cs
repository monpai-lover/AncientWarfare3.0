using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace AncientWarfare3.core.presentation
{
    public sealed class ArmyRtsPlanArtifactWriter : IDisposable
    {
        private readonly object _gate = new object();
        private readonly ArmyRtsPlanSequenceLedger _ledger;
        private readonly Action<Exception> _fault;
        private readonly string _sessionId;
        private bool _disposed;
        private string _saveDirectory;
        private long _worldGeneration;

        public ArmyRtsPlanArtifactWriter(string pStagingDirectory,
            bool pStartWorker = true, int pCapacity = 8,
            Action<Exception> pFault = null,
            int pMaximumFramesPerSequence =
                ArmyRtsPlanRules.DefaultMaximumFramesPerSequence,
            int pMaximumGlobalFrames =
                ArmyRtsPlanRules.DefaultMaximumGlobalFrames,
            int pMaximumSequences =
                ArmyRtsPlanRules.DefaultMaximumSequences,
            string pSessionId = null)
        {
            if (string.IsNullOrWhiteSpace(pStagingDirectory))
                throw new ArgumentException(
                    "Runtime directory is required.",
                    nameof(pStagingDirectory));
            _fault = pFault;
            _ledger = new ArmyRtsPlanSequenceLedger(
                pMaximumFramesPerSequence, pMaximumGlobalFrames,
                pMaximumSequences);
            _sessionId = string.IsNullOrWhiteSpace(pSessionId)
                ? DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff",
                    CultureInfo.InvariantCulture) + "_" +
                  System.Diagnostics.Process.GetCurrentProcess().Id
                : pSessionId;
        }

        public int PendingCount
        {
            get { lock (_gate) return _ledger.FrameCount; }
        }

        public long WorldGeneration
        {
            get { lock (_gate) return _worldGeneration; }
        }

        public bool TryEnqueue(ArmyRtsPlanArtifact pArtifact)
        {
            if (pArtifact?.Snapshot == null) return false;
            ArmyRtsPlanIndexedRaster raster = ArmyRtsPlanRasterizer.Render(
                pArtifact.Snapshot);
            ulong fingerprint = pArtifact.Fingerprint != 0UL
                ? pArtifact.Fingerprint
                : ArmyRtsPlanRules.Fingerprint(pArtifact.Snapshot);
            var frame = new ArmyRtsPlanGifFrame(fingerprint,
                pArtifact.Revision, pArtifact.Snapshot.WorldYear,
                pArtifact.Snapshot.Reason,
                ArmyRtsPlanRules.Summarize(pArtifact.Snapshot), raster);
            lock (_gate)
            {
                if (_disposed ||
                    pArtifact.WorldGeneration != _worldGeneration)
                    return false;
                return _ledger.TryAdd(_worldGeneration,
                    pArtifact.Snapshot.WarId, _saveDirectory, frame);
            }
        }

        public void ObserveSaveDirectory(string pSaveDirectory)
        {
            if (string.IsNullOrWhiteSpace(pSaveDirectory)) return;
            string save = Path.GetFullPath(pSaveDirectory);
            lock (_gate)
            {
                if (_disposed) return;
                _saveDirectory = save;
                _ledger.AssociateSaveDirectory(_worldGeneration, save);
            }
        }

        public void PublishToSave(string pSaveDirectory)
        {
            ObserveSaveDirectory(pSaveDirectory);
        }

        public void CloseWar(long pWarId)
        {
            lock (_gate)
                _ledger.CloseWar(_worldGeneration, pWarId);
        }

        public void ResetWorld()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _ledger.CloseWorld(_worldGeneration);
                if (_worldGeneration < long.MaxValue) _worldGeneration++;
                _saveDirectory = null;
            }
        }

        public void ClearSaveDirectory()
        {
            lock (_gate) _saveDirectory = null;
        }

        public void DiscardPending()
        {
            lock (_gate) _ledger.Clear();
        }

        public bool WaitForIdle(TimeSpan pTimeout)
        {
            return true;
        }

        public void Shutdown(TimeSpan pTimeout)
        {
            Shutdown(pTimeout, null);
        }

        public void Shutdown(TimeSpan pTimeout,
            Func<bool> pCancellationRequested)
        {
            ArmyRtsPlanSequence[] sequences;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                sequences = new ArmyRtsPlanSequence[
                    _ledger.Sequences.Count];
                for (int i = 0; i < sequences.Length; i++)
                    sequences[i] = _ledger.Sequences[i];
            }
            long deadline = Deadline(pTimeout);
            for (int i = 0; i < sequences.Length; i++)
            {
                if (IsCancelled(deadline, pCancellationRequested)) break;
                Func<bool> cancel = () => IsCancelled(deadline,
                    pCancellationRequested);
                try { WriteSequence(sequences[i], cancel); }
                catch (OperationCanceledException) { break; }
                catch (Exception error) { ReportFault(error); }
            }
            lock (_gate) _ledger.Clear();
        }

        public void Dispose()
        {
            Shutdown(TimeSpan.FromSeconds(5));
        }

        private void WriteSequence(ArmyRtsPlanSequence pSequence,
            Func<bool> pCancellationRequested)
        {
            if (pSequence.Frames.Count == 0 ||
                string.IsNullOrWhiteSpace(pSequence.SaveDirectory)) return;
            ThrowIfCancelled(pCancellationRequested);
            byte[] gif = ArmyRtsPlanGifEncoder.Encode(pSequence.Frames,
                ArmyRtsPlanRules.DefaultFrameDelayCentiseconds,
                pCancellationRequested);
            byte[] manifest = new UTF8Encoding(false).GetBytes(
                BuildManifest(pSequence, pCancellationRequested));
            ThrowIfCancelled(pCancellationRequested);
            string directory = ArmyRtsPlanRules.ResolveOutputDirectory(
                pSequence.SaveDirectory);
            Directory.CreateDirectory(directory);
            int firstYear = pSequence.Frames[0].WorldYear;
            string stem = ArmyRtsPlanRules.SequenceFileStem(pSequence.WarId,
                firstYear, pSequence.WorldGeneration, _sessionId);
            string basePath = ResolveUniqueBasePath(directory, stem);
            WritePairAtomically(basePath, gif, manifest,
                pCancellationRequested);
        }

        private static string BuildManifest(ArmyRtsPlanSequence pSequence,
            Func<bool> pCancellationRequested)
        {
            var text = new StringBuilder(256 +
                pSequence.Frames.Count * 128);
            text.AppendLine("format=aw3_rts_plan_gif_v2");
            text.AppendLine("war_id=" + pSequence.WarId);
            text.AppendLine("world_generation=" +
                            pSequence.WorldGeneration);
            text.AppendLine("frames=" + pSequence.Frames.Count);
            ArmyRtsPlanFrameSummary first =
                pSequence.Frames[0].Summary;
            text.AppendLine("map=" + first.WorldWidth + "x" +
                            first.WorldHeight);
            text.AppendLine("first_year=" +
                            pSequence.Frames[0].WorldYear);
            text.AppendLine("last_year=" +
                            pSequence.Frames[pSequence.Frames.Count - 1].
                                WorldYear);
            for (int i = 0; i < pSequence.Frames.Count; i++)
            {
                ThrowIfCancelled(pCancellationRequested);
                ArmyRtsPlanGifFrame frame = pSequence.Frames[i];
                ArmyRtsPlanFrameSummary summary = frame.Summary;
                text.AppendLine("frame=" + i + " revision=" +
                                frame.Revision + " year=" +
                                frame.WorldYear + " fingerprint=" +
                                frame.Fingerprint.ToString("x16",
                                    CultureInfo.InvariantCulture) +
                                " reason=" + Sanitize(frame.Reason) +
                                " kingdoms=" + summary.KingdomCount +
                                " cities=" + summary.CityCount +
                                " armies=" + summary.ArmyCount +
                                " fronts=" + summary.FrontCount +
                                " proposal_mask=0x" +
                                summary.ProposalKindMask.ToString("x",
                                    CultureInfo.InvariantCulture) +
                                " role_mask=0x" +
                                summary.RoleMask.ToString("x",
                                    CultureInfo.InvariantCulture) +
                                " posture_mask=0x" +
                                summary.PostureMask.ToString("x",
                                    CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private static string ResolveUniqueBasePath(string pDirectory,
            string pStem)
        {
            string candidate = Path.Combine(pDirectory, pStem);
            int suffix = 0;
            while (File.Exists(candidate + ".gif") ||
                   File.Exists(candidate + ".txt"))
                candidate = Path.Combine(pDirectory, pStem + "_" +
                    (++suffix).ToString(CultureInfo.InvariantCulture));
            return candidate;
        }

        private static void WritePairAtomically(string pBasePath,
            byte[] pGif, byte[] pManifest,
            Func<bool> pCancellationRequested)
        {
            string nonce = Guid.NewGuid().ToString("N");
            string gifPath = pBasePath + ".gif";
            string manifestPath = pBasePath + ".txt";
            string temporaryGif = gifPath + ".tmp." + nonce;
            string temporaryManifest = manifestPath + ".tmp." + nonce;
            bool gifPublished = false;
            bool manifestPublished = false;
            try
            {
                ThrowIfCancelled(pCancellationRequested);
                WriteTemporary(temporaryGif, pGif,
                    pCancellationRequested);
                WriteTemporary(temporaryManifest, pManifest,
                    pCancellationRequested);
                ThrowIfCancelled(pCancellationRequested);
                File.Move(temporaryGif, gifPath);
                gifPublished = true;
                ThrowIfCancelled(pCancellationRequested);
                File.Move(temporaryManifest, manifestPath);
                manifestPublished = true;
            }
            catch
            {
                if (manifestPublished && File.Exists(manifestPath))
                    File.Delete(manifestPath);
                if (gifPublished && File.Exists(gifPath))
                    File.Delete(gifPath);
                throw;
            }
            finally
            {
                if (File.Exists(temporaryGif)) File.Delete(temporaryGif);
                if (File.Exists(temporaryManifest))
                    File.Delete(temporaryManifest);
            }
        }

        private static void WriteTemporary(string pPath, byte[] pBytes,
            Func<bool> pCancellationRequested)
        {
            using var stream = new FileStream(pPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            int offset = 0;
            while (offset < pBytes.Length)
            {
                ThrowIfCancelled(pCancellationRequested);
                int count = Math.Min(64 * 1024, pBytes.Length - offset);
                stream.Write(pBytes, offset, count);
                offset += count;
            }
            ThrowIfCancelled(pCancellationRequested);
            stream.Flush();
        }

        private static long Deadline(TimeSpan pTimeout)
        {
            long now = Stopwatch.GetTimestamp();
            if (pTimeout <= TimeSpan.Zero) return now;
            double budget = pTimeout.TotalSeconds * Stopwatch.Frequency;
            if (budget >= long.MaxValue - now) return long.MaxValue;
            return now + (long)Math.Ceiling(budget);
        }

        private static bool IsCancelled(long pDeadline,
            Func<bool> pCancellationRequested)
        {
            return pCancellationRequested != null &&
                       pCancellationRequested() ||
                   Stopwatch.GetTimestamp() >= pDeadline;
        }

        private static void ThrowIfCancelled(
            Func<bool> pCancellationRequested)
        {
            if (pCancellationRequested != null &&
                pCancellationRequested())
                throw new OperationCanceledException(
                    "RTS plan artifact writing exceeded its shutdown budget.");
        }

        private static string Sanitize(string pValue)
        {
            return (pValue ?? string.Empty).Replace('\r', ' ')
                .Replace('\n', ' ');
        }

        private void ReportFault(Exception pError)
        {
            try { _fault?.Invoke(pError); }
            catch { }
        }
    }
}
