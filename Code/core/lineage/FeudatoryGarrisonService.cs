using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class FeudatoryGarrisonService
    {
        private static readonly Dictionary<long, int> CityCursorByFeudatory =
            new Dictionary<long, int>();

        public static void ClearRuntime()
        {
            CityCursorByFeudatory.Clear();
        }

        public static void ScheduleMaintenance(long pFeudatoryId)
        {
            if (pFeudatoryId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "feudatory_garrison", pFeudatoryId),
                DeferredWorkClass.Runtime,
                () => MaintainRoster(pFeudatoryId));
        }

        public static bool EnsureFor(FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot == null) return false;
            Kingdom empire = FindKingdom(pSnapshot.EmpireKingdomId);
            City seat = FindCity(pSnapshot.SeatCityId);
            if (empire?.data == null || seat?.data == null ||
                seat.kingdom != empire)
                return false;

            Actor captain = FindActor(pSnapshot.GarrisonCaptainActorId);
            if (!IsEligibleCaptain(captain, empire) ||
                IsUsedByAnotherFeudatory(pSnapshot.FeudatoryId,
                    captain.data.id))
                captain = SelectCaptain(empire, seat, pSnapshot.FeudatoryId);
            if (captain?.data == null) return false;

            string ownerName = (empire.name ?? "") + " " +
                               (seat.data.name ?? "");
            string name = AWArmyRoleRules.DisplayName(
                AWArmyRole.FeudatoryGarrison, ownerName, 1);
            Army army = FindArmy(pSnapshot.GarrisonArmyId);
            if (IsValidStoredArmy(army, empire))
            {
                AWArmyService.ReanchorArmy(army, empire, seat,
                    AWArmyRole.FeudatoryGarrison, name);
                AWArmyService.AddToArmy(captain, army);
                AWArmyService.SetCaptainIfChanged(army, captain);
            }
            else
            {
                army = AWArmyService.EnsureArmy(empire, seat, captain,
                    AWArmyRole.FeudatoryGarrison, name, pDetached: false);
            }
            if (army?.data == null) return false;
            return FeudatoryService.UpdateGarrison(pSnapshot.FeudatoryId,
                army.id, captain.data.id);
        }

        public static void Disband(FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot == null) return;
            Army army = FindArmy(pSnapshot.GarrisonArmyId);
            if (army?.data == null) return;
            var units = new List<Actor>();
            try
            {
                foreach (Actor actor in army.getUnits())
                    if (actor?.data != null)
                        units.Add(actor);
            }
            catch { }
            for (int i = 0; i < units.Count; i++)
            {
                Actor actor = units[i];
                actor.data.get(LineageKeys.FEUDATORY_GARRISON_ID,
                    out long feudatoryId, -1L);
                if (feudatoryId == pSnapshot.FeudatoryId)
                {
                    actor.data.set(LineageKeys.FEUDATORY_GARRISON_ID, -1L);
                    if (!actor.isRekt() && actor.isWarrior())
                        actor.stopBeingWarrior();
                }
                else if (actor.army == army)
                {
                    try { actor.removeFromArmy(); }
                    catch { actor.setArmy(null); }
                }
            }
            AWArmyService.RemoveSpecialArmy(army);
            CityCursorByFeudatory.Remove(pSnapshot.FeudatoryId);
        }

        public static void ReassignForJingnan(FeudatorySnapshot pSnapshot,
            Kingdom pKingdom)
        {
            if (pSnapshot == null || pKingdom?.data == null) return;
            Army army = FindArmy(pSnapshot.GarrisonArmyId);
            City seat = FindCity(pSnapshot.SeatCityId);
            if (army?.data == null || seat?.data == null) return;
            var units = new List<Actor>();
            try
            {
                foreach (Actor unit in army.getUnits())
                    if (unit?.data != null &&
                        units.Count < FeudatoryAutonomyRules.MaximumGarrisonSize)
                        units.Add(unit);
            }
            catch { }
            for (int i = 0; i < units.Count; i++)
            {
                Actor unit = units[i];
                if (unit.kingdom == pKingdom) continue;
                try { unit.joinKingdom(pKingdom); }
                catch { }
            }
            AWArmyService.ReanchorArmy(army, pKingdom, seat,
                AWArmyRole.FeudatoryGarrison,
                string.IsNullOrEmpty(pSnapshot.FeudatoryName)
                    ? "Feudatory Garrison"
                    : pSnapshot.FeudatoryName + "镇军");
        }

        public static bool NeedsRepair(FeudatorySnapshot pSnapshot)
        {
            if (pSnapshot == null) return false;
            Kingdom empire = FindKingdom(pSnapshot.EmpireKingdomId);
            City seat = FindCity(pSnapshot.SeatCityId);
            if (empire?.data == null || seat?.data == null ||
                seat.kingdom != empire)
                return false;
            Actor captain = FindActor(pSnapshot.GarrisonCaptainActorId);
            if (!IsEligibleCaptain(captain, empire) ||
                IsUsedByAnotherFeudatory(pSnapshot.FeudatoryId,
                    pSnapshot.GarrisonCaptainActorId))
                return true;
            Army army = FindArmy(pSnapshot.GarrisonArmyId);
            if (!IsValidStoredArmy(army, empire) ||
                AWArmyService.GetAnchorCityId(army) != seat.id)
                return true;
            try { return army.getCaptain()?.data?.id != captain.data.id; }
            catch { return true; }
        }

        private static void MaintainRoster(long pFeudatoryId)
        {
            if (!FeudatoryService.TryGet(pFeudatoryId,
                    out FeudatorySnapshot snapshot))
                return;
            if (!EnsureFor(snapshot) ||
                !FeudatoryService.TryGet(pFeudatoryId, out snapshot))
                return;
            Army army = FindArmy(snapshot.GarrisonArmyId);
            Kingdom empire = FindKingdom(snapshot.EmpireKingdomId);
            if (army?.data == null || empire?.data == null) return;

            int target = FeudatoryAutonomyRules.GarrisonTarget(
                TotalWarriorSlots(snapshot), snapshot.Autonomy);
            int current = SafeArmySize(army);
            int recruit = FeudatoryAutonomyRules.RecruitmentBatchSize(
                current, target);
            if (recruit > 0)
                RecruitBounded(snapshot, empire, army, recruit);
            else
            {
                int demobilize =
                    FeudatoryAutonomyRules.DemobilizationBatchSize(
                        current, target);
                if (demobilize > 0)
                    DemobilizeBounded(snapshot, army, demobilize);
            }
            Actor captain = null;
            try { captain = army.getCaptain(); }
            catch { }
            if (captain?.data != null)
                FeudatoryService.UpdateGarrison(pFeudatoryId, army.id,
                    captain.data.id);
        }

        private static int TotalWarriorSlots(FeudatorySnapshot pSnapshot)
        {
            int total = 0;
            for (int i = 0; i < pSnapshot.CityIds.Count; i++)
            {
                City city = FindCity(pSnapshot.CityIds[i]);
                if (city?.data == null || city.isRekt()) continue;
                try { total += Math.Max(0, city.status.warrior_slots); }
                catch { }
            }
            return total;
        }

        private static void RecruitBounded(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire, Army pArmy, int pLimit)
        {
            int cityCount = pSnapshot.CityIds.Count;
            if (cityCount == 0 || pLimit <= 0) return;
            CityCursorByFeudatory.TryGetValue(pSnapshot.FeudatoryId,
                out int startCity);
            startCity = PositiveModulo(startCity, cityCount);
            int scanned = 0;
            int recruited = 0;
            for (int offset = 0; offset < cityCount &&
                                 scanned < FeudatoryAutonomyRules
                                     .MaximumGarrisonCandidateScan &&
                                 recruited < pLimit; offset++)
            {
                int cityIndex = PositiveModulo(startCity + offset, cityCount);
                City city = FindCity(pSnapshot.CityIds[cityIndex]);
                ScanCity(pSnapshot, pEmpire, pArmy, city,
                    ref scanned, ref recruited, pLimit);
            }
            CityCursorByFeudatory[pSnapshot.FeudatoryId] =
                PositiveModulo(startCity + 1, cityCount);
        }

        private static void ScanCity(FeudatorySnapshot pSnapshot,
            Kingdom pEmpire, Army pArmy, City pCity, ref int pScanned,
            ref int pRecruited, int pRecruitLimit)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom != pEmpire)
                return;
            if (!OccupiedCitySupplyService.CanProvideToRealm(
                    pCity, pEmpire)) return;
            pCity.data.get(LineageKeys.FEUDATORY_GARRISON_SCAN_CURSOR,
                out int cursor, 0);
            int unitCount = pCity.units.Count;
            if (cursor < 0 || cursor >= unitCount) cursor = 0;
            int localScanned = 0;
            int available = Math.Min(unitCount - cursor,
                FeudatoryAutonomyRules.MaximumGarrisonCandidateScan -
                pScanned);
            for (int i = 0; i < available &&
                                pRecruited < pRecruitLimit; i++)
            {
                Actor actor = pCity.units[cursor + i];
                pScanned++;
                localScanned++;
                if (!CanRecruit(pEmpire, pCity, actor)) continue;
                if (!Recruit(pSnapshot, pCity, pArmy, actor)) continue;
                pRecruited++;
            }
            bool complete = cursor + localScanned >= unitCount;
            pCity.data.set(LineageKeys.FEUDATORY_GARRISON_SCAN_CURSOR,
                complete ? 0 : cursor + localScanned);
        }

        private static bool CanRecruit(Kingdom pEmpire, City pCity,
            Actor pActor)
        {
            if (pActor?.data == null || pActor.city != pCity ||
                pActor.kingdom != pEmpire || pActor.isRekt() ||
                !pActor.isAlive() || !pActor.isAdult() || pActor.isWarrior() ||
                pActor.asset?.is_boat == true ||
                !pActor.isProfession(UnitProfession.Unit))
                return false;
            if (pActor.isKing() || pActor.isCityLeader() ||
                HeirService.IsCurrentHeir(pEmpire, pActor) ||
                GeneralService.IsActiveGeneralFast(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) ||
                SlaveService.IsSlave(pActor) ||
                SlaveService.IsRetiredSoldier(pActor) ||
                RoyalAsylumService.IsActive(pActor))
                return false;
            if (!HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.OrdinaryWarrior))
                return false;
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            if (!string.IsNullOrEmpty(office) &&
                layer != CourtOfficeLayer.Military)
                return false;
            using (MilitaryRecruitmentScope.Open(
                       MilitaryRecruitmentKind.FeudatoryGarrison))
                return pCity.checkCanMakeWarrior(pActor);
        }

        private static bool Recruit(FeudatorySnapshot pSnapshot, City pCity,
            Army pArmy, Actor pActor)
        {
            using (MilitaryRecruitmentScope.Open(
                       MilitaryRecruitmentKind.FeudatoryGarrison))
            {
                if (!pCity.checkCanMakeWarrior(pActor)) return false;
                pCity.makeWarrior(pActor);
            }
            if (!pActor.isWarrior()) return false;
            AWArmyService.AddToArmy(pActor, pArmy);
            if (pActor.army != pArmy)
            {
                pActor.stopBeingWarrior();
                return false;
            }
            pActor.data.set(LineageKeys.FEUDATORY_GARRISON_ID,
                pSnapshot.FeudatoryId);
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
            return true;
        }

        private static void DemobilizeBounded(FeudatorySnapshot pSnapshot,
            Army pArmy, int pLimit)
        {
            var recruits = new List<Actor>(
                FeudatoryAutonomyRules.MaximumGarrisonSize);
            try
            {
                foreach (Actor actor in pArmy.getUnits())
                {
                    if (actor?.data == null) continue;
                    actor.data.get(LineageKeys.FEUDATORY_GARRISON_ID,
                        out long feudatoryId, -1L);
                    if (feudatoryId == pSnapshot.FeudatoryId)
                        recruits.Add(actor);
                }
            }
            catch { }
            recruits.Sort((left, right) =>
                right.data.id.CompareTo(left.data.id));
            int count = Math.Min(pLimit, recruits.Count);
            for (int i = 0; i < count; i++)
            {
                Actor actor = recruits[i];
                actor.data.set(LineageKeys.FEUDATORY_GARRISON_ID, -1L);
                if (!actor.isRekt() && actor.isWarrior())
                    actor.stopBeingWarrior();
                if (!actor.isRekt() && actor.ai != null)
                    try { actor.ai.setJob(actor.getNextJob()); }
                    catch { }
            }
        }

        private static int SafeArmySize(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static int PositiveModulo(int pValue, int pModulo)
        {
            if (pModulo <= 0) return 0;
            int result = pValue % pModulo;
            return result < 0 ? result + pModulo : result;
        }

        private static Actor SelectCaptain(Kingdom pEmpire, City pSeat,
            long pFeudatoryId)
        {
            Actor best = null;
            int bestScore = int.MinValue;
            foreach (Actor general in GeneralService.GetActiveGenerals(pEmpire))
            {
                if (!IsEligibleCaptain(general, pEmpire) ||
                    IsUsedByAnotherFeudatory(pFeudatoryId, general.data.id))
                    continue;
                int score = GeneralService.GetMerit(general) * 10 +
                            general.warfare;
                if (general.city == pSeat) score += 1000;
                if (best == null || score > bestScore ||
                    score == bestScore && general.data.id < best.data.id)
                {
                    best = general;
                    bestScore = score;
                }
            }
            return best;
        }

        private static bool IsEligibleCaptain(Actor pActor, Kingdom pEmpire)
        {
            return pActor?.data != null && !pActor.isRekt() && pActor.isAlive() &&
                   pActor.isAdult() && pActor.isSexMale() &&
                   pActor.kingdom == pEmpire && GeneralService.IsGeneral(pActor);
        }

        private static bool IsUsedByAnotherFeudatory(long pFeudatoryId,
            long pActorId)
        {
            if (pActorId < 0 ||
                !FeudatoryService.TryGet(pFeudatoryId,
                    out FeudatorySnapshot self))
                return false;
            IReadOnlyList<FeudatorySnapshot> rows =
                FeudatoryService.GetByKingdom(self.EmpireKingdomId);
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].FeudatoryId != pFeudatoryId &&
                    rows[i].GarrisonCaptainActorId == pActorId)
                    return true;
            return false;
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return World.world?.cities?.get(pId); }
            catch { return null; }
        }

        private static Actor FindActor(long pId)
        {
            if (pId < 0) return null;
            try
            {
                ActorManager units = World.world?.units;
                return units?.get(pId);
            }
            catch { return null; }
        }

        private static Army FindArmy(long pId)
        {
            if (pId < 0) return null;
            try
            {
                ArmyManager armies = World.world?.armies;
                return armies?.get(pId);
            }
            catch { return null; }
        }

        private static bool IsValidStoredArmy(Army pArmy, Kingdom pEmpire)
        {
            if (pArmy?.data == null || !pArmy.isAlive() ||
                !AWArmyService.IsRoleArmy(pArmy,
                    AWArmyRole.FeudatoryGarrison))
                return false;
            try { return pArmy.getKingdom() == pEmpire; }
            catch { return false; }
        }
    }
}
