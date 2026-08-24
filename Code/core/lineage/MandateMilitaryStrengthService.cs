using System;

namespace AncientWarfare3.core.lineage
{
#if !AW3_RULES_TESTS
    internal static class MandateMilitaryStrengthService
    {
        private const int DefaultTarget = MandateMilitaryStrengthRules.MaximumArmyTarget;
        private static long _lastKingdomId = -1L;
        private static int _lastRecruitYear = int.MinValue;
        private static int _lastDiagnosticYear = int.MinValue;

        internal static void ProcessAuthorityCycle()
        {
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null || mandate.isRekt() ||
                // A war notice is only preparation.  Emergency enlistment
                // starts after a real active war has entered the runtime
                // index, so preparation cannot consume actors repeatedly.
                !MilitaryEmergencyService.TryGetActiveWarId(mandate,
                    out _)) return;

            int current = CountWarriors(mandate);
            if (!MandateMilitaryStrengthRules.ShouldRecruit(true, true,
                    current, DefaultTarget)) return;
            int year = Date.getCurrentYear();
            if (_lastKingdomId == mandate.id && _lastRecruitYear == year)
                return;
            _lastKingdomId = mandate.id;
            _lastRecruitYear = year;
            int remaining = MandateMilitaryStrengthRules.RemainingTarget(
                current, DefaultTarget);
            int recruited = RecruitFromLocalCities(mandate, remaining);
            if (recruited > 0)
            {
                WarNoticeService.QueueArmyChanged(mandate, null,
                    pRosterExpanded: true);
            }
            else if (_lastDiagnosticYear != year)
            {
                _lastDiagnosticYear = year;
                ModClass.LogWarning("Mandate emergency recruitment stopped: " +
                    (current >= DefaultTarget
                        ? "target_reached"
                        : "no_native_local_candidates"));
            }
        }

        internal static void ClearRuntime()
        {
            _lastKingdomId = -1L;
            _lastRecruitYear = int.MinValue;
            _lastDiagnosticYear = int.MinValue;
        }

        private static int CountWarriors(Kingdom pKingdom)
        {
            int count = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                    count = SaturatingAdd(count, city?.countWarriors() ?? 0);
            }
            catch { }
            return count;
        }

        private static int RecruitFromLocalCities(Kingdom pKingdom,
            int pRemaining)
        {
            if (pRemaining <= 0) return 0;
            int recruited = 0;
            int inspected = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom || city.units == null)
                        continue;
                    for (int i = 0; i < city.units.Count &&
                         recruited < pRemaining &&
                         inspected < MandateMilitaryStrengthRules.
                             MaximumNativeCandidatesPerCycle; i++)
                    {
                        Actor actor = city.units[i];
                        inspected++;
                        if (!CanRecruit(city, actor)) continue;
                        using (MilitaryRecruitmentScope.Open(
                                   MilitaryRecruitmentKind.MandateEmergency))
                        {
                            if (!city.checkCanMakeWarrior(actor)) continue;
                            city.makeWarrior(actor);
                        }
                        if (actor.isWarrior()) recruited++;
                    }
                    if (inspected >= MandateMilitaryStrengthRules.
                            MaximumNativeCandidatesPerCycle)
                        break;
                }
            }
            catch { }
            return recruited;
        }

        private static bool CanRecruit(City pCity, Actor pActor)
        {
            if (pActor?.data == null || pCity?.data == null) return false;
            bool alive = false;
            bool adult = false;
            bool sameKingdom = false;
            bool king = false;
            bool leader = false;
            bool heir = false;
            bool royalGuard = false;
            bool warrior = false;
            bool hasArmy = false;
            try
            {
                alive = pActor.isAlive() && !pActor.isRekt();
                adult = pActor.isAdult();
                sameKingdom = pActor.kingdom == pCity.kingdom;
                king = pActor.isKing();
                leader = pActor.isCityLeader();
                heir = HeirService.IsCurrentHeir(pCity.kingdom, pActor);
                royalGuard = RoyalGuardService.IsRoyalGuard(pActor);
                warrior = pActor.isWarrior();
                hasArmy = pActor.army?.data != null;
            }
            catch { }
            return MandateMilitaryStrengthRules.CanUseNativeCandidate(
                valid: true, alive, adult, sameKingdom, king, heir, leader,
                royalGuard, warrior, hasArmy);
        }

        private static int SaturatingAdd(int pLeft, int pRight)
        {
            if (pRight <= 0) return Math.Max(0, pLeft);
            return pLeft > int.MaxValue - pRight
                ? int.MaxValue
                : pLeft + pRight;
        }
    }
#endif
}
