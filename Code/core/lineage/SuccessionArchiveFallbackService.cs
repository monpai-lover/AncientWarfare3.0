using System.Collections.Generic;
using System.Data.SQLite;
using ai;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct SuccessionArchiveFallbackResult
    {
        internal SuccessionArchiveFallbackResult(
            SuccessionEvidenceStatus pStatus, Actor pCandidate,
            bool pScanInProgress = false)
        {
            Status = pStatus;
            Candidate = pCandidate;
            ScanInProgress = pScanInProgress;
        }

        internal SuccessionEvidenceStatus Status { get; }
        internal Actor Candidate { get; }
        internal bool ScanInProgress { get; }
    }

    internal readonly struct SuccessionArchiveIdentityResult
    {
        internal SuccessionArchiveIdentityResult(bool pEvidenceAvailable,
            bool pFound, long pLineageId, long pShiId, int pGeneration)
        {
            EvidenceAvailable = pEvidenceAvailable;
            Found = pFound;
            LineageId = pLineageId;
            ShiId = pShiId;
            Generation = pGeneration;
        }

        internal bool EvidenceAvailable { get; }
        internal bool Found { get; }
        internal long LineageId { get; }
        internal long ShiId { get; }
        internal int Generation { get; }
    }

    internal static class SuccessionArchiveFallbackService
    {
        private const int MaximumActorsPerPage = 32;
        private const int MaximumRetainedCandidates = 16;

        private readonly struct RankedCandidate
        {
            internal RankedCandidate(long pActorId, int pAttribute)
            {
                ActorId = pActorId;
                Attribute = pAttribute;
            }

            internal long ActorId { get; }
            internal int Attribute { get; }
        }

        private sealed class ScanState
        {
            internal long LineageId = -1L;
            internal int PredecessorGeneration;
            internal long Cursor = -1L;
            internal readonly List<RankedCandidate> CandidateIds =
                new List<RankedCandidate>(MaximumRetainedCandidates);
            internal bool RestartedAfterCandidateLoss;

            internal void RestartAfterCandidateLoss()
            {
                Cursor = -1L;
                CandidateIds.Clear();
                RestartedAfterCandidateLoss = true;
            }
        }

        private static readonly Dictionary<KingSuccessionKey, ScanState>
            Scans = new Dictionary<KingSuccessionKey, ScanState>();

        internal static SuccessionArchiveIdentityResult ResolveIdentity(
            long pActorId)
        {
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (pActorId < 0L || db == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful)
                return new SuccessionArchiveIdentityResult(false, false,
                    -1L, -1L, 0);
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT LINEAGE_ID,SHI_ID,GENERATION FROM " +
                    ActorArchiveTableItem.GetTableName() +
                    " WHERE ID=@actor LIMIT 1";
                command.Parameters.AddWithValue("@actor", pActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                    return new SuccessionArchiveIdentityResult(true, false,
                        -1L, -1L, 0);
                if (reader.IsDBNull(2))
                    return new SuccessionArchiveIdentityResult(true, false,
                        -1L, -1L, 0);
                return new SuccessionArchiveIdentityResult(true, true,
                    reader.IsDBNull(0) ? -1L : reader.GetInt64(0),
                    reader.IsDBNull(1) ? -1L : reader.GetInt64(1),
                    reader.GetInt32(2));
            }
            catch
            {
                return new SuccessionArchiveIdentityResult(false, false,
                    -1L, -1L, 0);
            }
        }

        internal static SuccessionArchiveFallbackResult Resolve(
            KingSuccessionKey pKey, long pLineageId,
            int pPredecessorGeneration)
        {
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful ||
                pLineageId < 0L)
                return Pending();

            if (!Scans.TryGetValue(pKey, out ScanState state) ||
                state.LineageId != pLineageId ||
                state.PredecessorGeneration != pPredecessorGeneration)
            {
                state = new ScanState
                {
                    LineageId = pLineageId,
                    PredecessorGeneration = pPredecessorGeneration
                };
                Scans[pKey] = state;
            }

            if (!TryReadPage(db, pKey, state, out int rowCount))
                return Pending();
            if (rowCount >= MaximumActorsPerPage)
                return Pending(pScanInProgress: true);

            bool hadRetainedCandidates = state.CandidateIds.Count > 0;
            Actor best = ResolveBestEligibleActor(state, pKey.PredecessorId,
                pLineageId);
            if (best?.data != null)
                return new SuccessionArchiveFallbackResult(
                    SuccessionEvidenceStatus.Found, best);
            if (hadRetainedCandidates &&
                !state.RestartedAfterCandidateLoss)
            {
                state.RestartAfterCandidateLoss();
                return Pending(pScanInProgress: true);
            }

            if (!TryHasLivingLineageMembers(db, pLineageId,
                    out bool hasLivingMembers))
                return Pending();
            SuccessionEvidenceStatus status = AuthoritativeSuccessionRules.
                ResolveEvidenceStatus(false, true, true, hasLivingMembers);
            return new SuccessionArchiveFallbackResult(status, null);
        }

        internal static void Complete(KingSuccessionKey pKey)
        {
            Scans.Remove(pKey);
        }

        internal static void Restart(KingSuccessionKey pKey)
        {
            Scans.Remove(pKey);
        }

        internal static void Reset()
        {
            Scans.Clear();
        }

        private static bool TryReadPage(SQLiteConnection pDb,
            KingSuccessionKey pKey, ScanState pState, out int pRowCount)
        {
            pRowCount = 0;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT ID FROM " +
                    ActorArchiveTableItem.GetTableName() +
                    " WHERE LINEAGE_ID=@lineage AND IS_ALIVE=1" +
                    " AND GENERATION BETWEEN @minGeneration AND @maxGeneration" +
                    " AND ID>@cursor ORDER BY ID LIMIT @limit";
                command.Parameters.AddWithValue("@lineage", pState.LineageId);
                command.Parameters.AddWithValue("@minGeneration",
                    pState.PredecessorGeneration - 2);
                command.Parameters.AddWithValue("@maxGeneration",
                    pState.PredecessorGeneration + 2);
                command.Parameters.AddWithValue("@cursor", pState.Cursor);
                command.Parameters.AddWithValue("@limit", MaximumActorsPerPage);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long actorId = reader.GetInt64(0);
                    pState.Cursor = actorId;
                    pRowCount++;
                    Actor actor = ResolveEligibleActor(actorId,
                        pKey.PredecessorId, pState.LineageId);
                    if (actor?.data == null) continue;
                    int attribute;
                    try { attribute = ActorTool.attributeDice(actor); }
                    catch { continue; }
                    AddCandidate(pState.CandidateIds, actorId, attribute);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AddCandidate(List<RankedCandidate> pCandidates,
            long pActorId, int pAttribute)
        {
            int index = 0;
            while (index < pCandidates.Count)
            {
                RankedCandidate existing = pCandidates[index];
                if (existing.ActorId == pActorId) return;
                if (pAttribute > existing.Attribute ||
                    pAttribute == existing.Attribute &&
                    pActorId < existing.ActorId) break;
                index++;
            }
            if (index >= MaximumRetainedCandidates) return;
            pCandidates.Insert(index, new RankedCandidate(pActorId,
                pAttribute));
            if (pCandidates.Count > MaximumRetainedCandidates)
                pCandidates.RemoveAt(pCandidates.Count - 1);
        }

        private static Actor ResolveBestEligibleActor(ScanState pState,
            long pPredecessorId, long pLineageId)
        {
            for (int i = 0; i < pState.CandidateIds.Count; i++)
            {
                Actor actor = ResolveEligibleActor(
                    pState.CandidateIds[i].ActorId, pPredecessorId,
                    pLineageId);
                if (actor?.data != null) return actor;
            }
            pState.CandidateIds.Clear();
            return null;
        }

        private static bool TryHasLivingLineageMembers(SQLiteConnection pDb,
            long pLineageId, out bool pHasLivingMembers)
        {
            pHasLivingMembers = false;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "SELECT 1 FROM " +
                    ActorArchiveTableItem.GetTableName() +
                    " WHERE LINEAGE_ID=@lineage AND IS_ALIVE=1 LIMIT 1";
                command.Parameters.AddWithValue("@lineage", pLineageId);
                pHasLivingMembers = command.ExecuteScalar() != null;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Actor ResolveEligibleActor(long pActorId,
            long pPredecessorId, long pLineageId)
        {
            if (pActorId < 0L || pActorId == pPredecessorId) return null;
            Actor actor;
            try { actor = World.world?.units?.get(pActorId); }
            catch { return null; }
            if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                !actor.isSexMale() || actor.isKing() ||
                actor.hasTrait("madness")) return null;
            actor.data.get(LineageKeys.LINEAGE_ID,
                out long actorLineageId, -1L);
            return actorLineageId == pLineageId ? actor : null;
        }

        private static SuccessionArchiveFallbackResult Pending(
            bool pScanInProgress = false)
        {
            return new SuccessionArchiveFallbackResult(
                SuccessionEvidenceStatus.PendingEvidence, null,
                pScanInProgress);
        }
    }
}
