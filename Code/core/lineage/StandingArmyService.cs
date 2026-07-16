using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class StandingArmyService
    {
        private sealed class Candidate
        {
            public Actor Actor;
            public float Score;
        }

        public static void MaintainCity(City pCity)
        {
            if (!IsValidCity(pCity)) return;
            int core = StandingArmyRules.PeacetimeCore(pCity.status.warrior_slots);
            List<Actor> standing = CollectOrdinaryStanding(pCity);

            if (standing.Count > core)
            {
                ReduceSurplus(standing, standing.Count - core);
                return;
            }

            List<Candidate> candidates = CollectCandidates(pCity);
            if (standing.Count < core)
            {
                AppointCandidates(pCity, candidates, core - standing.Count);
                return;
            }

            ReplaceWeakestIfBetter(pCity, standing, candidates);
        }

        public static int CountOrdinaryStanding(City pCity)
        {
            return CollectOrdinaryStanding(pCity).Count;
        }

        private static bool IsValidCity(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            return pCity?.data != null && !pCity.isRekt() &&
                   kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral();
        }

        private static List<Actor> CollectOrdinaryStanding(City pCity)
        {
            var result = new List<Actor>();
            if (pCity?.data == null || !pCity.hasArmy()) return result;
            Army army = pCity.getArmy();
            if (army?.data == null || AWArmyService.IsSpecialArmy(army)) return result;

            foreach (Actor actor in army.getUnits())
            {
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                if (!actor.isWarrior() || actor.army != army) continue;
                actor.data.get(LineageKeys.TEMPORARY_LEVY, out bool levy, false);
                if (levy || RoyalGuardService.IsRoyalGuard(actor) || SlaveService.IsSlave(actor)) continue;
                result.Add(actor);
            }
            return result;
        }

        private static List<Candidate> CollectCandidates(City pCity)
        {
            var result = new List<Candidate>(StandingArmyRules.MaxAppointmentsPerPass + 1);
            pCity.data.get(LineageKeys.STANDING_ARMY_SCAN_CURSOR, out int cursor, 0);
            if (cursor < 0) cursor = 0;

            int skipped = 0;
            int scanned = 0;
            bool complete = true;
            foreach (Actor actor in pCity.getUnits())
            {
                if (skipped++ < cursor) continue;
                if (scanned >= StandingArmyRules.MaxCandidateScan)
                {
                    complete = false;
                    break;
                }
                scanned++;
                if (!IsCandidate(pCity, actor)) continue;
                AddBoundedBest(result, new Candidate { Actor = actor, Score = Score(actor) },
                    StandingArmyRules.MaxAppointmentsPerPass + StandingArmyRules.MaxReplacementsPerPass);
            }

            pCity.data.set(LineageKeys.STANDING_ARMY_SCAN_CURSOR, complete ? 0 : cursor + scanned);
            result.Sort(CompareBestFirst);
            return result;
        }

        private static bool IsCandidate(City pCity, Actor pActor)
        {
            if (pActor?.data == null || pActor.city != pCity || pActor.kingdom != pCity.kingdom) return false;
            if (pActor.isRekt() || !pActor.isAlive() || !pActor.isAdult() || pActor.asset?.is_boat == true)
                return false;
            if (!pActor.isProfession(UnitProfession.Unit)) return false;
            if (pActor.isKing() || pActor.isCityLeader() || GeneralService.IsActiveGeneralFast(pActor)) return false;
            if (HeirService.IsCurrentHeir(pCity.kingdom, pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor) || SlaveService.IsSlave(pActor) ||
                SlaveService.IsRetiredSoldier(pActor) || RoyalAsylumService.IsActive(pActor)) return false;
            if (!HistoricalMasterVocationService.CanEnter(pActor, HistoricalMasterMilitaryContext.OrdinaryWarrior))
                return false;

            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            if (!string.IsNullOrEmpty(office) && layer != CourtOfficeLayer.Military) return false;

            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.StandingArmy))
                return pCity.checkCanMakeWarrior(pActor);
        }

        private static void ReduceSurplus(List<Actor> pStanding, int pSurplus)
        {
            pStanding.Sort(CompareWeakestFirst);
            int count = System.Math.Min(System.Math.Min(pSurplus, pStanding.Count),
                StandingArmyRules.MaxReductionsPerPass);
            for (int i = 0; i < count; i++) DemoteWithoutRetirement(pStanding[i]);
        }

        private static void AppointCandidates(City pCity, List<Candidate> pCandidates, int pShortage)
        {
            int count = System.Math.Min(System.Math.Min(pShortage, pCandidates.Count),
                StandingArmyRules.MaxAppointmentsPerPass);
            for (int i = 0; i < count; i++) Appoint(pCity, pCandidates[i].Actor);
        }

        private static void ReplaceWeakestIfBetter(City pCity, List<Actor> pStanding,
            List<Candidate> pCandidates)
        {
            if (pStanding.Count == 0 || pCandidates.Count == 0 ||
                StandingArmyRules.MaxReplacementsPerPass <= 0) return;
            pStanding.Sort(CompareWeakestFirst);
            Actor weakest = pStanding[0];
            Candidate strongest = pCandidates[0];
            float weakestScore = Score(weakest);
            if (strongest.Score < weakestScore) return;
            if (strongest.Score == weakestScore && strongest.Actor.data.id > weakest.data.id) return;

            DemoteWithoutRetirement(weakest);
            Appoint(pCity, strongest.Actor);
        }

        private static void Appoint(City pCity, Actor pActor)
        {
            if (pCity?.data == null || pActor?.data == null || pActor.isWarrior()) return;
            using (MilitaryRecruitmentScope.Open(MilitaryRecruitmentKind.StandingArmy))
            {
                if (!pCity.checkCanMakeWarrior(pActor)) return;
                pCity.makeWarrior(pActor);
            }
        }

        private static void DemoteWithoutRetirement(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isWarrior()) return;
            pActor.stopBeingWarrior();
            pActor.data.set(LineageKeys.SOLDIER_SERVICE_START_TIME, -1f);
        }

        private static void AddBoundedBest(List<Candidate> pCandidates, Candidate pCandidate, int pLimit)
        {
            if (pCandidate?.Actor?.data == null || pLimit <= 0) return;
            if (pCandidates.Count < pLimit)
            {
                pCandidates.Add(pCandidate);
                return;
            }

            int weakest = 0;
            for (int i = 1; i < pCandidates.Count; i++)
                if (CompareBestFirst(pCandidates[weakest], pCandidates[i]) < 0)
                    weakest = i;
            if (CompareBestFirst(pCandidate, pCandidates[weakest]) < 0)
                pCandidates[weakest] = pCandidate;
        }

        private static int CompareBestFirst(Candidate pLeft, Candidate pRight)
        {
            int score = pRight.Score.CompareTo(pLeft.Score);
            return score != 0 ? score : pLeft.Actor.data.id.CompareTo(pRight.Actor.data.id);
        }

        private static int CompareWeakestFirst(Actor pLeft, Actor pRight)
        {
            int score = Score(pLeft).CompareTo(Score(pRight));
            return score != 0 ? score : pRight.data.id.CompareTo(pLeft.data.id);
        }

        private static float Score(Actor pActor)
        {
            return StandingArmyRules.MilitaryScore(
                SafeStat(pActor, "damage"),
                SafeStat(pActor, "warfare"),
                SafeStat(pActor, "health"),
                SafeStat(pActor, "armor"),
                SafeStat(pActor, "speed"));
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor?.stats?[pKey] ?? 0f; }
            catch { return 0f; }
        }
    }
}
