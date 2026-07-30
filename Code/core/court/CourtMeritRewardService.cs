using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtMeritRewardService
    {
        private const int MaximumProjectionRepairs = 4;
        private const int MaximumArchiveRepairInspections = 4;
        private const int MaximumGeneralRepairInspections = 32;
        private const int MaximumGeneralProjectionRepairs = 4;

        private static readonly CourtRepairCursorStore<long>
            ArchiveRepairCursorByKingdom = new();
        private static long _archiveRepairDatabaseEpoch = -1L;
        private static object _archiveRepairWorld;

        private sealed class CandidateState
        {
            public Actor Actor;
            public CourtMeritRewardDetachedCandidate Detached;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral() || !pKingdom.isCiv() ||
                !pKingdom.hasKing() || pKingdom.king?.data == null)
                return;

            ResetArchiveRepairCursorsIfNeeded();
            RepairIndependentGeneralProjections(pKingdom);

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_MERIT_REWARD_LAST_YEAR,
                out int lastYear, -1);
            if (lastYear >= 0 && (long)year - lastYear <
                CourtMeritRewardRules.EvaluationIntervalYears)
                return;

            int kingdomTitle = (int)KingdomTitleService.GetTitle(pKingdom);
            int realmRankCap =
                NobleRankRules.MaximumGrantableRank(kingdomTitle);
            int nonRoyalRankCap = Math.Min(realmRankCap,
                NobleRankRules.RankStateDuke);
            pKingdom.king.data.get(LineageKeys.LINEAGE_ID,
                out long rulerLineageId, -1L);
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);
            bool hasGrantableLand = SafeCityCount(pKingdom) > 1;
            int repairBudget = MaximumProjectionRepairs;
            Dictionary<long, CandidateState> candidates =
                CollectCandidates(pKingdom, year, rulerLineageId,
                    nonRoyalRankCap, realmRankCap, hasGrantableLand,
                    court.war + court.aggression >= 1.1f,
                    ref repairBudget);
            if (candidates.Count == 0) return;

            var facts = new List<CourtMeritRewardCandidateFacts>(
                candidates.Count);
            foreach (CandidateState candidate in candidates.Values)
                facts.Add(BuildFacts(candidate.Detached, rulerLineageId));

            CourtMeritRewardCandidateFacts selected =
                CourtMeritRewardRules.SelectBest(facts, year, kingdomTitle,
                    hasGrantableLand, court.war, court.aggression);
            if (selected.ActorId < 0 ||
                !candidates.TryGetValue(selected.ActorId,
                    out CandidateState selectedState))
                return;

            CourtMeritRewardKind kind = selectedState.Detached.RewardKind;
            if (kind != CourtMeritRewardRules.RewardKind(selected, year,
                    kingdomTitle, hasGrantableLand, court.war,
                    court.aggression))
                return;
            bool committed = CourtMeritRewardRules.TryCommitSelectedReward(
                kind,
                () => FiefService.TryGrantBestFief(pKingdom,
                    selectedState.Actor, "ai_military_merit"),
                () => selected.CanReceiveHonor &&
                      CourtMeritRewardRules.TargetNobleRank(selected,
                          kingdomTitle) > selected.CurrentNobleRank &&
                      NobleRankService.TryGrant(pKingdom, pKingdom.king,
                          selectedState.Actor,
                          CourtMeritRewardRules.TargetNobleRank(selected,
                              kingdomTitle), NobleTitleStyle.Male,
                          "ai_merit_reward", -1L, out _));
            CourtMeritRewardCooldownProjection cooldown =
                CourtMeritRewardRules.ResolveCooldownCommit(committed, year,
                    lastYear, selected.LastRewardYear);
            if (!cooldown.ShouldWrite) return;
            bool detachedPersisted =
                OfficialCareerStateService.RecordNobleRewardYear(
                    selectedState.Actor, pKingdom.id,
                    cooldown.ActorLastRewardYear);
            if (!CourtMeritRewardRules.ShouldWriteHotCooldown(
                    detachedPersisted))
                return;
            pKingdom.data.set(LineageKeys.COURT_MERIT_REWARD_LAST_YEAR,
                cooldown.KingdomLastRewardYear);
            selectedState.Actor.data.set(
                LineageKeys.NOBLE_AI_LAST_REWARD_YEAR,
                cooldown.ActorLastRewardYear);
        }

        private static Dictionary<long, CandidateState> CollectCandidates(
            Kingdom pKingdom, int pCurrentYear, long pRulerLineageId,
            int pNonRoyalMaximumNobleRank, int pRealmMaximumNobleRank,
            bool pHasGrantableLand, bool pMartialCourtSupportsLand,
            ref int pRepairBudget)
        {
            var result = new Dictionary<long, CandidateState>();
            var detached =
                new Dictionary<long, CourtMeritRewardDetachedCandidate>();
            SQLiteConnection db =
                LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return result;

            IReadOnlyList<CourtMeritRewardDetachedCandidate> officers;
            try
            {
                officers = CourtMeritRewardCandidateQuery
                    .LoadOfficerCandidates(db,
                        CourtOfficerTableItem.GetTableName(),
                        OfficialCareerStateTableItem.GetTableName(),
                        EnfeoffmentTableItem.GetTableName(),
                        ActorArchiveTableItem.GetTableName(),
                        GeneralStateTableItem.GetTableName(), pKingdom.id,
                        pRulerLineageId, pNonRoyalMaximumNobleRank,
                        pRealmMaximumNobleRank, pHasGrantableLand,
                        pMartialCourtSupportsLand, pCurrentYear,
                        CourtMeritRewardRules.ActorRewardCooldownYears,
                        CourtMeritRewardRules.MaximumOfficerCandidates);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Court merit officer candidate read failed: " +
                    error.Message);
                officers = Array.Empty<CourtMeritRewardDetachedCandidate>();
            }

            IReadOnlyList<CourtMeritRewardDetachedCandidate> generals;
            try
            {
                generals = CourtMeritRewardCandidateQuery
                    .LoadGeneralCandidates(db,
                        OfficialCareerStateTableItem.GetTableName(),
                        EnfeoffmentTableItem.GetTableName(),
                        ActorArchiveTableItem.GetTableName(),
                        GeneralStateTableItem.GetTableName(), pKingdom.id,
                        pRulerLineageId, pNonRoyalMaximumNobleRank,
                        pRealmMaximumNobleRank, pHasGrantableLand,
                        pMartialCourtSupportsLand, pCurrentYear,
                        CourtMeritRewardRules.ActorRewardCooldownYears,
                        CourtMeritRewardRules.MaximumGeneralCandidates);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Court merit general candidate read failed: " +
                    error.Message);
                generals = Array.Empty<CourtMeritRewardDetachedCandidate>();
            }

            RepairMissingArchives(db, pKingdom);
            for (int i = 0; i < officers.Count; i++)
                MergeDetached(detached, officers[i]);
            for (int i = 0; i < generals.Count; i++)
                MergeDetached(detached, generals[i]);

            foreach (CourtMeritRewardDetachedCandidate candidate in
                     detached.Values)
            {
                Actor actor = FindActor(candidate.ActorId);
                if (!IsEligibleActor(actor, pKingdom)) continue;
                if (!DetachedIdentityMatches(actor, candidate,
                        out bool repairArchive, out bool repairGeneral))
                {
                    RepairDetachedIdentity(actor, repairArchive,
                        repairGeneral, ref pRepairBudget);
                    continue;
                }
                result[candidate.ActorId] = new CandidateState
                {
                    Actor = actor,
                    Detached = candidate
                };
            }
            return result;
        }

        private static void MergeDetached(
            IDictionary<long, CourtMeritRewardDetachedCandidate> pCandidates,
            CourtMeritRewardDetachedCandidate pIncoming)
        {
            if (pIncoming.ActorId < 0) return;
            if (!pCandidates.TryGetValue(pIncoming.ActorId,
                    out CourtMeritRewardDetachedCandidate existing))
            {
                pCandidates[pIncoming.ActorId] = pIncoming;
                return;
            }
            pCandidates[pIncoming.ActorId] =
                new CourtMeritRewardDetachedCandidate(
                    pIncoming.ActorId, pIncoming.CivilMerit,
                    pIncoming.CivilMeritCap, pIncoming.LastRewardYear,
                    pIncoming.ArchiveKnown, pIncoming.ArchiveLineageId,
                    pIncoming.ArchiveSex, pIncoming.CurrentNobleRank,
                    pIncoming.CurrentNobleStyle,
                    pIncoming.GeneralProjectionKnown,
                    pIncoming.GeneralActive, pIncoming.MilitaryMerit,
                    pIncoming.FiefCityId,
                    existing.OfficerSource || pIncoming.OfficerSource,
                    existing.GeneralSource || pIncoming.GeneralSource,
                    pIncoming.RewardKind, pIncoming.EligibilityReason);
        }

        private static void RepairIndependentGeneralProjections(
            Kingdom pKingdom)
        {
            try
            {
                GeneralService.RepairDetachedProjections(pKingdom,
                    MaximumGeneralRepairInspections,
                    MaximumGeneralProjectionRepairs);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Court merit general projection repair failed: " +
                    error.Message);
            }
        }

        private static void RepairMissingArchives(SQLiteConnection pDb,
            Kingdom pKingdom)
        {
            if (!ArchiveRepairCursorByKingdom.TryGet(pKingdom.id,
                    out long afterActorId))
                afterActorId = -1L;
            IReadOnlyList<CourtMeritRewardArchiveRepairCandidate> repairs;
            try
            {
                repairs = CourtMeritRewardCandidateQuery
                    .LoadMissingArchiveRepairs(pDb,
                        CourtOfficerTableItem.GetTableName(),
                        ActorArchiveTableItem.GetTableName(),
                        GeneralStateTableItem.GetTableName(), pKingdom.id,
                        afterActorId, MaximumArchiveRepairInspections);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Court merit archive repair read failed: " +
                    error.Message);
                return;
            }

            for (int i = 0; i < repairs.Count; i++)
            {
                ArchiveRepairCursorByKingdom.Set(pKingdom.id,
                    repairs[i].ActorId);
                Actor actor = FindActor(repairs[i].ActorId);
                if (!IsRepairableActor(actor, pKingdom)) continue;
                try { LineageService.ArchiveActor(actor, pAlive: true); }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Court merit archive repair failed: " +
                        error.Message);
                }
            }
        }

        private static void ResetArchiveRepairCursorsIfNeeded()
        {
            long databaseEpoch = LineageArchiveManager.RuntimeDatabaseEpoch;
            object world = World.world;
            if (_archiveRepairDatabaseEpoch == databaseEpoch &&
                ReferenceEquals(_archiveRepairWorld, world))
                return;
            ArchiveRepairCursorByKingdom.Clear();
            _archiveRepairDatabaseEpoch = databaseEpoch;
            _archiveRepairWorld = world;
        }

        internal static void RemoveRepairCursor(long pKingdomId)
        {
            ArchiveRepairCursorByKingdom.Remove(pKingdomId);
        }

        private static bool DetachedIdentityMatches(Actor pActor,
            CourtMeritRewardDetachedCandidate pCandidate,
            out bool pRepairArchive, out bool pRepairGeneral)
        {
            pRepairArchive = false;
            pRepairGeneral = false;
            if (pActor?.data == null ||
                pActor.data.id != pCandidate.ActorId)
                return false;

            pActor.data.get(LineageKeys.LINEAGE_ID,
                out long liveLineageId, -1L);
            int liveSex = pActor.isSexMale() ? 0 : 1;
            pRepairArchive = !pCandidate.ArchiveKnown ||
                             pCandidate.ArchiveSex is < 0 or > 1 ||
                             liveLineageId != pCandidate.ArchiveLineageId ||
                             liveSex != pCandidate.ArchiveSex;

            bool liveGeneral = GeneralService.IsActiveGeneralFast(pActor);
            pRepairGeneral = liveGeneral != pCandidate.GeneralActive ||
                             liveGeneral &&
                             !pCandidate.GeneralProjectionKnown;
            return !pRepairArchive && !pRepairGeneral;
        }

        private static void RepairDetachedIdentity(Actor pActor,
            bool pRepairArchive, bool pRepairGeneral,
            ref int pRepairBudget)
        {
            if (pRepairBudget <= 0 || pActor?.data == null ||
                !pRepairArchive && !pRepairGeneral)
                return;
            CourtRepairOrchestration.TryRepairIndependent(pActor,
                pRepairArchive, pRepairGeneral, ref pRepairBudget,
                ArchiveDetachedIdentity,
                GeneralService.RepairDetachedProjection,
                LogDetachedIdentityRepairFailure);
        }

        private static void ArchiveDetachedIdentity(Actor pActor)
        {
            LineageService.ArchiveActor(pActor, pAlive: true);
        }

        private static void LogDetachedIdentityRepairFailure(Actor pActor,
            CourtRepairFailureStage pStage, Exception pError)
        {
            ModClass.LogWarning("Court merit candidate repair failed actor=" +
                (pActor?.data?.id ?? -1L) + " kingdom=" +
                (pActor?.kingdom?.id ?? -1L) + " stage=" + pStage +
                ": " + (pError?.Message ?? "unknown error"));
        }

        private static CourtMeritRewardCandidateFacts BuildFacts(
            CourtMeritRewardDetachedCandidate pCandidate,
            long pRulerLineageId)
        {
            return new CourtMeritRewardCandidateFacts(pCandidate.ActorId,
                eligible: pCandidate.ArchiveKnown,
                royal: pCandidate.ArchiveLineageId >= 0 &&
                       pCandidate.ArchiveLineageId == pRulerLineageId,
                general: pCandidate.GeneralActive,
                pCandidate.CivilMerit, pCandidate.CivilMeritCap,
                pCandidate.MilitaryMerit, pCandidate.CurrentNobleRank,
                pCandidate.FiefCityId >= 0, pCandidate.LastRewardYear,
                canReceiveHonor: pCandidate.ArchiveSex == 0);
        }

        private static bool IsEligibleActor(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                pActor.isRekt() || !pActor.isAlive() ||
                pActor.kingdom != pKingdom || pActor == pKingdom.king ||
                SlaveService.IsSlave(pActor))
                return false;
            return !HeirService.IsCurrentHeir(pKingdom, pActor);
        }

        private static bool IsRepairableActor(Actor pActor,
            Kingdom pKingdom)
        {
            return pActor?.data != null && pKingdom?.data != null &&
                   !pActor.isRekt() && pActor.isAlive() &&
                   pActor.kingdom == pKingdom;
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }
    }
}
