namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsSuccessionRecoveryRules
    {
        public const int MaximumArmiesPerCycle = 8;

        public static bool ShouldEnqueue(bool kingdomValid, bool kingValid,
            bool fromLoad, long currentKingId, long requestedKingId,
            long completedKingId)
        {
            return kingdomValid && kingValid &&
                   requestedKingId >= 0L &&
                   currentKingId == requestedKingId &&
                   completedKingId != requestedKingId;
        }

        public static bool ShouldEnqueueCaptainRecovery(bool armyValid,
            bool actorWasCaptain, bool missionActive)
        {
            return armyValid && actorWasCaptain && missionActive;
        }
    }
}
