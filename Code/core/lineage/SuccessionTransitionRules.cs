namespace AncientWarfare3.core.lineage
{
    public static class SuccessionTransitionRules
    {
        public static bool IsPending(float pTimerNewKing)
        {
            return pTimerNewKing > 0f;
        }

        public static long ResolveReferenceKingId(long pCurrentKingId, bool pCurrentKingValid,
            long pPreviousKingId)
        {
            if (pCurrentKingValid && pCurrentKingId >= 0) return pCurrentKingId;
            return pPreviousKingId >= 0 ? pPreviousKingId : -1L;
        }

        public static bool IsOfficialRoleEligible(bool pIsKing, bool pIsCityLeader,
            bool pIsGeneral, bool pIsArmyCaptain, bool pHasFief)
        {
            return !pIsKing;
        }

        public static bool ShouldTreatMissingHeirAsUnstable(bool pSuccessionPending, bool pHasHeir)
        {
            return !pSuccessionPending && !pHasHeir;
        }

        public static bool ShouldBlockVanillaMassFragmentation(bool pUsesManagedLineage)
        {
            return pUsesManagedLineage;
        }

        public static bool ShouldBlockShatteredCrownEvent(bool pUsesManagedLineage)
        {
            return false;
        }

        public static bool ShouldUseCachedHeir(bool pSuccessionPending, bool pCachedHeirEligible)
        {
            return pSuccessionPending && pCachedHeirEligible;
        }

        public static bool ShouldOverwriteCachedHeir(bool pSuccessionPending, bool pHasReferenceKing)
        {
            return !pSuccessionPending && pHasReferenceKing;
        }
    }
}
