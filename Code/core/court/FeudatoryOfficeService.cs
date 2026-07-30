using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class FeudatoryOfficeService
    {
        public static void ScheduleMaintenance(FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot == null) return;
            long feudatoryId = pSnapshot.FeudatoryId;
            long empireId = pSnapshot.EmpireKingdomId;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "feudatory_office", feudatoryId),
                DeferredWorkClass.Runtime,
                () => MaintainOne(empireId, feudatoryId));
        }

        public static void MaintainBatch(Kingdom pEmpire,
            IReadOnlyList<FeudatorySnapshot> pRows, int pCursor, int pCount)
        {
            if (pEmpire?.data == null || pEmpire.isRekt() ||
                pRows == null || pRows.Count == 0 || pCount <= 0) return;
            int cursor = PositiveModulo(pCursor, pRows.Count);
            int count = Math.Min(Math.Min(pCount, pRows.Count), 4);
            var incumbentBySeat = BuildTargetIncumbentIndex(pEmpire,
                pRows, cursor, count);
            for (int offset = 0; offset < count; offset++)
            {
                FeudatorySnapshot snapshot =
                    pRows[(cursor + offset) % pRows.Count];
                MaintainSnapshot(pEmpire, snapshot, incumbentBySeat);
            }
        }

        public static void OnSeatChanged(FeudatorySnapshot pPrevious,
            FeudatorySnapshot pCurrent)
        {
            if (pPrevious == null || pCurrent == null ||
                pPrevious.SeatCityId == pCurrent.SeatCityId) return;
            CloseSeatOffice(pPrevious, "feudatory_relocated");
            ScheduleMaintenance(pCurrent);
        }

        public static void OnFeudatoryEnded(FeudatorySnapshot pSnapshot,
            string pReason)
        {
            CloseSeatOffice(pSnapshot, pReason ?? "feudatory_ended");
        }

        private static void MaintainOne(long pEmpireId, long pFeudatoryId)
        {
            Kingdom empire;
            try { empire = World.world?.kingdoms?.get(pEmpireId); }
            catch { empire = null; }
            if (empire?.data == null || empire.isRekt() ||
                !FeudatoryService.TryGet(pFeudatoryId,
                    out FeudatorySnapshot snapshot)) return;
            IReadOnlyList<FeudatorySnapshot> rows =
                FeudatoryService.GetByKingdom(empire.id);
            int index = -1;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].FeudatoryId == snapshot.FeudatoryId)
                {
                    index = i;
                    break;
                }
            if (index >= 0)
                MaintainBatch(empire, rows, pCursor: index, pCount: 1);
        }

        private static Dictionary<long, CourtOfficerView>
            BuildTargetIncumbentIndex(Kingdom pEmpire,
                IReadOnlyList<FeudatorySnapshot> pRows, int pCursor,
                int pCount)
        {
            var result = new Dictionary<long, CourtOfficerView>();
            for (int offset = 0; offset < pCount; offset++)
            {
                FeudatorySnapshot snapshot =
                    pRows[(pCursor + offset) % pRows.Count];
                List<CourtOfficerView> rows =
                    CourtService.GetActiveFeudatoryOfficersAtSeat(
                        pEmpire, snapshot.SeatCityId);
                for (int i = 0; i < rows.Count; i++)
                {
                    CourtOfficerView row = rows[i];
                    if (!result.ContainsKey(snapshot.SeatCityId))
                        result[snapshot.SeatCityId] = row;
                    else
                        CloseRow(pEmpire, row,
                            "duplicate_feudatory_office");
                }
            }
            return result;
        }

        private static void MaintainSnapshot(Kingdom pEmpire,
            FeudatorySnapshot pSnapshot,
            IDictionary<long, CourtOfficerView> pIncumbentBySeat)
        {
            if (pSnapshot == null ||
                pSnapshot.EmpireKingdomId != pEmpire.id) return;
            City seat;
            try { seat = World.world?.cities?.get(pSnapshot.SeatCityId); }
            catch { seat = null; }
            if (seat?.data == null || seat.isRekt() || seat.kingdom != pEmpire)
                return;

            if (pIncumbentBySeat.TryGetValue(seat.id,
                    out CourtOfficerView incumbent))
            {
                Actor actor = FindActor(incumbent.actor_id);
                if (IsValidIncumbent(actor, pEmpire, seat)) return;
                CloseRow(pEmpire, incumbent, "invalid_feudatory_office");
                pIncumbentBySeat.Remove(seat.id);
            }

            Actor candidate = SelectCandidate(pEmpire, pSnapshot, seat);
            if (candidate == null || !CourtService.TryAssignFeudatoryChiefClerk(
                    candidate, pEmpire, seat)) return;
            pIncumbentBySeat[seat.id] = new CourtOfficerView
            {
                actor_id = candidate.data.id,
                actor_name = SafeName(candidate),
                layer = CourtOfficeLayer.Feudatory,
                office_id = CourtOfficeId.FeudatoryChiefClerk,
                city_id = seat.id
            };
        }

        private static Actor SelectCandidate(Kingdom pEmpire,
            FeudatorySnapshot pSnapshot, City pSeat)
        {
            int unitCount = pSeat.units?.Count ?? 0;
            if (unitCount <= 0) return null;
            int initialCursor = PositiveModulo(
                (int)(pSnapshot.FeudatoryId % int.MaxValue), unitCount);
            pSeat.data.get(LineageKeys.FEUDATORY_OFFICE_SCAN_CURSOR,
                out int storedCursor, initialCursor);
            int start = PositiveModulo(storedCursor, unitCount);
            int scan = Math.Min(FeudatoryOfficeRules.MaxCandidateScan, unitCount);
            Actor best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < scan; i++)
            {
                Actor actor = pSeat.units[(start + i) % unitCount];
                if (!IsEligibleCandidate(actor, pEmpire, pSnapshot)) continue;
                float score = FeudatoryOfficeRules.CandidateScore(
                    SafeStat(actor, "stewardship"),
                    SafeStat(actor, "diplomacy"),
                    SafeStat(actor, "intelligence"),
                    SafeStat(actor, "warfare"));
                if (best == null || score > bestScore ||
                    score.Equals(bestScore) && actor.data.id < best.data.id)
                {
                    best = actor;
                    bestScore = score;
                }
            }
            pSeat.data.set(LineageKeys.FEUDATORY_OFFICE_SCAN_CURSOR,
                FeudatoryOfficeRules.NextCandidateCursor(start, unitCount,
                    scan));
            return best;
        }

        private static bool IsEligibleCandidate(Actor pActor,
            Kingdom pEmpire, FeudatorySnapshot pSnapshot)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            var facts = new FeudatoryOfficeCandidateFacts(
                alive: pActor.isAlive() && !pActor.isRekt(),
                adult: pActor.isAdult(),
                male: pActor.isSexMale(),
                sameKingdom: pActor.kingdom == pEmpire,
                king: pActor.isKing(),
                heir: HeirService.IsCurrentHeir(pEmpire, pActor),
                prince: pActor.data.id == pSnapshot.PrinceActorId ||
                        FeudatoryService.IsActivePrince(pActor),
                slave: pActor.hasTrait(LineageKeys.TRAIT_SLAVE),
                madness: pActor.hasTrait("madness"),
                asylum: RoyalAsylumService.IsActive(pActor),
                hasIncompatibleOffice: !string.IsNullOrEmpty(office),
                cityLeader: pActor.isCityLeader(),
                general: GeneralService.IsActiveGeneralFast(pActor));
            return FeudatoryOfficeRules.CanServe(facts) &&
                   CourtAffiliationResolver.CanServe(pActor, pEmpire,
                       CourtOfficeLayer.Feudatory) &&
                   CivilServiceQualificationService.
                       CanReceiveFormalCivilAppointment(pActor, pEmpire,
                           CourtOfficeLayer.Feudatory,
                           CourtOfficeId.FeudatoryChiefClerk);
        }

        private static bool IsValidIncumbent(Actor pActor, Kingdom pEmpire,
            City pSeat)
        {
            if (pActor?.data == null || pActor.city != pSeat ||
                pActor.kingdom != pEmpire) return false;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long kingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            bool eligible = pActor.isAlive() && !pActor.isRekt() &&
                            pActor.isAdult() && pActor.isSexMale() &&
                            !pActor.isKing() && !pActor.isCityLeader() &&
                            !GeneralService.IsActiveGeneralFast(pActor) &&
                            !HeirService.IsCurrentHeir(pEmpire, pActor) &&
                            !FeudatoryService.IsActivePrince(pActor) &&
                            !pActor.hasTrait(LineageKeys.TRAIT_SLAVE) &&
                            !pActor.hasTrait("madness") &&
                            !RoyalAsylumService.IsActive(pActor) &&
                            CourtAffiliationResolver.CanServe(pActor, pEmpire,
                                CourtOfficeLayer.Feudatory);
            return eligible && kingdomId == pEmpire.id &&
                   layer == CourtOfficeLayer.Feudatory &&
                   office == CourtOfficeId.FeudatoryChiefClerk &&
                   pActor.city == pSeat;
        }

        private static void CloseSeatOffice(FeudatorySnapshot pSnapshot,
            string pReason)
        {
            if (pSnapshot == null) return;
            Kingdom empire;
            try { empire = World.world?.kingdoms?.get(pSnapshot.EmpireKingdomId); }
            catch { empire = null; }
            if (empire?.data == null) return;
            List<CourtOfficerView> rows =
                CourtService.GetActiveFeudatoryOfficersAtSeat(empire,
                    pSnapshot.SeatCityId);
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].city_id == pSnapshot.SeatCityId)
                    CloseRow(empire, rows[i], pReason);
        }

        private static void CloseRow(Kingdom pEmpire, CourtOfficerView pRow,
            string pReason)
        {
            if (pEmpire?.data == null || pRow == null) return;
            Actor actor = FindActor(pRow.actor_id);
            bool cleared = actor?.data != null &&
                           CourtService.ClearFeudatoryChiefClerk(actor,
                               pReason);
            if (!cleared)
                OfficialCareerService.EndForOffice(pRow.actor_id, pEmpire.id,
                    CourtOfficeLayer.Feudatory,
                    CourtOfficeId.FeudatoryChiefClerk, pReason);
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static int PositiveModulo(int pValue, int pModulo)
        {
            if (pModulo <= 0) return 0;
            int value = pValue % pModulo;
            return value < 0 ? value + pModulo : value;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }

        private static string SafeName(Actor pActor)
        {
            try { return pActor?.getName() ?? ""; }
            catch { return pActor?.data?.name ?? ""; }
        }
    }
}
