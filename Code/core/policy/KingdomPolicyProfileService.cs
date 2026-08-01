using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomPolicyProfileService
    {
        public static KingdomPolicyProfileId Resolve(Kingdom pKingdom)
        {
            if (!IsValidKingdom(pKingdom))
                return KingdomPolicyProfileId.None;

            ActorAsset resolvedActorAsset;
            try
            {
                resolvedActorAsset = pKingdom.getActorAsset();
            }
            catch
            {
                return KingdomPolicyProfileId.None;
            }

            bool nativeXia = LineageService.IsXiaKingdom(
                pKingdom, resolvedActorAsset);
            bool monkey = CivMonkeyPolicyRules.IsNativePolicySpecies(
                pKingdom.data.original_actor_asset,
                pKingdom.asset?.id,
                resolvedActorAsset?.id);
            bool enteredXia = XiaCultureIntegrationService.IsFullyIntegrated(
                pKingdom.culture);
            bool civilized = resolvedActorAsset != null &&
                             resolvedActorAsset.civ;

            return KingdomPolicyProfileRules.Resolve(
                valid: true,
                civilized: civilized,
                nativeXia: nativeXia,
                monkey: monkey,
                enteredXia: enteredXia);
        }

        public static bool TryGet(Kingdom pKingdom,
            out KingdomPolicyProfileId pProfileId)
        {
            pProfileId = KingdomPolicyProfileId.None;
            if (!TryDecideAssignment(pKingdom,
                    out KingdomPolicyProfileAssignmentDecision decision))
                return false;

            pProfileId = decision.ProfileId;
            return KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                pProfileId);
        }

        public static KingdomPolicyProfileId EnsureAssigned(Kingdom pKingdom)
        {
            if (!TryDecideAssignment(pKingdom,
                    out KingdomPolicyProfileAssignmentDecision decision))
                return KingdomPolicyProfileId.None;

            if (decision.ShouldWrite)
            {
                pKingdom.data.set(LineageKeys.POLICY_PROFILE_ID,
                    decision.PersistedId);
                KingdomPolicyEffectService.Invalidate(pKingdom);
            }

            return decision.ProfileId;
        }

        private static bool TryDecideAssignment(Kingdom pKingdom,
            out KingdomPolicyProfileAssignmentDecision pDecision)
        {
            KingdomPolicyProfileId runtimeProfileId = Resolve(pKingdom);
            if (!KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                    runtimeProfileId))
            {
                pDecision = new KingdomPolicyProfileAssignmentDecision(
                    KingdomPolicyProfileId.None, string.Empty, false);
                return false;
            }

            pKingdom.data.get(LineageKeys.POLICY_PROFILE_ID,
                out string persistedProfileId, string.Empty);
            pDecision = KingdomPolicyProfileRules.DecideAssignment(
                runtimeProfileId, persistedProfileId);
            return KingdomPolicyProfileRules.IsResolvableKingdomProfile(
                pDecision.ProfileId);
        }

        private static bool IsValidKingdom(Kingdom pKingdom)
        {
            if (pKingdom == null || pKingdom.data == null)
                return false;

            try
            {
                return !pKingdom.isRekt() && !pKingdom.isNeutral();
            }
            catch
            {
                return false;
            }
        }
    }
}
