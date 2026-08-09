using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal sealed class SuccessionDisputeSnapshot
    {
        public long DisputeId;
        public long OriginalKingdomId;
        public long RivalKingdomId = -1L;
        public long PredecessorActorId;
        public long SuccessorActorId;
        public long ClaimantActorId;
        public string OriginalStateName = "";
        public string OriginalQualifier = "";
        public string RivalQualifier = "";
        public InheritanceLaw AccessionLaw;
        public string SuccessorMode = "";
        public string ClaimantMode = "";
        public int SuccessorSupport;
        public int ClaimantSupport;
        public long WarId = -1L;
        public int DeadlineYear = -1;
        public SuccessionDisputeStatus Status;
        public long OriginalLineageId = -1L;
        public long OriginalShiId = -1L;
        public int ClaimGenerationBoundary =
            SuccessionDisputeRules.ReunificationClaimGenerations;
        public bool Materialized;
    }

    internal sealed class SuccessionDisputePreparationFacts
    {
        internal long WorldGeneration;
        internal long Revision;
        internal long KingdomId;
        internal long PredecessorActorId;
        internal long SuccessorActorId;
        internal long ClaimantActorId = -1L;
        internal long LegitimateClaimantId = -1L;
        internal long MilitaryClaimantId = -1L;
        internal long CivilClaimantId = -1L;
        internal string SuccessorMode = SuccessionMode.NONE;
        internal string ClaimantMode = SuccessionMode.NONE;
        internal SuccessionClaimantKind ClaimantKind;
        internal int SuccessorSupport;
        internal int ClaimantSupport;
        internal int RunnerUpSupport;
        internal InheritanceLaw AccessionLaw;
        internal long OriginalLineageId = -1L;
        internal long OriginalShiId = -1L;
        internal string OriginalStateName = string.Empty;
        internal string OriginalQualifier = string.Empty;
        internal string RivalQualifier = string.Empty;
        internal long[] SupportCityIds = Array.Empty<long>();
    }

    internal static class SuccessionDisputeService
    {
        private static readonly Dictionary<long, SuccessionDisputeSnapshot>
            ById = new Dictionary<long, SuccessionDisputeSnapshot>();
        private static readonly Dictionary<long, long> ByKingdom =
            new Dictionary<long, long>();
        private static readonly Dictionary<long, int> CompensationAttempts =
            new Dictionary<long, int>();
        private const int MaximumCompensationAttempts = 8;
        private const int RuntimeRebuildPageSize = 64;
        private static int _runtimeRebuildGeneration;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        internal static SuccessionDisputePreparationFacts
            BuildPreparationFacts(Kingdom pKingdom, Actor pPredecessor,
                Actor pInstalledSuccessor, string pSuccessorMode)
        {
            if (pKingdom?.data == null || pPredecessor?.data == null ||
                pInstalledSuccessor?.data == null ||
                pKingdom.countCities() <= 1 ||
                TryGetCachedByKingdom(pKingdom.id, out _))
                return null;
            InheritanceFactionSupport factionSupport =
                InheritanceCandidateService.ResolveFactionSupport(pKingdom,
                    pPredecessor, pInstalledSuccessor);
            Actor claimant = factionSupport.LeaderActor;
            if (!IsLegalAlternative(claimant, pInstalledSuccessor,
                    pKingdom)) return null;
            List<City> cities = SelectSupportCities(pKingdom, claimant,
                pInstalledSuccessor, factionSupport.LeaderMode,
                pSuccessorMode, factionSupport);
            if (!SuccessionDisputeRules.CanFormBalancedTerritorialSplit(
                    CountLiveCities(pKingdom), cities.Count) ||
                !SuccessionDisputeRules.CanPrepare(
                    new SuccessionClaimantFacts(claimant.data.id,
                        factionSupport.LeaderKind,
                        factionSupport.LeaderSupport,
                        factionSupport.RunnerUpSupport,
                        hasSupportCity: true, hasActiveDispute: false,
                        hasLivingDirectPaternalAncestor: false)))
                return null;

            SuccessionDirection rivalDirection = ResolveDirection(
                pKingdom.capital, cities[0], claimantAccededLater: true);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_LINEAGE_ID,
                out long lineageId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_LEGITIMATE_SHI_ID,
                out long shiId, -1L);
            factionSupport.Selections.TryGetValue(
                InheritanceLaw.Primogeniture, out var legitimate);
            factionSupport.Selections.TryGetValue(
                InheritanceLaw.MilitaryAcclaim, out var military);
            factionSupport.Selections.TryGetValue(
                InheritanceLaw.CivilAcclaim, out var civil);
            var cityIds = new long[cities.Count];
            for (int i = 0; i < cities.Count; i++) cityIds[i] = cities[i].id;
            return new SuccessionDisputePreparationFacts
            {
                WorldGeneration = AWAsyncRuntime.WorldGeneration,
                Revision = SuccessionDisputePersistenceService.CurrentRevision(
                    pKingdom.id),
                KingdomId = pKingdom.id,
                PredecessorActorId = pPredecessor.data.id,
                SuccessorActorId = pInstalledSuccessor.data.id,
                ClaimantActorId = claimant.data.id,
                LegitimateClaimantId = legitimate?.Actor?.data?.id ?? -1L,
                MilitaryClaimantId = military?.Actor?.data?.id ?? -1L,
                CivilClaimantId = civil?.Actor?.data?.id ?? -1L,
                SuccessorMode = pSuccessorMode ?? SuccessionMode.NONE,
                ClaimantMode = factionSupport.LeaderMode,
                ClaimantKind = factionSupport.LeaderKind,
                SuccessorSupport = factionSupport.DesignatedHeirSupport,
                ClaimantSupport = factionSupport.LeaderSupport,
                RunnerUpSupport = factionSupport.RunnerUpSupport,
                AccessionLaw = InheritanceLawService.GetEffectiveLaw(
                    pKingdom),
                OriginalLineageId = lineageId,
                OriginalShiId = shiId,
                OriginalStateName = pKingdom.name ?? string.Empty,
                OriginalQualifier = SuccessionDisputeRules.DirectionId(
                    Opposite(rivalDirection)),
                RivalQualifier = SuccessionDisputeRules.DirectionId(
                    rivalDirection),
                SupportCityIds = cityIds
            };
        }

        public static void OnSuccessorInstalled(Kingdom pKingdom,
            Actor pSuccessor)
        {
            if (!TryGetCachedByKingdom(pKingdom?.id ?? -1L,
                    out SuccessionDisputeSnapshot snapshot))
                return;
            if (snapshot.Status == SuccessionDisputeStatus.PermanentSplit)
            {
                RefreshReunificationGeneration(pKingdom, snapshot);
                return;
            }
            if (snapshot.Status != SuccessionDisputeStatus.Prepared)
                return;
            long installedSuccessorId = pSuccessor?.data?.id ?? -1L;
            if (!SuccessionDisputeRules.CanActivatePreparedForSuccessor(
                    snapshot.Status, snapshot.SuccessorActorId,
                    installedSuccessorId))
            {
                Close(snapshot, "prepared_successor_replaced");
                return;
            }
            Enqueue(snapshot.DisputeId);
        }

        internal static void PublishCommitted(
            SuccessionDisputePreparationFacts pFacts,
            SuccessionDisputeWriteFacts pWrite,
            SuccessionDisputeWriteResult pResult, Kingdom pKingdom,
            Actor pSuccessor)
        {
            if (pFacts == null || pWrite == null || pResult == null ||
                pKingdom?.data == null || pSuccessor?.data == null ||
                pKingdom.id != pFacts.KingdomId ||
                pSuccessor.data.id != pFacts.SuccessorActorId ||
                pResult.DisputeId < 0L) return;
            var snapshot = new SuccessionDisputeSnapshot
            {
                DisputeId = pResult.DisputeId,
                OriginalKingdomId = pFacts.KingdomId,
                RivalKingdomId = -1L,
                PredecessorActorId = pFacts.PredecessorActorId,
                SuccessorActorId = pFacts.SuccessorActorId,
                ClaimantActorId = pFacts.ClaimantActorId,
                OriginalStateName = pWrite.OriginalStateName ?? string.Empty,
                OriginalQualifier = pWrite.OriginalQualifier ?? string.Empty,
                RivalQualifier = pWrite.RivalQualifier ?? string.Empty,
                AccessionLaw = pFacts.AccessionLaw,
                SuccessorMode = pWrite.SuccessorMode ?? SuccessionMode.NONE,
                ClaimantMode = pWrite.ClaimantMode ?? SuccessionMode.NONE,
                SuccessorSupport = pWrite.SuccessorSupport,
                ClaimantSupport = pWrite.ClaimantSupport,
                WarId = -1L,
                DeadlineYear = pWrite.DeadlineYear,
                Status = SuccessionDisputeStatus.Prepared,
                OriginalLineageId = pWrite.OriginalLineageId,
                OriginalShiId = pWrite.OriginalShiId,
                ClaimGenerationBoundary = pWrite.ClaimGenerationBoundary,
                Materialized = false
            };
            Publish(snapshot);
            pKingdom.data.set(LineageKeys.ACTIVE_SUCCESSION_DISPUTE_ID,
                snapshot.DisputeId);
            OnSuccessorInstalled(pKingdom, pSuccessor);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!TryGetCachedByKingdom(pKingdom?.id ?? -1L,
                    out SuccessionDisputeSnapshot snapshot))
                return;
            if (snapshot.Status == SuccessionDisputeStatus.PermanentSplit)
            {
                if (!IsMaterializedNow(snapshot))
                {
                    RepairInvalidTerritorialDispute(snapshot);
                    return;
                }
                RefreshReunificationGeneration(pKingdom, snapshot);
                return;
            }
            if (snapshot.Status != SuccessionDisputeStatus.Active) return;
            bool materialized = IsMaterializedNow(snapshot);
            if (!materialized)
            {
                RepairInvalidTerritorialDispute(snapshot);
                return;
            }
            War war = FindWar(snapshot.WarId);
            if (!SuccessionDisputeRules.ShouldBecomePermanent(
                    Date.getCurrentYear(), snapshot.DeadlineYear,
                    war?.data != null && !war.hasEnded(), materialized))
                return;
            if (!UpdateStatus(snapshot.DisputeId,
                    SuccessionDisputeStatus.Active,
                    SuccessionDisputeStatus.PermanentSplit))
                return;
            snapshot.Status = SuccessionDisputeStatus.PermanentSplit;
            Publish(snapshot);
            ChronicleEvents.OnSuccessionPermanentSplit(
                FindKingdom(snapshot.OriginalKingdomId),
                FindKingdom(snapshot.RivalKingdomId),
                FindActor(snapshot.SuccessorActorId),
                FindActor(snapshot.ClaimantActorId),
                GetDisplayName(FindKingdom(snapshot.OriginalKingdomId)),
                GetDisplayName(FindKingdom(snapshot.RivalKingdomId)));
            MandatePhaseService.ForceChaos("succession_permanent_split");
            try { World.world?.wars?.endWar(war, WarWinner.Nobody); }
            catch { }
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null || pWar.getAsset()?.id !=
                SuccessionDisputeRules.WarTypeId)
                return;
            pWar.data.get(LineageKeys.SUCCESSION_DISPUTE_ID,
                out long disputeId, -1L);
            SuccessionDisputeSnapshot snapshot = Read(disputeId);
            if (snapshot == null || snapshot.Status ==
                SuccessionDisputeStatus.Closed)
                return;
            if (snapshot.Status == SuccessionDisputeStatus.PermanentSplit)
            {
                if (snapshot.WarId != pWar.data.id) return;
                long winnerKingdomId = pWinner == WarWinner.Attackers
                    ? pWar.getMainAttacker()?.id ?? -1L
                    : pWinner == WarWinner.Defenders
                        ? pWar.getMainDefender()?.id ?? -1L
                        : -1L;
                if (winnerKingdomId < 0)
                {
                    ClearReunificationWar(snapshot);
                    return;
                }
                SettleReunification(snapshot, winnerKingdomId,
                    "reunification_victory");
                return;
            }
            if (!SuccessionDisputeRules.CanSettleInitialWar(
                    snapshot.Status, snapshot.WarId, pWar.data.id,
                    snapshot.Materialized))
                return;
            bool claimantWins = pWinner == WarWinner.Attackers;
            Settle(snapshot, claimantWins,
                claimantWins ? "claimant_victory" :
                pWinner == WarWinner.Defenders
                    ? "successor_victory"
                    : "early_stalemate_suppressed");
        }

        public static bool TryGetByKingdom(long pKingdomId,
            out SuccessionDisputeSnapshot pSnapshot)
        {
            pSnapshot = null;
            if (pKingdomId < 0) return false;
            if (ByKingdom.TryGetValue(pKingdomId, out long disputeId) &&
                ById.TryGetValue(disputeId, out pSnapshot))
                return true;
            if (!Ready) return false;
            pSnapshot = ReadActiveByKingdom(pKingdomId);
            if (pSnapshot == null) return false;
            Publish(pSnapshot);
            return true;
        }

        public static bool TryGetCachedByKingdom(long pKingdomId,
            out SuccessionDisputeSnapshot pSnapshot)
        {
            pSnapshot = null;
            return pKingdomId >= 0 &&
                   ByKingdom.TryGetValue(pKingdomId, out long disputeId) &&
                   ById.TryGetValue(disputeId, out pSnapshot);
        }

        public static bool TryGetMaterializedByKingdom(long pKingdomId,
            out SuccessionDisputeSnapshot pSnapshot)
        {
            if (!TryGetCachedByKingdom(pKingdomId, out pSnapshot) ||
                !pSnapshot.Materialized)
            {
                pSnapshot = null;
                return false;
            }
            return true;
        }

        public static int ReadOpposedCourtOpinion(Kingdom pFirst,
            Kingdom pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null ||
                pFirst == pSecond ||
                !TryGetCachedByKingdom(pFirst.id,
                    out SuccessionDisputeSnapshot row)) return 0;
            return SuccessionDisputeRules.OpposedCourtOpinion(row.Status,
                pFirst.id, pSecond.id, row.OriginalKingdomId,
                row.RivalKingdomId,
                CountLiveCities(FindKingdom(row.OriginalKingdomId)),
                CountLiveCities(FindKingdom(row.RivalKingdomId)));
        }

        public static int GetReunificationClaimGeneration(
            Kingdom pKingdom, SuccessionDisputeSnapshot pRow)
        {
            if (pKingdom?.data == null || pRow == null) return -1;
            pKingdom.data.get(
                LineageKeys.SUCCESSION_REUNIFICATION_GENERATION,
                out int generation, -1);
            return generation;
        }

        public static string GetQualifier(long pKingdomId)
        {
            if (!TryGetMaterializedByKingdom(pKingdomId,
                    out SuccessionDisputeSnapshot row))
                return "";
            return pKingdomId == row.OriginalKingdomId
                ? row.OriginalQualifier
                : pKingdomId == row.RivalKingdomId
                    ? row.RivalQualifier
                    : "";
        }

        public static string GetCanonicalStateName(long pKingdomId)
        {
            if (TryGetMaterializedByKingdom(pKingdomId,
                    out SuccessionDisputeSnapshot activeRow))
            {
                Kingdom authority = FindKingdom(
                    activeRow.OriginalKingdomId);
                if (!string.IsNullOrWhiteSpace(authority?.data?.name))
                    return authority.data.name.Trim();
                if (!string.IsNullOrWhiteSpace(
                        activeRow.OriginalStateName))
                    return activeRow.OriginalStateName.Trim();
            }
            Kingdom kingdom = FindKingdom(pKingdomId);
            if (kingdom?.data != null &&
                StateNameRules.IsValid(kingdom.name))
                return kingdom.name;
            return TryGetCachedByKingdom(pKingdomId,
                    out SuccessionDisputeSnapshot row)
                ? row.OriginalStateName
                : "";
        }

        public static string GetDisplayName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            if (!TryGetMaterializedByKingdom(pKingdom.id,
                    out SuccessionDisputeSnapshot row))
                return pKingdom.name ?? "";
            string qualifier = pKingdom.id == row.OriginalKingdomId
                ? row.OriginalQualifier
                : pKingdom.id == row.RivalKingdomId
                    ? row.RivalQualifier
                    : "";
            Kingdom authority = FindKingdom(row.OriginalKingdomId);
            string sharedBase = !string.IsNullOrWhiteSpace(
                    authority?.data?.name)
                ? authority.data.name.Trim()
                : !string.IsNullOrWhiteSpace(pKingdom.data.name)
                    ? pKingdom.data.name.Trim()
                    : row.OriginalStateName;
            return SuccessionDisputeDisplayRules.BuildQualifiedName(
                sharedBase,
                qualifier, active: true,
                HistoryLocalizationRules.CurrentLanguage());
        }

        internal static Kingdom[] GetSharedNameMembers(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !TryGetMaterializedByKingdom(pKingdom.id,
                    out SuccessionDisputeSnapshot row))
                return pKingdom?.data == null
                    ? Array.Empty<Kingdom>()
                    : new[] { pKingdom };
            Kingdom original = FindKingdom(row.OriginalKingdomId);
            Kingdom rival = FindKingdom(row.RivalKingdomId);
            if (original?.data == null || original.isRekt())
                return rival?.data == null || rival.isRekt()
                    ? new[] { pKingdom }
                    : new[] { rival };
            if (rival?.data == null || rival.isRekt())
                return new[] { original };
            return new[] { original, rival };
        }

        internal static Kingdom GetSharedNameAuthority(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !TryGetMaterializedByKingdom(pKingdom.id,
                    out SuccessionDisputeSnapshot row))
                return pKingdom;
            Kingdom original = FindKingdom(row.OriginalKingdomId);
            return original?.data != null && !original.isRekt()
                ? original
                : pKingdom;
        }

        internal static string GetLegacySharedName(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   TryGetMaterializedByKingdom(pKingdom.id,
                       out SuccessionDisputeSnapshot row)
                ? row.OriginalStateName ?? string.Empty
                : string.Empty;
        }

        private static bool IsMaterializedNow(
            SuccessionDisputeSnapshot pRow)
        {
            if (pRow == null) return false;
            return SuccessionDisputeRules.IsMaterialized(pRow.Status,
                pRow.RivalKingdomId,
                CountLiveCities(FindKingdom(pRow.OriginalKingdomId)),
                CountLiveCities(FindKingdom(pRow.RivalKingdomId)));
        }

        private static void RepairInvalidTerritorialDispute(
            SuccessionDisputeSnapshot pRow)
        {
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            int originalCities = CountLiveCities(original);
            int rivalCities = CountLiveCities(rival);
            if (pRow.Status == SuccessionDisputeStatus.PermanentSplit)
            {
                long winnerId = originalCities > 0
                    ? pRow.OriginalKingdomId
                    : rivalCities > 0
                        ? pRow.RivalKingdomId
                        : -1L;
                if (winnerId >= 0)
                    SettleReunification(pRow, winnerId,
                        "invalid_territorial_reunification");
                else
                    Close(pRow, "invalid_territorial_split");
                return;
            }
            if (pRow.Status >= SuccessionDisputeStatus.WarStarted)
            {
                if (originalCities > 0 || rivalCities > 0)
                    Settle(pRow, originalCities <= 0 && rivalCities > 0,
                        "invalid_territorial_victory");
                else
                    Close(pRow, "invalid_territorial_split");
                return;
            }
            Compensate(pRow, "invalid_territorial_split");
        }

        public static bool ShouldPreserveOriginalKingdom(
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !TryGetCachedByKingdom(pKingdom.id,
                    out SuccessionDisputeSnapshot row))
                return false;
            bool disputeCourt = row.OriginalKingdomId == pKingdom.id ||
                                row.RivalKingdomId == pKingdom.id;
            return CountLiveCities(pKingdom) > 0 &&
                   SuccessionDisputeRules.ShouldPreserveDisputeIdentity(
                       row.Status, disputeCourt);
        }

        public static void OnZeroCityKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || CountLiveCities(pKingdom) > 0 ||
                !TryGetByKingdom(pKingdom.id,
                    out SuccessionDisputeSnapshot row)) return;
            Kingdom original = FindKingdom(row.OriginalKingdomId);
            Kingdom rival = FindKingdom(row.RivalKingdomId);
            int originalCities = CountLiveCities(original);
            int rivalCities = CountLiveCities(rival);
            if (row.Status == SuccessionDisputeStatus.PermanentSplit)
            {
                long winnerId = originalCities > 0
                    ? row.OriginalKingdomId
                    : rivalCities > 0
                        ? row.RivalKingdomId
                        : -1L;
                if (winnerId >= 0)
                    SettleReunification(row, winnerId,
                        "zero_city_reunification");
                else
                    Close(row, "both_courts_zero_city");
                return;
            }
            if (row.Status >= SuccessionDisputeStatus.WarStarted)
            {
                if (originalCities > 0 || rivalCities > 0)
                    Settle(row, originalCities <= 0 && rivalCities > 0,
                        "zero_city_victory");
                else
                    Close(row, "both_courts_zero_city");
                return;
            }
            Compensate(row, "zero_city_before_war");
        }

        public static bool CanDeclareReunification(Kingdom pAttacker,
            Kingdom pDefender)
        {
            if (pAttacker?.data == null || pDefender?.data == null ||
                pAttacker == pDefender ||
                !TryGetCachedByKingdom(pAttacker.id,
                    out SuccessionDisputeSnapshot row))
                return false;
            bool opposite = pAttacker.id == row.OriginalKingdomId &&
                            pDefender.id == row.RivalKingdomId ||
                            pAttacker.id == row.RivalKingdomId &&
                            pDefender.id == row.OriginalKingdomId;
            int generation = GetReunificationClaimGeneration(pAttacker,
                row);
            bool hasActiveWar = false;
            try
            {
                hasActiveWar = World.world?.wars?.getWar(pAttacker,
                    pDefender, pOnlyMain: false) != null;
            }
            catch { }
            return SuccessionDisputeRules.CanUseReunificationClaim(
                row.Status, opposite, generation,
                row.ClaimGenerationBoundary, hasActiveWar);
        }

        public static bool TryDeclareReunificationWar(Kingdom pAttacker,
            Kingdom pDefender)
        {
            if (!CanDeclareReunification(pAttacker, pDefender) ||
                !TryGetCachedByKingdom(pAttacker.id,
                    out SuccessionDisputeSnapshot row))
                return false;
            City targetCapital = pDefender?.capital ??
                                 WarTerritoryService.FindFirstTargetCity(
                                     pDefender);
            if (targetCapital?.data == null) return false;
            War war = WarDecisionService.TryStartWarWithResult(pAttacker,
                pDefender, SuccessionDisputeRules.WarTypeId,
                "succession_reunification");
            if (war?.data == null) return false;
            war.data.set(LineageKeys.SUCCESSION_DISPUTE_ID,
                row.DisputeId);
            war.data.set(
                LineageKeys.SUCCESSION_DISPUTE_ORIGINAL_KINGDOM_ID,
                row.OriginalKingdomId);
            war.data.set(LineageKeys.SUCCESSION_DISPUTE_RIVAL_KINGDOM_ID,
                row.RivalKingdomId);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    SuccessionDisputeTableItem.GetTableName() +
                    " SET WAR_ID=@war WHERE DISPUTE_ID=@id " +
                    "AND STATUS=@status AND END_TIME<0";
                command.Parameters.AddWithValue("@war", war.data.id);
                command.Parameters.AddWithValue("@id", row.DisputeId);
                command.Parameters.AddWithValue("@status",
                    (int)SuccessionDisputeStatus.PermanentSplit);
                if (command.ExecuteNonQuery() != 1)
                {
                    try { World.world?.wars?.endWar(war, WarWinner.Nobody); }
                    catch { }
                    return false;
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Succession reunification war bind failed: " +
                    exception.Message);
                try { World.world?.wars?.endWar(war, WarWinner.Nobody); }
                catch { }
                return false;
            }
            row.WarId = war.data.id;
            WarGoalCreateResult goal =
                WarTerritoryService.TryPersistGoalOrEndWar(war,
                    new WarTerritoryService.WarGoalRequest
                    {
                        goal_type =
                            WarTerritoryService.GOAL_REUNIFY_SUCCESSION,
                        target_kingdom = pDefender,
                        target_city = targetCapital,
                        claimant = pAttacker.king
                    });
            if (!goal.Success) return false;
            Publish(row);
            return true;
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (!Ready) return;
            int generation = _runtimeRebuildGeneration;
            int count = LoadRuntimePage(-1L, out long lastDisputeId);
            if (count == RuntimeRebuildPageSize)
                EnqueueRebuildPage(lastDisputeId, generation);
        }

        private static int LoadRuntimePage(long pAfterDisputeId,
            out long pLastDisputeId)
        {
            pLastDisputeId = pAfterDisputeId;
            var rows = new List<SuccessionDisputeSnapshot>(
                RuntimeRebuildPageSize);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = SelectColumns() +
                    " WHERE STATUS<>@closed AND END_TIME<0 " +
                    "AND DISPUTE_ID>@after ORDER BY DISPUTE_ID LIMIT @limit";
                command.Parameters.AddWithValue("@closed",
                    (int)SuccessionDisputeStatus.Closed);
                command.Parameters.AddWithValue("@after", pAfterDisputeId);
                command.Parameters.AddWithValue("@limit",
                    RuntimeRebuildPageSize);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) rows.Add(Read(reader));
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Succession dispute rebuild failed: " +
                                    exception.Message);
                return 0;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                SuccessionDisputeSnapshot row = rows[i];
                pLastDisputeId = row.DisputeId;
                Kingdom original = FindKingdom(row.OriginalKingdomId);
                Kingdom rival = FindKingdom(row.RivalKingdomId);
                Dictionary<long, string> presentationsBefore =
                    CaptureNamePresentations(original, rival);
                Publish(row);
                ApplyHotIds(row);
                CommitCanonicalNames(row);
                RefreshChangedNamePresentations(presentationsBefore,
                    original, rival);
                if (row.Status < SuccessionDisputeStatus.Active)
                    Enqueue(row.DisputeId);
            }
            return rows.Count;
        }

        private static void EnqueueRebuildPage(long pAfterDisputeId,
            int pGeneration)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "succession_dispute_rebuild:" + pGeneration + ":" +
                pAfterDisputeId, DeferredWorkClass.Persistent,
                () =>
                {
                    if (!Ready || pGeneration != _runtimeRebuildGeneration)
                        return;
                    int count = LoadRuntimePage(pAfterDisputeId,
                        out long lastDisputeId);
                    if (count == RuntimeRebuildPageSize &&
                        lastDisputeId > pAfterDisputeId)
                        EnqueueRebuildPage(lastDisputeId, pGeneration);
                });
        }

        public static void ClearRuntime()
        {
            ById.Clear();
            ByKingdom.Clear();
            CompensationAttempts.Clear();
            _runtimeRebuildGeneration = unchecked(
                _runtimeRebuildGeneration + 1);
        }

        private static void Enqueue(long pDisputeId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "succession_dispute:" + pDisputeId,
                DeferredWorkClass.Persistent,
                () => Advance(pDisputeId));
        }

        private static void Advance(long pDisputeId)
        {
            SuccessionDisputeSnapshot row = Read(pDisputeId);
            if (row == null || row.Status >= SuccessionDisputeStatus.Active)
                return;
            bool advanced;
            try
            {
                advanced = row.Status switch
                {
                    SuccessionDisputeStatus.Prepared => CreateRival(row),
                    SuccessionDisputeStatus.RivalCreated =>
                        TransferCities(row),
                    SuccessionDisputeStatus.CitiesTransferred => StartWar(row),
                    SuccessionDisputeStatus.WarStarted => Activate(row),
                    _ => false
                };
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Succession dispute stage failed: " +
                                    exception.Message);
                Compensate(row, "advance_exception");
                return;
            }
            if (!advanced)
            {
                Compensate(row, "advance_failed");
                return;
            }
            SuccessionDisputeSnapshot next = Read(pDisputeId);
            if (next != null) Publish(next);
            if (next != null && next.Status <
                SuccessionDisputeStatus.Active)
                Enqueue(pDisputeId);
        }

        private static bool CreateRival(SuccessionDisputeSnapshot pRow)
        {
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Actor successor = FindActor(pRow.SuccessorActorId);
            Actor claimant = FindActor(pRow.ClaimantActorId);
            List<City> cities = ReadCities(pRow.DisputeId);
            City seed = cities.Count > 0 ? cities[0] : null;
            int originalCitiesBefore = CountLiveCities(original);
            if (original?.data == null || original.king != successor ||
                claimant?.data == null || claimant.isRekt() ||
                seed?.data == null || cities.Count <= 0 ||
                !SuccessionDisputeRules.CanFormBalancedTerritorialSplit(
                    originalCitiesBefore, cities.Count))
                return false;
            Dictionary<long, string> presentationsBefore =
                CaptureNamePresentations(original);
            Kingdom rival;
            if (seed.kingdom != original &&
                claimant.kingdom == seed.kingdom &&
                seed.kingdom?.king == claimant)
            {
                rival = seed.kingdom;
            }
            else
            {
                if (claimant.kingdom != original ||
                    seed.kingdom != original ||
                    originalCitiesBefore <= cities.Count)
                    return false;
                rival = null;
                FeudatoryService.BeginIntentionalJingnanTransfer();
                try
                {
                    AWLocalizedNameProjectionRefreshScope.Suppress(() =>
                        rival = seed.makeOwnKingdom(claimant,
                            pRebellion: true, pFellApart: false));
                }
                finally
                {
                    FeudatoryService.EndIntentionalJingnanTransfer();
                }
            }
            if (rival?.data != null && !rival.isRekt())
                presentationsBefore[rival.getID()] = GetDisplayName(rival);
            if (rival?.data != null) pRow.RivalKingdomId = rival.id;
            if (rival?.data == null || seed.kingdom != rival ||
                !SuccessionDisputeRules.CanMaintainTerritorialInvariant(
                    CountLiveCities(original), CountLiveCities(rival)))
            {
                ReturnCities(original, rival);
                RemoveIfEmpty(rival);
                return false;
            }
            int updated;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    SuccessionDisputeTableItem.GetTableName() +
                    " SET RIVAL_KINGDOM_ID=@rival,STATUS=@next " +
                    "WHERE DISPUTE_ID=@id AND STATUS=@expected";
                command.Parameters.AddWithValue("@rival", rival.id);
                command.Parameters.AddWithValue("@next",
                    (int)SuccessionDisputeStatus.RivalCreated);
                command.Parameters.AddWithValue("@id", pRow.DisputeId);
                command.Parameters.AddWithValue("@expected",
                    (int)SuccessionDisputeStatus.Prepared);
                updated = command.ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Succession rival bind failed: " +
                                    exception.Message);
                if (ReturnCities(original, rival)) RemoveIfEmpty(rival);
                return false;
            }
            if (updated != 1)
            {
                if (ReturnCities(original, rival)) RemoveIfEmpty(rival);
                return false;
            }
            pRow.Status = SuccessionDisputeStatus.RivalCreated;
            Publish(pRow);
            CommitCanonicalNames(pRow);
            RefreshChangedNamePresentations(presentationsBefore,
                original, rival);
            ChronicleEvents.OnSuccessionDisputeStarted(original, rival,
                successor, claimant, GetDisplayName(original),
                GetDisplayName(rival));
            return true;
        }

        private static bool TransferCities(SuccessionDisputeSnapshot pRow)
        {
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            if (original?.data == null || rival?.data == null) return false;
            List<City> cities = ReadCities(pRow.DisputeId);
            int moving = 0;
            for (int i = 0; i < cities.Count; i++)
                if (cities[i]?.kingdom == original) moving++;
            if (!SuccessionDisputeRules.CanMaintainTerritorialInvariant(
                    CountLiveCities(original) - moving,
                    CountLiveCities(rival) + moving) ||
                !SuccessionDisputeRules.CanFormBalancedTerritorialSplit(
                    CountLiveCities(original) + CountLiveCities(rival),
                    CountLiveCities(rival) + moving))
                return false;
            FeudatoryService.BeginIntentionalJingnanTransfer();
            try
            {
                for (int i = 0; i < cities.Count; i++)
                {
                    City city = cities[i];
                    if (city?.data == null) return false;
                    if (city.kingdom == rival) continue;
                    if (city.kingdom != original) return false;
                    city.joinAnotherKingdom(rival, pCaptured: false,
                        pRebellion: true);
                    if (city.kingdom != rival) return false;
                }
            }
            finally
            {
                FeudatoryService.EndIntentionalJingnanTransfer();
            }
            if (!SuccessionDisputeRules.CanMaintainTerritorialInvariant(
                    CountLiveCities(original), CountLiveCities(rival)))
                return false;
            return UpdateStatus(pRow.DisputeId,
                SuccessionDisputeStatus.RivalCreated,
                SuccessionDisputeStatus.CitiesTransferred);
        }

        private static bool StartWar(SuccessionDisputeSnapshot pRow)
        {
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            if (original?.data == null || rival?.data == null ||
                !SuccessionDisputeRules.CanMaintainTerritorialInvariant(
                    CountLiveCities(original), CountLiveCities(rival)) ||
                !SuccessionDisputeRules.CanFormBalancedTerritorialSplit(
                    CountLiveCities(original) + CountLiveCities(rival),
                    CountLiveCities(rival)))
                return false;
            War war = FindDisputeWar(pRow) ??
                      WarDecisionService.TryStartSystemWar(rival, original,
                          SuccessionDisputeRules.WarTypeId,
                          "succession_dispute");
            if (war?.data == null) return false;
            pRow.WarId = war.data.id;
            war.data.set(LineageKeys.SUCCESSION_DISPUTE_ID,
                pRow.DisputeId);
            war.data.set(
                LineageKeys.SUCCESSION_DISPUTE_ORIGINAL_KINGDOM_ID,
                pRow.OriginalKingdomId);
            war.data.set(LineageKeys.SUCCESSION_DISPUTE_RIVAL_KINGDOM_ID,
                pRow.RivalKingdomId);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    SuccessionDisputeTableItem.GetTableName() +
                    " SET WAR_ID=@war,START_TIME=@time,STATUS=@next " +
                    "WHERE DISPUTE_ID=@id AND STATUS=@expected";
                command.Parameters.AddWithValue("@war", war.data.id);
                command.Parameters.AddWithValue("@time",
                    LineageService.CurTime());
                command.Parameters.AddWithValue("@next",
                    (int)SuccessionDisputeStatus.WarStarted);
                command.Parameters.AddWithValue("@id", pRow.DisputeId);
                command.Parameters.AddWithValue("@expected",
                    (int)SuccessionDisputeStatus.CitiesTransferred);
                if (command.ExecuteNonQuery() == 1) return true;
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Succession war bind failed: " + exception.Message);
            }
            EndUnboundWar(war);
            return false;
        }

        private static bool EndUnboundWar(War pWar)
        {
            if (pWar?.data == null) return true;
            try
            {
                if (pWar.hasEnded()) return true;
                World.world?.wars?.endWar(pWar, WarWinner.Nobody);
                return pWar.hasEnded() || FindWar(pWar.data.id) == null;
            }
            catch { return false; }
        }

        private static bool Activate(SuccessionDisputeSnapshot pRow)
        {
            War war = FindWar(pRow.WarId);
            if (war?.data == null || war.hasEnded()) return false;
            return UpdateStatus(pRow.DisputeId,
                SuccessionDisputeStatus.WarStarted,
                SuccessionDisputeStatus.Active);
        }

        private static void Settle(SuccessionDisputeSnapshot pRow,
            bool pClaimantWins, string pReason)
        {
            if (!UpdateStatus(pRow.DisputeId, pRow.Status,
                    SuccessionDisputeStatus.Settling)) return;
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            if (original?.data == null)
            {
                Close(pRow, pReason + "_original_missing");
                return;
            }
            ReturnCities(original, rival);
            if (pClaimantWins)
            {
                Actor claimant = FindActor(pRow.ClaimantActorId);
                if (PrepareAccession(claimant, original))
                {
                    PrepareClaimantAccessionMode(pRow, original, claimant);
                    try
                    {
                        if (original.king?.data != null &&
                            original.king != claimant)
                            original.kingLeftEvent();
                        original.setKing(claimant);
                    }
                    catch (Exception exception)
                    {
                        ModClass.LogWarning(
                            "Succession claimant accession failed: " +
                            exception.Message);
                    }
                }
            }
            ChronicleEvents.OnSuccessionDisputeResolved(original,
                FindActor(pRow.SuccessorActorId),
                FindActor(pRow.ClaimantActorId), pClaimantWins,
                GetDisplayName(original),
                rival?.data == null ? pRow.OriginalStateName :
                    GetDisplayName(rival));
            Close(pRow, pReason);
            RemoveIfEmpty(rival);
        }

        private static void PrepareClaimantAccessionMode(
            SuccessionDisputeSnapshot pRow, Kingdom pKingdom,
            Actor pClaimant)
        {
            if (pRow == null || pKingdom?.data == null ||
                pClaimant?.data == null) return;
            HeirService.ClearHeir(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_HEIR_ID,
                pClaimant.data.id);
            pKingdom.data.set(LineageKeys.KINGDOM_SUCCESSION_MODE,
                string.IsNullOrEmpty(pRow.ClaimantMode)
                    ? SuccessionMode.COLLATERAL_RESTORE
                    : pRow.ClaimantMode);
            HeirMinimapMarkerIndex.Refresh(pKingdom);
        }

        private static void SettleReunification(
            SuccessionDisputeSnapshot pRow, long pWinnerKingdomId,
            string pReason)
        {
            if (pRow == null ||
                pWinnerKingdomId != pRow.OriginalKingdomId &&
                pWinnerKingdomId != pRow.RivalKingdomId ||
                !UpdateStatus(pRow.DisputeId,
                    SuccessionDisputeStatus.PermanentSplit,
                    SuccessionDisputeStatus.Settling))
                return;
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            if (original?.data == null)
            {
                Close(pRow, pReason + "_original_missing");
                return;
            }
            Actor winningRuler = pWinnerKingdomId == pRow.RivalKingdomId
                ? rival?.king
                : original.king;
            string originalDisplay = GetDisplayName(original);
            string rivalDisplay = rival?.data == null
                ? pRow.OriginalStateName
                : GetDisplayName(rival);
            ReturnCities(original, rival);
            if (pWinnerKingdomId == pRow.RivalKingdomId &&
                PrepareAccession(winningRuler, original))
            {
                try
                {
                    if (original.king?.data != null &&
                        original.king != winningRuler)
                        original.kingLeftEvent();
                    original.setKing(winningRuler);
                }
                catch (Exception exception)
                {
                    ModClass.LogWarning(
                        "Succession reunification accession failed: " +
                        exception.Message);
                }
            }
            ChronicleEvents.OnSuccessionReunified(original, winningRuler,
                originalDisplay, rivalDisplay);
            Close(pRow, pReason);
            RemoveIfEmpty(rival);
        }

        private static void ClearReunificationWar(
            SuccessionDisputeSnapshot pRow)
        {
            if (pRow == null || !Ready) return;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    SuccessionDisputeTableItem.GetTableName() +
                    " SET WAR_ID=-1 WHERE DISPUTE_ID=@id " +
                    "AND STATUS=@status";
                command.Parameters.AddWithValue("@id", pRow.DisputeId);
                command.Parameters.AddWithValue("@status",
                    (int)SuccessionDisputeStatus.PermanentSplit);
                command.ExecuteNonQuery();
                pRow.WarId = -1L;
                Publish(pRow);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning(
                    "Succession reunification war clear failed: " +
                    exception.Message);
            }
        }

        private static void Compensate(SuccessionDisputeSnapshot pRow,
            string pReason)
        {
            if (pRow == null) return;
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            War temporaryWar = FindDisputeWar(pRow);
            bool warEnded = EndUnboundWar(temporaryWar);
            bool citiesReturned = ReturnCities(original, rival);
            bool canClose = citiesReturned &&
                SuccessionDisputeRules.CanCloseCompensation(
                    CountLiveCities(original), CountLiveCities(rival),
                    !warEnded);
            if (!canClose)
            {
                EnqueueCompensation(pRow.DisputeId,
                    pRow.RivalKingdomId, temporaryWar?.data?.id ?? pRow.WarId,
                    pReason);
                return;
            }
            if (!Close(pRow, pReason))
            {
                EnqueueCompensation(pRow.DisputeId,
                    pRow.RivalKingdomId, -1L, pReason);
                return;
            }
            RemoveIfEmpty(rival);
        }

        private static void EnqueueCompensation(long pDisputeId,
            long pTransientRivalId, long pTransientWarId, string pReason)
        {
            CompensationAttempts.TryGetValue(pDisputeId, out int attempts);
            if (attempts >= MaximumCompensationAttempts)
            {
                ModClass.LogWarning(
                    "Succession compensation remains incomplete: dispute=" +
                    pDisputeId);
                return;
            }
            CompensationAttempts[pDisputeId] = attempts + 1;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "succession_dispute_compensate:" + pDisputeId + ":" +
                attempts,
                DeferredWorkClass.Persistent,
                () => RetryCompensation(pDisputeId, pTransientRivalId,
                    pTransientWarId, pReason));
        }

        private static void RetryCompensation(long pDisputeId,
            long pTransientRivalId, long pTransientWarId, string pReason)
        {
            SuccessionDisputeSnapshot row = Read(pDisputeId);
            if (row == null) return;
            if (row.Status == SuccessionDisputeStatus.Closed)
            {
                CompensationAttempts.Remove(pDisputeId);
                return;
            }
            if (row.RivalKingdomId < 0) row.RivalKingdomId = pTransientRivalId;
            if (row.WarId < 0) row.WarId = pTransientWarId;
            Compensate(row, pReason + "_retry");
        }

        private static bool Close(SuccessionDisputeSnapshot pRow,
            string pReason)
        {
            if (pRow == null || !Ready) return false;
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            Dictionary<long, string> presentationsBefore =
                CaptureClosingNamePresentations(pRow, original, rival);
            double now = LineageService.CurTime();
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using (var dispute = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    dispute.CommandText = "UPDATE " +
                        SuccessionDisputeTableItem.GetTableName() +
                        " SET STATUS=@closed,END_TIME=@time,END_REASON=@reason " +
                        "WHERE DISPUTE_ID=@id AND STATUS<>@closed";
                    dispute.Parameters.AddWithValue("@closed",
                        (int)SuccessionDisputeStatus.Closed);
                    dispute.Parameters.AddWithValue("@time", now);
                    dispute.Parameters.AddWithValue("@reason", pReason ?? "");
                    dispute.Parameters.AddWithValue("@id", pRow.DisputeId);
                    if (dispute.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "succession dispute close raced");
                }
                using (var cities = new SQLiteCommand(DB)
                       { Transaction = transaction })
                {
                    cities.CommandText = "UPDATE " +
                        SuccessionDisputeCityTableItem.GetTableName() +
                        " SET ACTIVE=0,END_TIME=@time,END_REASON=@reason " +
                        "WHERE DISPUTE_ID=@id AND ACTIVE=1";
                    cities.Parameters.AddWithValue("@time", now);
                    cities.Parameters.AddWithValue("@reason", pReason ?? "");
                    cities.Parameters.AddWithValue("@id", pRow.DisputeId);
                    cities.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Succession dispute close failed: " +
                                    exception.Message);
                return false;
            }
            CommitCanonicalNames(pRow);
            ClearHotId(original, pRow.DisputeId);
            ClearHotId(rival, pRow.DisputeId);
            ById.Remove(pRow.DisputeId);
            ByKingdom.Remove(pRow.OriginalKingdomId);
            ByKingdom.Remove(pRow.RivalKingdomId);
            CompensationAttempts.Remove(pRow.DisputeId);
            RefreshChangedNamePresentations(presentationsBefore,
                original, rival);
            return true;
        }

        private static void CommitCanonicalNames(
            SuccessionDisputeSnapshot pRow)
        {
            if (pRow == null) return;
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            AWLocalizedNameIdentitySnapshot originalIdentity =
                original?.data == null || original.isRekt()
                    ? null
                    : AWLocalizedNamePersistence.Capture(original.data);
            AWLocalizedNameIdentitySnapshot rivalIdentity =
                rival?.data == null || rival.isRekt()
                    ? null
                    : AWLocalizedNamePersistence.Capture(rival.data);
            AWLocalizedNameIdentitySnapshot authorityIdentity =
                AWLocalizedKingdomIdentitySyncAdapter.SelectAuthority(
                    originalIdentity, rivalIdentity);
            Kingdom authority = ReferenceEquals(authorityIdentity,
                originalIdentity) ? original : rival;
            if (authority?.data == null) return;
            var members = new List<Kingdom>(2);
            if (original?.data != null && !original.isRekt())
                members.Add(original);
            if (rival?.data != null && !rival.isRekt())
                members.Add(rival);
            AWLocalizedKingdomNameService.SynchronizeSharedIdentity(
                authority, members);
        }

        private static Dictionary<long, string> CaptureNamePresentations(
            params Kingdom[] pKingdoms)
        {
            var result = new Dictionary<long, string>();
            if (pKingdoms == null) return result;
            for (int i = 0; i < pKingdoms.Length; i++)
            {
                Kingdom kingdom = pKingdoms[i];
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                result[kingdom.getID()] = GetDisplayName(kingdom);
            }
            return result;
        }

        private static Dictionary<long, string>
            CaptureClosingNamePresentations(
                SuccessionDisputeSnapshot pRow, Kingdom pOriginal,
                Kingdom pRival)
        {
            if (pRow == null || !SuccessionDisputeDisplayRules.
                    HasPersistedClosingQualifier(pRow.Status,
                        pRow.RivalKingdomId))
                return CaptureNamePresentations(pOriginal, pRival);

            var result = new Dictionary<long, string>();
            string sharedBase = !string.IsNullOrWhiteSpace(
                    pOriginal?.data?.name) && !pOriginal.isRekt()
                ? pOriginal.data.name.Trim()
                : !string.IsNullOrWhiteSpace(pRival?.data?.name) &&
                  !pRival.isRekt()
                    ? pRival.data.name.Trim()
                    : pRow.OriginalStateName ?? string.Empty;
            string language = HistoryLocalizationRules.CurrentLanguage();
            if (pOriginal?.data != null && !pOriginal.isRekt())
                result[pOriginal.getID()] = SuccessionDisputeDisplayRules.
                    BuildQualifiedName(sharedBase, pRow.OriginalQualifier,
                        active: true, language);
            if (pRival?.data != null && !pRival.isRekt())
                result[pRival.getID()] = SuccessionDisputeDisplayRules.
                    BuildQualifiedName(sharedBase, pRow.RivalQualifier,
                        active: true, language);
            return result;
        }

        private static void RefreshChangedNamePresentations(
            IReadOnlyDictionary<long, string> pBefore,
            params Kingdom[] pKingdoms)
        {
            if (pKingdoms == null) return;
            var invalidatedIds = new HashSet<long>();
            for (int i = 0; i < pKingdoms.Length; i++)
            {
                Kingdom kingdom = pKingdoms[i];
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                long id = kingdom.getID();
                string before = pBefore != null &&
                                pBefore.TryGetValue(id, out string captured)
                    ? captured
                    : string.Empty;
                string after = GetDisplayName(kingdom);
                if (AWLocalizedNameProjectionChangeRules.TryMarkInvalidated(
                        invalidatedIds, id, before, after))
                    KingdomRenameProjectionService.Refresh(kingdom);
            }
        }

        private sealed class LocalCitySupport
        {
            public City City;
            public bool IsCapital;
            public int Claimant;
            public int Loyalist;
        }

        private static List<City> SelectSupportCities(Kingdom pKingdom,
            Actor pClaimant, Actor pSuccessor, string pClaimantMode,
            string pSuccessorMode, InheritanceFactionSupport pFactionSupport)
        {
            var result = new List<City>(
                SuccessionDisputeRules.MaximumRivalCities);
            if (pKingdom?.data == null || pKingdom.capital?.data == null ||
                pClaimant?.data == null || pSuccessor?.data == null)
                return result;
            var support = new Dictionary<long, LocalCitySupport>();
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && !city.isRekt())
                        support[city.id] = new LocalCitySupport
                        {
                            City = city,
                            IsCapital = city == pKingdom.capital
                        };
            }
            catch { }
            int limit = SuccessionDisputeRules.CityLimit(
                pKingdom.countCities());
            if (limit <= 0 || support.Count == 0) return result;

            foreach (LocalCitySupport row in support.Values)
                AddAuthoritativeSupport(row, row.City.leader, 8,
                    pFactionSupport?.CivilSupportTargetByActorId,
                    pClaimant, pSuccessor);

            List<GeneralReadModelEntry> generals =
                GeneralService.GetActiveGeneralsForReadModel(pKingdom,
                    pAllowUnitFallback: false);
            int generalLimit = Math.Min(64, generals.Count);
            for (int i = 0; i < generalLimit; i++)
            {
                GeneralReadModelEntry general = generals[i];
                Actor actor = general?.Actor;
                if (actor?.city?.data == null ||
                    !support.TryGetValue(actor.city.id,
                        out LocalCitySupport row)) continue;
                AddAuthoritativeSupport(row, actor, 2 + Math.Min(8,
                        Math.Max(0, general.Merit) / 10),
                    pFactionSupport?.MilitarySupportTargetByActorId,
                    pClaimant, pSuccessor);
            }

            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(
                pKingdom, 96);
            for (int i = 0; i < officers.Count; i++)
            {
                CourtOfficerView officer = officers[i];
                if (!support.TryGetValue(officer.city_id,
                        out LocalCitySupport row)) continue;
                AddAuthoritativeSupport(row, FindActor(officer.actor_id),
                    1 + Math.Min(7, Math.Max(0,
                        (int)Math.Round(officer.influence)) / 10),
                    pFactionSupport?.CivilSupportTargetByActorId,
                    pClaimant, pSuccessor);
            }

            AddLocalBranchSupport(support, pClaimant, pSuccessor,
                pClaimantMode, pSuccessorMode);

            var facts = new List<SuccessionCitySupportFacts>(support.Count);
            foreach (LocalCitySupport row in support.Values)
                facts.Add(new SuccessionCitySupportFacts(row.City.id,
                    row.IsCapital, row.Claimant, row.Loyalist));
            IReadOnlyList<long> selected = SuccessionDisputeRules
                .SelectBalancedSupportCityIds(facts,
                    CountLiveCities(pKingdom), limit);
            for (int i = 0; i < selected.Count; i++)
                if (support.TryGetValue(selected[i], out LocalCitySupport row))
                    result.Add(row.City);

            return result;
        }

        private static void AddAuthoritativeSupport(LocalCitySupport pRow,
            Actor pSupporter, int pWeight,
            IReadOnlyDictionary<long, long> pSupportTargets,
            Actor pClaimant, Actor pSuccessor)
        {
            if (pRow == null || pSupporter?.data == null || pWeight <= 0 ||
                pSupportTargets == null || pClaimant?.data == null ||
                pSuccessor?.data == null ||
                !pSupportTargets.TryGetValue(pSupporter.data.id,
                    out long recordedTarget)) return;
            long target = SuccessionDisputeRules
                .SelectAuthoritativeSupportTarget(recordedTarget,
                    pClaimant.data.id, pSuccessor.data.id);
            if (target == pClaimant.data.id) pRow.Claimant += pWeight;
            else if (target == pSuccessor.data.id)
                pRow.Loyalist += pWeight;
        }

        private static void AddLocalSupport(LocalCitySupport pRow,
            Actor pSupporter, int pWeight, Actor pClaimant,
            Actor pSuccessor, string pLocalFactionMode,
            string pClaimantMode, string pSuccessorMode)
        {
            if (pRow == null || pSupporter?.data == null || pWeight <= 0)
                return;
            pSupporter.data.get(LineageKeys.SHI_ID,
                out long supporterShi, -1L);
            pClaimant.data.get(LineageKeys.SHI_ID,
                out long claimantShi, -1L);
            pSuccessor.data.get(LineageKeys.SHI_ID,
                out long successorShi, -1L);
            long target = SuccessionDisputeRules
                .SelectLocalFactionSupportTarget(supporterShi,
                    claimantShi, pClaimant.data.id, successorShi,
                    pSuccessor.data.id, pLocalFactionMode,
                    pClaimantMode, pSuccessorMode);
            if (target == pClaimant.data.id) pRow.Claimant += pWeight;
            else if (target == pSuccessor.data.id)
                pRow.Loyalist += pWeight;
        }

        private static void AddLocalBranchSupport(
            IReadOnlyDictionary<long, LocalCitySupport> pSupport,
            Actor pClaimant, Actor pSuccessor, string pClaimantMode,
            string pSuccessorMode)
        {
            if (pSupport == null || pClaimant?.data == null ||
                pSuccessor?.data == null) return;
            pClaimant.data.get(LineageKeys.SHI_ID,
                out long claimantShi, -1L);
            pSuccessor.data.get(LineageKeys.SHI_ID,
                out long successorShi, -1L);
            if (claimantShi < 0 || successorShi < 0) return;
            if (claimantShi == successorShi)
            {
                AddSharedShiAgnaticMembers(pSupport, claimantShi,
                    pClaimant, pSuccessor);
                return;
            }
            AddBranchMembers(pSupport, claimantShi, pClaimant,
                pSuccessor, pClaimantMode, pSuccessorMode);
            AddBranchMembers(pSupport, successorShi, pClaimant,
                pSuccessor, pClaimantMode, pSuccessorMode);
        }

        private static void AddSharedShiAgnaticMembers(
            IReadOnlyDictionary<long, LocalCitySupport> pSupport,
            long pShiId, Actor pClaimant, Actor pSuccessor)
        {
            foreach (long actorId in LineageQuery.GetLivingShiMemberIds(
                         pShiId, 256))
            {
                Actor member = FindActor(actorId);
                if (member?.data == null || member.city?.data == null ||
                    !pSupport.TryGetValue(member.city.id,
                        out LocalCitySupport row)) continue;
                bool claimantLine = actorId == pClaimant.data.id ||
                    LineageQuery.IsAgnaticDescendantOf(actorId,
                        pClaimant.data.id);
                bool successorLine = actorId == pSuccessor.data.id ||
                    LineageQuery.IsAgnaticDescendantOf(actorId,
                        pSuccessor.data.id);
                long target = SuccessionDisputeRules
                    .SelectAgnaticBranchSupportTarget(claimantLine,
                        successorLine, pClaimant.data.id,
                        pSuccessor.data.id);
                if (target == pClaimant.data.id) row.Claimant++;
                else if (target == pSuccessor.data.id) row.Loyalist++;
            }
        }

        private static void AddBranchMembers(
            IReadOnlyDictionary<long, LocalCitySupport> pSupport,
            long pShiId, Actor pClaimant, Actor pSuccessor,
            string pClaimantMode, string pSuccessorMode)
        {
            foreach (long actorId in LineageQuery.GetLivingShiMemberIds(
                         pShiId, 256))
            {
                Actor member = FindActor(actorId);
                if (member?.city?.data == null ||
                    !pSupport.TryGetValue(member.city.id,
                        out LocalCitySupport row)) continue;
                AddLocalSupport(row, member, 1, pClaimant, pSuccessor,
                    string.Empty, pClaimantMode, pSuccessorMode);
            }
        }

        private static bool IsLegalAlternative(Actor pActor,
            Actor pInstalled, Kingdom pKingdom)
        {
            return pActor?.data != null && pActor != pInstalled &&
                   pActor.kingdom == pKingdom && pActor.isAlive() &&
                   !pActor.isRekt() && pActor.isSexMale() &&
                   pActor.isAdult() && !pActor.isKing() &&
                   !SlaveService.IsSlave(pActor) &&
                   !pActor.hasTrait("madness");
        }

        private static SuccessionDirection ResolveDirection(City pCapital,
            City pRivalSeat, bool claimantAccededLater)
        {
            try
            {
                return SuccessionDisputeRules.ResolveDirection(
                    pRivalSeat.getTile().pos.x - pCapital.getTile().pos.x,
                    pRivalSeat.getTile().pos.y - pCapital.getTile().pos.y,
                    claimantAccededLater);
            }
            catch
            {
                return claimantAccededLater
                    ? SuccessionDirection.Later
                    : SuccessionDirection.Former;
            }
        }

        private static SuccessionDirection Opposite(
            SuccessionDirection pDirection)
        {
            return pDirection switch
            {
                SuccessionDirection.East => SuccessionDirection.West,
                SuccessionDirection.West => SuccessionDirection.East,
                SuccessionDirection.North => SuccessionDirection.South,
                SuccessionDirection.South => SuccessionDirection.North,
                SuccessionDirection.Later => SuccessionDirection.Former,
                _ => SuccessionDirection.Later
            };
        }

        private static bool ReturnCities(Kingdom pOriginal,
            Kingdom pRival)
        {
            if (pOriginal?.data == null) return false;
            if (pRival?.data == null) return true;
            var cities = new List<City>();
            try
            {
                foreach (City city in pRival.getCities())
                    if (city?.data != null) cities.Add(city);
            }
            catch { return false; }
            bool returned = true;
            FeudatoryService.BeginIntentionalJingnanTransfer();
            try
            {
                for (int i = 0; i < cities.Count; i++)
                {
                    if (cities[i].kingdom == pRival)
                    {
                        try
                        {
                            cities[i].joinAnotherKingdom(pOriginal,
                                pCaptured: false, pRebellion: false);
                        }
                        catch { returned = false; }
                    }
                    if (cities[i].kingdom != pOriginal) returned = false;
                }
            }
            finally
            {
                FeudatoryService.EndIntentionalJingnanTransfer();
            }
            return returned && CountLiveCities(pRival) == 0;
        }

        private static bool PrepareAccession(Actor pClaimant,
            Kingdom pOriginal)
        {
            City capital = pOriginal?.capital;
            if (pClaimant?.data == null || pClaimant.isRekt() ||
                capital?.data == null) return false;
            CourtService.ClearOfficeForReignTransition(pClaimant,
                "succession_dispute_accession");
            try { if (pClaimant.hasArmy()) pClaimant.removeFromArmy(); }
            catch { }
            try { pClaimant.stopBeingWarrior(); } catch { }
            try
            {
                using (FormalAffiliationTransferScope.Open(
                           pClaimant.data.id, pOriginal.id, capital.data.id))
                    pClaimant.joinCity(capital);
            }
            catch { return false; }
            return pClaimant.kingdom == pOriginal;
        }

        private static void RemoveIfEmpty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            try
            {
                if (pKingdom.countCities() == 0)
                    World.world?.kingdoms?.removeObject(pKingdom);
            }
            catch { }
        }

        private static int CountLiveCities(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            int count = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && !city.isRekt() &&
                        city.kingdom == pKingdom)
                        count++;
            }
            catch
            {
                try { count = Math.Max(0, pKingdom.countCities()); }
                catch { count = 0; }
            }
            return count;
        }

        private static bool UpdateStatus(long pDisputeId,
            SuccessionDisputeStatus pExpected,
            SuccessionDisputeStatus pNext)
        {
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " +
                    SuccessionDisputeTableItem.GetTableName() +
                    " SET STATUS=@next WHERE DISPUTE_ID=@id " +
                    "AND STATUS=@expected AND END_TIME<0";
                command.Parameters.AddWithValue("@next", (int)pNext);
                command.Parameters.AddWithValue("@id", pDisputeId);
                command.Parameters.AddWithValue("@expected", (int)pExpected);
                return command.ExecuteNonQuery() == 1;
            }
            catch { return false; }
        }

        private static List<City> ReadCities(long pDisputeId)
        {
            var result = new List<City>(
                SuccessionDisputeRules.MaximumRivalCities);
            if (!Ready || pDisputeId < 0) return result;
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT CITY_ID FROM " +
                SuccessionDisputeCityTableItem.GetTableName() +
                " WHERE DISPUTE_ID=@id AND ACTIVE=1 " +
                "ORDER BY ORDINAL,CITY_ID LIMIT " +
                SuccessionDisputeRules.MaximumRivalCities;
            command.Parameters.AddWithValue("@id", pDisputeId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                City city = FindCity(reader.GetInt64(0));
                if (city?.data != null) result.Add(city);
            }
            return result;
        }

        private static SuccessionDisputeSnapshot Read(long pDisputeId)
        {
            if (!Ready || pDisputeId < 0) return null;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = SelectColumns() +
                    " WHERE DISPUTE_ID=@id LIMIT 1";
                command.Parameters.AddWithValue("@id", pDisputeId);
                using SQLiteDataReader reader = command.ExecuteReader();
                return reader.Read() ? Read(reader) : null;
            }
            catch { return null; }
        }

        private static SuccessionDisputeSnapshot ReadActiveByKingdom(
            long pKingdomId)
        {
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = SelectColumns() +
                    " WHERE (ORIGINAL_KINGDOM_ID=@kingdom OR " +
                    "RIVAL_KINGDOM_ID=@kingdom) AND STATUS<>@closed " +
                    "AND END_TIME<0 ORDER BY DISPUTE_ID DESC LIMIT 1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@closed",
                    (int)SuccessionDisputeStatus.Closed);
                using SQLiteDataReader reader = command.ExecuteReader();
                return reader.Read() ? Read(reader) : null;
            }
            catch { return null; }
        }

        private static string SelectColumns()
        {
            return "SELECT DISPUTE_ID,ORIGINAL_KINGDOM_ID," +
                   "RIVAL_KINGDOM_ID,PREDECESSOR_ACTOR_ID," +
                   "SUCCESSOR_ACTOR_ID,CLAIMANT_ACTOR_ID," +
                   "ORIGINAL_STATE_NAME,ORIGINAL_QUALIFIER," +
                   "RIVAL_QUALIFIER,ACCESSION_LAW,SUCCESSOR_MODE," +
                   "CLAIMANT_MODE,SUCCESSOR_SUPPORT,CLAIMANT_SUPPORT," +
                   "WAR_ID,DEADLINE_YEAR,STATUS,ORIGINAL_LINEAGE_ID," +
                   "ORIGINAL_SHI_ID,CLAIM_GENERATION_BOUNDARY FROM " +
                   SuccessionDisputeTableItem.GetTableName();
        }

        private static SuccessionDisputeSnapshot Read(
            SQLiteDataReader pReader)
        {
            return new SuccessionDisputeSnapshot
            {
                DisputeId = pReader.GetInt64(0),
                OriginalKingdomId = pReader.GetInt64(1),
                RivalKingdomId = pReader.GetInt64(2),
                PredecessorActorId = pReader.GetInt64(3),
                SuccessorActorId = pReader.GetInt64(4),
                ClaimantActorId = pReader.GetInt64(5),
                OriginalStateName = SafeString(pReader, 6),
                OriginalQualifier = SafeString(pReader, 7),
                RivalQualifier = SafeString(pReader, 8),
                AccessionLaw = InheritanceLawRules.Normalize(
                    Convert.ToInt32(pReader.GetValue(9))),
                SuccessorMode = SafeString(pReader, 10),
                ClaimantMode = SafeString(pReader, 11),
                SuccessorSupport = Convert.ToInt32(pReader.GetValue(12)),
                ClaimantSupport = Convert.ToInt32(pReader.GetValue(13)),
                WarId = pReader.GetInt64(14),
                DeadlineYear = Convert.ToInt32(pReader.GetValue(15)),
                Status = (SuccessionDisputeStatus)Convert.ToInt32(
                    pReader.GetValue(16)),
                OriginalLineageId = pReader.GetInt64(17),
                OriginalShiId = pReader.GetInt64(18),
                ClaimGenerationBoundary = Convert.ToInt32(
                    pReader.GetValue(19))
            };
        }

        private static void Publish(SuccessionDisputeSnapshot pRow)
        {
            if (pRow == null) return;
            pRow.Materialized = IsMaterializedNow(pRow);
            ById[pRow.DisputeId] = pRow;
            ByKingdom[pRow.OriginalKingdomId] = pRow.DisputeId;
            if (pRow.RivalKingdomId >= 0)
                ByKingdom[pRow.RivalKingdomId] = pRow.DisputeId;
            ApplyHotIds(pRow);
        }

        private static void ApplyHotIds(SuccessionDisputeSnapshot pRow)
        {
            Kingdom original = FindKingdom(pRow.OriginalKingdomId);
            Kingdom rival = FindKingdom(pRow.RivalKingdomId);
            original?.data?.set(LineageKeys.ACTIVE_SUCCESSION_DISPUTE_ID,
                pRow.DisputeId);
            rival?.data?.set(LineageKeys.ACTIVE_SUCCESSION_DISPUTE_ID,
                pRow.DisputeId);
            RefreshReunificationGeneration(original, pRow);
            RefreshReunificationGeneration(rival, pRow);
        }

        private static void RefreshReunificationGeneration(Kingdom pKingdom,
            SuccessionDisputeSnapshot pRow)
        {
            if (pKingdom?.data == null || pRow == null) return;
            int generation = -1;
            if (pRow.Status == SuccessionDisputeStatus.PermanentSplit &&
                pKingdom.king?.data != null)
            {
                long anchorActorId =
                    pKingdom.id == pRow.OriginalKingdomId
                        ? pRow.SuccessorActorId
                        : pKingdom.id == pRow.RivalKingdomId
                            ? pRow.ClaimantActorId
                            : -1L;
                generation = RoyalRestorationRules
                    .ResolveAgnaticGeneration(anchorActorId,
                        pKingdom.king.data.id, LineageQuery.GetFatherId);
            }
            pKingdom.data.set(
                LineageKeys.SUCCESSION_REUNIFICATION_GENERATION,
                generation);
        }

        private static void ClearHotId(Kingdom pKingdom, long pDisputeId)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.ACTIVE_SUCCESSION_DISPUTE_ID,
                out long current, -1L);
            if (current == pDisputeId)
                pKingdom.data.set(LineageKeys.ACTIVE_SUCCESSION_DISPUTE_ID,
                    -1L);
            pKingdom.data.set(
                LineageKeys.SUCCESSION_REUNIFICATION_GENERATION, -1);
        }

        private static string SafeString(SQLiteDataReader pReader,
            int pIndex)
        {
            return pReader.IsDBNull(pIndex)
                ? ""
                : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static War FindDisputeWar(SuccessionDisputeSnapshot pRow)
        {
            if (pRow == null) return null;
            War recorded = FindWar(pRow.WarId);
            try
            {
                if (recorded?.data != null && !recorded.hasEnded())
                    return recorded;
            }
            catch { }
            int inspected = 0;
            try
            {
                foreach (War war in World.world.wars)
                {
                    if (inspected++ >= 256) break;
                    if (war?.data == null || war.hasEnded()) continue;
                    war.data.get(LineageKeys.SUCCESSION_DISPUTE_ID,
                        out long disputeId, -1L);
                    if (disputeId == pRow.DisputeId) return war;
                }
            }
            catch { }
            return null;
        }
    }
}
