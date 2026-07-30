using System;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyNativeNameService
    {
        internal static bool IsOrdinaryArmy(Army pArmy)
        {
            return pArmy?.data != null && !AWArmyService.IsSpecialArmy(pArmy) &&
                   !GarrisonSortieService.IsSortieArmy(pArmy);
        }

        internal static bool TryResolve(Army pArmy, Kingdom pKingdom,
            City pAnchorCity, out string pName)
        {
            pName = string.Empty;
            if (!IsOrdinaryArmy(pArmy)) return false;

            Kingdom kingdom = pKingdom ?? SafeKingdom(pArmy);
            if (kingdom?.data == null || kingdom.isRekt()) return false;
            City anchor = pAnchorCity ?? AWArmyService.FindAnchorCity(pArmy);
            int ordinal = ResolveOrdinal(pArmy, kingdom, anchor?.id ?? -1L);
            pName = ArmyNativeNameRules.ResolveName(
                isSpecialArmy: false, currentNativeName: pArmy.data.name,
                kingdomName: kingdom.data.name,
                anchorCityName: anchor?.data?.name, ordinal);
            return !string.IsNullOrEmpty(pName);
        }

        private static int ResolveOrdinal(Army pArmy, Kingdom pKingdom,
            long pAnchorCityId)
        {
            int ordinal = 0;
            try
            {
                foreach (Army candidate in World.world?.armies)
                {
                    if (!IsOrdinaryArmy(candidate) ||
                        SafeKingdom(candidate) != pKingdom ||
                        AWArmyService.GetAnchorCityId(candidate) !=
                        pAnchorCityId || candidate.id > pArmy.id) continue;
                    ordinal++;
                }
            }
            catch
            {
                ordinal = 0;
            }
            return Math.Max(1, ordinal);
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }
    }
}
