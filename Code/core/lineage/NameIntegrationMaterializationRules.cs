using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    public enum NameIntegrationAction
    {
        Skip,
        MaterializeBranchClan,
        MaterializePersonalClan,
        RecordProfileOnly
    }

    public static class NameIntegrationMaterializationRules
    {
        private static readonly string[] PersonalClanFallbacks =
        {
            "\u590f", "\u59ec", "\u59dc", "\u5b34", "\u59d2", "\u59d4",
            "\u59d2", "\u59e2"
        };

        public static NameIntegrationAction Decide(bool kingdomIntegrated,
            NamingProfileId profile, long shiId, bool protectedName,
            bool actorIntegrated)
        {
            if (!kingdomIntegrated || !actorIntegrated ||
                profile != NamingProfileId.Xia)
                return NameIntegrationAction.Skip;
            if (protectedName) return NameIntegrationAction.RecordProfileOnly;
            return shiId >= 0L
                ? NameIntegrationAction.MaterializeBranchClan
                : NameIntegrationAction.MaterializePersonalClan;
        }

        public static string ResolvePersonalClan(long actorId,
            long cultureId)
        {
            long seed = AWNamingSeedRules.Combine(actorId, cultureId,
                "aw_personal_clan", 1);
            int index = (int)((ulong)seed %
                              (ulong)PersonalClanFallbacks.Length);
            return PersonalClanFallbacks[index];
        }
    }
}
