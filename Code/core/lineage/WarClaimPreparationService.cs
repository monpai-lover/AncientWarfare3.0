namespace AncientWarfare3.core.lineage
{
    internal static class WarClaimPreparationService
    {
        internal const string TargetKey = "aw_war_ai_claim_target_id";

        public static bool IsLockedTo(Kingdom source, Kingdom target)
        {
            if (source?.data == null || target?.data == null) return false;
            source.data.get(TargetKey, out long targetId, -1L);
            if (targetId != target.id) return false;
            return WarTerritoryService.HasActiveProjectAgainst(source,
                       target) ||
                   DiplomaticOperationService.HasActiveSpyNetwork(source,
                       target, out _, out _) ||
                   WarDecisionService.HasValidCasusBelli(source, target,
                       WarDecisionService.WAR_NORMAL);
        }

        public static bool TryBeginWeakClaim(Kingdom source, Kingdom target)
        {
            City city = WarTerritoryService.FindFirstFabricationTargetCity(
                source, target);
            if (city?.data == null) return false;
            bool started = DiplomaticOperationService.HasActiveSpyNetwork(
                source, target, out _, out _)
                ? DiplomaticOperationService.TryStartForgeDocuments(source,
                    target, city, WarTerritoryService.PROJECT_WEAK_CLAIM,
                    pPlayerInitiated: false, out _, out _)
                : DiplomaticOperationService.TryStartSpyNetwork(source,
                    target, pPlayerInitiated: false, out _, out _);
            if (started) source.data.set(TargetKey, target.id);
            return started;
        }
    }
}
