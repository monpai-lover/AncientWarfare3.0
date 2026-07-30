namespace AncientWarfare3.core.lineage
{
    internal enum ArmyRtsReplenishmentArrivalAction
    {
        Wait,
        Teleport,
        Complete,
        Discard
    }

    internal static class ArmyRtsReplenishmentArrivalRules
    {
        internal const double TeleportAfterSeconds = 0d;
        internal const int MaximumArrivalChecksPerFrame = 4;

        internal static bool ShouldReleaseReplenishmentAfterArrival(
            bool arrivalTeleported, bool departureStrengthReady)
        {
            return arrivalTeleported && departureStrengthReady;
        }

        internal static ArmyRtsReplenishmentArrivalAction ResolveAction(
            bool tracked, bool targetArmyActive, bool memberStillEligible,
            bool atFormation, bool combatActive, bool transportActive,
            double elapsedRealtime)
        {
            if (!tracked || !targetArmyActive || !memberStillEligible)
                return ArmyRtsReplenishmentArrivalAction.Discard;
            if (atFormation)
                return ArmyRtsReplenishmentArrivalAction.Complete;
            if (combatActive || transportActive)
                return ArmyRtsReplenishmentArrivalAction.Wait;
            return elapsedRealtime >= TeleportAfterSeconds
                ? ArmyRtsReplenishmentArrivalAction.Teleport
                : ArmyRtsReplenishmentArrivalAction.Wait;
        }
    }
}
