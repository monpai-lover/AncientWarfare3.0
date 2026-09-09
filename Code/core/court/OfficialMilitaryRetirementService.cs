using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class OfficialMilitaryRetirementService
    {
        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral()) return;
            RetireOfficials(pKingdom);
            RetireGenerals(pKingdom);
        }

        private static void RetireOfficials(Kingdom pKingdom)
        {
            var states = OfficialCareerStateService.LoadKingdomStates(pKingdom.id);
            foreach (OfficialCareerStateView state in states.Values)
            {
                Actor actor = World.world?.units?.get(state.ActorId);
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                if (actor.getAge() < SoldierRetirementRules.HardRetirementAge) continue;
                OfficialCareerStateService.ClearCurrentOffice(actor,
                    pKingdom.id, state.OfficeId ?? "");
            }
        }

        private static void RetireGenerals(Kingdom pKingdom)
        {
            foreach (Actor actor in GeneralService.GetActiveGenerals(pKingdom))
            {
                if (actor?.data == null || actor.getAge() <
                    SoldierRetirementRules.HardRetirementAge) continue;
                GeneralService.RetireForAge(actor);
            }
        }
    }
}
