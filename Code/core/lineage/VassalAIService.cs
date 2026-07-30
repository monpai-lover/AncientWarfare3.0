using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class VassalAIService
    {
        private const string LAST_CHECK_YEAR = "aw_vassal_ai_last_check_year";
        private const string LAST_ACTION_YEAR = "aw_vassal_ai_last_action_year";
        private const int CHECK_INTERVAL = 4;
        private const int ACTION_COOLDOWN = 8;
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
            CourtSnapshot court = CourtService.GetSnapshot(pKingdom);

            bool isSubject = VassalService.GetDiplomaticSuzerain(pKingdom)?.data != null;
            isSubject |= VassalService.GetSuzerainId(pKingdom) >= 0 ||
                         VassalService.GetTributarySuzerainId(pKingdom) >= 0;
            bool acted = isSubject
                ? TryIndependenceWar(pKingdom)
                : TryAbsorbVassal(pKingdom, court) ||
                  TryVassalWar(pKingdom, court) ||
                  TryActiveVassal(pKingdom, court);
            if (acted)
            {
                pKingdom.data.set(LAST_ACTION_YEAR, year);
            }
        }

        private static bool TryActiveVassal(Kingdom pKingdom, CourtSnapshot pCourt)
        {
            if (VassalService.IsVassalKingdom(pKingdom)) return false;
            Kingdom threat = FindThreat(pKingdom);
            if (threat == null) return false;

            Kingdom suzerain = FindBestSuzerain(pKingdom, threat);
            if (suzerain == null) return false;
            if (!Chance(0.45f * CourtDirectionRules.VoluntaryDiplomacyMultiplier(
                    pCourt?.peace ?? 0.5f))) return false;

            return KingdomPolicyService.StartDecisionWithTarget(
                pKingdom, "aw_decision_seek_suzerain", suzerain);
        }

        private static bool TryVassalWar(Kingdom pKingdom, CourtSnapshot pCourt)
        {
            if (VassalService.IsVassalKingdom(pKingdom)) return false;
            int vassalSoftCap = CourtInstitutionEffectService.Read(pKingdom).
                VassalSoftCap;
            int subjectCount = VassalService.GetVassals(pKingdom,
                pRecursive: true).Count;
            if (CountCities(pKingdom) < 2) return false;
            if (!Chance(0.22f * CourtDirectionRules.ForcedVassalMultiplier(
                    pCourt?.aggression ?? 0.5f))) return false;

            Kingdom target = FindVassalWarTarget(pKingdom, subjectCount,
                vassalSoftCap);
            if (target == null) return false;
            return StartSubjugationWar(pKingdom, target, pCourt);
        }

        private static bool TryAbsorbVassal(Kingdom pKingdom, CourtSnapshot pCourt)
        {
            List<Kingdom> vassals = VassalService.GetVassals(pKingdom);
            if (vassals.Count == 0) return false;

            float lordPower = VassalService.GetPowerScore(pKingdom,
                pIncludeVassals: false);

            foreach (Kingdom vassal in vassals.OrderBy(v => VassalService.GetPowerScore(v, pIncludeVassals: false)))
            {
                if (vassal?.data == null || vassal.isRekt() || vassal.hasEnemies()) continue;
                int years = VassalService.GetYearsSinceRelationStarted(vassal);
                float vassalPower = Math.Max(1f, VassalService.GetPowerScore(vassal, pIncludeVassals: true));
                VassalEffectiveTerms terms =
                    VassalService.GetEffectiveRelationTerms(vassal);
                if (!VassalAIActionRules.ShouldAttemptAbsorption(years,
                        lordPower / vassalPower, terms.Autonomy,
                        pCourt?.aggression ?? 0.5f)) continue;

                if (!DiplomaticOperationService.HasActiveSpyNetwork(
                        pKingdom, vassal, out _, out _))
                {
                    DiplomaticOperationPreview preview =
                        DiplomaticOperationService.PrepareSpyNetwork(
                            pKingdom, vassal);
                    if (preview.Reason == "covert_operation_pending")
                        return true;
                    return DiplomaticOperationService.TryStartSpyNetwork(
                        pKingdom, vassal, pPlayerInitiated: false,
                        out _, out _);
                }

                return KingdomPolicyService.StartDecisionWithTarget(
                    pKingdom, "aw_decision_absorb_vassal", vassal);
            }

            return false;
        }

        private static bool TryIndependenceWar(Kingdom pKingdom)
        {
            Kingdom suzerain = VassalService.GetDiplomaticSuzerain(pKingdom);
            if (suzerain?.data == null || suzerain.isRekt()) return VassalService.EndVassal(pKingdom, "suzerain_missing");
            if (pKingdom.hasEnemies()) return false;

            int years = VassalService.GetYearsSinceRelationStarted(pKingdom);
            float own = VassalService.GetWarPowerScore(pKingdom,
                pIncludeVassals: true);
            float lord = Math.Max(1f, VassalService.GetWarPowerScore(
                suzerain, pIncludeVassals: false));
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
                if (!KingdomAdjacency.AreDirectNeighbors(pKingdom, other)) continue;
                if (!VassalService.CanSetVassal(pKingdom, other)) continue;
                if (other.isEnemy(pKingdom) || pKingdom.isEnemy(other)) continue;

                float power = VassalService.GetPowerScore(other, pIncludeVassals: true);
                if (power < own * 1.9f) continue;

                int opinion = Opinion(pKingdom, other);
                if (opinion < -25) continue;

                float score = power + opinion * 2f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = other;
            }

            return best;
        }

        private static Kingdom FindVassalWarTarget(Kingdom pKingdom,
            int pSubjectCount, int pSubjectSoftCap)
        {
            float own = VassalService.GetWarPowerScore(pKingdom,
                pIncludeVassals: true);
            Kingdom best = null;
            float bestScore = 0f;
            KingdomTitle ownTitle = KingdomTitleService.GetTitle(pKingdom);

            foreach (Kingdom other in BoundedBorderKingdoms(pKingdom))
            {
                if (other == pKingdom || other.isRekt() || other.hasEnemies()) continue;
                bool targetIndependent = VassalService.GetDiplomaticSuzerain(
                    other)?.data == null;
                if (!targetIndependent) continue;
                if (VassalService.GetRootSuzerain(other) == VassalService.GetRootSuzerain(pKingdom)) continue;
                bool vassalTitleBlocked = KingdomTitleService.GetTitle(other) >=
                                          ownTitle;
                bool tributaryTitleAllowed = VassalContractTierRules.
                    CanInitiateForcedTributary((int)ownTitle);
                if (vassalTitleBlocked && !tributaryTitleAllowed) continue;

                float target = Math.Max(1f,
                    VassalService.GetWarPowerScore(other,
                        pIncludeVassals: true));
                if (own < target * 1.25f) continue;
                bool diplomaticBlocked = DiplomacyProposalService.
                    HasActiveWarBlocker(pKingdom, other);
                if (!WarAiGoalSelectionRules.ShouldLaunchDedicatedSubjugationWar(
                        ResolvePeopleRelation(pKingdom, other),
                        directlyAdjacent: true, attackerIsSubject: false,
                        targetIndependent, diplomaticBlocked, own / target))
                    continue;

                float score = target + Math.Max(0, -Opinion(pKingdom, other));
                if (score <= bestScore) continue;
                bestScore = score;
                best = other;
            }

            return best;
        }

        private static IEnumerable<Kingdom> BoundedBorderKingdoms(
            Kingdom pKingdom)
        {
            const int maxCandidates = 24;
            var seen = new HashSet<long>();
            int yielded = 0;
            IEnumerable<City> cities;
            try { cities = pKingdom?.getCities() ?? Enumerable.Empty<City>(); }
            catch { yield break; }
            foreach (City city in cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                foreach (Kingdom other in city.neighbours_kingdoms)
                {
                    if (other?.data == null || other == pKingdom ||
                        other.isRekt() || !other.isCiv() ||
                        other.isNeutral() || !seen.Add(other.id)) continue;
                    yield return other;
                    yielded++;
                    if (yielded >= maxCandidates) yield break;
                }
            }
        }

        private static WarAiPeopleRelation ResolvePeopleRelation(
            Kingdom pSource, Kingdom pTarget)
        {
            string sourceSpecies = "";
            string targetSpecies = "";
            try { sourceSpecies = pSource?.getActorAsset()?.id ?? ""; }
            catch { }
            try { targetSpecies = pTarget?.getActorAsset()?.id ?? ""; }
            catch { }
            return WarAiGoalSelectionRules.ResolvePeopleRelation(
                sourceSpecies, targetSpecies,
                pSource?.culture?.data?.id ?? -1L,
                pTarget?.culture?.data?.id ?? -1L,
                LineageService.IsXiaKingdom(pSource),
                LineageService.IsXiaKingdom(pTarget));
        }

        private static bool StartWar(Kingdom pAttacker, Kingdom pDefender, string pWarType)
        {
            if (pAttacker?.data == null || pDefender?.data == null) return false;
            if (pAttacker == pDefender || pAttacker.hasEnemies() || pDefender.hasEnemies()) return false;
            if (pWarType != "independence_war" &&
                !KingdomAdjacency.AreDirectNeighbors(pAttacker, pDefender)) return false;
            if (DiplomaticWarDeclarationService.HasPending(pAttacker))
                return false;

            try
            {
                string goal = pWarType == "independence_war"
                    ? WarTerritoryService.GOAL_INDEPENDENCE
                    : WarTerritoryService.GOAL_FORCE_VASSAL;
                string label = pWarType == "independence_war" ? "\u8131\u79bb\u5b97\u4e3b" : "\u5f3a\u5236\u81e3\u670d";
                return DiplomaticWarDeclarationService.Issue(pAttacker,
                    pDefender, goal, null, pWarType, pWarType, label);
            }
            catch { return false; }
        }

        private static bool StartSubjugationWar(Kingdom pAttacker,
            Kingdom pDefender, CourtSnapshot pCourt)
        {
            float own = Math.Max(1f, VassalService.GetWarPowerScore(
                pAttacker, pIncludeVassals: true));
            float target = Math.Max(1f, VassalService.GetWarPowerScore(
                pDefender, pIncludeVassals: true));
            float expansionism = Math.Max(0f, Math.Min(1f,
                ((pCourt?.war ?? .5f) + (pCourt?.aggression ?? .5f) -
                 (pCourt?.peace ?? .5f)) * .5f));
            var context = new WarAiGoalContext(
                directlyAdjacent: KingdomAdjacency.AreDirectNeighbors(
                    pAttacker, pDefender), attackerIsSubject: false,
                targetIsIndependent: VassalService.GetDiplomaticSuzerain(
                    pDefender)?.data == null,
                diplomaticBlocked: DiplomacyProposalService
                    .HasActiveWarBlocker(pAttacker, pDefender),
                attackerToTargetPowerRatio: own / target,
                targetCityCount: CountCities(pDefender),
                attackerCentralization: CentralizationService
                    .ReadSnapshot(pAttacker).effective_level,
                attackerExpansionism: expansionism,
                courtWar: pCourt?.war ?? .5f,
                courtPeace: pCourt?.peace ?? .5f,
                attackerTitleRank: (int)KingdomTitleService.GetTitle(
                    pAttacker),
                targetTitleRank: (int)KingdomTitleService.GetTitle(
                    pDefender));
            WarAiPeopleRelation relation = ResolvePeopleRelation(pAttacker,
                pDefender);
            int currentSubjectCount = VassalService.GetVassals(pAttacker,
                pRecursive: true).Count;
            int subjectSoftCap = CourtInstitutionEffectService.Read(pAttacker)
                .VassalSoftCap;
            context = new WarAiGoalContext(
                directlyAdjacent: context.DirectlyAdjacent,
                attackerIsSubject: context.AttackerIsSubject,
                targetIsIndependent: context.TargetIsIndependent,
                diplomaticBlocked: context.DiplomaticBlocked,
                attackerToTargetPowerRatio:
                    context.AttackerToTargetPowerRatio,
                targetCityCount: context.TargetCityCount,
                attackerCentralization: context.AttackerCentralization,
                attackerExpansionism: context.AttackerExpansionism,
                courtWar: context.CourtWar, courtPeace: context.CourtPeace,
                currentSubjectCount: currentSubjectCount,
                subjectSoftCap: subjectSoftCap,
                attackerTitleRank: context.AttackerTitleRank,
                targetTitleRank: context.TargetTitleRank);
            var candidates = new List<WarAiGoalCandidate>();
            var optionsByGoal = new Dictionary<string,
                WarTerritoryService.WarTargetOption>(StringComparer.Ordinal);
            foreach (WarTerritoryService.WarTargetOption option in
                     WarTerritoryService.BuildTargetOptions(pAttacker,
                         pDefender))
            {
                if (option == null) continue;
                if (!DiplomaticWarDeclarationService.CanIssue(pAttacker,
                        option, out _)) continue;
                candidates.Add(new WarAiGoalCandidate(option.goal_type,
                    option.score,
                    WarAiGoalSelectionRules.ObjectiveUrgency(
                        option.goal_type, option.score)));
                optionsByGoal[option.goal_type] = option;
            }
            string selectedGoal = WarAiGoalSelectionRules.SelectBestGoal(
                candidates, relation, context);
            if (selectedGoal != WarTerritoryService.GOAL_FORCE_VASSAL &&
                selectedGoal != WarTerritoryService.GOAL_FORCE_TRIBUTARY)
                return false;
            if (!optionsByGoal.TryGetValue(selectedGoal,
                    out WarTerritoryService.WarTargetOption selected))
                return false;
            if (ShouldPrepareTerritorialClaim(pAttacker, pDefender,
                    selected, relation, context)) return false;
            return DiplomaticWarDeclarationService.Issue(pAttacker,
                selected);
        }

        private static bool ShouldPrepareTerritorialClaim(Kingdom pSource,
            Kingdom pTarget,
            WarTerritoryService.WarTargetOption pImmediateOption,
            WarAiPeopleRelation pRelation, WarAiGoalContext pContext)
        {
            City city = WarTerritoryService.FindFirstFabricationTargetCity(
                pSource, pTarget);
            if (city?.data == null) return false;
            int population = 0;
            try { population = city.getPopulationPeople(); }
            catch { }
            int prospectiveScore = WarTargetSelectionRules.ScoreTarget(
                WarTerritoryService.GOAL_PRESS_CLAIM_CITY,
                pHasCore: false, pHasStrongClaim: false,
                pHasWeakClaim: true, pRestorationStrength: 0,
                pPopulation: population);
            return WarAiGoalSelectionRules.ShouldPreferTerritorialPreparation(
                pRelation, pContext, prospectiveScore,
                new WarAiGoalCandidate(pImmediateOption.goal_type,
                    pImmediateOption.score,
                    WarAiGoalSelectionRules.ObjectiveUrgency(
                        pImmediateOption.goal_type,
                        pImmediateOption.score)));
        }

        private static IEnumerable<Kingdom> CandidateKingdoms(Kingdom pKingdom)
        {
            foreach (Kingdom kingdom in BoundedBorderKingdoms(pKingdom))
                yield return kingdom;
        }

        private static bool AreNeighbors(Kingdom pA, Kingdom pB)
        {
            return KingdomAdjacency.AreDirectNeighbors(pA, pB);
        }

        private static bool CanRunFor(Kingdom pKingdom)
        {
            bool validCivilizedRealm = pKingdom?.data != null &&
                                        !pKingdom.isRekt() &&
                                        pKingdom.isCiv() &&
                                        !pKingdom.isNeutral();
            return VassalAIActionRules.CanEvaluateRealm(
                validCivilizedRealm,
                pKingdom?.hasKing() == true,
                CountCities(pKingdom));
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
