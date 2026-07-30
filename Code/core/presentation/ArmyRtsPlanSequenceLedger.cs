using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.presentation
{
    public sealed class ArmyRtsPlanSequence
    {
        private readonly List<ArmyRtsPlanGifFrame> _frames =
            new List<ArmyRtsPlanGifFrame>();

        internal ArmyRtsPlanSequence(long pWorldGeneration, long pWarId,
            string pSaveDirectory, long pOrdinal)
        {
            WorldGeneration = pWorldGeneration;
            WarId = pWarId;
            SaveDirectory = pSaveDirectory ?? string.Empty;
            Ordinal = pOrdinal;
        }

        public long WorldGeneration { get; }
        public long WarId { get; }
        public string SaveDirectory { get; internal set; }
        public bool Closed { get; internal set; }
        public IReadOnlyList<ArmyRtsPlanGifFrame> Frames => _frames;
        internal long Ordinal { get; }
        internal List<ArmyRtsPlanGifFrame> MutableFrames => _frames;
    }

    public sealed class ArmyRtsPlanSequenceLedger
    {
        private readonly int _maximumFramesPerSequence;
        private readonly int _maximumGlobalFrames;
        private readonly int _maximumSequences;
        private readonly List<ArmyRtsPlanSequence> _sequences =
            new List<ArmyRtsPlanSequence>();
        private long _nextOrdinal;

        public ArmyRtsPlanSequenceLedger(
            int pMaximumFramesPerSequence =
                ArmyRtsPlanRules.DefaultMaximumFramesPerSequence,
            int pMaximumGlobalFrames =
                ArmyRtsPlanRules.DefaultMaximumGlobalFrames,
            int pMaximumSequences =
                ArmyRtsPlanRules.DefaultMaximumSequences)
        {
            _maximumFramesPerSequence = Math.Max(2,
                pMaximumFramesPerSequence);
            _maximumGlobalFrames = Math.Max(2, pMaximumGlobalFrames);
            _maximumSequences = Math.Max(1, pMaximumSequences);
        }

        public IReadOnlyList<ArmyRtsPlanSequence> Sequences => _sequences;

        public int FrameCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _sequences.Count; i++)
                    count += _sequences[i].Frames.Count;
                return count;
            }
        }

        public bool TryAdd(long pWorldGeneration, long pWarId,
            string pSaveDirectory, ArmyRtsPlanGifFrame pFrame)
        {
            if (pWarId < 0L || pFrame == null) return false;
            ArmyRtsPlanSequence sequence = Find(pWorldGeneration, pWarId);
            if (sequence == null)
            {
                sequence = new ArmyRtsPlanSequence(pWorldGeneration,
                    pWarId, pSaveDirectory, _nextOrdinal++);
                _sequences.Add(sequence);
            }
            else if (!string.IsNullOrWhiteSpace(pSaveDirectory))
                sequence.SaveDirectory = pSaveDirectory;
            List<ArmyRtsPlanGifFrame> frames = sequence.MutableFrames;
            if (frames.Count > 0 &&
                frames[frames.Count - 1].Fingerprint == pFrame.Fingerprint)
                return false;
            if (frames.Count >= _maximumFramesPerSequence)
                Decimate(frames);
            frames.Add(pFrame);
            EnforceBounds(sequence);
            return ReferenceEquals(Find(pWorldGeneration, pWarId), sequence);
        }

        public void AssociateSaveDirectory(long pWorldGeneration,
            string pSaveDirectory)
        {
            if (string.IsNullOrWhiteSpace(pSaveDirectory)) return;
            for (int i = 0; i < _sequences.Count; i++)
                if (_sequences[i].WorldGeneration == pWorldGeneration)
                    _sequences[i].SaveDirectory = pSaveDirectory;
        }

        public void CloseWar(long pWorldGeneration, long pWarId)
        {
            ArmyRtsPlanSequence sequence = Find(pWorldGeneration, pWarId);
            if (sequence != null) sequence.Closed = true;
        }

        public void CloseWorld(long pWorldGeneration)
        {
            for (int i = 0; i < _sequences.Count; i++)
                if (_sequences[i].WorldGeneration == pWorldGeneration)
                    _sequences[i].Closed = true;
        }

        public void Clear()
        {
            _sequences.Clear();
        }

        private ArmyRtsPlanSequence Find(long pWorldGeneration,
            long pWarId)
        {
            for (int i = 0; i < _sequences.Count; i++)
                if (_sequences[i].WorldGeneration == pWorldGeneration &&
                    _sequences[i].WarId == pWarId)
                    return _sequences[i];
            return null;
        }

        private void EnforceBounds(ArmyRtsPlanSequence pIncoming)
        {
            while (_sequences.Count > _maximumSequences)
            {
                int victim = OldestSequenceIndex(pIncoming,
                    pClosedOnly: true);
                if (victim < 0)
                    victim = OldestSequenceIndex(pIncoming,
                        pClosedOnly: false);
                if (victim < 0) victim = 0;
                _sequences.RemoveAt(victim);
            }
            while (FrameCount > _maximumGlobalFrames)
            {
                int completed = OldestSequenceIndex(pIncoming,
                    pClosedOnly: true);
                if (completed >= 0)
                {
                    _sequences.RemoveAt(completed);
                    continue;
                }
                ArmyRtsPlanSequence victim = OldestWithInterior();
                if (victim != null)
                {
                    victim.MutableFrames.RemoveAt(1);
                    continue;
                }
                int index = OldestSequenceIndex(pIncoming,
                    pClosedOnly: false);
                if (index < 0)
                {
                    if (pIncoming.MutableFrames.Count > 1)
                        pIncoming.MutableFrames.RemoveAt(1);
                    else break;
                }
                else _sequences.RemoveAt(index);
            }
        }

        private ArmyRtsPlanSequence OldestWithInterior()
        {
            ArmyRtsPlanSequence selected = null;
            for (int i = 0; i < _sequences.Count; i++)
            {
                ArmyRtsPlanSequence candidate = _sequences[i];
                if (candidate.Frames.Count <= 2) continue;
                if (selected == null || candidate.Ordinal < selected.Ordinal)
                    selected = candidate;
            }
            return selected;
        }

        private int OldestSequenceIndex(ArmyRtsPlanSequence pIncoming,
            bool pClosedOnly)
        {
            int selected = -1;
            for (int i = 0; i < _sequences.Count; i++)
            {
                ArmyRtsPlanSequence candidate = _sequences[i];
                if (candidate == pIncoming ||
                    pClosedOnly && !candidate.Closed) continue;
                if (selected < 0 || candidate.Ordinal <
                    _sequences[selected].Ordinal) selected = i;
            }
            return selected;
        }

        private static void Decimate(List<ArmyRtsPlanGifFrame> pFrames)
        {
            if (pFrames.Count <= 2)
            {
                if (pFrames.Count == 2) pFrames.RemoveAt(1);
                return;
            }
            var retained = new List<ArmyRtsPlanGifFrame>(
                pFrames.Count / 2 + 2) { pFrames[0] };
            for (int i = 2; i < pFrames.Count - 1; i += 2)
                retained.Add(pFrames[i]);
            retained.Add(pFrames[pFrames.Count - 1]);
            pFrames.Clear();
            pFrames.AddRange(retained);
        }
    }
}
