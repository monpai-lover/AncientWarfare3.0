namespace AncientWarfare3.core.lineage
{
    internal static class MatrilocalLineageService
    {
        internal static bool ReconcileParents(Actor pParent1,
            Actor pParent2)
        {
            Actor woman = ResolveWoman(pParent1, pParent2);
            Actor man = ResolveMan(pParent1, pParent2);
            if (woman?.data == null || man?.data == null) return false;

            if (!RulerHouseholdRules.ShouldEstablishMatrilocal(
                    womanValid: IsValid(woman),
                    womanAuthorityTier: AuthorityTier(woman),
                    manValid: IsValid(man),
                    manAuthorityTier: AuthorityTier(man))) return false;

            if (IsMatrilocalTo(man, woman)) return true;
            man.data.set(LineageKeys.MATRILOCAL_IN_LAW, true);
            man.data.set(LineageKeys.MATRILOCAL_WIFE_ID, woman.data.id);
            return true;
        }

        internal static bool IsMatrilocalTo(Actor pMan, Actor pWoman)
        {
            if (pMan?.data == null || pWoman?.data == null ||
                !pMan.isSexMale() || !pWoman.isSexFemale()) return false;
            pMan.data.get(LineageKeys.MATRILOCAL_IN_LAW,
                out bool matrilocal, false);
            pMan.data.get(LineageKeys.MATRILOCAL_WIFE_ID,
                out long wifeId, -1L);
            return matrilocal && wifeId == pWoman.data.id;
        }

        private static int AuthorityTier(Actor pActor)
        {
            if (!IsValid(pActor)) return 0;
            try
            {
                if (pActor.isKing() || pActor.kingdom?.king == pActor)
                    return 2;
            }
            catch { }
            try
            {
                if (pActor.isCityLeader() || pActor.city?.leader == pActor)
                    return 1;
            }
            catch { }
            return 0;
        }

        private static Actor ResolveWoman(Actor pParent1, Actor pParent2)
        {
            if (pParent1?.isSexFemale() == true) return pParent1;
            return pParent2?.isSexFemale() == true ? pParent2 : null;
        }

        private static Actor ResolveMan(Actor pParent1, Actor pParent2)
        {
            if (pParent1?.isSexMale() == true) return pParent1;
            return pParent2?.isSexMale() == true ? pParent2 : null;
        }

        private static bool IsValid(Actor pActor)
        {
            return pActor?.data != null && !pActor.isRekt() &&
                   pActor.isAlive();
        }
    }
}
