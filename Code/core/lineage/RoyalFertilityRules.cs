namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     "求嗣"纯规则:无在世男嗣的国王疯狂生育直到有儿子。决定是否给国王挂/撤"求嗣"特质。
    /// </summary>
    public static class RoyalFertilityRules
    {
        /// <summary>
        ///     该给国王挂"求嗣"特质吗?——他是本系可生育的成年在世君主,且当前**没有在世男嗣(儿子)**。
        ///     有男嗣后返回 false(应撤销特质),即"直到有儿子"。
        /// </summary>
        public static bool ShouldUrgeHeir(bool pHasKing, bool pKingInLineageSystem,
            bool pKingFertileAdultAlive, bool pHasLivingMaleSon)
        {
            if (!pHasKing || !pKingInLineageSystem || !pKingFertileAdultAlive) return false;
            return !pHasLivingMaleSon;
        }
    }
}
