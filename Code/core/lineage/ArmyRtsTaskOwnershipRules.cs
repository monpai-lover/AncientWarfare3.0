namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsTaskOwnershipRules
    {
        public const double ImmediateCombatPriorityDistanceTiles = 16d;

        public static string ResolveWatchdogTaskId(
            ArmyWatchdogPositionSource pPositionSource,
            ArmyRtsState pState, ArmyRtsProposalKind pProposalKind,
            ArmyRtsTransportPhase pTransportPhase)
        {
            if (pPositionSource ==
                ArmyWatchdogPositionSource.FormationMember)
                return "aw_army_rts_formation";
            if (pProposalKind == ArmyRtsProposalKind.FrontHold)
                return "aw_army_rts_front_hold";
            switch (pTransportPhase)
            {
                case ArmyRtsTransportPhase.AwaitingPickup:
                    return "aw_army_rts_transport_awaiting_pickup";
                case ArmyRtsTransportPhase.Embarking:
                    return "aw_army_rts_transport_embarking";
                case ArmyRtsTransportPhase.Sailing:
                    return "aw_army_rts_transport_sailing";
                case ArmyRtsTransportPhase.Landing:
                    return "aw_army_rts_transport_landing";
            }
            switch (pState)
            {
                case ArmyRtsState.Rally: return "aw_army_rts_rally";
                case ArmyRtsState.Replenish: return "aw_army_rts_replenish";
                case ArmyRtsState.March: return "aw_army_rts_march";
                case ArmyRtsState.Deploy: return "aw_army_rts_deploy";
                case ArmyRtsState.Assault: return "aw_army_rts_assault";
                case ArmyRtsState.Pursue: return "aw_army_rts_pursue";
                case ArmyRtsState.Retreat: return "aw_army_rts_retreat";
                case ArmyRtsState.Regroup: return "aw_army_rts_regroup";
                case ArmyRtsState.Hold: return "aw_army_rts_front_hold";
                default: return "aw_army_rts_mission";
            }
        }

        public static bool HasImmediateCombatPriority(
            bool hasAttackTarget, bool targetAlive, bool targetHostile,
            bool targetCombatant, double distanceSquared)
        {
            if (!hasAttackTarget || !targetAlive || !targetHostile ||
                !targetCombatant || double.IsNaN(distanceSquared) ||
                distanceSquared < 0d) return false;
            double maximum = ImmediateCombatPriorityDistanceTiles;
            return distanceSquared <= maximum * maximum;
        }

        public static bool ShouldReassertMissionTask(
            ArmyRtsMode pMode, bool pOwnsActor, bool pActorAlive,
            bool pExpectedJobActive, bool pExpectedTaskActive,
            bool pImmediateCombat, bool pRequiredBoatWork,
            bool pForceRecovery = false)
        {
            if (!ArmyRtsRuntimeModeRules.ShouldCommit(pMode) ||
                !pOwnsActor || !pActorAlive ||
                pImmediateCombat && !pForceRecovery ||
                pRequiredBoatWork)
                return false;
            return !pExpectedJobActive || !pExpectedTaskActive;
        }
    }
}
