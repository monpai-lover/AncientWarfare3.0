namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsPresentationRules
    {
        public static string OperationLocalizationKey(ArmyRtsState pState,
            ArmyRtsTransportPhase pTransportPhase)
        {
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
                default:
                    return StateLocalizationKey(pState);
            }
        }

        public static string OperationFallback(ArmyRtsState pState,
            ArmyRtsTransportPhase pTransportPhase)
        {
            switch (pTransportPhase)
            {
                case ArmyRtsTransportPhase.AwaitingPickup:
                    return "Awaiting transport";
                case ArmyRtsTransportPhase.Embarking:
                    return "Embarking";
                case ArmyRtsTransportPhase.Sailing:
                    return "Sailing";
                case ArmyRtsTransportPhase.Landing:
                    return "Landing";
                default:
                    return StateFallback(pState);
            }
        }

        public static string StateLocalizationKey(ArmyRtsState pState)
        {
            return "aw_army_rts_state_" + pState.ToString().ToLowerInvariant();
        }

        public static string StateFallback(ArmyRtsState pState)
        {
            switch (pState)
            {
                case ArmyRtsState.Rally: return "Rallying";
                case ArmyRtsState.March: return "Marching";
                case ArmyRtsState.Deploy: return "Deploying";
                case ArmyRtsState.Assault: return "Assaulting";
                case ArmyRtsState.Hold: return "Holding";
                case ArmyRtsState.Pursue: return "Pursuing";
                case ArmyRtsState.Retreat: return "Retreating";
                case ArmyRtsState.Regroup: return "Regrouping";
                case ArmyRtsState.Replenish: return "Replenishing";
                default: return "Idle";
            }
        }

        public static string RoleLocalizationKey(ArmyRtsRole pRole)
        {
            return "aw_army_rts_role_" + pRole.ToString().ToLowerInvariant();
        }

        public static string RoleFallback(ArmyRtsRole pRole)
        {
            switch (pRole)
            {
                case ArmyRtsRole.Assault: return "Assault";
                case ArmyRtsRole.Defense: return "Defense";
                case ArmyRtsRole.Reserve: return "Reserve";
                case ArmyRtsRole.Reinforcement: return "Reinforcement";
                case ArmyRtsRole.TemporaryGarrisonSortie:
                    return "Garrison sortie";
                default: return "Army";
            }
        }

        public static string ComposeOperation(string pState, string pRole,
            string pTarget, bool pPlayerOrder,
            string pPlayerOrderLabel = "Player order")
        {
            string result = (pState ?? string.Empty).Trim();
            string role = (pRole ?? string.Empty).Trim();
            string target = (pTarget ?? string.Empty).Trim();
            if (role.Length > 0)
                result = result.Length > 0 ? result + " / " + role : role;
            if (target.Length > 0)
                result = result.Length > 0 ? result + " / " + target : target;
            if (!pPlayerOrder) return result;
            string label = (pPlayerOrderLabel ?? string.Empty).Trim();
            return label.Length == 0
                ? result
                : result.Length == 0 ? label : label + " / " + result;
        }
    }
}
