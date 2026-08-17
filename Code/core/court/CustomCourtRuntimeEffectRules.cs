using System;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtRuntimeEffectRules
    {
        public static bool IsActiveIncumbent(long expectedKingdomId,
            string expectedOfficeId, long rowActorId, string rowOfficeId,
            bool actorExists, bool actorAlive, long actorKingdomId,
            long runtimeKingdomId, string runtimeOfficeId)
        {
            return expectedKingdomId >= 0L && rowActorId >= 0L &&
                !string.IsNullOrWhiteSpace(expectedOfficeId) &&
                string.Equals(rowOfficeId, expectedOfficeId,
                    StringComparison.Ordinal) &&
                actorExists && actorAlive &&
                actorKingdomId == expectedKingdomId &&
                runtimeKingdomId == expectedKingdomId &&
                string.Equals(runtimeOfficeId, expectedOfficeId,
                    StringComparison.Ordinal);
        }
    }
}
