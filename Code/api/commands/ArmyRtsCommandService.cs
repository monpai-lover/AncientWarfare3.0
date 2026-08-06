using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.api.commands
{
    internal static class ArmyRtsCommandService
    {
        private const int MaximumWarsInspected = 16;

        internal static AW3CommandResult Dispatch(AW3CommandRequest pRequest)
        {
            if (pRequest == null || !IsArmyCommand(pRequest.Kind))
                return Reject(AW3CommandError.InvalidRequest,
                    "aw3_army_command_invalid");
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
                return Reject(AW3CommandError.Unauthorized,
                    "aw3_command_replica_read_only");
            if (!ThreadHelper.isMainThread())
                return Reject(AW3CommandError.Unauthorized,
                    "aw3_army_command_main_thread_required");
            if (!ArmyRtsRuntimeMode.ShouldCommit)
                return Reject(AW3CommandError.Conflict,
                    "aw3_army_command_live_mode_required");

            Kingdom kingdom = FindKingdom(pRequest.CountryId);
            Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                pRequest.SecondaryId, pRequest.CountryId);
            if (!IsLiveKingdom(kingdom) || !IsOwnedArmy(army, kingdom))
                return Reject(AW3CommandError.NotFound,
                    "aw3_army_command_army_missing");

            AW3CommandResult result;
            switch (pRequest.Kind)
            {
                case AW3CommandKind.SetArmyRallyPoint:
                    result = SetCityMission(kingdom, army,
                        pRequest.CityId, rally: true);
                    break;
                case AW3CommandKind.SetArmyTargetCity:
                    result = SetCityMission(kingdom, army,
                        pRequest.CityId, rally: false);
                    break;
                case AW3CommandKind.SetArmyPosture:
                    result = SetPosture(kingdom, army, pRequest.Key);
                    break;
                case AW3CommandKind.CancelArmyOrder:
                    result = CancelOrder(kingdom, army);
                    break;
                default:
                    result = Reject(AW3CommandError.InvalidRequest,
                        "aw3_army_command_invalid");
                    break;
            }
            if (result.Accepted)
                KingdomWarDirectorService.OnArmyChanged(kingdom);
            return result;
        }

        private static AW3CommandResult SetCityMission(Kingdom pKingdom,
            Army pArmy, long pCityId, bool rally)
        {
            City city = FindCity(pCityId);
            if (!IsLiveCity(city))
                return Reject(AW3CommandError.NotFound,
                    "aw3_army_command_city_missing");
            Kingdom cityKingdom = city.kingdom;
            War war = rally
                ? FindActiveWar(pKingdom, null)
                : FindActiveWar(pKingdom, cityKingdom);
            if (!ArmyRtsCommandRules.IsLegalCityTarget(rally,
                    cityHasOwner: cityKingdom?.data != null,
                    cityOwnedByCommander: cityKingdom == pKingdom,
                    hasApplicableWar: war?.data != null))
                return Reject(AW3CommandError.IllegalTarget,
                    "aw3_army_command_illegal_city");

            var mission = new ArmyRtsMission
            {
                ArmyId = pArmy.id,
                KingdomId = pKingdom.id,
                WarId = war.data.id,
                FrontId = city.id,
                TargetCityId = city.id,
                ProposalKind = rally
                    ? ArmyRtsProposalKind.Defend
                    : ArmyRtsProposalKind.Attack,
                Role = rally ? ArmyRtsRole.Defense : ArmyRtsRole.Assault,
                Posture = rally
                    ? ArmyRtsPosture.Defend
                    : ArmyRtsPosture.Attack,
                PlayerOrder = true,
                IssuedTime = CurrentWorldTime()
            };
            ArmyRtsControllerService.AssignMission(pArmy, mission);
            return AW3CommandResult.Success(
                rally ? "aw3_army_rally_point_set" :
                    "aw3_army_target_city_set", pArmy.id);
        }

        private static AW3CommandResult SetPosture(Kingdom pKingdom,
            Army pArmy, string pPostureId)
        {
            if (!Enum.TryParse(pPostureId, true,
                    out ArmyRtsPosture posture) ||
                !Enum.IsDefined(typeof(ArmyRtsPosture), posture))
                return Reject(AW3CommandError.InvalidRequest,
                    "aw3_army_command_posture_invalid");
            if (posture == ArmyRtsPosture.Retreat)
            {
                if (!ArmyRetreatService.AssignArmyRetreat(pArmy, -1L,
                        ArmyRtsWithdrawalOrigin.PlayerCommand) ||
                    !ArmyRtsControllerService.TryGetMission(pArmy,
                        out ArmyRtsMission retreatMission))
                    return Reject(AW3CommandError.Conflict,
                        "aw3_army_command_retreat_unavailable");
                retreatMission.PlayerOrder = true;
                retreatMission.IssuedTime = CurrentWorldTime();
                ArmyRtsControllerService.AssignMission(pArmy,
                    retreatMission);
                return AW3CommandResult.Success(
                    "aw3_army_posture_set", pArmy.id);
            }
            if (!ArmyRtsControllerService.TryGetMission(pArmy,
                    out ArmyRtsMission mission))
                return Reject(AW3CommandError.Conflict,
                    "aw3_army_command_mission_missing");
            mission.Posture = posture;
            mission.PlayerOrder = true;
            mission.IssuedTime = CurrentWorldTime();
            ArmyRtsControllerService.AssignMission(pArmy, mission);
            return AW3CommandResult.Success("aw3_army_posture_set",
                pArmy.id);
        }

        private static AW3CommandResult CancelOrder(Kingdom pKingdom,
            Army pArmy)
        {
            if (!ArmyRtsControllerService.TryGetMission(pArmy,
                    out ArmyRtsMission mission) || !mission.PlayerOrder)
                return Reject(AW3CommandError.Conflict,
                    "aw3_army_player_order_missing");
            ArmyRtsControllerService.Invalidate(pArmy.id);
            return AW3CommandResult.Success("aw3_army_order_cancelled",
                pArmy.id);
        }

        private static War FindActiveWar(Kingdom pKingdom,
            Kingdom pTargetKingdom)
        {
            if (pKingdom?.data == null) return null;
            War selected = null;
            int inspected = 0;
            try
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (inspected++ >= MaximumWarsInspected) break;
                    if (war?.data == null || war.hasEnded() ||
                        !war.hasKingdom(pKingdom)) continue;
                    if (pTargetKingdom?.data != null &&
                        !war.isInWarWith(pKingdom, pTargetKingdom))
                        continue;
                    if (selected?.data == null ||
                        war.data.id < selected.data.id) selected = war;
                }
            }
            catch { return null; }
            return selected;
        }

        private static bool IsArmyCommand(AW3CommandKind pKind)
        {
            return pKind == AW3CommandKind.SetArmyRallyPoint ||
                   pKind == AW3CommandKind.SetArmyTargetCity ||
                   pKind == AW3CommandKind.SetArmyPosture ||
                   pKind == AW3CommandKind.CancelArmyOrder;
        }

        private static bool IsOwnedArmy(Army pArmy, Kingdom pKingdom)
        {
            try
            {
                return pArmy?.data != null && pArmy.isAlive() &&
                       pArmy.getKingdom() == pKingdom;
            }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && !pKingdom.isRekt() &&
                       pKingdom.isAlive();
            }
            catch { return false; }
        }

        private static bool IsLiveCity(City pCity)
        {
            try { return pCity?.data != null && !pCity.isRekt(); }
            catch { return false; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static double CurrentWorldTime()
        {
            try { return Math.Max(0d, World.world?.getCurWorldTime() ?? 0d); }
            catch { return 0d; }
        }

        private static AW3CommandResult Reject(AW3CommandError pError,
            string pMessageKey)
        {
            return AW3CommandResult.Rejected(pError, pMessageKey);
        }
    }
}
