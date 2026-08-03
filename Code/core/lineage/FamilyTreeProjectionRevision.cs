using System.Collections.Generic;
using System.Threading;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    public enum FamilyTreeProjectionChange
    {
        None = 0,
        RulerAccession = 1,
        RankOrMandate = 2,
        Era = 3,
        DynastyOrStateName = 4,
        Heir = 5,
        LifeStatus = 6,
        PosthumousTitle = 7,
        FamilyStructure = 8,
        IdentityOrTitle = 9
    }

    public static class FamilyTreeProjectionRevisionRules
    {
        public static bool ShouldAdvance(FamilyTreeProjectionChange change)
        {
            return change != FamilyTreeProjectionChange.None;
        }

        public static FamilyTreeProjectionChange ResolveArchiveChange(
            bool firstArchive, bool familyStructureChanged,
            bool lifeStatusChanged, bool identityOrTitleChanged)
        {
            if (firstArchive || familyStructureChanged)
                return FamilyTreeProjectionChange.FamilyStructure;
            if (lifeStatusChanged)
                return FamilyTreeProjectionChange.LifeStatus;
            return identityOrTitleChanged
                ? FamilyTreeProjectionChange.IdentityOrTitle
                : FamilyTreeProjectionChange.None;
        }

        public static FamilyTreeProjectionChange MergeArchiveChanges(
            FamilyTreeProjectionChange current,
            FamilyTreeProjectionChange incoming)
        {
            return ArchiveChangePriority(incoming) >
                   ArchiveChangePriority(current)
                ? incoming
                : current;
        }

        public static bool CanFinalizeFounderBoundary(
            bool allDescendantWritesAccepted)
        {
            return allDescendantWritesAccepted;
        }

        private static int ArchiveChangePriority(
            FamilyTreeProjectionChange change)
        {
            return change switch
            {
                FamilyTreeProjectionChange.FamilyStructure => 3,
                FamilyTreeProjectionChange.LifeStatus => 2,
                FamilyTreeProjectionChange.IdentityOrTitle => 1,
                _ => 0
            };
        }
    }

    public sealed class FamilyTreeProjectionPendingState
    {
        private sealed class Entry
        {
            public long LatestSequence;
            public readonly SortedDictionary<long,
                FamilyTreeProjectionChange> Sources =
                new SortedDictionary<long, FamilyTreeProjectionChange>();
        }

        private readonly object _gate = new object();
        private readonly Dictionary<long, Entry> _entries =
            new Dictionary<long, Entry>();

        public void IncludePrerequisite(long actorId,
            FamilyTreeProjectionChange change)
        {
            if (!FamilyTreeProjectionRevisionRules.ShouldAdvance(change))
                return;
            lock (_gate)
            {
                if (_entries.TryGetValue(actorId, out Entry existing))
                {
                    MergeSource(existing, 0L, change);
                    return;
                }

                var entry = new Entry();
                entry.Sources[0L] = change;
                _entries[actorId] = entry;
            }
        }

        public void Publish(long actorId, long sequence,
            FamilyTreeProjectionChange change)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(actorId, out Entry existing))
                {
                    MergeSource(existing, sequence, change);
                    if (sequence > existing.LatestSequence)
                        existing.LatestSequence = sequence;
                    return;
                }

                var entry = new Entry { LatestSequence = sequence };
                entry.Sources[sequence] = change;
                _entries[actorId] = entry;
            }
        }

        public bool PublishDeferred(long actorId, long sequence,
            bool writeAccepted)
        {
            if (!writeAccepted || sequence <= 0L) return false;
            lock (_gate)
            {
                if (_entries.TryGetValue(actorId, out Entry existing))
                {
                    MergeSource(existing, sequence,
                        FamilyTreeProjectionChange.None);
                    if (sequence > existing.LatestSequence)
                        existing.LatestSequence = sequence;
                    return true;
                }

                var entry = new Entry { LatestSequence = sequence };
                entry.Sources[sequence] =
                    FamilyTreeProjectionChange.None;
                _entries[actorId] = entry;
            }
            return true;
        }

        public bool TryComplete(long actorId, long sequence,
            out FamilyTreeProjectionChange change)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(actorId, out Entry existing) ||
                    sequence <= 0L ||
                    !existing.Sources.ContainsKey(sequence))
                {
                    change = FamilyTreeProjectionChange.None;
                    return false;
                }

                change = ConsumeThrough(existing, sequence);
                if (existing.Sources.Count == 0)
                    _entries.Remove(actorId);
                return true;
            }
        }

        public bool Fail(long actorId, long sequence)
        {
            lock (_gate)
            {
                if (!_entries.TryGetValue(actorId, out Entry existing) ||
                    sequence <= 0L ||
                    !existing.Sources.ContainsKey(sequence))
                    return false;

                ConsumeThrough(existing, sequence);
                if (existing.Sources.Count == 0)
                    _entries.Remove(actorId);
                return true;
            }
        }

        public FamilyTreeProjectionChange FinalizeSynchronous(long actorId,
            FamilyTreeProjectionChange finalChange,
            bool finalWriteSucceeded)
        {
            if (!finalWriteSucceeded)
                return FamilyTreeProjectionChange.None;
            lock (_gate)
            {
                if (!_entries.TryGetValue(actorId, out Entry existing))
                    return finalChange;
                _entries.Remove(actorId);
                return FamilyTreeProjectionRevisionRules.MergeArchiveChanges(
                    MergeAll(existing), finalChange);
            }
        }

        private static void MergeSource(Entry entry, long sequence,
            FamilyTreeProjectionChange change)
        {
            if (entry.Sources.TryGetValue(sequence,
                    out FamilyTreeProjectionChange existing))
                entry.Sources[sequence] = FamilyTreeProjectionRevisionRules.
                    MergeArchiveChanges(existing, change);
            else
                entry.Sources[sequence] = change;
        }

        private static FamilyTreeProjectionChange ConsumeThrough(
            Entry entry, long sequence)
        {
            var consumed = new List<long>();
            FamilyTreeProjectionChange change =
                FamilyTreeProjectionChange.None;
            foreach (KeyValuePair<long, FamilyTreeProjectionChange> source
                     in entry.Sources)
            {
                if (source.Key > sequence) break;
                consumed.Add(source.Key);
                change = FamilyTreeProjectionRevisionRules.
                    MergeArchiveChanges(change, source.Value);
            }
            for (int index = 0; index < consumed.Count; index++)
                entry.Sources.Remove(consumed[index]);
            return change;
        }

        private static FamilyTreeProjectionChange MergeAll(Entry entry)
        {
            FamilyTreeProjectionChange change =
                FamilyTreeProjectionChange.None;
            foreach (FamilyTreeProjectionChange source in
                     entry.Sources.Values)
                change = FamilyTreeProjectionRevisionRules.
                    MergeArchiveChanges(change, source);
            return change;
        }

        public void Discard(long actorId)
        {
            lock (_gate) _entries.Remove(actorId);
        }

        public void Clear()
        {
            lock (_gate) _entries.Clear();
        }
    }

    internal static class FamilyTreeProjectionPendingStore
    {
        private static readonly FamilyTreeProjectionPendingState State =
            new FamilyTreeProjectionPendingState();

        public static void Publish(long actorId, long sequence,
            FamilyTreeProjectionChange change)
        {
            State.Publish(actorId, sequence, change);
        }

        public static void IncludePrerequisite(long actorId,
            FamilyTreeProjectionChange change)
        {
            State.IncludePrerequisite(actorId, change);
        }

        public static bool PublishDeferred(long actorId, long sequence,
            bool writeAccepted)
        {
            return State.PublishDeferred(actorId, sequence, writeAccepted);
        }

        public static bool TryComplete(long actorId, long sequence,
            out FamilyTreeProjectionChange change)
        {
            return State.TryComplete(actorId, sequence, out change);
        }

        public static bool Fail(long actorId, long sequence)
        {
            return State.Fail(actorId, sequence);
        }

        public static FamilyTreeProjectionChange FinalizeSynchronous(
            long actorId, FamilyTreeProjectionChange finalChange,
            bool finalWriteSucceeded)
        {
            return State.FinalizeSynchronous(actorId, finalChange,
                finalWriteSucceeded);
        }

        public static void Discard(long actorId)
        {
            State.Discard(actorId);
        }

        public static void Clear()
        {
            State.Clear();
        }
    }

    internal static class FamilyTreeProjectionRevision
    {
        private static long _revision = 1L;

        public static long Current => Interlocked.Read(ref _revision);

        public static long Advance(FamilyTreeProjectionChange change)
        {
            if (!FamilyTreeProjectionRevisionRules.ShouldAdvance(change))
                return Current;

            HistoricalContentRevision.Advance();
            while (true)
            {
                long current = Interlocked.Read(ref _revision);
                if (current == long.MaxValue) return current;
                long next = current + 1L;
                if (Interlocked.CompareExchange(ref _revision, next,
                        current) == current)
                    return next;
            }
        }
    }
}
