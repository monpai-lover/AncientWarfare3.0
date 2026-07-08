using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class SlaveService
    {
        private const float RETIREMENT_AGE_RATIO = 0.7f;
        private const float SLAVE_ARMY_TARGET_PERCENT = 0.8f;
        private const float CITY_FALL_SLAVE_RATIO = 0.10f;
        private const float DIRECT_CAPTURE_HEALTH_RATIO = 0.85f;
        private const float IMPORTANT_CAPTURE_HEALTH_RATIO = 0.45f;
        private const float COMBAT_CAPTURE_WARRIOR_CHANCE = 0.28f;
        private const float COMBAT_CAPTURE_CIVILIAN_CHANCE = 0.40f;
        private const float COMBAT_CAPTURE_IMPORTANT_CHANCE = 0.08f;
        private const float COMBAT_CAPTURE_CATCHER_BONUS = 0.25f;
        private const int SLAVE_CATCHER_SEARCH_RADIUS = 80;
        private const int MIN_SLAVES_FOR_SLAVE_ARMY = 3;
        private const int MAX_SLAVE_ARMY_SIZE = 25;
        private const int SLAVE_ARMY_FILL_BATCH_LIMIT = 6;
        private const int MAX_CITY_FALL_SLAVES = 8;
        private const int MIN_SERVICE_YEARS_BEFORE_RETIREMENT = 5;
        private const int SLAVE_MIN_SERVICE_YEARS_BEFORE_RETIREMENT = 8;
        private const int MERIT_FOR_FREEDOM = 8;
        private const int CITY_RETIREMENT_CHECK_INTERVAL = 20;
        private const int CITY_SLAVE_LABOR_CHECK_INTERVAL = 30;
        private const int CITY_SLAVE_CATCHER_CHECK_INTERVAL = 10;
        private const int CITY_SLAVE_ARMY_CHECK_INTERVAL = 20;
        private const int SLAVE_ARMY_FAILED_MAINTENANCE_COOLDOWN = 60;
        private const double SLAVE_CAPTURE_SEARCH_MISS_COOLDOWN = 2.0;
        private const float SLAVE_CAPTURE_NO_TARGET_WAIT_MIN = 3f;
        private const float SLAVE_CAPTURE_NO_TARGET_WAIT_MAX = 8f;
        private const float SLAVE_CAPTURE_FAILURE_WAIT_MIN = 2f;
        private const float SLAVE_CAPTURE_FAILURE_WAIT_MAX = 5f;
        private const float SLAVE_CAPTURE_SUCCESS_WAIT_MIN = 5f;
        private const float SLAVE_CAPTURE_SUCCESS_WAIT_MAX = 10f;
        private static readonly System.Random Rng = new System.Random();
        private static readonly Dictionary<long, double> CaptureSearchNextAllowed = new Dictionary<long, double>();
        private static readonly Dictionary<long, List<PendingSlaveCaptureSummary>> PendingWarSlaveCaptures =
            new Dictionary<long, List<PendingSlaveCaptureSummary>>();
        private static bool _formingSlaveArmy;

        private sealed class PendingSlaveCaptureSummary
        {
            public City city;
            public string cityName;
            public int count;
        }

        public static bool IsSlave(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (pActor.hasTrait(LineageKeys.TRAIT_SLAVE)) return true;
            pActor.data.get(LineageKeys.LINEAGE_STATUS, out string status, LineageStatus.NONE);
            return status == LineageStatus.SLAVE;
        }

        public static bool AreBothSlaves(Actor pA, Actor pB)
        {
            return IsSlave(pA) && IsSlave(pB);
        }

        public static bool IsRetiredSoldier(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.RETIRED_SOLDIER, out bool retired, false);
            return retired;
        }

        public static bool IsSlaveryEnabled(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.SLAVERY_ENABLED, out bool enabled, false);
            return enabled;
        }

        public static void SetSlaveryEnabled(Kingdom pKingdom, bool pEnabled)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.SLAVERY_ENABLED, pEnabled);
            if (!pEnabled)
                pKingdom.data.set(LineageKeys.SLAVE_ARMY_ENABLED, false);
        }

        public static void SetSlaveArmyEnabled(Kingdom pKingdom, bool pEnabled)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.SLAVE_ARMY_ENABLED, pEnabled);
            if (pEnabled)
                SetSlaveryEnabled(pKingdom, true);
        }

        public static void EnforceSlaveControl(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !IsSlaveryEnabled(pKingdom)) return;
            foreach (City city in pKingdom.getCities())
            {
                EnsureSlaveArmy(city);
                CheckCitySlaveLabor(city, pForce: true);
                if (city != null && city.hasArmy())
                {
                    EnsureNonSlaveCaptain(city.getArmy());
                    RenameArmyIfSlaveArmy(city.getArmy());
                }
            }
        }

        public static void ResetSlaveFoodQuota(City pCity)
        {
            if (pCity?.data == null) return;

            int quota = CountSlaves(pCity) > 0 ? (int)(pCity.countFood() * 0.1f) : 0;
            pCity.data.set(LineageKeys.SLAVE_FOOD_YEAR, Date.getCurrentYear());
            pCity.data.set(LineageKeys.SLAVE_FOOD_QUOTA, quota);
        }

        public static bool CanConsumeCityFood(Actor pActor, City pCity)
        {
            if (!IsSlave(pActor)) return true;
            if (pCity?.data == null) return false;

            int year = Date.getCurrentYear();
            pCity.data.get(LineageKeys.SLAVE_FOOD_YEAR, out int quotaYear, int.MinValue);
            if (quotaYear != year)
                ResetSlaveFoodQuota(pCity);

            pCity.data.get(LineageKeys.SLAVE_FOOD_QUOTA, out int quota, 0);
            if (quota <= 0) return false;

            pCity.data.set(LineageKeys.SLAVE_FOOD_QUOTA, quota - 1);
            return true;
        }

        public static bool CanBeSlaveCatcher(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (!IsSupportedSlaveryActor(pActor)) return false;
            if (pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (IsRetiredSoldier(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor)) return false;
            if (HeirService.IsCurrentHeir(pActor.kingdom, pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            return SlaveCaptureCommandRules.CanCommandSlaveCapture(
                pIsSlaveArmyCaptain: IsSlaveArmyCaptain(pActor),
                pIsSlave: IsSlave(pActor),
                pSlaveryEnabled: IsSlaveryEnabled(pActor.kingdom));
        }

        public static void AssignSlaveCatchers(City pCity, bool pForce = false)
        {
            if (pCity?.data == null) return;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null) return;
            if (!IsSlaveryEnabled(kingdom)) return;
            if (SlaveryContent.SlaveCatcherJob == null) return;
            if (pCity.getUnitsTotal() < 15) return;
            Bench.bench(CityMaintenanceBenchmarkRules.SlaveCatchersJobGate, CityMaintenanceBenchmarkRules.Group);
            bool alreadyHasCatcher = pCity.jobs.countCurrentJobs(SlaveryContent.SlaveCatcherJob) > 0;
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveCatchersJobGate, CityMaintenanceBenchmarkRules.Group);
            if (alreadyHasCatcher) return;
            if (!pForce && !ShouldRunCityMaintenance(pCity, LineageKeys.SLAVE_CATCHER_LAST_CHECK,
                    CITY_SLAVE_CATCHER_CHECK_INTERVAL)) return;
            Bench.bench(CityMaintenanceBenchmarkRules.SlaveCatchersTargetScan, CityMaintenanceBenchmarkRules.Group);
            bool hasTarget = HasCaptureTargetForCity(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveCatchersTargetScan, CityMaintenanceBenchmarkRules.Group);
            if (!hasTarget) return;

            pCity.jobs.addToJob(SlaveryContent.SlaveCatcherJob, 1);
        }

        public static Actor FindSlaveCaptureTarget(Actor pCatcher, int pSearchRadius)
        {
            if (!CanBeSlaveCatcher(pCatcher)) return null;
            if (pCatcher.current_tile == null || pCatcher.kingdom == null) return null;
            if (!ShouldScanCaptureTargetsFromCurrentPosition(pCatcher)) return null;
            if (!ShouldRunCaptureSearch(pCatcher)) return null;

            Actor best = null;
            int bestDist = int.MaxValue;
            int radius = Math.Max(pSearchRadius, SLAVE_CATCHER_SEARCH_RADIUS);
            int maxDist = radius * radius;
            using ListPool<Kingdom> enemies = pCatcher.kingdom.getEnemiesKingdoms();
            foreach (Kingdom enemy in enemies)
            {
                if (enemy?.data == null) continue;
                foreach (Actor target in enemy.getUnits())
                {
                    if (!CanCaptureTargetForKnownCatcher(pCatcher, target)) continue;
                    int dist = Toolbox.SquaredDistTile(pCatcher.current_tile, target.current_tile);
                    if (dist > maxDist || dist >= bestDist) continue;
                    bestDist = dist;
                    best = target;
                }
            }
            MarkCaptureSearchResult(pCatcher, best);
            return best;
        }

        public static void WaitAfterSlaveCaptureNoTarget(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(SLAVE_CAPTURE_NO_TARGET_WAIT_MIN, SLAVE_CAPTURE_NO_TARGET_WAIT_MAX));
        }

        public static void WaitAfterSlaveCaptureFailure(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(SLAVE_CAPTURE_FAILURE_WAIT_MIN, SLAVE_CAPTURE_FAILURE_WAIT_MAX));
        }

        public static void WaitAfterSlaveCaptureSuccess(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(SLAVE_CAPTURE_SUCCESS_WAIT_MIN, SLAVE_CAPTURE_SUCCESS_WAIT_MAX));
        }

        public static bool TryCaptureCombatTarget(Actor pTarget, BaseSimObject pAttacker, AttackType pAttackType)
        {
            if (pAttackType != AttackType.Weapon) return false;
            if (pTarget?.data == null || pTarget.isRekt() || !pTarget.isAlive() || !pTarget.hasHealth()) return false;
            if (!CanBeCapturedAsTarget(pTarget, pAllowImportantCapture: true)) return false;

            Actor captor = pAttacker?.a;
            if (captor?.data == null || captor == pTarget) return false;
            if (captor.isRekt() || !captor.isAlive()) return false;
            if (!IsSupportedSlaveryActor(captor)) return false;

            Kingdom captorKingdom = captor.kingdom;
            Kingdom targetKingdom = pTarget.kingdom;
            if (captorKingdom?.data == null || targetKingdom?.data == null) return false;
            if (captorKingdom == targetKingdom) return false;
            if (!captorKingdom.isEnemy(targetKingdom)) return false;
            if (!IsSlaveryEnabled(captorKingdom)) return false;
            if (pTarget.current_tile != null && captor.current_tile != null &&
                !pTarget.current_tile.isSameIsland(captor.current_tile)) return false;

            float threshold = IsImportantCaptureTarget(pTarget)
                ? IMPORTANT_CAPTURE_HEALTH_RATIO
                : DIRECT_CAPTURE_HEALTH_RATIO;
            if (pTarget.getHealthRatio() > threshold) return false;
            if (!RollCombatCapture(captor, pTarget)) return false;

            City city = captor.city ?? captorKingdom.capital;
            if (city?.data == null) return false;

            pTarget.cancelAllBeh();
            pTarget.clearAttackTarget();
            pTarget.beh_actor_target = null;
            pTarget.attackedBy = null;
            captor.clearAttackTarget();
            if (captor.beh_actor_target == pTarget)
                captor.beh_actor_target = null;
            pTarget.joinCity(city);
            pTarget.setHealth(Math.Max(1, pTarget.getMaxHealthPercent(0.2f)));

            bool nationalRecord = IsImportantCaptureTarget(pTarget);
            bool changed = Enslave(pTarget, "battlefield_capture", captor, city, captorKingdom,
                pForceRecord: true, pForceNationalRecord: nationalRecord);
            if (!changed) return false;

            QueueWarSlaveCaptureSummary(captorKingdom, city, 1);
            CheckCitySlaveLabor(city);
            return true;
        }

        public static bool CaptureTargetAsSlave(Actor pCatcher, Actor pTarget)
        {
            if (!CanCaptureTarget(pCatcher, pTarget)) return false;

            bool nationalRecord = IsImportantCaptureTarget(pTarget);
            City city = pCatcher.city ?? pCatcher.kingdom?.capital;
            Kingdom kingdom = pCatcher.kingdom ?? city?.kingdom;
            if (city?.data == null || kingdom?.data == null) return false;

            pTarget.joinCity(city);
            bool changed = Enslave(pTarget, "captured", pCatcher, city, kingdom, pForceRecord: true,
                pForceNationalRecord: nationalRecord);
            if (changed)
                QueueWarSlaveCaptureSummary(kingdom, city, 1);
            CheckCitySlaveLabor(city);
            return changed;
        }

        public static bool Enslave(Actor pActor, string pReason, Actor pCaptor = null,
            City pContextCity = null, Kingdom pContextKingdom = null, bool pForceRecord = false,
            bool pForceNationalRecord = false)
        {
            Kingdom contextKingdom = pContextKingdom ?? pActor?.kingdom ?? pContextCity?.kingdom;
            if (!IsSlaveryEnabled(contextKingdom)) return false;
            if (!CanBeEnslaved(pActor, pForceNationalRecord)) return false;

            bool wasSlave = IsSlave(pActor);
            bool wasKing = SafeIsKing(pActor);
            Kingdom formerKingdom = wasKing ? pActor.kingdom : null;
            ApplySlaveIdentity(pActor, pReason, pCaptor);
            UpsertSlaveState(pActor, pActive: true, pContextCity, pContextKingdom);

            if (!wasSlave || pForceRecord)
                ChronicleEvents.OnEnslaved(pActor, pReason, pContextKingdom ?? pActor.kingdom,
                    pContextCity ?? pActor.city, pForceNationalRecord);

            SlaveKingAbdicationService.TryForceAbdicate(pActor, pReason, wasKing, wasSlave,
                formerKingdom ?? contextKingdom);

            CheckCitySlaveLabor(pContextCity ?? pActor.city);
            return !wasSlave || pForceRecord;
        }

        public static bool EnslaveByOccupation(Actor pActor, City pContextCity, Kingdom pOccupier,
            bool pImportantRecord = false)
        {
            if (pActor?.data == null || pContextCity?.data == null || pOccupier?.data == null) return false;
            if (!CanBeEnslaved(pActor, pImportantRecord)) return false;
            if (!IsSlaveryEnabled(pOccupier))
                SetSlaveryEnabled(pOccupier, true);
            return Enslave(pActor, "foreign_occupation", null, pContextCity, pOccupier,
                pForceRecord: true, pForceNationalRecord: pImportantRecord);
        }

        public static void EnsureSlaveChild(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            if (pBaby?.data == null) return;
            if (!IsSupportedSlaveryActor(pBaby)) return;
            if (!IsSlave(pParent1) && !IsSlave(pParent2)) return;

            Enslave(pBaby, "born_slave", null, pBaby.city, pBaby.kingdom, pForceRecord: true);
        }

        public static bool FreeSlave(Actor pActor, string pReason)
        {
            if (pActor?.data == null || !IsSlave(pActor)) return false;

            pActor.removeTrait(LineageKeys.TRAIT_SLAVE);
            pActor.data.set(LineageKeys.SLAVE_SOLDIER, false);
            pActor.data.set(LineageKeys.FREEDMAN, true);

            pActor.data.get(LineageKeys.LINEAGE_ID, out long lineageId, -1L);
            pActor.data.set(LineageKeys.LINEAGE_STATUS, lineageId >= 0 ? LineageStatus.COMMON : LineageStatus.NONE);

            LineageService.ApplyDisplayName(pActor);
            LineageService.ArchiveActor(pActor, pAlive: true);
            pActor.clearGraphicsFully();

            UpsertSlaveState(pActor, pActive: false, pActor.city, pActor.kingdom);
            ChronicleEvents.OnFreedSlave(pActor, pReason, pActor.kingdom, pActor.city);
            return true;
        }

        public static bool CanFallInLoveByStatus(Actor pA, Actor pB)
        {
            bool aSlave = IsSlave(pA);
            bool bSlave = IsSlave(pB);
            return aSlave == bSlave;
        }

        public static bool RetireIfNeeded(Actor pActor)
        {
            if (pActor?.data == null) return false;
            bool supportedActor = IsSupportedSlaveryActor(pActor);
            bool rekt = pActor.isRekt();
            bool warrior = pActor.isWarrior();
            bool alreadyRetired = IsRetiredSoldier(pActor);
            float lifespan = pActor.stats["lifespan"];
            if (!SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(supportedActor, rekt, warrior,
                    alreadyRetired, pActor.getAge(), lifespan, RETIREMENT_AGE_RATIO)) return false;

            bool general = GeneralService.IsGeneral(pActor);
            bool fiefHolder = GeneralService.IsFiefHolder(pActor);
            bool royalGuard = supportedActor && !rekt && warrior && !alreadyRetired && !general && !fiefHolder &&
                              RoyalGuardService.IsRoyalGuard(pActor);

            if (!SoldierRetirementRules.CanConsiderForRetirement(supportedActor, rekt, warrior, alreadyRetired,
                    general, fiefHolder, royalGuard)) return false;

            if (!HasServedEnoughForRetirement(pActor)) return false;

            pActor.stopBeingWarrior();
            pActor.data.set(LineageKeys.RETIRED_SOLDIER, true);
            pActor.data.set(LineageKeys.SLAVE_SOLDIER, false);
            if (!pActor.hasTrait(LineageKeys.TRAIT_VETERAN)) pActor.addTrait(LineageKeys.TRAIT_VETERAN);

            LineageService.ArchiveActor(pActor, pAlive: true);
            pActor.clearGraphicsFully();
            UpsertSlaveState(pActor, IsSlave(pActor), pActor.city, pActor.kingdom);
            ChronicleEvents.OnRetiredSoldier(pActor, pActor.kingdom, pActor.city);
            return true;
        }

        private static void MarkSoldierServiceStarted(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, (float)LineageService.CurTime());
        }

        private static bool HasServedEnoughForRetirement(Actor pActor)
        {
            if (pActor?.data == null) return false;
            float now = (float)LineageService.CurTime();
            pActor.data.get(LineageKeys.SOLDIER_SERVICE_START_TIME, out float startTime, -1f);
            if (startTime <= 0f || startTime > now)
            {
                pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, now);
                if (IsSlave(pActor))
                    UpsertSlaveState(pActor, true, pActor.city, pActor.kingdom);
                return false;
            }

            int requiredYears = IsSlave(pActor)
                ? SLAVE_MIN_SERVICE_YEARS_BEFORE_RETIREMENT
                : MIN_SERVICE_YEARS_BEFORE_RETIREMENT;
            return Date.getYearsSince(startTime) >= requiredYears;
        }

        public static void CheckCityRetirements(City pCity)
        {
            if (pCity?.data == null) return;
            bool maintenanceDue = ShouldRunCityMaintenanceStaggered(pCity, LineageKeys.SLAVE_RETIREMENT_LAST_CHECK,
                CITY_RETIREMENT_CHECK_INTERVAL);
            if (!SoldierRetirementRules.ShouldRunCityRetirementScan(
                    pActorUpdateAgeRetirementEnabled: true,
                    pMaintenanceDue: maintenanceDue)) return;
            Bench.bench(CityMaintenanceBenchmarkRules.RetirementsScan, CityMaintenanceBenchmarkRules.Group);
            foreach (Actor unit in pCity.getUnits())
            {
                if (unit?.data == null || !unit.isWarrior()) continue;
                RetireIfNeeded(unit);
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.RetirementsScan, CityMaintenanceBenchmarkRules.Group);
        }

        public static bool ShouldBlockConscription(City pCity, Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (!IsSupportedSlaveryActor(pActor)) return false;
            if (IsRetiredSoldier(pActor)) return true;
            if (!IsSlave(pActor)) return false;
            if (!IsSlaveryEnabled(pActor.kingdom ?? pCity?.kingdom)) return true;

            if (!CanUseSlaveArmy(pCity)) return true;

            CountCityWarriors(pCity, out int totalWarriors, out int slaveWarriors, out int nonSlaveWarriors);
            if (nonSlaveWarriors <= 0) return true;

            int nextTotal = totalWarriors + 1;
            int nextSlave = slaveWarriors + 1;
            int maxSlaves = (int)Math.Ceiling(nextTotal * SLAVE_ARMY_TARGET_PERCENT);
            return nextSlave > maxSlaves;
        }

        public static void OnMadeWarrior(City pCity, Actor pActor)
        {
            if (pActor?.data == null) return;
            if (!IsSupportedSlaveryActor(pActor)) return;

            if (IsRetiredSoldier(pActor))
            {
                pActor.stopBeingWarrior();
                return;
            }

            MarkSoldierServiceStarted(pActor);
            ApplyFiefSoldierTraining(pCity, pActor);

            if (!IsSlave(pActor)) return;
            if (!IsSlaveryEnabled(pActor.kingdom ?? pCity?.kingdom))
            {
                pActor.stopBeingWarrior();
                return;
            }

            pActor.data.set(LineageKeys.SLAVE_SOLDIER, true);
            EnableSlaveArmy(pActor.kingdom ?? pCity?.kingdom);
            UpsertSlaveState(pActor, pActive: true, pCity ?? pActor.city, pActor.kingdom ?? pCity?.kingdom);
            RecordSlaveArmyFormation(pActor.kingdom ?? pCity?.kingdom, pCity ?? pActor.city);
            if (!_formingSlaveArmy)
                EnsureSlaveArmy(pCity ?? pActor.city);
            ChronicleEvents.OnSlaveEnlisted(pActor, pActor.kingdom ?? pCity?.kingdom, pCity ?? pActor.city);
        }

        private static void ApplyFiefSoldierTraining(City pCity, Actor pActor)
        {
            if (pActor?.data == null || pCity?.data == null) return;
            if (!FiefMilitaryRules.ShouldApplyFiefSoldierTrait(FiefService.IsActiveFief(pCity),
                    pActor.isWarrior(), pActor.hasTrait(LineageKeys.TRAIT_FIEF_SOLDIER),
                    IsSlave(pActor), RoyalGuardService.IsRoyalGuard(pActor))) return;

            pActor.addTrait(LineageKeys.TRAIT_FIEF_SOLDIER);
        }

        public static void TryPromoteSlaveByMerit(Actor pKiller, Actor pDead)
        {
            if (pKiller?.data == null || pDead?.data == null) return;
            if (!IsSlave(pKiller)) return;

            int points = 1;
            if (ChronicleGate.IsImportant(pDead)) points = 4;
            else if (pDead.isWarrior()) points = 2;

            pKiller.data.get(LineageKeys.SLAVE_MERIT, out int merit, 0);
            merit += points;
            pKiller.data.set(LineageKeys.SLAVE_MERIT, merit);
            UpsertSlaveState(pKiller, pActive: true, pKiller.city, pKiller.kingdom);

            if (points >= 4 || merit >= MERIT_FOR_FREEDOM)
                ChronicleEvents.OnSlaveMerit(pKiller, points, merit, pKiller.kingdom, pKiller.city);

            if (merit >= MERIT_FOR_FREEDOM)
                FreeSlave(pKiller, "military_merit");
        }

        public static void HandleCityCaptured(City pCity, Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null || pNewKingdom?.data == null) return;
            if (pOldKingdom == null || pOldKingdom == pNewKingdom) return;
            if (!IsSlaveryEnabled(pNewKingdom)) return;

            var candidates = new List<Actor>();
            foreach (Actor unit in pCity.getUnits())
            {
                if (unit?.data == null) continue;
                if (!unit.isAdult()) continue;
                if (!CanBeEnslaved(unit)) continue;
                candidates.Add(unit);
            }

            if (candidates.Count == 0) return;
            int target = Math.Min(MAX_CITY_FALL_SLAVES, Math.Max(1, (int)Math.Ceiling(candidates.Count * CITY_FALL_SLAVE_RATIO)));
            int enslaved = 0;
            for (int i = 0; i < target && i < candidates.Count; i++)
                if (Enslave(candidates[i], "city_fall", null, pCity, pNewKingdom))
                    enslaved++;
            QueueWarSlaveCaptureSummary(pNewKingdom, pCity, enslaved);
        }

        public static void FlushPendingWarSlaveCaptures(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!PendingWarSlaveCaptures.TryGetValue(pKingdom.id, out List<PendingSlaveCaptureSummary> summaries))
                return;

            PendingWarSlaveCaptures.Remove(pKingdom.id);
            foreach (PendingSlaveCaptureSummary summary in summaries)
                ChronicleEvents.OnWarSlavesCaptured(pKingdom, summary.city, summary.cityName, summary.count);
        }

        private static void QueueWarSlaveCaptureSummary(Kingdom pKingdom, City pCity, int pCount)
        {
            if (pCount <= 0 || pKingdom?.data == null) return;
            if (!PendingWarSlaveCaptures.TryGetValue(pKingdom.id, out List<PendingSlaveCaptureSummary> summaries))
            {
                summaries = new List<PendingSlaveCaptureSummary>();
                PendingWarSlaveCaptures[pKingdom.id] = summaries;
            }

            long cityId = pCity?.data?.id ?? -1L;
            foreach (PendingSlaveCaptureSummary summary in summaries)
            {
                long existingId = summary.city?.data?.id ?? -1L;
                if (existingId != cityId) continue;
                summary.count += pCount;
                if (summary.city == null) summary.city = pCity;
                if (string.IsNullOrEmpty(summary.cityName)) summary.cityName = pCity?.data?.name ?? "某城";
                return;
            }

            summaries.Add(new PendingSlaveCaptureSummary
            {
                city = pCity,
                cityName = pCity?.data?.name ?? "某城",
                count = pCount
            });
        }

        public static void CheckCitySlaveLabor(City pCity, bool pForce = false)
        {
            if (pCity?.data == null) return;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null) return;

            pCity.data.get(LineageKeys.SLAVE_LABOR_RECORDED, out long recordedKingdomId, -1L);
            if (recordedKingdomId == kingdom.id) return;
            if (!pForce && !ShouldRunCityMaintenance(pCity, LineageKeys.SLAVE_LABOR_LAST_CHECK,
                    CITY_SLAVE_LABOR_CHECK_INTERVAL)) return;

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveLaborCount, CityMaintenanceBenchmarkRules.Group);
            int slaveCount = CountSlaves(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveLaborCount, CityMaintenanceBenchmarkRules.Group);
            if (slaveCount <= 0) return;
            pCity.data.set(LineageKeys.SLAVE_LABOR_RECORDED, kingdom.id);

            ChronicleEvents.OnSlaveLaborStarted(kingdom, pCity, slaveCount);
        }

        public static void PrepareArmyCaptain(ref Actor pActor, City pCity)
        {
            if (!IsSlave(pActor)) return;
            Actor replacement = PickBestSlaveArmyCadre(pCity, pRequireWarrior: true);
            if (replacement == null)
                replacement = TryRaiseNonSlaveCaptain(pCity);
            if (replacement != null) pActor = replacement;
        }

        public static bool TryReplaceSlaveCaptain(Army pArmy, ref Actor pActor)
        {
            if (!IsSlave(pActor)) return true;

            Actor replacement = PickNonSlaveWarrior(pArmy);
            if (replacement == null) replacement = PickNonSlaveWarrior(FindArmyAnchorCity(pArmy) ?? pArmy?.getCity());
            if (replacement == null) return false;

            pActor = replacement;
            return true;
        }

        public static void EnsureNonSlaveCaptain(Army pArmy)
        {
            if (pArmy == null) return;
            Actor captain = pArmy.getCaptain();
            if (!IsSlave(captain)) return;

            Actor replacement = PickNonSlaveWarrior(pArmy);
            if (replacement == null)
                replacement = PickBestSlaveArmyCadre(FindArmyAnchorCity(pArmy) ?? pArmy.getCity(), pRequireWarrior: true);
            if (replacement != null)
                AWArmyService.SetCaptainIfChanged(pArmy, replacement);
        }

        public static void EnsureSlaveArmy(City pCity)
        {
            if (pCity?.data == null) return;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null) return;
            bool slaveryEnabled = IsSlaveryEnabled(kingdom);
            bool slaveArmyEnabled = IsSlaveArmyEnabled(kingdom);
            if (!slaveryEnabled || !slaveArmyEnabled) return;
            bool onSchedule = ShouldRunCityMaintenanceStaggered(pCity, LineageKeys.SLAVE_ARMY_LAST_CHECK,
                CITY_SLAVE_ARMY_CHECK_INTERVAL);
            if (!SlaveArmyMaintenanceRules.ShouldRunMaintenance(slaveryEnabled, slaveArmyEnabled, onSchedule)) return;
            int now = (int)LineageService.CurTime();
            pCity.data.get(LineageKeys.SLAVE_ARMY_FAILURE_YEAR, out int lastFailure, -1);
            if (SlaveArmyMaintenanceRules.ShouldSkipAfterFailedMaintenance(
                    now, lastFailure, SLAVE_ARMY_FAILED_MAINTENANCE_COOLDOWN))
                return;

            int slaveCount = -1;
            Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyExisting, CityMaintenanceBenchmarkRules.Group);
            Army army = AWArmyService.FindArmy(kingdom, pCity, AWArmyRole.SlaveArmy);
            if (army != null)
            {
                CountArmyComposition(army, out int total, out int slaves, out int nonSlaves);
                Actor existingCaptain = army.getCaptain();
                bool captainValid = CanBeSlaveArmyCaptainCandidate(existingCaptain, kingdom, pCity,
                    pRequireWarrior: true);
                Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmySlaveCount, CityMaintenanceBenchmarkRules.Group);
                slaveCount = CountSlaves(pCity);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmySlaveCount, CityMaintenanceBenchmarkRules.Group);
                if (SlaveArmyMaintenanceRules.ShouldSkipStableArmyFill(
                        pArmyExists: true,
                        pTotalWarriors: total,
                        pSlaveWarriors: slaves,
                        pNonSlaveWarriors: nonSlaves,
                        pCaptainValid: captainValid,
                        pCitySlaveCount: slaveCount))
                {
                    Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyExisting,
                        CityMaintenanceBenchmarkRules.Group);
                    ClearSlaveArmyMaintenanceFailure(pCity);
                    AssignSlaveCatcherJobToCaptain(existingCaptain);
                    if (SlaveArmyMaintenanceRules.ShouldDriveFrontline(
                            pHasArmy: true,
                            pHasEnemies: KingdomHasEnemies(kingdom),
                            pOnSchedule: onSchedule))
                    {
                        Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyFrontline,
                            CityMaintenanceBenchmarkRules.Group);
                        DriveSlaveArmyFrontline(army, pCity);
                        Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyFrontline,
                            CityMaintenanceBenchmarkRules.Group);
                    }
                    RenameArmyIfSlaveArmy(army);
                    RecordSlaveArmyFormation(kingdom, pCity);
                    return;
                }
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyExisting, CityMaintenanceBenchmarkRules.Group);

            if (slaveCount < 0)
            {
                Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmySlaveCount, CityMaintenanceBenchmarkRules.Group);
                slaveCount = CountSlaves(pCity);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmySlaveCount, CityMaintenanceBenchmarkRules.Group);
            }
            if (slaveCount < MIN_SLAVES_FOR_SLAVE_ARMY)
            {
                MarkSlaveArmyMaintenanceFailure(pCity, now);
                return;
            }

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyCaptain, CityMaintenanceBenchmarkRules.Group);
            Actor captain = PickSlaveArmyCaptain(pCity, army);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyCaptain, CityMaintenanceBenchmarkRules.Group);
            if (captain == null)
            {
                MarkSlaveArmyMaintenanceFailure(pCity, now);
                return;
            }

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyEnsure, CityMaintenanceBenchmarkRules.Group);
            army = AWArmyService.EnsureArmy(kingdom, pCity, captain, AWArmyRole.SlaveArmy,
                BuildSlaveArmyName(kingdom, pCity, 1), pDetached: false);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyEnsure, CityMaintenanceBenchmarkRules.Group);
            if (army == null)
            {
                MarkSlaveArmyMaintenanceFailure(pCity, now);
                return;
            }

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyFill, CityMaintenanceBenchmarkRules.Group);
            _formingSlaveArmy = true;
            int addedThisPass = 0;
            try
            {
                addedThisPass = FillSlaveArmy(army, pCity);
            }
            finally
            {
                _formingSlaveArmy = false;
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyFill, CityMaintenanceBenchmarkRules.Group);
            EnsureNonSlaveCaptain(army);
            CountArmyComposition(army, out int finalTotal, out int finalSlaves, out int finalNonSlaves);
            Actor finalCaptain = army.getCaptain();
            bool finalCaptainValid = CanBeSlaveArmyCaptainCandidate(finalCaptain, kingdom, pCity,
                pRequireWarrior: true);
            if (addedThisPass > 0 || SlaveArmyMaintenanceRules.ShouldSkipStableArmyFill(
                    pArmyExists: true,
                    pTotalWarriors: finalTotal,
                    pSlaveWarriors: finalSlaves,
                    pNonSlaveWarriors: finalNonSlaves,
                    pCaptainValid: finalCaptainValid,
                    pCitySlaveCount: slaveCount))
                ClearSlaveArmyMaintenanceFailure(pCity);
            else
                MarkSlaveArmyMaintenanceFailure(pCity, now);
            AssignSlaveCatcherJobToCaptain(army.getCaptain());
            if (SlaveArmyMaintenanceRules.ShouldDriveFrontline(
                    pHasArmy: true,
                    pHasEnemies: KingdomHasEnemies(kingdom),
                    pOnSchedule: onSchedule))
            {
                Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyFrontline, CityMaintenanceBenchmarkRules.Group);
                DriveSlaveArmyFrontline(army, pCity);
                Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyFrontline, CityMaintenanceBenchmarkRules.Group);
            }
            RenameArmyIfSlaveArmy(army);
            Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyRecord, CityMaintenanceBenchmarkRules.Group);
            RecordSlaveArmyFormation(kingdom, pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyRecord, CityMaintenanceBenchmarkRules.Group);
        }

        public static void RenameArmyIfSlaveArmy(Army pArmy)
        {
            if (pArmy == null) return;
            if (!SlaveArmyMaintenanceRules.ShouldRefreshKingdomArmyNames(IsSlaveArmy(pArmy))) return;
            RefreshSingleSlaveArmyName(pArmy);
        }

        private static void RefreshSingleSlaveArmyName(Army pArmy)
        {
            if (pArmy?.data == null) return;
            Kingdom kingdom = pArmy.getKingdom();
            City anchor = FindArmyAnchorCity(pArmy);
            string name = BuildSlaveArmyName(kingdom, anchor, 1);
            if (pArmy.data.name == name && pArmy.data.custom_name) return;
            pArmy.data.custom_name = true;
            pArmy.setName(name);
        }

        public static void RefreshArmyNames(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveArmyNameScan, CityMaintenanceBenchmarkRules.Group);
            var slaveArmies = new List<Army>();
            if (World.world?.armies != null)
            {
                foreach (Army army in World.world.armies)
                {
                    if (army?.data == null || !army.isAlive()) continue;
                    try
                    {
                        if (army.getKingdom() != pKingdom) continue;
                    }
                    catch { continue; }
                    if (IsSlaveArmy(army))
                        slaveArmies.Add(army);
                }
            }
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveArmyNameScan, CityMaintenanceBenchmarkRules.Group);

            for (int i = 0; i < slaveArmies.Count; i++)
            {
                Army army = slaveArmies[i];
                City anchor = FindArmyAnchorCity(army);
                string name = BuildSlaveArmyName(pKingdom, anchor, slaveArmies.Count == 1 ? 1 : i + 1);
                if (army.data.name == name && army.data.custom_name) continue;
                army.data.custom_name = true;
                army.setName(name);
            }
        }

        private static bool CanBeEnslaved(Actor pActor, bool pAllowImportantCapture = false)
        {
            if (pActor?.data == null) return false;
            if (!IsSupportedSlaveryActor(pActor)) return false;
            if (pActor.isRekt()) return false;
            if ((pActor.isKing() || pActor.isCityLeader()) && !pAllowImportantCapture) return false;
            if (HeirService.IsCurrentHeir(pActor.kingdom, pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            if (IsSlave(pActor) || IsRetiredSoldier(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor)) return false;
            return true;
        }

        private static bool SafeIsKing(Actor pActor)
        {
            try { return pActor?.data != null && pActor.isKing(); }
            catch { return false; }
        }

        private static bool HasCaptureTargetForCity(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom == null) return false;
            WorldTile origin = pCity.getTile();
            if (origin == null) return false;

            using ListPool<Kingdom> enemies = pCity.kingdom.getEnemiesKingdoms();
            foreach (Kingdom enemy in enemies)
            {
                if (enemy?.data == null) continue;
                foreach (Actor target in enemy.getUnits())
                {
                    if (!CanBeCapturedAsTarget(target, pAllowImportantCapture: true)) continue;
                    if (target.current_tile == null || !origin.isSameIsland(target.current_tile)) continue;
                    if (Toolbox.SquaredDistTile(origin, target.current_tile) <=
                        SLAVE_CATCHER_SEARCH_RADIUS * SLAVE_CATCHER_SEARCH_RADIUS) return true;
                }
            }
            return false;
        }

        private static bool ShouldRunCityMaintenance(City pCity, string pKey, int pInterval)
        {
            if (pCity?.data == null) return false;
            int now = (int)LineageService.CurTime();
            pCity.data.get(pKey, out int lastRun, -1);
            if (!CityMaintenanceThrottleRules.ShouldRun(now, lastRun, pInterval)) return false;
            pCity.data.set(pKey, now);
            return true;
        }

        private static bool ShouldRunCityMaintenanceStaggered(City pCity, string pKey, int pInterval)
        {
            if (pCity?.data == null) return false;
            int now = (int)LineageService.CurTime();
            pCity.data.get(pKey, out int lastRun, -1);
            if (!CityMaintenanceThrottleRules.ShouldRunStaggered(now, lastRun, pInterval, pCity.id)) return false;
            pCity.data.set(pKey, now);
            return true;
        }

        private static bool CanCaptureTarget(Actor pCatcher, Actor pTarget)
        {
            if (!CanBeSlaveCatcher(pCatcher)) return false;
            return CanCaptureTargetForKnownCatcher(pCatcher, pTarget);
        }

        private static bool CanCaptureTargetForKnownCatcher(Actor pCatcher, Actor pTarget)
        {
            if (!CanBeCapturedAsTarget(pTarget, pAllowImportantCapture: true)) return false;
            if (pCatcher.kingdom == null || pTarget.kingdom == null) return false;
            if (pCatcher.kingdom == pTarget.kingdom) return false;
            if (!pCatcher.kingdom.isEnemy(pTarget.kingdom)) return false;
            if (pCatcher.current_tile == null || pTarget.current_tile == null) return false;
            if (!pCatcher.current_tile.isSameIsland(pTarget.current_tile)) return false;
            float threshold = IsImportantCaptureTarget(pTarget)
                ? IMPORTANT_CAPTURE_HEALTH_RATIO
                : DIRECT_CAPTURE_HEALTH_RATIO;
            return pTarget.getHealthRatio() <= threshold;
        }

        private static bool ShouldRunCaptureSearch(Actor pCatcher)
        {
            if (pCatcher?.data == null) return false;
            double now = LineageService.CurTime();
            CaptureSearchNextAllowed.TryGetValue(pCatcher.data.id, out double nextAllowed);
            return ActorAiSearchThrottleRules.ShouldSearch(now, nextAllowed);
        }

        private static bool ShouldScanCaptureTargetsFromCurrentPosition(Actor pCatcher)
        {
            Kingdom kingdom = pCatcher?.kingdom;
            bool hasEnemyWar = false;
            try { hasEnemyWar = kingdom?.data != null && kingdom.hasEnemies(); }
            catch { hasEnemyWar = false; }

            bool inEnemyTerritory = IsInEnemyTerritory(pCatcher?.current_tile, kingdom);
            return SlaveCaptureCommandRules.ShouldScanForCaptureTargets(hasEnemyWar, inEnemyTerritory);
        }

        private static bool IsInEnemyTerritory(WorldTile pTile, Kingdom pKingdom)
        {
            if (pTile == null || pKingdom?.data == null) return false;
            try
            {
                if (!pTile.hasCity()) return false;
                Kingdom owner = pTile.zone_city?.kingdom;
                return owner?.data != null && owner != pKingdom && pKingdom.isEnemy(owner);
            }
            catch { return false; }
        }

        private static void MarkCaptureSearchResult(Actor pCatcher, Actor pTarget)
        {
            if (pCatcher?.data == null) return;
            long id = pCatcher.data.id;
            if (pTarget?.data != null)
            {
                CaptureSearchNextAllowed.Remove(id);
                return;
            }

            CaptureSearchNextAllowed[id] = ActorAiSearchThrottleRules.NextAllowedAfterMiss(
                LineageService.CurTime(), SLAVE_CAPTURE_SEARCH_MISS_COOLDOWN);
        }

        private static bool RollCombatCapture(Actor pCaptor, Actor pTarget)
        {
            float chance = IsImportantCaptureTarget(pTarget)
                ? COMBAT_CAPTURE_IMPORTANT_CHANCE
                : pTarget.isWarrior()
                    ? COMBAT_CAPTURE_WARRIOR_CHANCE
                    : COMBAT_CAPTURE_CIVILIAN_CHANCE;

            if (pCaptor != null && IsSlaveArmyCaptain(pCaptor))
                chance += COMBAT_CAPTURE_CATCHER_BONUS;

            chance = Math.Max(0f, Math.Min(0.95f, chance));
            return Rng.NextDouble() < chance;
        }

        private static bool CanBeCapturedAsTarget(Actor pTarget, bool pAllowImportantCapture = false)
        {
            if (!CanBeEnslaved(pTarget, pAllowImportantCapture)) return false;
            if (!pTarget.isAdult()) return false;
            if (ChronicleGate.IsImportant(pTarget) && !IsImportantCaptureTarget(pTarget)) return false;
            return true;
        }

        private static bool IsImportantCaptureTarget(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            return pActor.isKing() || pActor.isCityLeader();
        }

        public static bool IsSlaveArmy(Army pArmy)
        {
            if (pArmy == null) return false;
            if (AWArmyService.IsRoleArmy(pArmy, AWArmyRole.SlaveArmy)) return true;

            int total = 0;
            int slaves = 0;
            int nonSlaves = 0;
            foreach (Actor unit in pArmy.getUnits())
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (!unit.isWarrior()) continue;
                total++;
                if (IsSlave(unit)) slaves++;
                else nonSlaves++;
            }

            if (total < MIN_SLAVES_FOR_SLAVE_ARMY) return false;
            Actor captain = pArmy.getCaptain();
            return SlaveArmyFormationRules.IsSlaveArmyComposition(total, slaves, nonSlaves,
                captain?.data != null && !IsSlave(captain));
        }

        public static bool IsSlaveArmyCaptain(Actor pActor)
        {
            if (pActor?.data == null || !pActor.hasArmy()) return false;
            Army army = pActor.army;
            if (!IsSlaveArmy(army)) return false;
            return army.getCaptain() == pActor;
        }

        private static void ApplySlaveIdentity(Actor pActor, string pReason, Actor pCaptor)
        {
            if (RoyalGuardService.IsRoyalGuard(pActor))
                RoyalGuardService.DismissGuard(pActor, "enslaved");

            long now = (long)LineageService.CurTime();
            pActor.data.get(LineageKeys.SLAVE_SINCE, out long since, -1L);
            if (since < 0) pActor.data.set(LineageKeys.SLAVE_SINCE, now);

            pActor.data.set(LineageKeys.SLAVE_REASON, pReason ?? "");
            pActor.data.set(LineageKeys.SLAVE_CAPTURED_BY, pCaptor?.data?.id ?? -1L);
            pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.SLAVE);
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 99);

            if (pActor.isWarrior()) pActor.stopBeingWarrior();
            if (!pActor.hasTrait(LineageKeys.TRAIT_SLAVE)) pActor.addTrait(LineageKeys.TRAIT_SLAVE);
            if (pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) pActor.removeTrait(LineageKeys.TRAIT_GUIZU);
            if (pActor.hasTrait(LineageKeys.TRAIT_ZHUHOU)) pActor.removeTrait(LineageKeys.TRAIT_ZHUHOU);

            BreakInvalidLover(pActor);
            LineageService.ApplyDisplayName(pActor);
            LineageService.ArchiveActor(pActor, pAlive: true);
            pActor.clearGraphicsFully();
        }

        private static void BreakInvalidLover(Actor pActor)
        {
            Actor lover = pActor?.lover;
            if (lover?.data == null) return;
            if (CanFallInLoveByStatus(pActor, lover)) return;

            pActor.setLover(null);
            lover.setLover(null);
            pActor.data.lover = -1L;
            lover.data.lover = -1L;
        }

        private static bool CanUseSlaveArmy(City pCity)
        {
            if (pCity?.data == null) return false;
            Kingdom kingdom = pCity.kingdom;
            if (!IsSlaveryEnabled(kingdom)) return false;
            return IsSlaveArmyEnabled(kingdom) && CountSlaves(pCity) >= MIN_SLAVES_FOR_SLAVE_ARMY;
        }

        private static bool KingdomHasEnemies(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && pKingdom.hasEnemies(); }
            catch { return false; }
        }

        private static float RandomWait(float pMin, float pMax)
        {
            if (pMax < pMin) return pMax;
            return (float)(pMin + Rng.NextDouble() * (pMax - pMin));
        }

        private static Actor PickSlaveArmyCaptain(City pCity, Army pExisting = null)
        {
            if (pCity?.data == null) return null;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null) return null;

            Army existing = pExisting ?? AWArmyService.FindArmy(kingdom, pCity, AWArmyRole.SlaveArmy);
            Actor currentCaptain = existing?.getCaptain();
            if (CanBeSlaveArmyCaptainCandidate(currentCaptain, kingdom, pCity, pRequireWarrior: true))
                return currentCaptain;

            Actor warrior = PickBestSlaveArmyCadre(pCity, pRequireWarrior: true);
            if (warrior != null) return warrior;

            return TryRaiseNonSlaveCaptain(pCity);
        }

        private static int FillSlaveArmy(Army pArmy, City pCity)
        {
            if (pArmy?.data == null || pCity?.data == null) return 0;

            int addedThisPass = 0;
            Actor captain = pArmy.getCaptain();
            if (CanBeSlaveArmyCaptainCandidate(captain, pCity.kingdom, pCity, pRequireWarrior: false) &&
                EnsureWarriorForSlaveArmy(pCity, captain))
            {
                AWArmyService.AddToArmy(captain, pArmy);
            }

            CountArmyComposition(pArmy, out int total, out int slaves, out int nonSlaves);
            foreach (Actor cadre in pCity.getUnits())
            {
                if (SlaveArmyMaintenanceRules.ShouldStopFillBatch(addedThisPass, SLAVE_ARMY_FILL_BATCH_LIMIT)) break;
                if (total >= MAX_SLAVE_ARMY_SIZE) break;
                if (nonSlaves >= SlaveArmyFormationRules.MaxNonSlaveCadres) break;
                if (cadre == captain || cadre.army == pArmy) continue;
                if (!CanBeSlaveArmyCaptainCandidate(cadre, pCity.kingdom, pCity, pRequireWarrior: false)) continue;
                if (!EnsureWarriorForSlaveArmy(pCity, cadre)) continue;
                AWArmyService.AddToArmy(cadre, pArmy);
                total++;
                nonSlaves++;
                addedThisPass++;
            }

            foreach (Actor slave in pCity.getUnits())
            {
                if (SlaveArmyMaintenanceRules.ShouldStopFillBatch(addedThisPass, SLAVE_ARMY_FILL_BATCH_LIMIT)) break;
                if (total >= MAX_SLAVE_ARMY_SIZE) break;
                if (slave?.data == null || slave.isRekt() || !slave.isAdult()) continue;
                if (!IsSlave(slave) || IsRetiredSoldier(slave)) continue;
                if (RoyalGuardService.IsRoyalGuard(slave)) continue;
                if (slave.asset?.is_boat == true) continue;
                if (slave.army == pArmy) continue;
                if (!EnsureWarriorForSlaveArmy(pCity, slave)) continue;
                if (!SlaveArmyFormationRules.CanAddSlaveToArmy(total, slaves, nonSlaves)) break;
                AWArmyService.AddToArmy(slave, pArmy);
                slave.data.set(LineageKeys.SLAVE_SOLDIER, true);
                total++;
                slaves++;
                addedThisPass++;
            }

            return addedThisPass;
        }

        private static void MarkSlaveArmyMaintenanceFailure(City pCity, int pNow)
        {
            if (pCity?.data == null) return;
            pCity.data.set(LineageKeys.SLAVE_ARMY_FAILURE_YEAR, pNow);
        }

        private static void ClearSlaveArmyMaintenanceFailure(City pCity)
        {
            if (pCity?.data == null) return;
            pCity.data.set(LineageKeys.SLAVE_ARMY_FAILURE_YEAR, -1);
        }

        private static void AssignSlaveCatcherJobToCaptain(Actor pCaptain)
        {
            if (SlaveryContent.SlaveCatcherJob == null) return;
            if (!CanBeSlaveCatcher(pCaptain)) return;
            if (pCaptain.citizen_job == SlaveryContent.SlaveCatcherJob) return;
            try { pCaptain.setCitizenJob(SlaveryContent.SlaveCatcherJob); }
            catch { }
        }

        private static void DriveSlaveArmyFrontline(Army pArmy, City pCity)
        {
            if (pArmy?.data == null || pCity?.data == null || pCity.kingdom?.data == null) return;
            try
            {
                if (!pCity.kingdom.hasEnemies()) return;
            }
            catch { return; }

            Actor target = FindNearestEnemyForSlaveArmy(pArmy, pCity);
            if (target?.current_tile == null) return;

            int issued = 0;
            foreach (Actor unit in pArmy.getUnits())
            {
                if (issued >= MAX_SLAVE_ARMY_SIZE) break;
                if (unit?.data == null || unit.isRekt() || unit.current_tile == null) continue;
                if (unit.army != pArmy || !unit.isWarrior()) continue;
                if (!unit.current_tile.isSameIsland(target.current_tile)) continue;
                try
                {
                    unit.beh_actor_target = target;
                    unit.goTo(target.current_tile);
                    issued++;
                }
                catch { }
            }
        }

        private static Actor FindNearestEnemyForSlaveArmy(Army pArmy, City pCity)
        {
            WorldTile origin = null;
            try { origin = pArmy.getCaptain()?.current_tile; } catch { }
            if (origin == null) origin = pCity.getTile();
            if (origin == null) return null;

            Actor best = null;
            int bestDist = int.MaxValue;
            using ListPool<Kingdom> enemies = pCity.kingdom.getEnemiesKingdoms();
            foreach (Kingdom enemy in enemies)
            {
                if (enemy?.data == null) continue;
                foreach (Actor target in enemy.getUnits())
                {
                    if (target?.data == null || target.isRekt() || target.current_tile == null) continue;
                    if (!origin.isSameIsland(target.current_tile)) continue;
                    int dist = Toolbox.SquaredDistTile(origin, target.current_tile);
                    if (dist >= bestDist) continue;
                    bestDist = dist;
                    best = target;
                }
            }
            return best;
        }

        private static string BuildSlaveArmyName(Kingdom pKingdom, City pCity, int pIndex)
        {
            string name = AWArmyRoleRules.DisplayName(AWArmyRole.SlaveArmy, pKingdom?.name ?? "", pIndex);
            string cityName = pCity?.data?.name;
            return string.IsNullOrEmpty(cityName) ? name : cityName + " " + name;
        }

        private static City FindArmyAnchorCity(Army pArmy)
        {
            long cityId = AWArmyService.GetAnchorCityId(pArmy);
            if (cityId >= 0 && World.world?.cities != null)
            {
                try
                {
                    City city = World.world.cities.get(cityId);
                    if (city?.data != null) return city;
                }
                catch { }
            }

            try { return pArmy?.getCity(); }
            catch { return null; }
        }

        private static Actor PickBestSlaveArmyCadre(City pCity, bool pRequireWarrior)
        {
            Actor best = null;
            float bestScore = float.MinValue;
            foreach (Actor unit in GetSlaveArmyCadreCandidates(pCity))
            {
                if (pRequireWarrior && !unit.isWarrior()) continue;
                float score = SafeCombatScore(unit);
                if (score <= bestScore) continue;
                bestScore = score;
                best = unit;
            }
            return best;
        }

        private static List<Actor> GetSlaveArmyCadreCandidates(City pCity)
        {
            var result = new List<Actor>();
            if (pCity?.data == null) return result;
            Kingdom kingdom = pCity.kingdom;
            foreach (Actor unit in pCity.getUnits())
                if (CanBeSlaveArmyCaptainCandidate(unit, kingdom, pCity, pRequireWarrior: false))
                    result.Add(unit);
            return result;
        }

        private static List<Actor> GetSlaveArmySlaveCandidates(City pCity)
        {
            var result = new List<Actor>();
            if (pCity?.data == null) return result;
            foreach (Actor unit in pCity.getUnits())
            {
                if (unit?.data == null || unit.isRekt() || !unit.isAdult()) continue;
                if (!IsSlave(unit) || IsRetiredSoldier(unit)) continue;
                if (RoyalGuardService.IsRoyalGuard(unit)) continue;
                if (unit.asset?.is_boat == true) continue;
                result.Add(unit);
            }
            return result;
        }

        private static bool EnsureWarriorForSlaveArmy(City pCity, Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            if (pActor.isWarrior()) return true;
            if (pCity?.data == null || pActor.city != pCity) return false;
            if (!pCity.checkCanMakeWarrior(pActor)) return false;
            pCity.makeWarrior(pActor);
            return pActor.isWarrior();
        }

        private static bool CanBeSlaveArmyCaptainCandidate(Actor pActor, Kingdom pKingdom, City pCity,
            bool pRequireWarrior)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom || pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.asset?.is_boat == true) return false;
            if (pRequireWarrior && !pActor.isWarrior()) return false;
            if (IsSlave(pActor) || IsRetiredSoldier(pActor)) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (GeneralService.IsGeneral(pActor) || GeneralService.IsFiefHolder(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor)) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
            return pCity == null || pActor.city == pCity || pActor.isWarrior();
        }

        private static void CountArmyComposition(Army pArmy, out int pTotal, out int pSlaves, out int pNonSlaves)
        {
            pTotal = 0;
            pSlaves = 0;
            pNonSlaves = 0;
            if (pArmy == null) return;
            foreach (Actor unit in pArmy.getUnits())
            {
                if (unit?.data == null || unit.isRekt() || unit.army != pArmy || !unit.isWarrior()) continue;
                pTotal++;
                if (IsSlave(unit)) pSlaves++;
                else pNonSlaves++;
            }
        }

        private static float SafeCombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return SafeStat(pActor, "damage") + SafeStat(pActor, "warfare") * 2f +
                   SafeStat(pActor, "health") * 0.08f + SafeStat(pActor, "armor") * 1.5f;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }

        private static bool IsSlaveArmyEnabled(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.SLAVE_ARMY_ENABLED, out bool enabled, false);
            return enabled;
        }

        private static void EnableSlaveArmy(Kingdom pKingdom)
        {
            SetSlaveArmyEnabled(pKingdom, true);
        }

        private static bool IsSupportedSlaveryActor(Actor pActor)
        {
            return LineageService.IsXia(pActor) || LineageService.IsHuman(pActor);
        }

        private static void RecordSlaveArmyFormation(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.SLAVE_ARMY_RECORDED, out bool recorded, false);
            if (recorded) return;
            pKingdom.data.set(LineageKeys.SLAVE_ARMY_RECORDED, true);
            ChronicleEvents.OnSlaveArmyFormed(pKingdom, pCity);
        }

        private static int CountSlaves(City pCity)
        {
            if (pCity?.data == null) return 0;
            int count = 0;
            foreach (Actor unit in pCity.getUnits())
                if (IsSlave(unit)) count++;
            return count;
        }

        private static void CountCityWarriors(City pCity, out int pTotal, out int pSlaves, out int pNonSlaves)
        {
            pTotal = 0;
            pSlaves = 0;
            pNonSlaves = 0;
            if (pCity?.data == null) return;

            foreach (Actor unit in pCity.getUnits())
            {
                if (unit?.data == null || unit.isRekt() || !unit.isWarrior()) continue;
                pTotal++;
                if (IsSlave(unit)) pSlaves++;
                else pNonSlaves++;
            }
        }

        private static Actor PickNonSlaveWarrior(City pCity)
        {
            if (pCity?.data == null) return null;
            foreach (Actor unit in pCity.getUnits())
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (!unit.isWarrior()) continue;
                if (!CanBeSlaveArmyCaptainCandidate(unit, pCity.kingdom, pCity, pRequireWarrior: true)) continue;
                return unit;
            }
            return null;
        }

        private static Actor TryRaiseNonSlaveCaptain(City pCity)
        {
            if (pCity?.data == null) return null;
            foreach (Actor unit in pCity.getUnits())
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (unit.asset?.is_boat == true || unit.isBaby()) continue;
                if (unit.isWarrior()) continue;
                if (!CanBeSlaveArmyCaptainCandidate(unit, pCity.kingdom, pCity, pRequireWarrior: false)) continue;
                if (!pCity.checkCanMakeWarrior(unit)) continue;

                pCity.makeWarrior(unit);
                return unit.isWarrior() ? unit : null;
            }
            return null;
        }

        private static Actor PickNonSlaveWarrior(Army pArmy)
        {
            if (pArmy == null) return null;
            foreach (Actor unit in pArmy.getUnits())
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (!unit.isWarrior()) continue;
                if (unit.army != pArmy) continue;
                if (IsSlave(unit)) continue;
                return unit;
            }
            return null;
        }

        private static void UpsertSlaveState(Actor pActor, bool pActive, City pContextCity, Kingdom pContextKingdom)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null || pActor?.data == null) return;

            string table = SlaveStateTableItem.GetTableName();
            Kingdom kingdom = pContextKingdom ?? pActor.kingdom ?? pContextCity?.kingdom;
            City city = pContextCity ?? pActor.city;
            pActor.data.get(LineageKeys.SLAVE_SINCE, out long sinceRaw, (long)LineageService.CurTime());
            double since = sinceRaw;
            pActor.data.get(LineageKeys.SLAVE_REASON, out string reason, "");
            pActor.data.get(LineageKeys.SLAVE_CAPTURED_BY, out long capturedBy, -1L);
            pActor.data.get(LineageKeys.SLAVE_MERIT, out int merit, 0);
            pActor.data.get(LineageKeys.SLAVE_SOLDIER, out bool soldier, false);
            pActor.data.get(LineageKeys.SOLDIER_SERVICE_START_TIME, out float soldierStart, -1f);
            pActor.data.get(LineageKeys.FREEDMAN, out bool freedman, false);

            var values = new[]
            {
                ColumnVal.Create("ACTOR_NAME", pActor.getName() ?? ""),
                ColumnVal.Create("KINGDOM_ID", kingdom?.id ?? -1L),
                ColumnVal.Create("KINGDOM_NAME", kingdom?.name ?? ""),
                ColumnVal.Create("CITY_ID", city?.id ?? -1L),
                ColumnVal.Create("CITY_NAME", city?.data?.name ?? ""),
                ColumnVal.Create("ENSLAVED_TIME", since),
                ColumnVal.Create("FREED_TIME", pActive ? -1.0 : LineageService.CurTime()),
                ColumnVal.Create("REASON", reason ?? ""),
                ColumnVal.Create("CAPTURED_BY_ACTOR_ID", capturedBy),
                ColumnVal.Create("MERIT", merit),
                ColumnVal.Create("ACTIVE", pActive ? 1 : 0),
                ColumnVal.Create("SOLDIER", soldier ? 1 : 0),
                ColumnVal.Create("SOLDIER_START_TIME", soldierStart),
                ColumnVal.Create("FREEDMAN", freedman ? 1 : 0)
            };

            try
            {
                if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id)))
                {
                    db.UpdateValue(table,
                        new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pActor.data.id) },
                        values);
                    return;
                }

                var insertValues = new List<ColumnVal> { ColumnVal.Create("ACTOR_ID", pActor.data.id) };
                insertValues.AddRange(values);
                db.Insert(table, insertValues.ToArray());
            }
            catch (Exception e)
            {
                ModClass.LogWarning("SlaveState upsert failed: " + e.Message);
            }
        }

        public static string ReasonLabel(string pReason)
        {
            return pReason switch
            {
                "city_fall" => "城破被俘",
                "captured" => "俘获",
                "battlefield_capture" => "\u6218\u573A\u4FD8\u83B7",
                "foreign_occupation" => "\u5916\u65CF\u5360\u9886",
                "slave_king" => "\u56FD\u738B\u4E3A\u5974",
                "slave_only_rebel" => "\u5974\u96B6\u56FD\u6539\u7F16\u4E3A\u4E49\u519B",
                "born_slave" => "奴籍所生",
                "military_merit" => "军功释奴",
                "promoted" => "因受任官职释奴",
                _ => string.IsNullOrEmpty(pReason) ? "奴籍登记" : pReason
            };
        }
    }
}
