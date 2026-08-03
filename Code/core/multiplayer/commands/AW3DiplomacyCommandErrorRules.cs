using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.multiplayer.commands
{
    public static class AW3DiplomacyCommandErrorRules
    {
        public static AW3CommandError Map(string pReason)
        {
            switch (pReason)
            {
                case "not_found":
                case "invalid_participants":
                    return AW3CommandError.NotFound;
                case "already_responded":
                case "pending_exists":
                case "covert_operation_pending":
                    return AW3CommandError.Conflict;
                case "expired":
                case "target_city_changed":
                case "war_target_option_changed":
                case "marriage_candidate_stale":
                    return AW3CommandError.StaleState;
                case "insufficient_resources":
                case "insufficient_points":
                    return AW3CommandError.InsufficientResources;
                case "cooldown":
                    return AW3CommandError.Cooldown;
                case "write_failed":
                case "execution_failed":
                case "covert_operation_write_failed":
                    return AW3CommandError.ExecutionFailed;
                default:
                    return AW3CommandError.IllegalTarget;
            }
        }
    }
}
