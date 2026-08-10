using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Threading;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class LineageBirthArchiveService
    {
        private static long _nextFailureLogTimestamp;

        internal static LineageBirthArchiveResult TryRecord(Actor pChild,
            Actor pParent1, Actor pParent2)
        {
            long childId = pChild?.data?.id ?? -1L;
            long parent1Id = pParent1?.data?.id ?? -1L;
            long parent2Id = pParent2?.data?.id ?? -1L;
            if (pChild?.data == null)
                return new LineageBirthArchiveResult(
                    LineageBirthArchiveStatus.NotEligible, childId,
                    parent1Id, parent2Id, "child is unavailable");

            try
            {
                var parents = FamilyTreeRelationRules.MergeParentSlots(
                    pChild.data.parent_id_1, pChild.data.parent_id_2,
                    parent1Id, parent2Id);
                parent1Id = parents.slot1;
                parent2Id = parents.slot2;
                pChild.data.parent_id_1 = parent1Id;
                pChild.data.parent_id_2 = parent2Id;

                if (LineageArchiveManager.Instance.OperatingDB == null ||
                    !LineageArchiveManager.Instance.InitializeSuccessful)
                    return Failed(childId, parent1Id, parent2Id,
                        "lineage archive is unavailable");

                ActorArchiveTableItem snapshot = LineageArchiveWriter.
                    CaptureRelationshipSnapshot(pChild, pAlive: true);
                if (snapshot == null)
                    return new LineageBirthArchiveResult(
                        LineageBirthArchiveStatus.NotEligible, childId,
                        parent1Id, parent2Id,
                        "child snapshot is not eligible");

                var write = new LineageBirthArchiveWrite(snapshot,
                    parent1Id, parent2Id, LineageService.CurTime());
                string operationKey = LineageBirthArchiveEnvelope.
                    BuildOperationKey(childId);
                if (HistoricalWriteService.TryEnqueueCustom(operationKey,
                        (sequence, stamp) =>
                            new LineageBirthArchiveEnvelope(sequence, stamp,
                                write),
                        (sequence, replacedSequence) =>
                        {
                            ActorArchivePendingStore.Publish(childId,
                                sequence, snapshot);
                            FamilyTreeProjectionPendingStore.
                                TransferOwnership(childId, replacedSequence,
                                    sequence);
                            FamilyTreeProjectionPendingStore.Publish(childId,
                                sequence,
                                FamilyTreeProjectionChange.FamilyStructure);
                        },
                        (sequence, outcome) => OnCommitted(childId,
                            parent1Id, parent2Id, sequence, outcome),
                        (sequence, error) => OnFailed(childId, parent1Id,
                            parent2Id, sequence, error),
                        out long queuedSequence, out _))
                {
                    ActorArchivePresenceIndex.Mark(childId);
                    return new LineageBirthArchiveResult(
                        LineageBirthArchiveStatus.Queued, childId,
                        parent1Id, parent2Id, string.Empty);
                }

                return WriteSynchronously(write);
            }
            catch (Exception error)
            {
                return Failed(childId, parent1Id, parent2Id,
                    error.Message);
            }
        }

        private static void OnCommitted(long pChildId, long pParent1Id,
            long pParent2Id, long pSequence, object pOutcome)
        {
            ActorArchivePendingStore.Complete(pChildId, pSequence);
            if (!(pOutcome is LineageBirthArchiveOutcome outcome) ||
                outcome.ChildId != pChildId)
            {
                FamilyTreeProjectionPendingStore.TryComplete(pChildId,
                    pSequence, out _);
                Failed(pChildId, pParent1Id, pParent2Id,
                    "async completion returned an invalid outcome");
                return;
            }

            if (FamilyTreeProjectionPendingStore.TryComplete(pChildId,
                    pSequence,
                    out FamilyTreeProjectionChange committedChange))
                FamilyTreeProjectionRevision.Advance(committedChange);
        }

        private static void OnFailed(long pChildId, long pParent1Id,
            long pParent2Id, long pSequence, string pError)
        {
            ActorArchivePendingStore.Complete(pChildId, pSequence);
            FamilyTreeProjectionPendingStore.Fail(pChildId, pSequence);
            Failed(pChildId, pParent1Id, pParent2Id, pError);
        }

        private static LineageBirthArchiveResult WriteSynchronously(
            LineageBirthArchiveWrite pWrite)
        {
            long childId = pWrite.Child.id;
            long parent1Id = pWrite.ParentSlot1;
            long parent2Id = pWrite.ParentSlot2;
            if (!HistoricalWriteService.FlushForSynchronousFallback(
                    TimeSpan.FromSeconds(5), out string flushError))
                return Failed(childId, parent1Id, parent2Id,
                    "ordering barrier failed: " + flushError);

            SQLiteConnection db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful)
                return Failed(childId, parent1Id, parent2Id,
                    "lineage archive is unavailable");

            using SQLiteTransaction transaction = db.BeginTransaction();
            try
            {
                LineageBirthArchivePersistence.Execute(db, transaction,
                    pWrite);
                transaction.Commit();
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); }
                catch { }
                return Failed(childId, parent1Id, parent2Id,
                    error.Message);
            }

            FamilyTreeProjectionChange committedChange =
                FamilyTreeProjectionPendingStore.FinalizeSynchronous(
                    childId, FamilyTreeProjectionChange.FamilyStructure,
                    finalWriteSucceeded: true);
            FamilyTreeProjectionRevision.Advance(committedChange);
            ActorArchivePresenceIndex.Mark(childId);
            return new LineageBirthArchiveResult(
                LineageBirthArchiveStatus.Committed, childId, parent1Id,
                parent2Id, string.Empty);
        }

        private static LineageBirthArchiveResult Failed(long pChildId,
            long pParent1Id, long pParent2Id, string pReason)
        {
            var result = new LineageBirthArchiveResult(
                LineageBirthArchiveStatus.Failed, pChildId, pParent1Id,
                pParent2Id, string.IsNullOrWhiteSpace(pReason)
                    ? "unknown error"
                    : pReason);
            if (result.Status == LineageBirthArchiveStatus.Failed)
                LogFailedRateLimited(result);
            return result;
        }

        private static void LogFailedRateLimited(
            LineageBirthArchiveResult pResult)
        {
            long now = Stopwatch.GetTimestamp();
            long observed = Volatile.Read(ref _nextFailureLogTimestamp);
            if (now < observed) return;
            long interval = Math.Max(1L, Stopwatch.Frequency * 10L);
            if (Interlocked.CompareExchange(ref _nextFailureLogTimestamp,
                    now + interval, observed) != observed) return;
            ModClass.LogWarning("Lineage birth archive failed: child=" +
                                pResult.ChildId + " parent1=" +
                                pResult.Parent1Id + " parent2=" +
                                pResult.Parent2Id + " reason=" +
                                pResult.Reason);
        }
    }
}
