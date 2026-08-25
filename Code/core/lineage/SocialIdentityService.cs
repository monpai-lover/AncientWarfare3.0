using AncientWarfare3.content;

namespace AncientWarfare3.core.lineage
{
    internal static class SocialIdentityService
    {
        internal const string NobleValue = "noble";
        internal const string ScholarOfficialValue = "scholar_official";

        internal static bool IsFormalNoble(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (pActor.isKing()) return true;
            if (HeirService.PeekRegisteredHeir(pActor.kingdom) == pActor)
                return true;
            pActor.data.get(LineageKeys.ROYAL_CHILD, out bool royalChild, false);
            if (royalChild || FeudatoryService.IsActivePrince(pActor)) return true;
            try { return NobleRankService.ReadHot(pActor).Rank > 0; }
            catch { return false; }
        }

        internal static SocialIdentityClass ApplyOfficial(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt())
                return SocialIdentityClass.None;
            SocialIdentityClass identity = SocialIdentityRules.Resolve(
                isOfficial: true, isActing: false,
                isCurrentRuler: pActor.isKing(),
                isRegisteredHeir: HeirService.PeekRegisteredHeir(pActor.kingdom) == pActor,
                isRoyalRelative: IsRoyalRelative(pActor),
                hasFormalTitle: HasFormalTitle(pActor));
            ApplyTraits(pActor, identity);
            return identity;
        }

        internal static void ApplyTraits(Actor pActor, SocialIdentityClass pIdentity)
        {
            if (pActor?.data == null) return;
            if (pIdentity == SocialIdentityClass.Noble)
            {
                if (pActor.hasTrait(LineageKeys.TRAIT_SHIDAFU))
                    pActor.removeTrait(LineageKeys.TRAIT_SHIDAFU);
                if (!pActor.hasTrait(LineageKeys.TRAIT_GUIZU))
                    pActor.addTrait(LineageKeys.TRAIT_GUIZU);
                pActor.data.set(LineageKeys.SOCIAL_IDENTITY, NobleValue);
            }
            else if (pIdentity == SocialIdentityClass.ScholarOfficial)
            {
                if (pActor.hasTrait(LineageKeys.TRAIT_GUIZU))
                    pActor.removeTrait(LineageKeys.TRAIT_GUIZU);
                if (!pActor.hasTrait(LineageKeys.TRAIT_SHIDAFU))
                    pActor.addTrait(LineageKeys.TRAIT_SHIDAFU);
                pActor.data.set(LineageKeys.SOCIAL_IDENTITY,
                    ScholarOfficialValue);
            }
        }

        private static bool IsRoyalRelative(Actor pActor)
        {
            pActor.data.get(LineageKeys.ROYAL_CHILD, out bool royalChild, false);
            return royalChild || FeudatoryService.IsActivePrince(pActor);
        }

        private static bool HasFormalTitle(Actor pActor)
        {
            try { return NobleRankService.ReadHot(pActor).Rank > 0; }
            catch { return false; }
        }
    }
}
