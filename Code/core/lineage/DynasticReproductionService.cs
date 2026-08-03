namespace AncientWarfare3.core.lineage
{
    internal static class DynasticReproductionService
    {
        public static bool ShouldProtectFromOrdinaryMilitaryService(
            Actor pActor)
        {
            if (pActor?.data == null) return false;
            Actor partner = LivingPartner(pActor);
            return DynasticReproductionRules
                .ShouldProtectFromOrdinaryMilitaryService(
                    NeedsWindow(pActor), partner != null,
                    NeedsWindow(partner));
        }

        public static void ReleaseExistingMilitaryRole(Actor pActor)
        {
            ReleaseExistingMilitaryRole(pActor,
                ShouldReleaseExistingMilitaryRole(pActor));
        }

        internal static void ReleaseExistingMilitaryRole(Actor pActor,
            bool pShouldRelease)
        {
            if (!pShouldRelease || pActor?.data == null) return;
            pActor.stopBeingWarrior();
        }

        public static bool ShouldReleaseExistingMilitaryRole(Actor pActor)
        {
            if (pActor?.data == null) return false;
            bool warrior;
            try { warrior = pActor.isWarrior(); }
            catch { return false; }
            if (!warrior) return false;
            bool careerStanding = StandingArmyPeacetimeService
                .IsCareerStandingSoldier(pActor);
            bool militaryEmergency = StandingArmyPeacetimeService
                .HasMilitaryEmergency(pActor);
            bool inCombat = StandingArmyPeacetimeService.IsInCombat(pActor);
            bool cityAttackOrder = StandingArmyPeacetimeService
                .HasCityAttackOrder(pActor);
            if (careerStanding || militaryEmergency || inCombat ||
                cityAttackOrder) return false;
            bool reproductionProtected =
                ShouldProtectFromOrdinaryMilitaryService(pActor);
            bool currentHeir = !reproductionProtected &&
                               HeirService.IsCurrentHeir(
                                   pActor.kingdom, pActor);
            return DynasticReproductionRules
                .ShouldReleaseExistingMilitaryRole(warrior,
                    currentHeir,
                    reproductionProtected,
                    careerStanding,
                    militaryEmergency,
                    inCombat,
                    cityAttackOrder);
        }

        public static float ReproductionDecisionWeight(Actor pActor,
            float pOriginalWeight)
        {
            if (pActor?.data == null || !pActor.isAlive() ||
                pActor.isRekt()) return pOriginalWeight;
            bool usesDynasticSystem = UsesDynasticSystem(pActor);
            bool isRuler;
            try { isRuler = pActor.isKing(); }
            catch { isRuler = false; }
            Kingdom kingdom = pActor.kingdom;
            bool isCurrentHeir = false;
            if (kingdom?.data != null)
            {
                kingdom.data.get(LineageKeys.KINGDOM_HEIR_ID,
                    out long heirId, -1L);
                isCurrentHeir = heirId == pActor.data.id;
            }
            bool isFeudatoryPrince = FeudatoryService
                .IsActivePrince(pActor);
            NobleTitleSnapshot title = NobleRankService.ReadHot(pActor);
            bool holdsMaleNobleTitle = title.IsActive &&
                                       title.Style == NobleTitleStyle.Male;
            bool hasNobleIdentity = NobleIdentityService.IsNobleActor(pActor);
            float result = DynasticReproductionRules
                .ReproductionDecisionWeight(
                pOriginalWeight, usesDynasticSystem,
                hasNobleIdentity,
                isRuler, isCurrentHeir, isFeudatoryPrince,
                holdsMaleNobleTitle,
                DynasticLivingSonIndexService.HasLivingSon(pActor));
            return DynasticMaleLineContinuityService.NeedsContinuation(
                    pActor)
                ? System.Math.Max(result,
                    DynasticReproductionRules.PrioritizedReproductionWeight)
                : result;
        }

        private static bool NeedsWindow(Actor pActor)
        {
            if (pActor?.data == null) return false;
            Actor partner = LivingPartner(pActor);
            if (FamilyExpansionService.NeedsExpansion(pActor, partner))
                return true;
            return DynasticReproductionRules
                .NeedsCivilianReproductionWindow(
                    IsDynasticIdentity(pActor),
                    pActor.isAlive() && !pActor.isRekt(),
                    pActor.isAdult(), pActor.isBreedingAge(),
                    pActor.canProduceBabies(),
                    DynasticLivingSonIndexService.HasLivingSon(pActor));
        }

        private static bool IsDynasticIdentity(Actor pActor)
        {
            if (NobleIdentityService.IsNobleActor(pActor)) return true;
            pActor.data.get(LineageKeys.ROYAL_CHILD,
                out bool royalChild, false);
            return royalChild || FeudatoryService.IsActivePrince(pActor);
        }

        private static bool UsesDynasticSystem(Actor pActor)
        {
            return LineageService.IsNativeXiaCultureActor(pActor) ||
                   LineageService.UsesAwLineageSystem(pActor);
        }

        private static Actor LivingPartner(Actor pActor)
        {
            Actor partner = pActor?.lover;
            return partner?.data != null && partner.isAlive() &&
                   !partner.isRekt()
                ? partner
                : null;
        }
    }
}
