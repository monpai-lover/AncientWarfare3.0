using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ai;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct SuccessionCourtFallbackResult
    {
        internal SuccessionCourtFallbackResult(Actor pCandidate,
            bool pScanInProgress, bool pEvidenceAvailable)
        {
            Candidate = pCandidate;
            ScanInProgress = pScanInProgress;
            EvidenceAvailable = pEvidenceAvailable;
        }

        internal Actor Candidate { get; }
        internal bool ScanInProgress { get; }
        internal bool EvidenceAvailable { get; }
    }

    internal static class SuccessionCourtFallbackService
    {
        private const int MaximumActorsPerPage = 32;
        private const int MaximumRetainedCandidates = 16;

        private enum CourtRole
        {
            CityLeader,
            General,
            MilitaryGovernor,
            CentralHigh
        }

        private readonly struct OfficeFact
        {
            internal OfficeFact(long pActorId, long pShiId, CourtRole pRole)
            {
                ActorId = pActorId;
                ShiId = pShiId;
                Role = pRole;
            }
            internal long ActorId { get; }
            internal long ShiId { get; }
            internal CourtRole Role { get; }
        }

        private readonly struct FamilyRank
        {
            internal FamilyRank(long pShiId, int pWeight, int pMembers)
            {
                ShiId = pShiId;
                Weight = pWeight;
                Members = pMembers;
            }
            internal long ShiId { get; }
            internal int Weight { get; }
            internal int Members { get; }
        }

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
            internal readonly List<long> RankedShiIds = new List<long>();
            internal int FamilyIndex;
            internal long Cursor = -1L;
            internal readonly List<RankedCandidate> CandidateIds =
                new List<RankedCandidate>(MaximumRetainedCandidates);
            internal bool RestartedAfterCandidateLoss;

            internal long CurrentShiId =>
                FamilyIndex >= 0 && FamilyIndex < RankedShiIds.Count
                    ? RankedShiIds[FamilyIndex]
                    : -1L;

            internal bool AdvanceFamily()
            {
                FamilyIndex++;
                Cursor = -1L;
                CandidateIds.Clear();
                RestartedAfterCandidateLoss = false;
                return FamilyIndex < RankedShiIds.Count;
            }

            internal void RestartCurrentFamily()
            {
                Cursor = -1L;
                CandidateIds.Clear();
                RestartedAfterCandidateLoss = true;
            }
        }

        private static readonly Dictionary<KingSuccessionKey, ScanState>
            Scans = new Dictionary<KingSuccessionKey, ScanState>();

        internal static SuccessionCourtFallbackResult ResolveCandidate(
            Kingdom pKingdom, KingSuccessionKey pKey)
        {
            if (pKingdom?.data == null) return Pending();
            if (!Scans.TryGetValue(pKey, out ScanState state))
            {
                pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                    out long rulingShiId, -1L);
                state = new ScanState();
                foreach (FamilyRank rank in RankFamilies(
                             CollectOfficeFacts(pKingdom), rulingShiId))
                    state.RankedShiIds.Add(rank.ShiId);
                Scans[pKey] = state;
                if (state.RankedShiIds.Count == 0)
                    return new SuccessionCourtFallbackResult(null, false,
                        true);
            }

            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful)
                return Pending();
            if (state.CurrentShiId < 0L)
                return new SuccessionCourtFallbackResult(null, false, true);
            if (!TryReadPage(db, pKey, state, out int rowCount))
                return Pending();
            if (rowCount >= MaximumActorsPerPage)
                return new SuccessionCourtFallbackResult(null, true, true);

            bool hadRetainedCandidates = state.CandidateIds.Count > 0;
            Actor best = ResolveBestEligibleActor(state,
                pKey.PredecessorId);
            if (best?.data != null)
                return new SuccessionCourtFallbackResult(best, false, true);
            if (hadRetainedCandidates &&
                !state.RestartedAfterCandidateLoss)
            {
                state.RestartCurrentFamily();
                return new SuccessionCourtFallbackResult(null, true, true);
            }
            bool hasMoreFamilies = state.AdvanceFamily();
            return new SuccessionCourtFallbackResult(null,
                hasMoreFamilies, true);
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

        private static IReadOnlyList<OfficeFact> CollectOfficeFacts(
            Kingdom pKingdom)
        {
            var facts = new List<OfficeFact>();
            foreach (CourtOfficerView officer in
                     CourtService.GetActiveOfficers(pKingdom, 96))
            {
                if (officer == null ||
                    officer.layer != CourtOfficeLayer.Central ||
                    CourtPyramidRules.RankForOffice(officer.office_id) >
                    CourtPyramidRules.HighOfficeRank) continue;
                AddFact(facts, World.world?.units?.get(officer.actor_id),
                    CourtRole.CentralHigh);
            }
            foreach (MilitaryGovernorateSnapshot governorate in
                     MilitaryGovernorateStore.GetDirectActive(pKingdom, 256))
                AddFact(facts,
                    World.world?.units?.get(governorate.GovernorActorId),
                    CourtRole.MilitaryGovernor);
            foreach (GeneralReadModelEntry general in
                     GeneralService.GetActiveGeneralsForReadModel(
                         pKingdom, pAllowUnitFallback: false))
                AddFact(facts, general?.Actor, CourtRole.General);
            foreach (City city in pKingdom.getCities())
                AddFact(facts, city?.leader, CourtRole.CityLeader);
            return facts;
        }

        private static void AddFact(ICollection<OfficeFact> pFacts,
            Actor pActor, CourtRole pRole)
        {
            if (pActor?.data == null || !pActor.isAlive() ||
                pActor.isRekt()) return;
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            if (shiId >= 0L)
                pFacts.Add(new OfficeFact(pActor.data.id, shiId, pRole));
        }

        private static IReadOnlyList<FamilyRank> RankFamilies(
            IReadOnlyList<OfficeFact> pFacts, long pRulingShiId)
        {
            var strongestByActor = new Dictionary<long, OfficeFact>();
            foreach (OfficeFact fact in pFacts)
            {
                if (fact.ShiId < 0L || fact.ShiId == pRulingShiId) continue;
                if (!strongestByActor.TryGetValue(fact.ActorId,
                        out OfficeFact current) ||
                    RoleWeight(fact.Role) > RoleWeight(current.Role))
                    strongestByActor[fact.ActorId] = fact;
            }
            var weights = new Dictionary<long, int>();
            var members = new Dictionary<long, int>();
            foreach (OfficeFact fact in strongestByActor.Values)
            {
                weights.TryGetValue(fact.ShiId, out int weight);
                members.TryGetValue(fact.ShiId, out int count);
                weights[fact.ShiId] = weight + RoleWeight(fact.Role);
                members[fact.ShiId] = count + 1;
            }
            return weights.Select(p => new FamilyRank(p.Key, p.Value,
                    members[p.Key]))
                .OrderByDescending(p => p.Weight)
                .ThenByDescending(p => p.Members)
                .ThenBy(p => p.ShiId)
                .ToArray();
        }

        private static int RoleWeight(CourtRole pRole)
        {
            switch (pRole)
            {
                case CourtRole.CentralHigh: return 50;
                case CourtRole.MilitaryGovernor: return 40;
                case CourtRole.General: return 35;
                default: return 25;
            }
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
                    " WHERE SHI_ID=@shi AND IS_ALIVE=1" +
                    " AND ID>@cursor ORDER BY ID LIMIT @limit";
                command.Parameters.AddWithValue("@shi", pState.CurrentShiId);
                command.Parameters.AddWithValue("@cursor", pState.Cursor);
                command.Parameters.AddWithValue("@limit", MaximumActorsPerPage);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long actorId = reader.GetInt64(0);
                    pState.Cursor = actorId;
                    pRowCount++;
                    Actor actor = ResolveEligibleShiMember(actorId,
                        pState.CurrentShiId, pKey.PredecessorId);
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
            long pPredecessorId)
        {
            for (int i = 0; i < pState.CandidateIds.Count; i++)
            {
                Actor actor = ResolveEligibleShiMember(
                    pState.CandidateIds[i].ActorId, pState.CurrentShiId,
                    pPredecessorId);
                if (actor?.data != null) return actor;
            }
            pState.CandidateIds.Clear();
            return null;
        }

        private static Actor ResolveEligibleShiMember(long pActorId,
            long pShiId, long pPredecessorId)
        {
            if (pActorId < 0L || pActorId == pPredecessorId) return null;
            Actor actor;
            try { actor = World.world?.units?.get(pActorId); }
            catch { return null; }
            if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                !actor.isSexMale() || actor.isKing() ||
                actor.hasTrait("madness")) return null;
            actor.data.get(LineageKeys.SHI_ID, out long actorShiId, -1L);
            return actorShiId == pShiId ? actor : null;
        }

        private static SuccessionCourtFallbackResult Pending()
        {
            return new SuccessionCourtFallbackResult(null, false, false);
        }
    }
}
