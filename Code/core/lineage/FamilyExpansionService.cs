using System;

namespace AncientWarfare3.core.lineage
{
    internal static class FamilyExpansionService
    {
        internal static float ReproductionDecisionWeight(Actor pActor,
            float pOriginalWeight)
        {
            if (SyntheticLevyService.IsSynthetic(pActor))
                return pOriginalWeight;
            float result = DynasticReproductionService
                .ReproductionDecisionWeight(pActor, pOriginalWeight);
            Actor partner = LivingMutualPartner(pActor);
            return NeedsExpansion(pActor, partner)
                ? Math.Max(result,
                    FamilyExpansionRules.PrioritizedReproductionWeight)
                : result;
        }

        internal static bool NeedsExpansion(Actor pFirst, Actor pSecond)
        {
            if (SyntheticLevyService.IsSynthetic(pFirst) ||
                SyntheticLevyService.IsSynthetic(pSecond)) return false;
            if (!IsCivilizedActor(pFirst)) return false;
            FamilyExpansionTier tier = ResolveTier(pFirst, pSecond);
            int livingChildren = Math.Max(CountLivingChildren(pFirst),
                CountLivingChildren(pSecond));
            return FamilyExpansionRules.NeedsExpansion(livingChildren, tier);
        }

        internal static int CountLivingChildren(Actor pActor)
        {
            if (pActor?.data == null) return 0;
            int count = 0;
            try
            {
                foreach (Actor child in pActor.getChildren(
                             pOnlyCurrentFamily: false))
                {
                    if (child?.data != null && child.isAlive() &&
                        !child.isRekt()) count++;
                }
            }
            catch { return 0; }
            return count;
        }

        internal static bool ShouldDeliverCivilianImmediately(Actor pMother,
            out Actor pFather)
        {
            pFather = LivingMutualPartner(pMother);
            if (SyntheticLevyService.IsSynthetic(pMother) ||
                SyntheticLevyService.IsSynthetic(pFather)) return false;
            if (pFather?.data == null || !pFather.isSexMale() ||
                !pMother.isSexFemale() || !IsCivilizedActor(pMother) ||
                !IsCivilizedActor(pFather)) return false;
            return ResolveTier(pMother, pFather) ==
                   FamilyExpansionTier.Civilian &&
                   NeedsExpansion(pMother, pFather);
        }

        internal static FamilyExpansionTier ResolveTier(Actor pFirst,
            Actor pSecond = null)
        {
            FamilyExpansionTier first = ResolveIndividualTier(pFirst);
            FamilyExpansionTier second = ResolveIndividualTier(pSecond);
            return first >= second ? first : second;
        }

        private static FamilyExpansionTier ResolveIndividualTier(Actor pActor)
        {
            if (!IsCivilizedActor(pActor)) return FamilyExpansionTier.Civilian;
            if (IsRoyalOrFeudatory(pActor)) return FamilyExpansionTier.Royal;
            return NobleHeirPregnancyService.IsEligibleNoble(pActor)
                ? FamilyExpansionTier.Noble
                : FamilyExpansionTier.Civilian;
        }

        private static bool IsRoyalOrFeudatory(Actor pActor)
        {
            try
            {
                if (pActor.isKing() ||
                    HeirService.IsCurrentHeir(pActor.kingdom, pActor) ||
                    FeudatoryService.IsActivePrince(pActor)) return true;
            }
            catch { }
            long rulerId = -1L;
            if (pActor?.data != null)
                pActor.data.get(LineageKeys.RULER_HOUSEHOLD_RULER_ID,
                    out rulerId, -1L);
            return rulerId >= 0L;
        }

        private static bool IsCivilizedActor(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.asset?.civ == true &&
                       pActor.kingdom?.data != null && pActor.city?.data != null;
            }
            catch { return false; }
        }

        private static Actor LivingMutualPartner(Actor pActor)
        {
            Actor partner = pActor?.lover;
            try
            {
                return partner?.data != null && partner.isAlive() &&
                       !partner.isRekt() && partner.lover == pActor
                    ? partner
                    : null;
            }
            catch { return null; }
        }
    }
}
