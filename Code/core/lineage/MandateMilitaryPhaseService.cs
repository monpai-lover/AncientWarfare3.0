using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateMilitaryPhaseService
    {
        private const float StatusDuration = 1000000f;
        private static readonly string[] SpecialRoles =
        {
            AWArmyRole.RoyalGuard,
            AWArmyRole.SlaveArmy,
            AWArmyRole.BorderArmy,
            AWArmyRole.FeudatoryGarrison
        };

        public static int EffectiveWarriorSlots(Kingdom pKingdom,
            int pBaseSlots)
        {
            int phaseAdjusted =
                MandateMilitaryPhaseRules.EffectiveWarriorSlots(
                pBaseSlots,
                MandateService.IsMandateKingdom(pKingdom),
                MandatePhaseService.CurrentPhase);
            return CourtInstitutionEffectRules.ApplyWarriorSlotMultiplier(
                phaseAdjusted,
                CourtInstitutionEffectService.Read(pKingdom).
                    WarriorSlotMultiplier);
        }

        public static void ReconcileWarrior(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return;
            Kingdom kingdom = pActor.kingdom ?? pActor.city?.kingdom;
            string expected = MandateMilitaryPhaseRules.ExpectedStatusId(
                MandateService.IsMandateKingdom(kingdom),
                pActor.isWarrior(), MandatePhaseService.CurrentPhase);
            IReadOnlyDictionary<string, Status> statuses =
                pActor.getStatusesDict();
            bool hasExpected = !string.IsNullOrEmpty(expected) &&
                               statuses != null &&
                               statuses.ContainsKey(expected);
            int phaseStatusCount = CountPhaseStatuses(statuses);
            if (!MandateMilitaryPhaseRules.NeedsReconcile(hasExpected,
                    phaseStatusCount))
                return;

            Clear(pActor);
            if (!string.IsNullOrEmpty(expected))
                pActor.addStatusEffect(expected, StatusDuration,
                    pColorEffect: false);
        }

        public static void Clear(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.finishStatusEffect(MandateMilitaryPhaseRules.GoldenStatusId);
            pActor.finishStatusEffect(MandateMilitaryPhaseRules.DeclineStatusId);
            pActor.finishStatusEffect(MandateMilitaryPhaseRules.ChaosStatusId);
            pActor.finishStatusEffect(MandateMilitaryPhaseRules.RenewalStatusId);
        }

        public static void OnPhaseChanged(MandatePhase pPrevious,
            MandatePhase pNext)
        {
            if (pPrevious == pNext) return;
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null || mandate.isRekt()) return;

            QueueKingdomReconciliation(mandate);
        }

        public static void OnMandateEnded(Kingdom pFormerMandate)
        {
            if (pFormerMandate?.data == null) return;
            QueueKingdomReconciliation(pFormerMandate);
        }

        private static void QueueKingdomReconciliation(Kingdom pKingdom)
        {
            int cityCount = pKingdom.cities?.Count ?? 0;
            for (int i = 0; i < cityCount; i++)
            {
                City city = pKingdom.cities[i];
                if (city?.data == null || city.isRekt()) continue;
                long cityId = city.id;
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    DeferredRuntimeWorkRules.CoalescingKey(
                        "mandate_military_city", cityId),
                    DeferredWorkClass.Runtime,
                    () => ReconcileCityArmy(cityId));
            }

            for (int roleIndex = 0; roleIndex < SpecialRoles.Length; roleIndex++)
            {
                List<Army> armies = AWArmyService.GetRoleArmies(pKingdom,
                    SpecialRoles[roleIndex]);
                for (int i = 0; i < armies.Count; i++)
                {
                    Army army = armies[i];
                    if (army?.data == null || !army.isAlive()) continue;
                    long armyId = army.id;
                    DeferredRuntimeWorkService.EnqueueCoalesced(
                        DeferredRuntimeWorkRules.CoalescingKey(
                            "mandate_military_army", armyId),
                        DeferredWorkClass.Runtime,
                        () => ReconcileArmy(FindArmy(armyId)));
                }
            }
        }

        private static void ReconcileCityArmy(long pCityId)
        {
            City city;
            try { city = World.world?.cities?.get(pCityId); }
            catch { city = null; }
            if (city?.data == null || city.isRekt() || !city.hasArmy()) return;
            ReconcileArmy(city.getArmy());
        }

        private static void ReconcileArmy(Army pArmy)
        {
            if (pArmy?.data == null || !pArmy.isAlive()) return;
            try
            {
                foreach (Actor actor in pArmy.getUnits())
                    ReconcileWarrior(actor);
            }
            catch { }
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static int CountPhaseStatuses(
            IReadOnlyDictionary<string, Status> pStatuses)
        {
            if (pStatuses == null || pStatuses.Count == 0) return 0;
            int count = 0;
            if (pStatuses.ContainsKey(MandateMilitaryPhaseRules.GoldenStatusId)) count++;
            if (pStatuses.ContainsKey(MandateMilitaryPhaseRules.DeclineStatusId)) count++;
            if (pStatuses.ContainsKey(MandateMilitaryPhaseRules.ChaosStatusId)) count++;
            if (pStatuses.ContainsKey(MandateMilitaryPhaseRules.RenewalStatusId)) count++;
            return count;
        }
    }
}
