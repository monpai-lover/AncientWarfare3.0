using AncientWarfare3.content.schools;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalMasterVocationService
    {
        public static bool CanEnter(Actor pActor, HistoricalMasterMilitaryContext pContext)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.SCHOOL_MASTER_ID, out string masterId, "");
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            bool canonical = !string.IsNullOrEmpty(masterId) || definition != null;
            return HistoricalMasterVocationRules.CanEnter(canonical,
                !canonical || definition != null, definition?.MilitaryEligible == true,
                pContext);
        }

        public static bool CanEnterArmyRole(Actor pActor, string pRole)
        {
            HistoricalMasterMilitaryContext context = pRole == AWArmyRole.RoyalGuard
                ? HistoricalMasterMilitaryContext.RoyalGuard
                : pRole == AWArmyRole.SlaveArmy
                    ? HistoricalMasterMilitaryContext.SlaveArmyCadre
                    : pRole == AWArmyRole.BorderArmy
                        ? HistoricalMasterMilitaryContext.BorderArmy
                        : HistoricalMasterMilitaryContext.NormalArmy;
            return CanEnter(pActor, context);
        }

        public static bool CanJoinArmy(Actor pActor, Army pArmy)
        {
            if (pArmy == null) return true;
            string role = AWArmyService.IsRoleArmy(pArmy, AWArmyRole.RoyalGuard)
                ? AWArmyRole.RoyalGuard
                : AWArmyService.IsRoleArmy(pArmy, AWArmyRole.SlaveArmy)
                    ? AWArmyRole.SlaveArmy
                    : AWArmyService.IsRoleArmy(pArmy, AWArmyRole.BorderArmy)
                        ? AWArmyRole.BorderArmy
                        : "";
            return CanEnterArmyRole(pActor, role);
        }
    }
}
