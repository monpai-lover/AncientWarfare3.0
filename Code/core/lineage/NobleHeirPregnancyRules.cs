namespace AncientWarfare3.core.lineage
{
    public enum NobleHeirRetryDisposition
    {
        Clear,
        Wait,
        Start
    }

    public static class NobleHeirPregnancyRules
    {
        public const float TenMonthPregnancySeconds = 50f;

        public static float ResolvePregnancyDuration(float pOriginalDuration,
            bool pPregnancyStatus, bool pHasLivingPartner,
            bool pMotherEligible, bool pFatherEligible)
        {
            return pPregnancyStatus && pHasLivingPartner &&
                   (pMotherEligible || pFatherEligible)
                ? TenMonthPregnancySeconds
                : pOriginalDuration;
        }

        public static bool ShouldCreateRetryRequest(bool pManagedPregnancy,
            bool pNobleCoupleEligible, bool pNeedsExpansion,
            bool pAlreadyPending)
        {
            return pManagedPregnancy && pNobleCoupleEligible &&
                   pNeedsExpansion && !pAlreadyPending;
        }

        public static NobleHeirRetryDisposition EvaluateRetry(
            bool pAuthoritative, bool pNextCycleReached,
            bool pMotherAlive, bool pNobleCoupleEligible,
            bool pNeedsExpansion, bool pPartnerReady,
            bool pPregnancyRemoved, bool pMotherAdult,
            bool pMotherBreedingAge, bool pFertile, bool pHasNutrition,
            bool pCitySafe, bool pPersonalOffspringRoom,
            bool pPersonalOffspringLimitBypass, bool pMetaLimitRoom,
            bool pWorldLawAllows)
        {
            if (!pMotherAlive || !pNobleCoupleEligible ||
                !pNeedsExpansion)
                return NobleHeirRetryDisposition.Clear;

            bool offspringRoom = DynasticMaleLineContinuityRules
                .HasPersonalOffspringRoom(pPersonalOffspringRoom,
                    pPersonalOffspringLimitBypass) && pMetaLimitRoom;
            return pAuthoritative && pNextCycleReached && pPartnerReady &&
                   pPregnancyRemoved && pMotherAdult && pMotherBreedingAge &&
                   pFertile && pHasNutrition && pCitySafe && offspringRoom &&
                   pWorldLawAllows
                ? NobleHeirRetryDisposition.Start
                : NobleHeirRetryDisposition.Wait;
        }
    }
}
