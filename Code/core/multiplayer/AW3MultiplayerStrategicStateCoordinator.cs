using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
#if !AW3_RULES_TESTS
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.naming;
#endif

namespace AncientWarfare3.core.multiplayer
{
    internal interface IAW3MultiplayerStrategicStateStore
    {
        bool IsMainThread { get; }
        IReadOnlyList<AW3MultiplayerArmyProjection> CaptureArmies();
        IReadOnlyList<AW3MultiplayerActorProjection> CaptureActors();
        IReadOnlyList<AW3MultiplayerMilitaryGovernorateProjection>
            CaptureMilitaryGovernorates();
        IReadOnlyList<AW3MultiplayerBanditStrongholdProjection>
            CaptureBanditStrongholds();
        bool HasArmy(long pArmyId);
        bool HasActor(long pActorId);
        bool CanApplyMilitaryGovernorate(
            AW3MultiplayerMilitaryGovernorateProjection pGovernorate);
        bool CanApplyBanditStronghold(
            AW3MultiplayerBanditStrongholdProjection pStronghold);
        void ApplyArmy(AW3MultiplayerArmyProjection pArmy);
        void ApplyActor(AW3MultiplayerActorProjection pActor);
        void ApplyMilitaryGovernorate(
            AW3MultiplayerMilitaryGovernorateProjection pGovernorate);
        void ApplyBanditStronghold(
            AW3MultiplayerBanditStrongholdProjection pStronghold);
        void CompleteArmySnapshot(
            IReadOnlyList<AW3MultiplayerArmyProjection> pArmies);
        void CompleteMilitaryGovernorateSnapshot(
            IReadOnlyList<AW3MultiplayerMilitaryGovernorateProjection>
                pGovernorates);
        void CompleteBanditStrongholdSnapshot(
            IReadOnlyList<AW3MultiplayerBanditStrongholdProjection>
                pStrongholds);
        void RebuildMilitaryReadModels();
    }

    internal static class AW3MultiplayerStrategicStateCoordinator
    {
        internal static AW3MultiplayerStrategicSnapshot Capture(long pTick,
            IAW3MultiplayerStrategicStateStore pStore)
        {
            if (pTick < 0)
                throw new ArgumentOutOfRangeException(nameof(pTick));
            if (pStore == null) throw new ArgumentNullException(nameof(pStore));
            if (!pStore.IsMainThread)
                throw new AW3MultiplayerStrategicCaptureException(
                    AW3MultiplayerStrategicError.WrongThread,
                    "Strategic capture requires the WorldBox main thread.");
            try
            {
                return new AW3MultiplayerStrategicSnapshot(pTick,
                    pStore.CaptureArmies(), pStore.CaptureActors(),
                    pStore.CaptureMilitaryGovernorates(),
                    pStore.CaptureBanditStrongholds());
            }
            catch (AW3MultiplayerStrategicCaptureException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new AW3MultiplayerStrategicCaptureException(
                    AW3MultiplayerStrategicError.CaptureFailed,
                    error.Message, error);
            }
        }

        internal static AW3MultiplayerStrategicApplyResult Apply(
            AW3MultiplayerStrategicSnapshot pSnapshot,
            IAW3MultiplayerStrategicStateStore pStore)
        {
            if (pStore == null)
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.InvalidSnapshot,
                    "Strategic state store is required.");
            if (!pStore.IsMainThread)
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.WrongThread,
                    "Strategic apply requires the WorldBox main thread.");
            if (pSnapshot == null)
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.InvalidSnapshot,
                    "Strategic snapshot is required.");

            for (var index = 0; index < pSnapshot.Armies.Count; index++)
            {
                long id = pSnapshot.Armies[index].ArmyId;
                if (!pStore.HasArmy(id))
                    return AW3MultiplayerStrategicApplyResult.Failure(
                        AW3MultiplayerStrategicError.MissingArmy,
                        "Snapshot army identity does not exist.", id);
            }
            for (var index = 0; index < pSnapshot.Actors.Count; index++)
            {
                long id = pSnapshot.Actors[index].ActorId;
                if (!pStore.HasActor(id))
                    return AW3MultiplayerStrategicApplyResult.Failure(
                        AW3MultiplayerStrategicError.MissingActor,
                        "Snapshot actor identity does not exist.", id);
            }
            for (var index = 0;
                 index < pSnapshot.MilitaryGovernorates.Count; index++)
            {
                AW3MultiplayerMilitaryGovernorateProjection governorate =
                    pSnapshot.MilitaryGovernorates[index];
                if (!pStore.CanApplyMilitaryGovernorate(governorate))
                    return AW3MultiplayerStrategicApplyResult.Failure(
                        AW3MultiplayerStrategicError.InvalidSnapshot,
                        "Snapshot governorate identities do not exist.",
                        governorate.StateId);
            }
            for (var index = 0;
                 index < pSnapshot.BanditStrongholds.Count; index++)
            {
                AW3MultiplayerBanditStrongholdProjection stronghold =
                    pSnapshot.BanditStrongholds[index];
                if (!pStore.CanApplyBanditStronghold(stronghold))
                    return AW3MultiplayerStrategicApplyResult.Failure(
                        AW3MultiplayerStrategicError.InvalidSnapshot,
                        "Snapshot bandit kingdom identity does not exist.",
                        stronghold.KingdomId);
            }

            try
            {
                using (AW3MultiplayerReplicaScope.EnterApply())
                {
                    for (var index = 0;
                         index < pSnapshot.Armies.Count; index++)
                        pStore.ApplyArmy(pSnapshot.Armies[index]);
                    for (var index = 0;
                         index < pSnapshot.Actors.Count; index++)
                        pStore.ApplyActor(pSnapshot.Actors[index]);
                    for (var index = 0;
                         index < pSnapshot.MilitaryGovernorates.Count;
                         index++)
                        pStore.ApplyMilitaryGovernorate(
                            pSnapshot.MilitaryGovernorates[index]);
                    for (var index = 0;
                         index < pSnapshot.BanditStrongholds.Count;
                         index++)
                        pStore.ApplyBanditStronghold(
                            pSnapshot.BanditStrongholds[index]);
                    pStore.CompleteArmySnapshot(pSnapshot.Armies);
                    pStore.CompleteMilitaryGovernorateSnapshot(
                        pSnapshot.MilitaryGovernorates);
                    pStore.CompleteBanditStrongholdSnapshot(
                        pSnapshot.BanditStrongholds);
                    pStore.RebuildMilitaryReadModels();
                }
                return AW3MultiplayerStrategicApplyResult.Success(
                    pSnapshot.Armies.Count, pSnapshot.Actors.Count);
            }
            catch (Exception error)
            {
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.ApplyFailed,
                    error.Message);
            }
        }
    }

#if !AW3_RULES_TESTS
    internal sealed class AW3MultiplayerStrategicWorldStore :
        IAW3MultiplayerStrategicStateStore
    {
        public bool IsMainThread => ThreadHelper.isMainThread();

        public IReadOnlyList<AW3MultiplayerArmyProjection> CaptureArmies()
        {
            var result = new List<AW3MultiplayerArmyProjection>();
            if (World.world?.armies == null) return result;
            var reserveAvailableByKingdom = new Dictionary<long, int>();
            foreach (Army army in World.world.armies)
            {
                if (army?.data == null || !army.isAlive()) continue;
                Actor captain = null;
                try { captain = army.getCaptain(); }
                catch { }
                string orderId = captain?.ai?.task?.id ?? string.Empty;
                WorldTile target = captain?.tile_target;
                AW3MultiplayerStrategicTargetKind targetKind =
                    target?.data == null
                        ? AW3MultiplayerStrategicTargetKind.None
                        : AW3MultiplayerStrategicTargetKind.Tile;
                ArmyRtsState operationalState = ArmyRtsState.Idle;
                ArmyRtsRole rtsRole = ArmyRtsRole.Reserve;
                ArmyRtsPosture posture = ArmyRtsPosture.Automatic;
                long warId = -1L;
                long frontId = -1L;
                long targetId = -1L;
                int supply = 100;
                int organization = 100;
                int replenishmentShortage = 0;
                int kingdomReserveAvailable = 0;
                bool playerOrder = false;
                if (ArmyRtsControllerService.TryGetProjection(army,
                        out ArmyRtsStrategicProjection projection))
                {
                    operationalState = projection.State;
                    rtsRole = projection.Role;
                    posture = projection.Posture;
                    warId = projection.WarId;
                    frontId = projection.FrontId;
                    targetId = projection.TargetCityId;
                    supply = projection.Supply;
                    organization = projection.Organization;
                    playerOrder = projection.PlayerOrder;
                    if (targetId >= 0L)
                        targetKind =
                            AW3MultiplayerStrategicTargetKind.City;
                }
                int living = 0;
                try { living = Math.Max(0, army.countUnits()); }
                catch { }
                if (ArmyRtsControllerService.TryGetMission(army,
                        out ArmyRtsMission mission) && mission != null)
                    replenishmentShortage = Math.Max(0,
                        Math.Max(0, mission.TargetStrength) - living);
                Kingdom kingdom = null;
                try { kingdom = army.getKingdom(); }
                catch { }
                if (kingdom?.data != null &&
                    !reserveAvailableByKingdom.TryGetValue(kingdom.id,
                        out kingdomReserveAvailable))
                {
                    kingdomReserveAvailable =
                        CityReservePoolService.CountAvailable(kingdom);
                    reserveAvailableByKingdom[kingdom.id] =
                        kingdomReserveAvailable;
                }
                result.Add(new AW3MultiplayerArmyProjection(army.id,
                    AWArmyService.GetRole(army),
                    AWArmyService.GetAnchorCityId(army), orderId,
                    targetKind, targetId, target?.x ?? -1,
                    target?.y ?? -1, replenishmentShortage,
                    kingdomReserveAvailable,
                    operationalState.ToString().ToLowerInvariant(),
                    posture.ToString().ToLowerInvariant(), warId, frontId,
                    supply, organization, playerOrder,
                    rtsRole.ToString().ToLowerInvariant()));
            }
            return result;
        }

        public IReadOnlyList<AW3MultiplayerActorProjection> CaptureActors()
        {
            var result = new List<AW3MultiplayerActorProjection>();
            List<Actor> units = World.world?.units?.units_only_alive;
            if (units == null) return result;
            for (var index = 0; index < units.Count; index++)
            {
                Actor actor = units[index];
                if (actor?.data == null || !actor.isAlive()) continue;
                bool isGeneral = GeneralService.IsActiveGeneralFast(actor);
                if (actor.army?.data == null && !isGeneral) continue;
                actor.data.get(LineageKeys.GENERAL_MERIT,
                    out int merit, 0);
                AWLocalizedNameIdentitySnapshot identity =
                    AWLocalizedNamePersistence.Capture(actor.data);
                result.Add(new AW3MultiplayerActorProjection(actor.data.id,
                    isGeneral, Math.Max(0, merit), identity.NativeName,
                    identity.ChineseName, identity.GivenName,
                    identity.FamilyComponent, identity.GeneratorId,
                    identity.CultureId, Math.Max(0, identity.SchemaVersion)));
            }
            return result;
        }

        public IReadOnlyList<AW3MultiplayerMilitaryGovernorateProjection>
            CaptureMilitaryGovernorates()
        {
            List<MilitaryGovernorateSnapshot> states =
                MilitaryGovernorateStore.CaptureAuthoritativeState();
            var result =
                new List<AW3MultiplayerMilitaryGovernorateProjection>(
                    states.Count);
            for (var index = 0; index < states.Count; index++)
            {
                MilitaryGovernorateSnapshot state = states[index];
                result.Add(
                    new AW3MultiplayerMilitaryGovernorateProjection(
                        state.StateId, state.RelationId,
                        state.SubjectKingdomId, state.SuzerainKingdomId,
                        state.SeatCityId, state.GovernorActorId,
                        state.SuccessorActorId, state.CommandName,
                        state.SuccessionState, state.ReplacementAllowed,
                        active: true));
            }
            return result;
        }

        public IReadOnlyList<AW3MultiplayerBanditStrongholdProjection>
            CaptureBanditStrongholds()
        {
            var result =
                new List<AW3MultiplayerBanditStrongholdProjection>();
            if (World.world?.kingdoms == null) return result;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                kingdom.data.get(
                    LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE,
                    out string stateJson, "");
                if (string.IsNullOrWhiteSpace(stateJson)) continue;
                result.Add(new AW3MultiplayerBanditStrongholdProjection(
                    kingdom.getID(), stateJson));
            }
            return result;
        }

        public bool HasArmy(long pArmyId)
        {
            if (pArmyId < 0 || World.world?.armies == null) return false;
            try { return World.world.armies.get(pArmyId)?.data != null; }
            catch { return false; }
        }

        public bool HasActor(long pActorId)
        {
            if (pActorId < 0 || World.world?.units == null) return false;
            try { return World.world.units.get(pActorId)?.data != null; }
            catch { return false; }
        }

        public bool CanApplyMilitaryGovernorate(
            AW3MultiplayerMilitaryGovernorateProjection pGovernorate)
        {
            if (pGovernorate == null ||
                pGovernorate.SubjectKingdomId < 0) return false;
            Kingdom subject;
            try
            {
                subject = World.world?.kingdoms?.get(
                    pGovernorate.SubjectKingdomId);
            }
            catch { return false; }
            if (subject?.data == null) return false;
            if (!pGovernorate.Active) return true;
            try
            {
                if (World.world?.kingdoms?.get(
                        pGovernorate.SuzerainKingdomId)?.data == null ||
                    World.world?.cities?.get(
                        pGovernorate.SeatCityId)?.data == null)
                    return false;
            }
            catch { return false; }
            return HasActor(pGovernorate.GovernorActorId) &&
                   (pGovernorate.SuccessorActorId < 0 ||
                    HasActor(pGovernorate.SuccessorActorId));
        }

        public bool CanApplyBanditStronghold(
            AW3MultiplayerBanditStrongholdProjection pStronghold)
        {
            if (pStronghold == null || World.world?.kingdoms == null)
                return false;
            try
            {
                return World.world.kingdoms.get(
                           pStronghold.KingdomId)?.data != null;
            }
            catch { return false; }
        }

        public void ApplyArmy(AW3MultiplayerArmyProjection pArmy)
        {
            Army army = World.world.armies.get(pArmy.ArmyId);
            if (army?.data == null)
                throw new InvalidOperationException(
                    "Snapshot army identity no longer exists.");
            army.data.set(LineageKeys.AW_ARMY_ROLE, pArmy.RoleId);
            army.data.set(LineageKeys.AW_ARMY_CITY_ID,
                pArmy.AnchorCityId);
            army.data.set(
                LineageKeys.AW_ARMY_PROJECTED_REPLENISHMENT_SHORTAGE,
                pArmy.ReplenishmentShortage);
            army.data.set(
                LineageKeys.AW_ARMY_PROJECTED_KINGDOM_RESERVE_AVAILABLE,
                pArmy.KingdomReserveAvailable);
            ArmyRtsControllerService.InstallReplicaProjection(army,
                pArmy.OperationalStateId, pArmy.RtsRoleId,
                pArmy.PostureId, pArmy.WarId, pArmy.FrontId,
                pArmy.TargetKind ==
                    AW3MultiplayerStrategicTargetKind.City
                    ? pArmy.TargetId
                    : -1L,
                pArmy.Supply, pArmy.Organization, pArmy.PlayerOrder);
        }

        public void ApplyActor(AW3MultiplayerActorProjection pActor)
        {
            Actor actor = World.world.units.get(pActor.ActorId);
            if (actor?.data == null)
                throw new InvalidOperationException(
                    "Snapshot actor identity no longer exists.");
            actor.data.set(LineageKeys.GENERAL_ACTIVE, pActor.IsGeneral);
            actor.data.set(LineageKeys.GENERAL_MERIT,
                pActor.GeneralMerit);
            AWLocalizedNamePersistence.Apply(actor.data,
                new AWLocalizedNameIdentitySnapshot(pActor.NativeName,
                    pActor.ChineseName, pActor.GivenName,
                    pActor.FamilyComponent, pActor.GeneratorId,
                pActor.CultureId, pActor.NamingSchemaVersion));
        }

        public void ApplyMilitaryGovernorate(
            AW3MultiplayerMilitaryGovernorateProjection pGovernorate)
        {
            Kingdom subject = null;
            try
            {
                subject = World.world?.kingdoms?.get(
                    pGovernorate.SubjectKingdomId);
            }
            catch { }
            if (subject?.data == null)
                throw new InvalidOperationException(
                    "Snapshot governorate subject identity does not exist.");
            MilitaryGovernorateStore.ApplyAuthoritativeProjection(subject,
                pGovernorate.StateId, pGovernorate.RelationId,
                pGovernorate.SuzerainKingdomId,
                pGovernorate.SeatCityId, pGovernorate.GovernorActorId,
                pGovernorate.SuccessorActorId, pGovernorate.CommandName,
                pGovernorate.SuccessionState,
                pGovernorate.ReplacementAllowed, pGovernorate.Active);
        }

        public void ApplyBanditStronghold(
            AW3MultiplayerBanditStrongholdProjection pStronghold)
        {
            Kingdom kingdom = World.world.kingdoms.get(
                pStronghold.KingdomId);
            if (kingdom?.data == null)
                throw new InvalidOperationException(
                    "Snapshot bandit kingdom identity no longer exists.");
            kingdom.data.set(
                LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE,
                pStronghold.StateJson);
        }

        public void CompleteArmySnapshot(
            IReadOnlyList<AW3MultiplayerArmyProjection> pArmies)
        {
            var retained = new long[pArmies.Count];
            for (var index = 0; index < pArmies.Count; index++)
                retained[index] = pArmies[index].ArmyId;
            ArmyRtsControllerService.RetainReplicaProjections(retained);
        }

        public void CompleteMilitaryGovernorateSnapshot(
            IReadOnlyList<AW3MultiplayerMilitaryGovernorateProjection>
                pGovernorates)
        {
            var retained = new long[pGovernorates.Count];
            for (var index = 0; index < pGovernorates.Count; index++)
                retained[index] = pGovernorates[index].SubjectKingdomId;
            MilitaryGovernorateStore.RetainAuthoritativeProjections(
                retained);
        }

        public void CompleteBanditStrongholdSnapshot(
            IReadOnlyList<AW3MultiplayerBanditStrongholdProjection>
                pStrongholds)
        {
            var retained = new HashSet<long>();
            for (var index = 0; index < pStrongholds.Count; index++)
                retained.Add(pStrongholds[index].KingdomId);
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() ||
                    retained.Contains(kingdom.getID())) continue;
                kingdom.data.get(
                    LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE,
                    out string stateJson, "");
                if (!string.IsNullOrWhiteSpace(stateJson))
                    kingdom.data.set(
                        LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE,
                        "");
            }
        }

        public void RebuildMilitaryReadModels()
        {
            AWArmyService.RepairSpecialArmiesAfterLoad();
            KingdomMilitaryReadinessService.RebuildRuntime();
            MilitaryEmergencyService.RebuildRuntime();
        }
    }
#endif
}
