using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class SlaveService
    {
        private const float RETIREMENT_AGE_RATIO = 0.7f;
        private const float CITY_FALL_SLAVE_RATIO = 0.10f;
        private const float DIRECT_CAPTURE_HEALTH_RATIO = 0.85f;
        private const float IMPORTANT_CAPTURE_HEALTH_RATIO = 0.45f;
        private const float COMBAT_CAPTURE_WARRIOR_CHANCE = 0.28f;
        private const float COMBAT_CAPTURE_CIVILIAN_CHANCE = 0.40f;
        private const float COMBAT_CAPTURE_IMPORTANT_CHANCE = 0.08f;
        private const float COMBAT_CAPTURE_CATCHER_BONUS = 0.25f;
        private const int SLAVE_CATCHER_SEARCH_RADIUS = 80;
        private const int MAX_CITY_FALL_SLAVES = 8;
        private const int MAX_CITY_FALL_SCAN = 80;
        private const int MIN_SERVICE_YEARS_BEFORE_RETIREMENT = 5;
        private const int SLAVE_MIN_SERVICE_YEARS_BEFORE_RETIREMENT = 8;
        private const int MERIT_FOR_FREEDOM = 8;
        private const int CITY_RETIREMENT_CHECK_INTERVAL = 20;
        private const int CITY_SLAVE_LABOR_CHECK_INTERVAL = 30;
        private const int CITY_SLAVE_CATCHER_CHECK_INTERVAL = 10;
        private const double SLAVE_CAPTURE_SEARCH_MISS_COOLDOWN = 2.0;
        private const float SLAVE_CAPTURE_NO_TARGET_WAIT_MIN = 3f;
        private const float SLAVE_CAPTURE_NO_TARGET_WAIT_MAX = 8f;
        private const float SLAVE_CAPTURE_FAILURE_WAIT_MIN = 2f;
        private const float SLAVE_CAPTURE_FAILURE_WAIT_MAX = 5f;
        private const float SLAVE_CAPTURE_SUCCESS_WAIT_MIN = 5f;
        private const float SLAVE_CAPTURE_SUCCESS_WAIT_MAX = 10f;
        private static readonly System.Random Rng = new System.Random();
        private static readonly Dictionary<long, List<PendingSlaveCaptureSummary>> PendingWarSlaveCaptures =
            new Dictionary<long, List<PendingSlaveCaptureSummary>>();

        private sealed class PendingSlaveCaptureSummary
        {
            public City city;
            public string cityName;
            public int count;
        }

        private sealed class SlaveStateSnapshot
        {
            public long actorId;
            public string actorName;
            public long kingdomId;
            public string kingdomName;
            public long cityId;
            public string cityName;
            public double enslavedTime;
            public double freedTime;
            public string reason;
            public long capturedByActorId;
            public int merit;
            public bool active;
            public bool soldier;
            public double soldierStartTime;
            public bool freedman;
        }

        internal static void ClearRuntimeCaches()
        {
            PendingWarSlaveCaptures.Clear();
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
            TemporarySlaveVanguardService.OnEmergencyChanged(pKingdom);
        }

        public static void SetSlaveArmyEnabled(Kingdom pKingdom, bool pEnabled)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.SLAVE_ARMY_ENABLED, pEnabled);
            if (pEnabled)
                pKingdom.data.set(LineageKeys.SLAVERY_ENABLED, true);
            TemporarySlaveVanguardService.OnEmergencyChanged(pKingdom);
        }

        public static void EnforceSlaveControl(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !IsSlaveryEnabled(pKingdom)) return;
            for (int i = 0; i < pKingdom.cities.Count; i++)
            {
                City city = pKingdom.cities[i];
                CheckCitySlaveLabor(city, pForce: true);
                if (city != null && city.hasArmy())
                {
                    EnsureNonSlaveCaptain(city.getArmy());
                }
            }
            TemporarySlaveVanguardService.OnEmergencyChanged(pKingdom);
        }

        public static void ResetSlaveFoodQuota(City pCity, bool pForceCount = false)
        {
            if (pCity?.data == null) return;

            Kingdom kingdom = pCity.kingdom;
            if (!SlaveFoodQuotaRules.ShouldCountSlavesForFoodQuota(
                    pHasCity: true,
                    pSlaveryEnabled: IsSlaveryEnabled(kingdom),
                    pForceCount: pForceCount))
                return;

            int quota = SlavePopulationIndexService.HasAny(pCity) ? (int)(pCity.countFood() * 0.1f) : 0;
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
                ResetSlaveFoodQuota(pCity, pForceCount: true);

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
            Bench.bench(CityMaintenanceBenchmarkRules.CaptureScanSubmit, CityMaintenanceBenchmarkRules.Group);
            CaptureTargetSearchState state = SlaveCaptureScanService.FindOrRequest(
                kingdom, pCity.getTile(), out _, pCity.id);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.CaptureScanSubmit, CityMaintenanceBenchmarkRules.Group);
            if (state == CaptureTargetSearchState.Hit)
                AssignSlaveCatcherAfterScan(pCity.id, kingdom.id);
        }

        public static Actor FindSlaveCaptureTarget(Actor pCatcher, int pSearchRadius)
        {
            FindSlaveCaptureTarget(pCatcher, pSearchRadius, out Actor target);
            return target;
        }

        internal static CaptureTargetSearchState FindSlaveCaptureTarget(Actor pCatcher, int pSearchRadius,
            out Actor pTarget)
        {
            pTarget = null;
            if (!CanBeSlaveCatcher(pCatcher)) return CaptureTargetSearchState.Miss;
            if (pCatcher.current_tile == null || pCatcher.kingdom == null) return CaptureTargetSearchState.Miss;
            if (!ShouldScanCaptureTargetsFromCurrentPosition(pCatcher)) return CaptureTargetSearchState.Miss;
            if (!ShouldRunCaptureSearch(pCatcher)) return CaptureTargetSearchState.Pending;

            Bench.bench(CityMaintenanceBenchmarkRules.CaptureScanSubmit, CityMaintenanceBenchmarkRules.Group);
            CaptureTargetSearchState state = SlaveCaptureScanService.FindOrRequest(
                pCatcher.kingdom, pCatcher.current_tile, out pTarget);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.CaptureScanSubmit, CityMaintenanceBenchmarkRules.Group);
            if (state == CaptureTargetSearchState.Hit)
            {
                MarkCaptureSearchResult(pCatcher, pTarget);
                return state;
            }
            if (state == CaptureTargetSearchState.Miss)
                MarkCaptureSearchResult(pCatcher, null);
            return state;
        }

        internal static void WaitAfterSlaveCapturePending(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(0.25f, 0.75f));
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
            Actor captor = pAttacker?.a;
            if (captor?.data == null || captor == pTarget) return false;
            if (captor.isRekt() || !captor.isAlive()) return false;
            if (!IsSupportedSlaveryActor(captor)) return false;

            Kingdom captorKingdom = captor.kingdom;
            if (captorKingdom?.data == null || !IsSlaveryEnabled(captorKingdom)) return false;
            if (pTarget?.data == null || pTarget.isRekt() || !pTarget.isAlive() || !pTarget.hasHealth()) return false;
            Kingdom targetKingdom = pTarget.kingdom;
            if (targetKingdom?.data == null) return false;
            if (captorKingdom == targetKingdom) return false;
            if (!captorKingdom.isEnemy(targetKingdom)) return false;
            if (pTarget.current_tile != null && captor.current_tile != null &&
                !pTarget.current_tile.isSameIsland(captor.current_tile)) return false;
            if (!CanBeCapturedAsTarget(pTarget, pAllowImportantCapture: true)) return false;

            float threshold = IsImportantCaptureTarget(pTarget)
                ? IMPORTANT_CAPTURE_HEALTH_RATIO
                : DIRECT_CAPTURE_HEALTH_RATIO;
            if (pTarget.getHealthRatio() > threshold) return false;
            if (!RollCombatCapture(captor, pTarget)) return false;

            City city = captor.city ?? captorKingdom.capital;
            if (city?.data == null) return false;

            bool nationalRecord = IsImportantCaptureTarget(pTarget);
            bool wasKingBeforeRelocation = SafeIsKing(pTarget);
            bool wasLeaderBeforeRelocation = SafeIsCityLeader(pTarget) || SafeIsArmyLeader(pTarget);
            Kingdom formerRulerKingdom = (wasKingBeforeRelocation || wasLeaderBeforeRelocation) ? targetKingdom : null;

            pTarget.cancelAllBeh();
            pTarget.clearAttackTarget();
            pTarget.beh_actor_target = null;
            pTarget.attackedBy = null;
            captor.clearAttackTarget();
            if (captor.beh_actor_target == pTarget)
                captor.beh_actor_target = null;
            pTarget.joinCity(city);
            pTarget.setHealth(Math.Max(1, pTarget.getMaxHealthPercent(0.2f)));

            bool changed = Enslave(pTarget, "battlefield_capture", captor, city, captorKingdom,
                pForceRecord: true, pForceNationalRecord: nationalRecord,
                pFormerRulerKingdom: formerRulerKingdom,
                pWasKingBeforeRelocation: wasKingBeforeRelocation,
                pWasLeaderBeforeRelocation: wasLeaderBeforeRelocation);
            if (!changed) return false;

            if (ShouldCountAsWarSlaveCapture(pTarget))
                QueueWarSlaveCaptureSummary(captorKingdom, city, 1);
            CheckCitySlaveLabor(city);
            return true;
        }

        public static bool CaptureTargetAsSlave(Actor pCatcher, Actor pTarget)
        {
            if (!CanCaptureTarget(pCatcher, pTarget)) return false;
            float captureChance = NobleCaptureRules.ResolveChance(
                1f, 0f, pTarget.hasTrait(LineageKeys.TRAIT_GUIZU));
            if (Rng.NextDouble() >= captureChance) return false;

            bool nationalRecord = IsImportantCaptureTarget(pTarget);
            bool wasKingBeforeRelocation = SafeIsKing(pTarget);
            bool wasLeaderBeforeRelocation = SafeIsCityLeader(pTarget) || SafeIsArmyLeader(pTarget);
            Kingdom formerRulerKingdom = (wasKingBeforeRelocation || wasLeaderBeforeRelocation) ? pTarget.kingdom : null;
            City city = pCatcher.city ?? pCatcher.kingdom?.capital;
            Kingdom kingdom = pCatcher.kingdom ?? city?.kingdom;
            if (city?.data == null || kingdom?.data == null) return false;

            pTarget.joinCity(city);
            bool changed = Enslave(pTarget, "captured", pCatcher, city, kingdom, pForceRecord: true,
                pForceNationalRecord: nationalRecord,
                pFormerRulerKingdom: formerRulerKingdom,
                pWasKingBeforeRelocation: wasKingBeforeRelocation,
                pWasLeaderBeforeRelocation: wasLeaderBeforeRelocation);
            if (changed && ShouldCountAsWarSlaveCapture(pTarget))
                QueueWarSlaveCaptureSummary(kingdom, city, 1);
            CheckCitySlaveLabor(city);
            return changed;
        }

        public static bool Enslave(Actor pActor, string pReason, Actor pCaptor = null,
            City pContextCity = null, Kingdom pContextKingdom = null, bool pForceRecord = false,
            bool pForceNationalRecord = false, Kingdom pFormerRulerKingdom = null,
            bool pWasKingBeforeRelocation = false, bool pWasLeaderBeforeRelocation = false)
        {
            Kingdom contextKingdom = pContextKingdom ?? pActor?.kingdom ?? pContextCity?.kingdom;
            if (!IsSlaveryEnabled(contextKingdom)) return false;
            if (!CanBeEnslaved(pActor, pForceNationalRecord)) return false;
            if (!RoyalGuardOfficeRules.CanReplaceLifetimeGuardIdentity(
                    RoyalGuardService.IsRoyalGuard(pActor))) return false;

            bool wasSlave = IsSlave(pActor);
            bool liveWasKing = SafeIsKing(pActor);
            bool liveWasLeader = SafeIsCityLeader(pActor) || SafeIsArmyLeader(pActor);
            bool wasKing = pWasKingBeforeRelocation || liveWasKing;
            bool wasLeader = pWasLeaderBeforeRelocation || liveWasLeader;
            Kingdom formerKingdom = pFormerRulerKingdom ?? (liveWasKing ? pActor.kingdom : null);
            bool preserveCapturedRuler = CapturedRulerCaptureRules.ShouldPreserveFormerKingContext(
                wasKing,
                formerKingdom?.id ?? -1L,
                contextKingdom?.id ?? -1L);
            if (!preserveCapturedRuler && !liveWasKing)
                formerKingdom = null;

            string dominantSchool = GetDominantCourtSchool(contextKingdom);
            CaptiveTreatmentAction captiveTreatment = CaptiveTreatmentRules.Decide(
                dominantSchool,
                wasKing,
                wasLeader,
                IsAtWar(contextKingdom, formerKingdom),
                EstimateHostilePowerRatio(formerKingdom, contextKingdom));

            if (captiveTreatment == CaptiveTreatmentAction.ExecuteCaptive)
            {
                if (preserveCapturedRuler)
                    RememberCapturedRulerContext(pActor, formerKingdom);
                if (preserveCapturedRuler)
                    CloseCapturedRulerOpenReign(pActor, formerKingdom, "captured_executed");

                ChronicleEvents.OnImportantCaptiveExecuted(pActor, pReason, formerKingdom,
                    contextKingdom ?? pActor.kingdom, pContextCity ?? pActor.city, pCaptor, dominantSchool);
                ExecuteImportantCaptive(pActor, pCaptor, dominantSchool);
                CheckCitySlaveLabor(pContextCity ?? pActor.city);
                return !wasSlave || pForceRecord;
            }

            bool releaseAsNobleDependent = captiveTreatment == CaptiveTreatmentAction.SettleAsNobleDependent &&
                                           CapturedRulerCaptureRules.ShouldReleaseAsNobleDependent(
                                               wasKing, wasLeader);
            TemporarySlaveVanguardService.OnMemberInvalidated(pActor);
            ApplySlaveIdentity(pActor, pReason, pCaptor);
            SlavePopulationIndexService.Activate(pActor, pContextCity ?? pActor.city);
            if (preserveCapturedRuler)
                RememberCapturedRulerContext(pActor, formerKingdom);
            UpsertSlaveState(pActor, pActive: true, pContextCity, pContextKingdom);

            if (!wasSlave || pForceRecord)
                ChronicleEvents.OnEnslaved(pActor, pReason, pContextKingdom ?? pActor.kingdom,
                    pContextCity ?? pActor.city, pForceNationalRecord);

            if (preserveCapturedRuler)
                ChronicleEvents.OnCapturedRulerEnslaved(pActor, pReason, formerKingdom,
                    contextKingdom ?? pActor.kingdom, pContextCity ?? pActor.city, pCaptor);

            bool abdicated = SlaveKingAbdicationService.TryForceAbdicate(pActor, pReason, wasKing, wasSlave,
                formerKingdom ?? contextKingdom);
            if (!abdicated && preserveCapturedRuler)
                CloseCapturedRulerOpenReign(pActor, formerKingdom, "captured_slave");

            if (releaseAsNobleDependent)
                ReleaseImportantCaptiveAsNobleDependent(pActor, pReason, contextKingdom ?? pActor.kingdom,
                    pContextCity ?? pActor.city, wasKing);

            if (IsSlave(pActor))
                TemporarySlaveVanguardService.OnCandidateAvailable(
                    pContextKingdom ?? pActor.kingdom ?? pContextCity?.kingdom,
                    pContextCity ?? pActor.city, pActor);

            CheckCitySlaveLabor(pContextCity ?? pActor.city);
            return !wasSlave || pForceRecord;
        }

        public static bool EnslaveByOccupation(Actor pActor, City pContextCity, Kingdom pOccupier,
            bool pImportantRecord = false)
        {
            if (pActor?.data == null || pContextCity?.data == null || pOccupier?.data == null) return false;
            if (!CanBeEnslaved(pActor, pImportantRecord)) return false;
            float captureChance = NobleCaptureRules.ResolveChance(
                1f, 0f, pActor.hasTrait(LineageKeys.TRAIT_GUIZU));
            if (Rng.NextDouble() >= captureChance) return false;
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

            TemporarySlaveVanguardService.OnMemberInvalidated(pActor);
            SlavePopulationIndexService.Deactivate(pActor);
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
            if (!SoldierRetirementRules.ShouldReadRetirementState(supportedActor, rekt, warrior)) return false;
            if (ActiveMilitaryLifecycleService.
                    HasWartimeMilitaryLock(pActor)) return false;
            float age = pActor.getAge();
            bool hardRetirement = SoldierRetirementRules.
                HasReachedHardRetirementAge(age);
            if (!hardRetirement &&
                (TemporaryLevyService.IsTemporaryLevy(pActor) ||
                 TemporarySlaveVanguardService.IsMember(pActor))) return false;
            bool alreadyRetired = IsRetiredSoldier(pActor);
            float lifespan = pActor.stats["lifespan"];
            if (!SoldierRetirementRules.ShouldRunExpensiveRetirementChecks(supportedActor, rekt, warrior,
                    alreadyRetired, age, lifespan, RETIREMENT_AGE_RATIO)) return false;

            bool general = GeneralService.IsGeneral(pActor);
            bool fiefHolder = GeneralService.IsFiefHolder(pActor);
            bool royalGuard = supportedActor && !rekt && warrior && !alreadyRetired && !general && !fiefHolder &&
                              RoyalGuardService.IsRoyalGuard(pActor);

            if (!SoldierRetirementRules.CanConsiderForRetirement(supportedActor, rekt, warrior, alreadyRetired,
                    general, fiefHolder, royalGuard, hardRetirement)) return false;

            if (!hardRetirement && !HasServedEnoughForRetirement(pActor))
                return false;

            pActor.stopBeingWarrior();
            if (!ActiveMilitaryLifecycleService.
                    CanCommitRetirement(pActor)) return false;
            pActor.data.set(LineageKeys.RETIRED_SOLDIER, true);
            pActor.data.set(LineageKeys.SLAVE_SOLDIER, false);
            if (!pActor.hasTrait(LineageKeys.TRAIT_VETERAN)) pActor.addTrait(LineageKeys.TRAIT_VETERAN);

            LineageService.ArchiveActor(pActor, pAlive: true);
            pActor.clearGraphicsFully();
            UpsertSlaveState(pActor, IsSlave(pActor), pActor.city, pActor.kingdom);
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
            if (IsRetiredSoldier(pActor))
                return MilitaryRecruitmentScope.Current !=
                       MilitaryRecruitmentKind.TemporaryLevy;
            if (!IsSlave(pActor)) return false;
            return MilitaryRecruitmentScope.Current !=
                       MilitaryRecruitmentKind.SlaveVanguard &&
                   MilitaryRecruitmentScope.Current !=
                       MilitaryRecruitmentKind.TemporaryLevy;
        }

        public static void OnMadeWarrior(City pCity, Actor pActor)
        {
            if (pActor?.data == null) return;
            if (!IsSupportedSlaveryActor(pActor)) return;

            if (IsRetiredSoldier(pActor))
            {
                if (MilitaryRecruitmentScope.Current !=
                        MilitaryRecruitmentKind.TemporaryLevy ||
                    !pActor.isWarrior())
                {
                    pActor.stopBeingWarrior();
                    return;
                }
                pActor.data.set(LineageKeys.RETIRED_SOLDIER, false);
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
            UpsertSlaveState(pActor, pActive: true, pCity ?? pActor.city, pActor.kingdom ?? pCity?.kingdom);
            ChronicleEvents.OnSlaveEnlisted(pActor, pActor.kingdom ?? pCity?.kingdom, pCity ?? pActor.city);
        }

        public static void OnTemporaryLevyDemobilized(Actor pActor)
        {
            if (pActor?.data == null || !IsSlave(pActor)) return;
            pActor.data.set(LineageKeys.SLAVE_SOLDIER, false);
            QueueSlaveStatePersistence(pActor, pActive: true,
                pActor.city, pActor.kingdom);
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
            int oldMerit = merit;
            merit += points;
            pKiller.data.set(LineageKeys.SLAVE_MERIT, merit);
            if (SlaveMeritPersistenceRules.ShouldPersist(
                    pOldMerit: oldMerit,
                    pNewMerit: merit,
                    pPoints: points,
                    pMilestone: 4,
                    pFreedomThreshold: MERIT_FOR_FREEDOM))
            {
                Bench.bench(CityMaintenanceBenchmarkRules.SlaveMeritPersist,
                    CityMaintenanceBenchmarkRules.Group);
                try
                {
                    UpsertSlaveState(pKiller, pActive: true, pKiller.city, pKiller.kingdom);
                }
                finally
                {
                    Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveMeritPersist,
                        CityMaintenanceBenchmarkRules.Group);
                }
            }

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

            var candidates = new List<Actor>(MAX_CITY_FALL_SLAVES);
            int eligibleCount = 0;
            int scanCount = Math.Min(MAX_CITY_FALL_SCAN, pCity.units.Count);
            for (int i = 0; i < scanCount; i++)
            {
                Actor unit = pCity.units[i];
                if (unit?.data == null) continue;
                if (!unit.isAdult()) continue;
                if (!CanBeEnslaved(unit)) continue;
                eligibleCount++;
                if (candidates.Count < MAX_CITY_FALL_SLAVES)
                    candidates.Add(unit);
            }

            int target = SlaveCaptureCommandRules.CityFallSlaveTargetCount(
                eligibleCount, CITY_FALL_SLAVE_RATIO, MAX_CITY_FALL_SLAVES);
            if (target <= 0) return;
            int enslaved = 0;
            for (int i = 0; i < target && i < candidates.Count; i++)
            {
                Actor candidate = candidates[i];
                float captureChance = NobleCaptureRules.ResolveChance(
                    1f, 0f, candidate.hasTrait(LineageKeys.TRAIT_GUIZU));
                if (Rng.NextDouble() >= captureChance) continue;
                if (Enslave(candidate, "city_fall", null, pCity, pNewKingdom) &&
                    ShouldCountAsWarSlaveCapture(candidates[i]))
                    enslaved++;
            }
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
                if (string.IsNullOrEmpty(summary.cityName))
                    summary.cityName = pCity?.data?.name ?? HistoryLocalizationRules.Text("aw_unknown_city");
                return;
            }

            summaries.Add(new PendingSlaveCaptureSummary
            {
                city = pCity,
                cityName = pCity?.data?.name ?? HistoryLocalizationRules.Text("aw_unknown_city"),
                count = pCount
            });
        }

        public static void CheckCitySlaveLabor(City pCity, bool pForce = false)
        {
            if (pCity?.data == null) return;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null) return;
            bool slaveryEnabled = IsSlaveryEnabled(kingdom);
            if (!slaveryEnabled) return;

            pCity.data.get(LineageKeys.SLAVE_LABOR_RECORDED, out long recordedKingdomId, -1L);
            bool alreadyRecorded = recordedKingdomId == kingdom.id;
            if (alreadyRecorded) return;
            bool maintenanceDue = pForce || ShouldRunCityMaintenanceStaggered(
                pCity, LineageKeys.SLAVE_LABOR_LAST_CHECK, CITY_SLAVE_LABOR_CHECK_INTERVAL);
            if (!SlaveArmyMaintenanceRules.ShouldCheckSlaveLabor(
                    pHasCity: true,
                    pHasKingdom: true,
                    pSlaveryEnabled: slaveryEnabled,
                    pAlreadyRecordedForKingdom: alreadyRecorded,
                    pMaintenanceDue: maintenanceDue)) return;

            Bench.bench(CityMaintenanceBenchmarkRules.SlaveLaborCount, CityMaintenanceBenchmarkRules.Group);
            int slaveCount = SlavePopulationIndexService.Count(pCity);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.SlaveLaborCount, CityMaintenanceBenchmarkRules.Group);
            if (slaveCount <= 0) return;
            pCity.data.set(LineageKeys.SLAVE_LABOR_RECORDED, kingdom.id);

            ChronicleEvents.OnSlaveLaborStarted(kingdom, pCity, slaveCount);
        }

        public static bool TryReplaceSlaveCaptain(Army pArmy, ref Actor pActor)
        {
            return !IsSlave(pActor);
        }

        public static void EnsureNonSlaveCaptain(Army pArmy)
        {
            if (pArmy == null) return;
            Actor captain = pArmy.getCaptain();
            if (!IsSlave(captain)) return;

            Actor replacement = PickNonSlaveWarrior(pArmy);
            if (replacement != null)
                AWArmyService.SetCaptainIfChanged(pArmy, replacement);
        }

        public static void RenameArmyIfSlaveArmy(Army pArmy)
        {
            if (!AWArmyService.IsRoleArmy(pArmy, AWArmyRole.SlaveArmy)) return;
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

            List<Army> slaveArmies = AWArmyService.GetRoleArmies(pKingdom, AWArmyRole.SlaveArmy);
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
            if ((pActor.isKing() || pActor.isCityLeader() || SafeIsArmyLeader(pActor)) && !pAllowImportantCapture) return false;
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

        private static bool SafeIsCityLeader(Actor pActor)
        {
            try { return pActor?.data != null && pActor.isCityLeader(); }
            catch { return false; }
        }

        private static bool SafeIsArmyLeader(Actor pActor)
        {
            try
            {
                if (pActor?.data == null) return false;
                if (GeneralService.IsGeneral(pActor)) return true;
                return pActor.hasArmy() && pActor.army?.getCaptain() == pActor;
            }
            catch { return false; }
        }

        private static bool ShouldCountAsWarSlaveCapture(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() && IsSlave(pActor);
        }

        private static string GetDominantCourtSchool(Kingdom pKingdom)
        {
            try
            {
                return CourtService.GetSnapshot(pKingdom).dominant_school ?? CourtSchoolId.None;
            }
            catch { return CourtSchoolId.None; }
        }

        private static bool IsAtWar(Kingdom pCaptor, Kingdom pFormer)
        {
            try { return pCaptor?.data != null && pFormer?.data != null && pCaptor.isEnemy(pFormer); }
            catch { return false; }
        }

        private static float EstimateHostilePowerRatio(Kingdom pFormer, Kingdom pCaptor)
        {
            try
            {
                float captorPower = Math.Max(1f, VassalService.GetPowerScore(pCaptor, pIncludeVassals: true));
                float formerPower = Math.Max(1f, VassalService.GetPowerScore(pFormer, pIncludeVassals: true));
                return formerPower / captorPower;
            }
            catch { return 1f; }
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
            return pCatcher?.kingdom?.data != null && pCatcher.current_tile != null &&
                   IsCaptureTargetForScan(pCatcher.kingdom, pCatcher.current_tile, pTarget, pRadius: 0);
        }

        internal static bool IsCaptureTargetForScan(Kingdom pKingdom, WorldTile pOrigin,
            Actor pTarget, int pRadius)
        {
            if (!CanBeCapturedAsTarget(pTarget, pAllowImportantCapture: true)) return false;
            if (pKingdom?.data == null || pTarget.kingdom?.data == null) return false;
            if (pKingdom == pTarget.kingdom || !pKingdom.isEnemy(pTarget.kingdom)) return false;
            if (pOrigin == null || pTarget.current_tile == null) return false;
            if (!pOrigin.isSameIsland(pTarget.current_tile)) return false;
            if (pRadius > 0 && Toolbox.SquaredDistTile(pOrigin, pTarget.current_tile) > pRadius * pRadius)
                return false;
            float threshold = IsImportantCaptureTarget(pTarget)
                ? IMPORTANT_CAPTURE_HEALTH_RATIO
                : DIRECT_CAPTURE_HEALTH_RATIO;
            return pTarget.getHealthRatio() <= threshold;
        }

        internal static void AssignSlaveCatcherAfterScan(long pCityId, long pKingdomId)
        {
            City city;
            try { city = World.world?.cities?.get(pCityId); }
            catch { return; }
            if (city?.data == null || city.kingdom?.data == null || city.kingdom.id != pKingdomId) return;
            if (!IsSlaveryEnabled(city.kingdom) || SlaveryContent.SlaveCatcherJob == null) return;
            if (city.getUnitsTotal() < 15 || city.jobs.countCurrentJobs(SlaveryContent.SlaveCatcherJob) > 0) return;
            city.jobs.addToJob(SlaveryContent.SlaveCatcherJob, 1);
        }

        private static bool ShouldRunCaptureSearch(Actor pCatcher)
        {
            if (pCatcher?.data == null) return false;
            double now = LineageService.CurTime();
            pCatcher.data.get(LineageKeys.SLAVE_CAPTURE_NEXT_SEARCH_TIME, out float nextAllowed, -1f);
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
            if (pTarget?.data != null)
            {
                pCatcher.data.set(LineageKeys.SLAVE_CAPTURE_NEXT_SEARCH_TIME, -1f);
                return;
            }

            pCatcher.data.set(LineageKeys.SLAVE_CAPTURE_NEXT_SEARCH_TIME,
                (float)ActorAiSearchThrottleRules.NextAllowedAfterMiss(
                    LineageService.CurTime(), SLAVE_CAPTURE_SEARCH_MISS_COOLDOWN));
        }

        private static bool RollCombatCapture(Actor pCaptor, Actor pTarget)
        {
            float baseChance = IsImportantCaptureTarget(pTarget)
                ? COMBAT_CAPTURE_IMPORTANT_CHANCE
                : pTarget.isWarrior()
                    ? COMBAT_CAPTURE_WARRIOR_CHANCE
                    : COMBAT_CAPTURE_CIVILIAN_CHANCE;

            float captorBonus = pCaptor != null && IsSlaveArmyCaptain(pCaptor)
                ? COMBAT_CAPTURE_CATCHER_BONUS
                : 0f;
            float chance = NobleCaptureRules.ResolveChance(baseChance,
                captorBonus, pTarget.hasTrait(LineageKeys.TRAIT_GUIZU));
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
            return pActor.isKing() || pActor.isCityLeader() || SafeIsArmyLeader(pActor);
        }

        public static bool IsSlaveArmy(Army pArmy)
        {
            return AWArmyService.IsRoleArmy(pArmy, AWArmyRole.SlaveArmy);
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

        private static void ReleaseImportantCaptiveAsNobleDependent(Actor pActor, string pReason,
            Kingdom pKingdom, City pCity, bool pWasKing)
        {
            if (pActor?.data == null || !IsSlave(pActor)) return;

            SlavePopulationIndexService.Deactivate(pActor);
            pActor.removeTrait(LineageKeys.TRAIT_SLAVE);
            pActor.data.set(LineageKeys.SLAVE_SOLDIER, false);
            pActor.data.set(LineageKeys.FREEDMAN, true);
            pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 0);

            try
            {
                LineageService.OnActorPromoted(pActor, pWasKing ? NobleTrigger.King : NobleTrigger.CityLeader);
            }
            catch { }

            pActor.data.set(LineageKeys.LINEAGE_STATUS, LineageStatus.NOBLE);
            pActor.data.set(LineageKeys.NOBLE_DISTANCE, 0);
            if (!pActor.hasTrait(LineageKeys.TRAIT_GUIZU)) pActor.addTrait(LineageKeys.TRAIT_GUIZU);

            string color = HistoryColors.FromKingdom(pKingdom ?? pActor.kingdom);
            pActor.data.set(LineageKeys.CAPTIVE_NOBLE_TITLE,
                HistoryLocalizationRules.Text("aw_hist_captive_noble_title"));
            pActor.data.set(LineageKeys.CAPTIVE_NOBLE_COLOR, color);

            BreakInvalidLover(pActor);
            LineageService.ApplyDisplayName(pActor);
            LineageService.ArchiveActor(pActor, pAlive: true);
            pActor.clearGraphicsFully();

            UpsertSlaveState(pActor, pActive: false, pCity ?? pActor.city, pKingdom ?? pActor.kingdom);
            ChronicleEvents.OnImportantCaptiveReleasedAsNoble(pActor, pReason, pKingdom, pCity);
        }

        private static void RememberCapturedRulerContext(Actor pActor, Kingdom pFormerKingdom)
        {
            if (pActor?.data == null || pFormerKingdom?.data == null) return;
            pActor.data.set(LineageKeys.CAPTURED_RULER_KINGDOM_ID, pFormerKingdom.id);
            pActor.data.set(LineageKeys.CAPTURED_RULER_KINGDOM_NAME, pFormerKingdom.name ?? "");
            pActor.data.set(LineageKeys.CAPTURED_RULER_KINGDOM_COLOR, HistoryColors.FromKingdom(pFormerKingdom));
            pActor.data.set(LineageKeys.CAPTURED_RULER_TITLE, (int)KingdomTitleService.GetTitle(pFormerKingdom));
        }

        private static void CloseCapturedRulerOpenReign(Actor pActor, Kingdom pFormerKingdom, string pEndReason)
        {
            if (pActor?.data == null || pFormerKingdom?.data == null) return;
            ReignRecordWriter.ReignInfo open = ReignRecordWriter.ReadOpenReignInfo(pFormerKingdom.id);
            if (!open.IsValid || open.KingActorId != pActor.data.id) return;
            ReignRecordWriter.CloseOpenReign(pFormerKingdom,
                string.IsNullOrEmpty(pEndReason) ? "captured_slave" : pEndReason,
                pActor);
        }

        private static void ExecuteImportantCaptive(Actor pActor, Actor pCaptor, string pDominantSchool)
        {
            if (pActor?.data == null || !pActor.isAlive()) return;
            string school = CaptiveTreatmentRules.SchoolLabel(pDominantSchool);
            string cause = HistoryLocalizationRules.Text("aw_death_cause_captive_execution");
            if (!string.IsNullOrEmpty(school))
                cause += HistoryLocalizationRules.Text("aw_death_cause_captive_execution_school") + school;
            if (pCaptor?.data != null)
                cause += HistoryLocalizationRules.Text("aw_death_cause_captive_execution_by") + pCaptor.getName();
            pActor.data.set(LineageKeys.DEATH_CAUSE, cause);
            pActor.cancelAllBeh();
            pActor.clearAttackTarget();
            pActor.beh_actor_target = null;
            pActor.attackedBy = null;
            pActor.dieSimpleNone();
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

        private static float RandomWait(float pMin, float pMax)
        {
            if (pMax < pMin) return pMax;
            return (float)(pMin + Rng.NextDouble() * (pMax - pMin));
        }

        internal static string BuildSlaveArmyName(Kingdom pKingdom, City pCity, int pIndex)
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

        internal static bool IsSupportedSlaveryActor(Actor pActor)
        {
            return LineageService.IsXia(pActor) || LineageService.IsHuman(pActor);
        }

        internal static void RecordSlaveArmyFormation(Kingdom pKingdom, City pCity)
        {
            if (pKingdom?.data == null) return;
            ChronicleEvents.OnSlaveArmyFormed(pKingdom, pCity);
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
                if (!HistoricalMasterVocationService.CanJoinArmy(unit, pArmy) ||
                    !HistoricalMasterVocationService.CanEnter(unit,
                        HistoricalMasterMilitaryContext.ArmyCaptain)) continue;
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

        internal static void QueueSlaveStatePersistence(Actor pActor, bool pActive,
            City pCity, Kingdom pKingdom)
        {
            if (pActor?.data == null) return;
            SlaveStateSnapshot state = CaptureSlaveState(pActor, pActive,
                pCity ?? pActor.city, pKingdom ?? pActor.kingdom ?? pCity?.kingdom);
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("slave_state", state.actorId),
                DeferredWorkClass.Persistent, () => UpsertSlaveState(state));
        }

        private static SlaveStateSnapshot CaptureSlaveState(Actor pActor, bool pActive,
            City pContextCity, Kingdom pContextKingdom)
        {
            Kingdom kingdom = pContextKingdom ?? pActor?.kingdom ?? pContextCity?.kingdom;
            City city = pContextCity ?? pActor?.city;
            pActor.data.get(LineageKeys.SLAVE_SINCE, out long sinceRaw, (long)LineageService.CurTime());
            pActor.data.get(LineageKeys.SLAVE_REASON, out string reason, "");
            pActor.data.get(LineageKeys.SLAVE_CAPTURED_BY, out long capturedBy, -1L);
            pActor.data.get(LineageKeys.SLAVE_MERIT, out int merit, 0);
            pActor.data.get(LineageKeys.SLAVE_SOLDIER, out bool soldier, false);
            pActor.data.get(LineageKeys.SOLDIER_SERVICE_START_TIME, out float soldierStart, -1f);
            pActor.data.get(LineageKeys.FREEDMAN, out bool freedman, false);
            return new SlaveStateSnapshot
            {
                actorId = pActor.data.id,
                actorName = pActor.getName() ?? "",
                kingdomId = kingdom?.id ?? -1L,
                kingdomName = kingdom?.name ?? "",
                cityId = city?.id ?? -1L,
                cityName = city?.data?.name ?? "",
                enslavedTime = sinceRaw,
                freedTime = pActive ? -1.0 : LineageService.CurTime(),
                reason = reason ?? "",
                capturedByActorId = capturedBy,
                merit = merit,
                active = pActive,
                soldier = soldier,
                soldierStartTime = soldierStart,
                freedman = freedman
            };
        }

        private static void UpsertSlaveState(SlaveStateSnapshot pSnapshot)
        {
            var db = LineageArchiveManager.Instance.OperatingDB;
            if (db == null) throw new InvalidOperationException("Slave archive is unavailable.");
            if (pSnapshot == null || pSnapshot.actorId < 0) return;

            string table = SlaveStateTableItem.GetTableName();
            var values = new[]
            {
                ColumnVal.Create("ACTOR_NAME", pSnapshot.actorName),
                ColumnVal.Create("KINGDOM_ID", pSnapshot.kingdomId),
                ColumnVal.Create("KINGDOM_NAME", pSnapshot.kingdomName),
                ColumnVal.Create("CITY_ID", pSnapshot.cityId),
                ColumnVal.Create("CITY_NAME", pSnapshot.cityName),
                ColumnVal.Create("ENSLAVED_TIME", pSnapshot.enslavedTime),
                ColumnVal.Create("FREED_TIME", pSnapshot.freedTime),
                ColumnVal.Create("REASON", pSnapshot.reason),
                ColumnVal.Create("CAPTURED_BY_ACTOR_ID", pSnapshot.capturedByActorId),
                ColumnVal.Create("MERIT", pSnapshot.merit),
                ColumnVal.Create("ACTIVE", pSnapshot.active ? 1 : 0),
                ColumnVal.Create("SOLDIER", pSnapshot.soldier ? 1 : 0),
                ColumnVal.Create("SOLDIER_START_TIME", pSnapshot.soldierStartTime),
                ColumnVal.Create("FREEDMAN", pSnapshot.freedman ? 1 : 0)
            };
            if (db.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("ACTOR_ID", pSnapshot.actorId)))
            {
                db.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("ACTOR_ID", pSnapshot.actorId) },
                    values);
                return;
            }
            var insertValues = new List<ColumnVal> { ColumnVal.Create("ACTOR_ID", pSnapshot.actorId) };
            insertValues.AddRange(values);
            db.Insert(table, insertValues.ToArray());
        }

        public static string ReasonLabel(string pReason)
        {
            return ReasonLabel(pReason, HistoryLocalizationRules.CurrentLanguage());
        }

        public static string ReasonLabel(string pReason, string pLanguage)
        {
            return pReason switch
            {
                "city_fall" => HistoryLocalizationRules.Text("aw_hist_slave_reason_city_fall", pLanguage),
                "captured" => HistoryLocalizationRules.Text("aw_hist_slave_reason_captured", pLanguage),
                "battlefield_capture" => HistoryLocalizationRules.Text("aw_hist_slave_reason_battlefield_capture", pLanguage),
                "foreign_occupation" => HistoryLocalizationRules.Text("aw_hist_slave_reason_foreign_occupation", pLanguage),
                "slave_king" => HistoryLocalizationRules.Text("aw_hist_slave_reason_slave_king", pLanguage),
                "slave_only_rebel" => HistoryLocalizationRules.Text("aw_hist_slave_reason_slave_only_rebel", pLanguage),
                "born_slave" => HistoryLocalizationRules.Text("aw_hist_slave_reason_born_slave", pLanguage),
                "military_merit" => HistoryLocalizationRules.Text("aw_hist_slave_reason_military_merit", pLanguage),
                "promoted" => HistoryLocalizationRules.Text("aw_hist_slave_reason_promoted", pLanguage),
                "noble_dependent" => HistoryLocalizationRules.Text("aw_hist_slave_reason_noble_dependent", pLanguage),
                _ => string.IsNullOrEmpty(pReason) ? HistoryLocalizationRules.Text("aw_hist_slave_reason_registered", pLanguage) : pReason
            };
        }
    }
}
