using AncientWarfare3.content;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     入伍编年史:Postfix City.makeWarrior(Actor)(makeWarrior 在 City 自身声明,typeof 正确)。
    ///     贵族被征为战士 → 记一条"入伍从军"(war 分类)。仅贵族(ChronicleEvents 内部门槛)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_EnlistPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        public static bool MakeWarrior_Asylum_Prefix(Actor pActor)
        {
            return pActor?.data != null &&
                   !IsCivilAuthority(pActor) &&
                   SoldierRetirementRules.IsOrdinaryServiceAgeAllowed(
                       pActor.getAge()) &&
                   !FeudatoryService.IsActivePrince(pActor) &&
                   !DynasticReproductionService
                       .ShouldProtectFromOrdinaryMilitaryService(pActor) &&
                   RoyalAsylumRules.CanPerformProtectedRole(
                       RoyalAsylumService.IsActive(pActor));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "setProfession",
            new[] { typeof(UnitProfession), typeof(bool) })]
        public static bool SetProfession_Asylum_Prefix(Actor __instance,
            UnitProfession pType, out MilitaryProfessionState __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying)
            {
                __state = default(MilitaryProfessionState);
                return true;
            }
            if (ActorProfessionLoadSafetyRules.
                ShouldBypassTransitionRestrictions(
                    __instance?.profession_asset != null))
            {
                __state = default(MilitaryProfessionState);
                return true;
            }
            __state = MilitaryProfessionState.Capture(__instance);
            bool actorIsDead = __instance?.data == null || __instance.isRekt() ||
                               !__instance.isAlive();
            if (pType != UnitProfession.Warrior &&
                !RoyalGuardOfficeRules.CanLeaveMilitaryService(
                    RoyalGuardService.IsRoyalGuard(__instance), actorIsDead))
                return false;
            bool becomingAuthority = pType == UnitProfession.King ||
                                     pType == UnitProfession.Leader;
            if (pType != UnitProfession.Warrior &&
                ArmyCaptainContinuityRules.
                    ShouldRejectCaptainRetirement(
                        ArmyRtsRuntimeMode.Current,
                        replicaApplying: false,
                        ArmyIsLive(__instance?.army),
                        IsCurrentCaptain(__instance),
                        ActorIsAlive(__instance),
                        becomingAuthority))
                return false;
            return pType != UnitProfession.Warrior ||
                   __instance?.data != null &&
                   !IsCivilAuthority(__instance) &&
                   SoldierRetirementRules.IsOrdinaryServiceAgeAllowed(
                       __instance.getAge()) &&
                   !FeudatoryService.IsActivePrince(__instance) &&
                   RoyalAsylumRules.CanPerformProtectedRole(
                       RoyalAsylumService.IsActive(__instance));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "setProfession",
            new[] { typeof(UnitProfession), typeof(bool) })]
        public static void SetProfession_MilitaryState_Postfix(
            Actor __instance, UnitProfession pType,
            MilitaryProfessionState __state, bool __runOriginal)
        {
            if (!__runOriginal) return;
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__instance?.data == null) return;
            CityReservePoolService.OnActorProfessionChanged(__instance);
            bool warrior = __instance.isWarrior();
            bool becomingKing = pType == UnitProfession.King;
            bool becomingLeader = pType == UnitProfession.Leader;
            bool authorityRole = becomingKing || becomingLeader;
            if (authorityRole)
            {
                ReleaseMilitaryOwnership(__instance,
                    OccupiedCityCivilianProtectionRules.
                        ShouldDetachArmyForAuthorityRole(
                            __instance.hasArmy(), becomingKing,
                            becomingLeader),
                    "became_civil_authority");
            }
            if (!__state.WasWarrior && warrior)
            {
                CityReservePoolService.OnActorEnlisted(__instance);
                if (!__state.TrackPermanentHistory) return;
                __instance.data.get(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                    out bool active, false);
                if (active) return;
                __instance.data.set(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                    true);
                __instance.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME,
                    (float)LineageService.CurTime());
                ChronicleEvents.OnEnlisted(__instance);
                return;
            }
            if (!__state.WasWarrior || warrior) return;

            if (!authorityRole)
                ReleaseMilitaryOwnership(__instance,
                    detachArmy: __instance.hasArmy(),
                    "left_military_service");

            __instance.data.get(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                out bool biographyActive, false);
            if (!biographyActive) return;
            __instance.data.set(LineageKeys.MILITARY_BIOGRAPHY_ACTIVE,
                false);
            ChronicleEvents.OnRetiredSoldier(__instance,
                __state.Kingdom, __state.City);
        }

        private static void ReleaseMilitaryOwnership(Actor pActor,
            bool detachArmy, string pReason)
        {
            if (pActor?.data == null) return;
            if (!RoyalGuardOfficeRules.CanLeaveMilitaryService(
                    RoyalGuardService.IsRoyalGuard(pActor),
                    pActor.isRekt() || !pActor.isAlive())) return;
            Army army = pActor.army;
            if (ArmyCaptainContinuityRules.
                    ShouldRejectCaptainDetachment(
                        ArmyRtsRuntimeMode.Current,
                        AW3MultiplayerReplicaScope.IsApplying,
                        ArmyIsLive(army),
                        IsCurrentCaptain(pActor),
                        ActorIsAlive(pActor),
                        leavingCurrentArmy: detachArmy,
                        actorIsCivilAuthority: IsCivilAuthority(pActor)))
                return;
            if (detachArmy)
            {
                try { pActor.removeFromArmy(); }
                catch { pActor.setArmy(null); }
            }
            ArmyRtsControllerService.ReleaseActor(pActor);
            ArmyDeploymentService.ReleaseActor(pActor, restoreJob: true);
            TemporaryLevyService.OnActorInvalidated(pActor);
            WartimeGarrisonService.OnActorInvalidated(pActor);
            TemporarySlaveVanguardService.OnMemberInvalidated(pActor);
            MandateMilitaryPhaseService.Clear(pActor);
        }

        private static bool IsCivilAuthority(Actor pActor)
        {
            try
            {
                return pActor?.data != null &&
                       (pActor.isKing() || pActor.isCityLeader());
            }
            catch { return false; }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        public static void MakeWarrior_Postfix(City __instance, Actor pActor)
        {
            if (pActor?.data == null || !pActor.isWarrior()) return;
            if (!AW3MultiplayerReplicaScope.IsApplying)
                CityReservePoolService.OnActorEnlisted(pActor);
            KingdomMilitaryReadinessService.ObserveCity(__instance);
            WarNoticeService.QueueArmyChanged(__instance?.kingdom ?? pActor.kingdom,
                pActor.army, pRosterExpanded: true);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.stopBeingWarrior))]
        public static bool StopBeingWarrior_Prefix(Actor __instance)
        {
            if (__instance?.data == null || !__instance.isWarrior()) return true;
            if (!RoyalGuardOfficeRules.CanLeaveMilitaryService(
                    RoyalGuardService.IsRoyalGuard(__instance),
                    __instance.isRekt() || !__instance.isAlive())) return false;
            if (ActiveMilitaryLifecycleService.
                    HasWartimeMilitaryLock(__instance)) return false;
            if (__instance.army?.data == null) return true;
            if (ArmyCaptainContinuityRules.
                    ShouldRejectCaptainRetirement(
                        ArmyRtsRuntimeMode.Current,
                        AW3MultiplayerReplicaScope.IsApplying,
                        ArmyIsLive(__instance.army),
                        IsCurrentCaptain(__instance),
                        ActorIsAlive(__instance),
                        becomingAuthority: false))
                return false;
            WarNoticeService.QueueArmyChanged(__instance.kingdom, __instance.army);
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.stopBeingWarrior))]
        public static void StopBeingWarrior_Postfix(Actor __instance,
            bool __runOriginal)
        {
            if (!__runOriginal) return;
            ArmyDeploymentService.ReleaseActor(__instance, restoreJob: true);
            TemporaryLevyService.OnActorInvalidated(__instance);
            WartimeGarrisonService.OnActorInvalidated(__instance);
            KingdomMilitaryReadinessService.ObserveCity(__instance?.city);
        }

        private static bool IsCurrentCaptain(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.army?.data != null &&
                       ReferenceEquals(pActor.army.getCaptain(), pActor);
            }
            catch { return false; }
        }

        private static bool ActorIsAlive(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool ArmyIsLive(Army pArmy)
        {
            try { return pArmy?.data != null && pArmy.isAlive(); }
            catch { return false; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.getNextJob))]
        public static bool GetNextJob_Asylum_Prefix(Actor __instance, ref string __result)
        {
            string garrisonJob = WartimeGarrisonService.GetJob(__instance);
            if (!string.IsNullOrEmpty(garrisonJob))
            {
                __result = garrisonJob;
                return false;
            }
            string standingJob =
                StandingArmyPeacetimeService.GetJob(__instance);
            if (!string.IsNullOrEmpty(standingJob))
            {
                __result = standingJob;
                return false;
            }
            if (RoyalAsylumService.IsActive(__instance))
            {
                __result = RoyalAsylumContent.ActorJobId;
                return false;
            }
            if (!FeudatoryService.IsActivePrince(__instance) ||
                __instance.isWarrior()) return true;
            __result = FeudatoryContent.ActorJobId;
            return false;
        }

        public readonly struct MilitaryProfessionState
        {
            private MilitaryProfessionState(bool pWasWarrior,
                Kingdom pKingdom, City pCity, bool pTrackPermanentHistory)
            {
                WasWarrior = pWasWarrior;
                Kingdom = pKingdom;
                City = pCity;
                TrackPermanentHistory = pTrackPermanentHistory;
            }

            public bool WasWarrior { get; }
            public Kingdom Kingdom { get; }
            public City City { get; }
            public bool TrackPermanentHistory { get; }

            public static MilitaryProfessionState Capture(Actor pActor)
            {
                bool warrior = false;
                try { warrior = pActor?.isWarrior() == true; }
                catch { }
                return new MilitaryProfessionState(warrior,
                    pActor?.kingdom, pActor?.city,
                    MilitaryRecruitmentScope.
                        ShouldTrackPermanentEnlistmentHistory(
                            ChronicleGate.IsNobleActor(pActor)));
            }
        }
    }
}
