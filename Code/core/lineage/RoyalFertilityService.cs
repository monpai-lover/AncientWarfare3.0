namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     求嗣服务:无在世男嗣(儿子)的国王挂"求嗣"特质大幅提高生育,直到诞下儿子再撤销。
    ///     由都城周期维护调用(错帧,见 AW_RetirementPatch)。
    /// </summary>
    internal static class RoyalFertilityService
    {
        public static void RefreshHeirUrge(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv()) return;

            Actor king = pKingdom.king;
            bool hasKing = king?.data != null && !king.isRekt();
            bool inSystem = hasKing && (LineageService.IsXia(king) || LineageService.UsesAwLineageSystem(king));
            bool fertile = hasKing && king.isAdult() && king.isAlive() && king.isSexMale();
            bool hasSon = hasKing && HasLivingMaleSon(king);

            bool shouldUrge = RoyalFertilityRules.ShouldUrgeHeir(hasKing, inSystem, fertile, hasSon);

            if (!hasKing) return;
            bool hasTrait = king.hasTrait(LineageKeys.TRAIT_HEIR_URGE);
            if (shouldUrge && !hasTrait)
                king.addTrait(LineageKeys.TRAIT_HEIR_URGE);
            else if (!shouldUrge && hasTrait)
                king.removeTrait(LineageKeys.TRAIT_HEIR_URGE);
        }

        private static bool HasLivingMaleSon(Actor pKing)
        {
            if (pKing?.data == null) return false;
            try
            {
                foreach (Actor child in pKing.getChildren(false))
                {
                    if (child?.data == null || child == pKing) continue;
                    if (child.isSexMale() && !child.isRekt() && child.isAlive())
                        return true;
                }
            }
            catch { return false; }
            return false;
        }
    }
}
