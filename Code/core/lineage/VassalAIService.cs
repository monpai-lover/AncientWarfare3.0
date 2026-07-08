using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class VassalAIService
    {
        private const string LAST_CHECK_YEAR = "aw_vassal_ai_last_check_year";
        private const string LAST_ACTION_YEAR = "aw_vassal_ai_last_action_year";
        private const int CHECK_INTERVAL = 5;
        private const int ACTION_COOLDOWN = 12;
        private static readonly Random Rng = new Random();

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!CanRunFor(pKingdom)) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LAST_CHECK_YEAR, out int lastCheck, -99999);
            if (year - lastCheck < CHECK_INTERVAL) return;
            pKingdom.data.set(LAST_CHECK_YEAR, year);

            pKingdom.data.get(LAST_ACTION_YEAR, out int lastAction, -99999);
            if (year - lastAction < ACTION_COOLDOWN) return;

            if (TryActiveVassal(pKingdom) ||
                TryIndependenceWar(pKingdom) ||
                TryAbsorbVassal(pKingdom) ||
                TryVassalWar(pKingdom))
            {
                pKingdom.data.set(LAST_ACTION_YEAR, year);
            }
        }

        private static bool TryActiveVassal(Kingdom pKingdom)
        {
            if (VassalService.IsVassalKingdom(pKingdom)) return false;
            Kingdom threat = FindThreat(pKingdom);
            if (threat == null) return false;

            Kingdom suzerain = FindBestSuzerain(pKingdom, threat);
            if (suzerain == null) return false;
            if (!Chance(0.45f)) return false;

            return string.IsNullOrEmpty(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision)) &&
                   KingdomPolicyService.StartDecisionWithTarget(pKingdom, "aw_decision_seek_suzerain", suzerain);
        }

        private static bool TryVassalWar(Kingdom pKingdom)
        {
            if (VassalService.IsVassalKingdom(pKingdom)) return false;
            if (VassalService.GetVassals(pKingdom, pRecursive: true).Count >= 6) return false;
            if (CountCities(pKingdom) < 2) return false;
            if (!Chance(0.22f)) return false;

            Kingdom target = FindVassalWarTarget(pKingdom);
            if (target == null) return false;
            return StartWar(pKingdom, target, "vassal_war");
        }

        private static bool TryAbsorbVassal(Kingdom pKingdom)
        {
            List<Kingdom> vassals = VassalService.GetVassals(pKingdom);
            if (vassals.Count == 0) return false;
            if (!Chance(0.16f)) return false;

            foreach (Kingdom vassal in vassals.OrderBy(v => VassalService.GetPowerScore(v, pIncludeVassals: false)))
            {
                if (vassal?.data == null || vassal.isRekt() || vassal.hasEnemies()) continue;
                int years = VassalService.GetYearsSinceRelationStarted(vassal);
                if (years >= 0 && years < 30) continue;

                float lordPower = VassalService.GetPowerScore(pKingdom, pIncludeVassals: false);
                float vassalPower = Math.Max(1f, VassalService.GetPowerScore(vassal, pIncludeVassals: true));
                if (lordPower < vassalPower * 1.55f) continue;

                return string.IsNullOrEmpty(KingdomPolicyService.GetCurrent(pKingdom, PolicyNodeKind.Decision)) &&
                       KingdomPolicyService.StartDecisionWithTarget(pKingdom, "aw_decision_absorb_vassal", vassal);
            }

            return false;
        }

        private static bool TryIndependenceWar(Kingdom pKingdom)
        {
            if (!VassalService.IsVassalKingdom(pKingdom)) return false;
            Kingdom suzerain = VassalService.GetSuzerain(pKingdom);
            if (suzerain?.data == null || suzerain.isRekt()) return VassalService.EndVassal(pKingdom, "suzerain_missing");
            if (pKingdom.hasEnemies()) return false;

            int years = VassalService.GetYearsSinceRelationStarted(pKingdom);
            float own = VassalService.GetPowerScore(pKingdom, pIncludeVassals: true);
            float lord = Math.Max(1f, VassalService.GetPowerScore(suzerain, pIncludeVassals: false));
            int opinion = Opinion(pKingdom, suzerain);
            if (!VassalIndependenceRules.ShouldAttemptIndependence(own, lord, years, opinion,
                    (float)Rng.NextDouble()))
                return false;

            return StartWar(pKingdom, suzerain, "independence_war");
        }

        private static Kingdom FindThreat(Kingdom pKingdom)
        {
            float own = Math.Max(1f, VassalService.GetPowerScore(pKingdom, pIncludeVassals: false));
            Kingdom best = null;
            float bestPower = 0f;

            foreach (Kingdom other in CandidateKingdoms(pKingdom))
            {
                if (other == pKingdom) continue;
                if (VassalService.GetRootSuzerain(other) == pKingdom) continue;
                float power = VassalService.GetPowerScore(other, pIncludeVassals: true);
                bool hostile = pKingdom.isEnemy(other) || Opinion(pKingdom, other) <= -80;
                if (!hostile && !AreNeighbors(pKingdom, other)) continue;
                if (power < own * 1.6f) continue;
                if (power <= bestPower) continue;
                best = other;
                bestPower = power;
            }

            return best;
        }

        private static Kingdom FindBestSuzerain(Kingdom pKingdom, Kingdom pThreat)
        {
            float own = Math.Max(1f, VassalService.GetPowerScore(pKingdom, pIncludeVassals: false));
            Kingdom best = null;
            float bestScore = 0f;

            foreach (Kingdom other in CandidateKingdoms(pKingdom))
            {
                if (other == pKingdom || other == pThreat) continue;
                if (!VassalService.CanSetVassal(pKingdom, other)) continue;
                if (other.isEnemy(pKingdom) || pKingdom.isEnemy(other)) continue;

                float power = VassalService.GetPowerScore(other, pIncludeVassals: true);
                if (power < own * 1.9f) continue;

                int opinion = Opinion(pKingdom, other);
                if (opinion < -25) continue;

                float distanceScore = AreNeighbors(pKingdom, other) ? 60f : 0f;
                float score = power + opinion * 2f + distanceScore;
                if (score <= bestScore) continue;
                bestScore = score;
                best = other;
            }

            return best;
        }

        private static Kingdom FindVassalWarTarget(Kingdom pKingdom)
        {
            float own = VassalService.GetPowerScore(pKingdom, pIncludeVassals: true);
            Kingdom best = null;
            float bestScore = 0f;
            KingdomTitle ownTitle = KingdomTitleService.GetTitle(pKingdom);

            foreach (Kingdom other in CandidateKingdoms(pKingdom))
            {
                if (other == pKingdom || other.isRekt() || other.hasEnemies()) continue;
                if (VassalService.IsVassalKingdom(other) || VassalService.IsSuzerain(other)) continue;
                if (VassalService.GetRootSuzerain(other) == VassalService.GetRootSuzerain(pKingdom)) continue;
                if (KingdomTitleService.GetTitle(other) > ownTitle) continue;
                if (!AreNeighbors(pKingdom, other)) continue;

                float target = Math.Max(1f, VassalService.GetPowerScore(other, pIncludeVassals: true));
                if (own < target * 1.35f) continue;

                float score = target + Math.Max(0, -Opinion(pKingdom, other));
                if (score <= bestScore) continue;
                bestScore = score;
                best = other;
            }

            return best;
        }

        private static bool StartWar(Kingdom pAttacker, Kingdom pDefender, string pWarType)
        {
            if (pAttacker?.data == null || pDefender?.data == null) return false;
            if (pAttacker == pDefender || pAttacker.hasEnemies() || pDefender.hasEnemies()) return false;
            if (!string.IsNullOrEmpty(KingdomPolicyService.GetCurrent(pAttacker, PolicyNodeKind.Decision)))
                return false;

            try
            {
                string goal = pWarType == "independence_war"
                    ? WarTerritoryService.GOAL_INDEPENDENCE
                    : WarTerritoryService.GOAL_FORCE_VASSAL;
                string label = pWarType == "independence_war" ? "\u8131\u79bb\u5b97\u4e3b" : "\u5f3a\u5236\u81e3\u670d";
                return KingdomPolicyService.StartWarDecision(pAttacker, pDefender, goal, null, pWarType, pWarType, label);
            }
            catch { return false; }
        }

        private static IEnumerable<Kingdom> CandidateKingdoms(Kingdom pKingdom)
        {
            if (World.world?.kingdoms == null) yield break;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv() || kingdom.isNeutral()) continue;
                if (kingdom == pKingdom) continue;
                yield return kingdom;
            }
        }

        private static bool AreNeighbors(Kingdom pA, Kingdom pB)
        {
            try
            {
                foreach (City city in pA.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    foreach (Kingdom neighbor in city.neighbours_kingdoms)
                        if (neighbor == pB) return true;
                }
            }
            catch { }

            return false;
        }

        private static bool CanRunFor(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return false;
            if (!pKingdom.hasKing()) return false;
            if (CountCities(pKingdom) <= 0) return false;
            return KingdomPolicyService.CanUsePolicySystem(pKingdom) || LineageService.IsXiaKingdom(pKingdom);
        }

        private static int CountCities(Kingdom pKingdom)
        {
            try { return pKingdom.countCities(); }
            catch { return 0; }
        }

        private static int Opinion(Kingdom pMain, Kingdom pTarget)
        {
            try { return World.world.diplomacy.getOpinion(pMain, pTarget).total; }
            catch { return 0; }
        }

        private static bool Chance(float pChance)
        {
            return Rng.NextDouble() < pChance;
        }
    }
}
