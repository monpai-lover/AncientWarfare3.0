using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public readonly struct PosthumousTitleDecision
    {
        public readonly string Name;
        public readonly string QualificationKey;
        public readonly string GradeKey;
        public readonly string DominantKey;
        public readonly string Reason;
        public readonly int Civil;
        public readonly int Territory;
        public readonly int War;
        public readonly int Order;
        public readonly int Ending;
        public readonly int Total;
        public readonly int CycleNo;

        public PosthumousTitleDecision(string pName, string pQualificationKey,
            string pGradeKey, string pDominantKey, string pReason,
            int pCivil, int pTerritory, int pWar, int pOrder, int pEnding,
            int pTotal, int pCycleNo)
        {
            Name = pName ?? "";
            QualificationKey = pQualificationKey ?? "";
            GradeKey = pGradeKey ?? "";
            DominantKey = pDominantKey ?? "";
            Reason = pReason ?? "";
            Civil = pCivil;
            Territory = pTerritory;
            War = pWar;
            Order = pOrder;
            Ending = pEnding;
            Total = pTotal;
            CycleNo = Math.Max(0, pCycleNo);
        }
    }

    public static class PosthumousTitleRules
    {
        private const string Taizu = "\u592a\u7956";
        private const string EmperorSuffix = "\u5e1d";
        private const string EmperorFullSuffix = "\u7687\u5e1d";

        private static readonly HashSet<string> MandateSplitChars = new HashSet<string>(
            new[] { "成", "康", "昭", "定", "烈", "穆", "庄", "襄", "献", "宪", "元" },
            StringComparer.Ordinal);

        private readonly struct Scores
        {
            public readonly int Civil;
            public readonly int Territory;
            public readonly int War;
            public readonly int Order;
            public readonly int Ending;
            public readonly int Total;
            public readonly PosthumousGrade Grade;
            public readonly PosthumousDimension Dominant;

            public Scores(int pCivil, int pTerritory, int pWar, int pOrder, int pEnding)
            {
                Civil = pCivil;
                Territory = pTerritory;
                War = pWar;
                Order = pOrder;
                Ending = pEnding;
                Total = pCivil + pTerritory + pWar + pOrder + pEnding;
                Grade = GradeFor(Total);
                Dominant = DominantFor(pCivil, pTerritory, pWar, pOrder, pEnding);
            }
        }

        private sealed class Candidate
        {
            public PosthumousTitleChar Definition;
            public int Eligibility;
            public int DimensionMatch;
            public int DimensionScore;
            public uint StableTie;
        }

        public static bool AllowsWen(RulerTitleFacts pFacts, RulerTitleDerivedFacts pDerived)
        {
            if (pFacts == null || pDerived == null) return false;
            RulerTraitFlags blocked = RulerTraitFlags.Hotheaded |
                                      RulerTraitFlags.Lustful |
                                      RulerTraitFlags.Ambitious |
                                      RulerTraitFlags.Greedy |
                                      RulerTraitFlags.Deceitful |
                                      RulerTraitFlags.Gluttonous |
                                      RulerTraitFlags.Paranoid |
                                      RulerTraitFlags.Evil |
                                      RulerTraitFlags.Psychopath |
                                      RulerTraitFlags.Bloodlust |
                                      RulerTraitFlags.Kingslayer |
                                      RulerTraitFlags.Madness |
                                      RulerTraitFlags.Stupid;
            if ((pFacts.Traits & blocked) != 0 || pDerived.GraveCrime) return false;
            if (pFacts.LostCapital || string.Equals(pFacts.EndReason, "kingdom_fell",
                    StringComparison.Ordinal))
                return false;

            Scores scores = Evaluate(pFacts, pDerived);
            return scores.Civil >= 0 || scores.Order >= 0;
        }

        public static bool AllowsWu(RulerTitleFacts pFacts, RulerTitleDerivedFacts pDerived)
        {
            if (pFacts == null || pDerived == null) return false;
            RulerTraitFlags blocked = RulerTraitFlags.Peaceful |
                                      RulerTraitFlags.Pacifist |
                                      RulerTraitFlags.Content |
                                      RulerTraitFlags.Slow |
                                      RulerTraitFlags.Weak |
                                      RulerTraitFlags.FragileHealth |
                                      RulerTraitFlags.Crippled |
                                      RulerTraitFlags.Stupid;
            if ((pFacts.Traits & blocked) != 0 || pDerived.Frail || pDerived.GraveCrime)
                return false;
            return pFacts.Health >= RulerTitleFactRules.LowStat &&
                   pFacts.Warfare >= RulerTitleFactRules.HighStat &&
                   pFacts.WarWins > 0 && pFacts.WarWins >= pFacts.WarLosses;
        }

        public static PosthumousTitleDecision Select(RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived, IEnumerable<string> pUsedNames,
            bool pMandateDouble, int pCycleNo = 0)
        {
            pFacts ??= new RulerTitleFacts();
            pDerived ??= RulerTitleFactRules.Derive(pFacts);
            Scores scores = Evaluate(pFacts, pDerived);
            List<Candidate> candidates = BuildCandidates(pFacts, pDerived, scores);
            if (candidates.Count == 0)
                candidates.Add(FallbackCandidate(pFacts.ActorId));

            var used = new HashSet<string>(StringComparer.Ordinal);
            if (pUsedNames != null)
            {
                foreach (string value in pUsedNames)
                {
                    string normalized = (value ?? "").Trim();
                    if (normalized.Length > 0) used.Add(normalized);
                }
            }

            List<string> orderedNames = pMandateDouble
                ? BuildMandateNames(candidates, pFacts, pDerived, scores)
                : candidates.Select(pCandidate => pCandidate.Definition.Char).ToList();
            if (orderedNames.Count == 0)
                orderedNames.Add(pMandateDouble ? "平安" : "平");

            string selected = orderedNames.FirstOrDefault(pName => !used.Contains(pName));
            int cycleNo = Math.Max(0, pCycleNo);
            if (string.IsNullOrEmpty(selected))
            {
                selected = orderedNames[0];
                cycleNo++;
            }

            string reason = "civil=" + scores.Civil +
                            ";territory=" + scores.Territory +
                            ";war=" + scores.War +
                            ";order=" + scores.Order +
                            ";ending=" + scores.Ending;
            return new PosthumousTitleDecision(
                selected,
                "posthumous_qualification_" + string.Join("_", selected.ToCharArray()),
                GradeKey(scores.Grade),
                DimensionKey(scores.Dominant),
                reason,
                scores.Civil,
                scores.Territory,
                scores.War,
                scores.Order,
                scores.Ending,
                scores.Total,
                cycleNo);
        }

        public static string BuildRankedAppellation(string pStateName,
            string pPosthumousName, int pHighestTitle)
        {
            string suffix = pHighestTitle switch
            {
                0 => "伯",
                1 => "侯",
                2 => "公",
                3 => "王",
                4 => "帝",
                _ => "君"
            };
            return (pStateName ?? "").Trim() +
                   (pPosthumousName ?? "").Trim() + suffix;
        }

        public static bool ShouldUseTaizuForOrdinaryFirstEmperor(bool pIsMandateKingdom,
            bool pIsEmperor, bool pHasPriorEmperorTitle)
        {
            return !pIsMandateKingdom && pIsEmperor && !pHasPriorEmperorTitle;
        }

        public static string BuildFullTitle(string pKingdomPrefix, string pTitleChar, string pSuffix,
            bool pUseOrdinaryFirstEmperorTaizu)
        {
            string prefix = pKingdomPrefix ?? "";
            string titleChar = pTitleChar ?? "";
            string suffix = pSuffix ?? "";
            if (pUseOrdinaryFirstEmperorTaizu && suffix == EmperorSuffix)
                return prefix + Taizu + titleChar + suffix;
            return prefix + titleChar + suffix;
        }

        public static bool IsCompactOrdinaryEmperorTitle(string pTitle)
        {
            if (string.IsNullOrEmpty(pTitle)) return false;
            if (!pTitle.EndsWith(EmperorSuffix)) return false;
            if (pTitle.Contains(EmperorFullSuffix)) return false;
            if (pTitle.Contains(" ")) return false;
            return pTitle.Length >= 3;
        }

        public static string RepairFirstOrdinaryEmperorDisplayTitle(string pTitle,
            bool pHasPriorOrdinaryEmperorTitle)
        {
            if (pHasPriorOrdinaryEmperorTitle) return pTitle ?? "";
            if (!IsCompactOrdinaryEmperorTitle(pTitle)) return pTitle ?? "";
            if (pTitle.Contains(Taizu)) return pTitle;

            string body = pTitle.Substring(0, pTitle.Length - EmperorSuffix.Length);
            if (body.Length < 2) return pTitle;
            string prefix = body.Substring(0, 1);
            string rest = body.Substring(1);
            if (rest.Contains("\u7956") || rest.Contains("\u5b97")) return pTitle;
            return prefix + Taizu + rest + EmperorSuffix;
        }

        private static List<Candidate> BuildCandidates(RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived, Scores pScores)
        {
            var result = new List<Candidate>(PosthumousTitleDefs.Pool.Length);
            foreach (PosthumousTitleChar definition in PosthumousTitleDefs.Pool)
            {
                bool agePriority = IsAgePriority(definition.Char, pFacts);
                if (!Eligible(definition.Char, pFacts, pDerived, pScores)) continue;
                bool factQualifiedBlame = pScores.Grade == PosthumousGrade.Blame &&
                                          definition.MinGrade == PosthumousGrade.BlameHigh &&
                                          IsBlameCharacter(definition.Char);
                if (!agePriority && !factQualifiedBlame &&
                    !GradeCompatible(pScores.Grade, definition.MinGrade)) continue;
                result.Add(new Candidate
                {
                    Definition = definition,
                    Eligibility = EligibilityStrength(definition.Char, pFacts, pDerived, pScores),
                    DimensionMatch = definition.Dimension == pScores.Dominant ? 1 : 0,
                    DimensionScore = DimensionScore(definition.Dimension, pScores),
                    StableTie = StableTie(pFacts.ActorId, definition.Char)
                });
            }

            result.Sort((pLeft, pRight) =>
            {
                int compare = pRight.Eligibility.CompareTo(pLeft.Eligibility);
                if (compare != 0) return compare;
                compare = pRight.DimensionMatch.CompareTo(pLeft.DimensionMatch);
                if (compare != 0) return compare;
                compare = pRight.DimensionScore.CompareTo(pLeft.DimensionScore);
                if (compare != 0) return compare;
                return pRight.StableTie.CompareTo(pLeft.StableTie);
            });
            return result;
        }

        private static List<string> BuildMandateNames(List<Candidate> pCandidates,
            RulerTitleFacts pFacts, RulerTitleDerivedFacts pDerived, Scores pScores)
        {
            var result = new List<string>(pCandidates.Count * 4);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool disastrous = pScores.Grade == PosthumousGrade.Blame ||
                              pScores.Grade == PosthumousGrade.BlameHigh;
            bool allowsWen = AllowsWen(pFacts, pDerived);
            bool allowsWu = AllowsWu(pFacts, pDerived);

            foreach (Candidate candidate in pCandidates)
            {
                string value = candidate.Definition.Char;
                if (!disastrous && MandateSplitChars.Contains(value))
                {
                    string first = pDerived.CivilScore >= pDerived.MartialScore ? "文" : "武";
                    if (first == "文" && !allowsWen) first = "武";
                    if (first == "武" && !allowsWu) first = "文";
                    if ((first == "文" && allowsWen) || (first == "武" && allowsWu))
                        AddUnique(result, seen, first + value);

                    string alternate = first == "文" ? "武" : "文";
                    if ((alternate == "文" && allowsWen) || (alternate == "武" && allowsWu))
                        AddUnique(result, seen, alternate + value);
                }

                int partnerLimit = Math.Min(pCandidates.Count, 8);
                for (int partnerIndex = 0; partnerIndex < partnerLimit; partnerIndex++)
                {
                    string partner = pCandidates[partnerIndex].Definition.Char;
                    if (string.Equals(value, partner, StringComparison.Ordinal)) continue;
                    AddUnique(result, seen, value + partner);
                }
            }
            return result;
        }

        private static void AddUnique(List<string> pValues, HashSet<string> pSeen, string pValue)
        {
            if (string.IsNullOrEmpty(pValue) || pValue.Length != 2 || !pSeen.Add(pValue)) return;
            pValues.Add(pValue);
        }

        private static bool Eligible(string pChar, RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived, Scores pScores)
        {
            bool Has(RulerTraitFlags pFlag) => (pFacts.Traits & pFlag) != 0;
            bool populationStable = pFacts.StartPopulation <= 0 ||
                                    (long)pFacts.EndPopulation * 10 >=
                                    (long)pFacts.StartPopulation * 9;
            bool fallen = pFacts.LostCapital ||
                          string.Equals(pFacts.EndReason, "kingdom_fell", StringComparison.Ordinal);
            bool extremePraiseAllowed = !pDerived.GraveCrime && !fallen;
            bool militaryWin = pFacts.Warfare >= RulerTitleFactRules.HighStat && pFacts.WarWins > 0;
            bool badRule = pDerived.GraveCrime || fallen || pScores.Total <= -2;
            bool young = pFacts.Age > 0 && pFacts.Age <= 35;
            bool veryYoung = pFacts.Age > 0 && pFacts.Age <= 20;
            bool violentEnd = IsViolentEnd(pFacts);

            switch (pChar)
            {
                case "文":
                    return AllowsWen(pFacts, pDerived) && pScores.Civil >= 2 &&
                           CountTrue(pDerived.Scholar, pDerived.Administrator, pDerived.Just) >= 2;
                case "明":
                    return extremePraiseAllowed && pDerived.Scholar && pDerived.Just && pScores.Civil >= 2;
                case "睿":
                    return extremePraiseAllowed && pDerived.Scholar &&
                           (pFacts.Intelligence >= RulerTitleFactRules.ExcellentStat ||
                            Has(RulerTraitFlags.Genius) || Has(RulerTraitFlags.Wise));
                case "宣":
                    return extremePraiseAllowed && pFacts.Diplomacy >= RulerTitleFactRules.HighStat &&
                           Has(RulerTraitFlags.Honest) && populationStable;
                case "昭":
                    return extremePraiseAllowed &&
                           (pFacts.Diplomacy >= RulerTitleFactRules.HighStat || pDerived.Compassionate);
                case "景":
                    return extremePraiseAllowed && pDerived.Diligent &&
                           (pScores.Civil > 0 || pScores.Territory > 0);
                case "德":
                    return extremePraiseAllowed && pDerived.Just && pDerived.Compassionate;
                case "惠":
                    return pDerived.Compassionate && populationStable;
                case "仁":
                    return extremePraiseAllowed && pDerived.Compassionate && pDerived.Generous;
                case "孝":
                    return pDerived.FamilyFirst && pDerived.Patient;
                case "康":
                    return pDerived.Healthy && pDerived.StableOrder && pFacts.ReignYears >= 10;
                case "懿":
                    return extremePraiseAllowed && pDerived.Just && pDerived.Patient && pDerived.StableOrder;
                case "端":
                    return pDerived.Just && pDerived.StableOrder;
                case "恪":
                    return pDerived.Diligent && pDerived.StableOrder;
                case "勤":
                    return pDerived.Diligent && (pFacts.MajorReforms > 0 || pScores.Civil > 0);
                case "宪":
                    return extremePraiseAllowed && pDerived.Just && pDerived.Administrator &&
                           pDerived.StableOrder;
                case "敏":
                    return pDerived.Scholar &&
                           (pDerived.Diligent || pFacts.Intelligence >= RulerTitleFactRules.HighStat);
                case "成":
                    return extremePraiseAllowed && pDerived.Administrator &&
                           pFacts.CityDelta >= 0 && pScores.Order >= 0;
                case "元":
                    return extremePraiseAllowed &&
                           (pFacts.IsFounder || pFacts.IsAutonomousRefounder) &&
                           pScores.Territory >= 0 && pScores.Order >= 0;
                case "定":
                    return pDerived.StableOrder &&
                           (pFacts.RestoredLegalCore || pScores.Territory >= 0);
                case "襄":
                case "庄":
                    return militaryWin && pFacts.WarWins >= pFacts.WarLosses;
                case "献":
                    return pDerived.Generous && (pDerived.Compassionate || pScores.Territory >= 0);
                case "穆":
                    return pDerived.Patient && (pDerived.FamilyFirst || pDerived.StableOrder);
                case "肃":
                    return pScores.Order > 0 && (pDerived.Administrator || militaryWin);
                case "靖":
                    return pDerived.StableOrder && pDerived.Patient;
                case "宁":
                    return pDerived.StableOrder && populationStable;
                case "绥":
                    return pDerived.StableOrder &&
                           (pDerived.Compassionate || pFacts.Diplomacy >= RulerTitleFactRules.HighStat);
                case "贞":
                    return pDerived.Just && (pDerived.Patient || pDerived.StableOrder);
                case "武":
                    return AllowsWu(pFacts, pDerived) && pScores.War >= 2 &&
                           CountTrue(pDerived.Strategist, pDerived.Brave, pDerived.GreatConquest) >= 2;
                case "桓":
                    return militaryWin && pFacts.WarWins >= pFacts.WarLosses;
                case "烈":
                    return AllowsWu(pFacts, pDerived) && pDerived.Brave && pScores.War >= 2;
                case "威":
                    return militaryWin && pFacts.WarWins > pFacts.WarLosses;
                case "毅":
                    return pDerived.Brave && pDerived.Patient && pFacts.WarWins > 0;
                case "勇":
                    return pDerived.Brave && pFacts.WarWins > 0;
                case "壮":
                    return (Has(RulerTraitFlags.Strong) || Has(RulerTraitFlags.Tough)) &&
                           pFacts.WarWins > 0;
                case "刚":
                    return pDerived.Brave && !pDerived.Frail;
                case "雄":
                    return pDerived.GreatConquest &&
                           pFacts.Warfare >= RulerTitleFactRules.HighStat;
                case "胜":
                    return pFacts.WarWins >= 3 && pFacts.WarWins > pFacts.WarLosses;
                case "平":
                    return pScores.Order >= 0 || pFacts.WarWins + pFacts.WarLosses == 0;
                case "安":
                    return pDerived.StableOrder;
                case "顺":
                    return string.Equals(pFacts.EndReason, "abdicated", StringComparison.Ordinal) ||
                           Has(RulerTraitFlags.Content) || pDerived.Patient;
                case "恭":
                    return pDerived.FamilyFirst || pDerived.Patient || Has(RulerTraitFlags.Honest);
                case "简":
                    return Has(RulerTraitFlags.Content) ||
                           (pFacts.MajorReforms == 0 && pDerived.StableOrder);
                case "敬":
                    return pDerived.Just || pDerived.Patient;
                case "静":
                    return Has(RulerTraitFlags.Peaceful) || Has(RulerTraitFlags.Pacifist) ||
                           Has(RulerTraitFlags.Content);
                case "和":
                    return pDerived.Compassionate ||
                           pFacts.Diplomacy >= RulerTitleFactRules.HighStat;
                case "质":
                    return Has(RulerTraitFlags.Honest) || pDerived.StableOrder;
                case "隐":
                    return string.Equals(pFacts.EndReason, "abdicated", StringComparison.Ordinal) ||
                           string.Equals(pFacts.EndReason, "replaced", StringComparison.Ordinal) ||
                           pDerived.SmallRealm;
                case "僖":
                    return pDerived.StableOrder || Has(RulerTraitFlags.Content);
                case "节":
                    return pDerived.Patient || pDerived.Just;
                case "哀":
                case "悼":
                    return young;
                case "怀":
                    return pFacts.Age > 0 && pFacts.Age <= 30 &&
                           (pDerived.Compassionate || pScores.Civil >= 0);
                case "殇":
                    return pFacts.Age > 0 && pFacts.Age <= 18;
                case "冲":
                    return veryYoung || (pFacts.ReignYears > 0 && pFacts.ReignYears < 2);
                case "愍":
                case "闵":
                    return young && violentEnd;
                case "思":
                    return Has(RulerTraitFlags.Honest) &&
                           (pScores.Territory < 0 || pScores.War < 0 || pScores.Ending < 0) &&
                           (pScores.Civil >= 0 || pScores.Order >= 0);
                case "厉":
                    return badRule && (Has(RulerTraitFlags.Hotheaded) || Has(RulerTraitFlags.Cruel) ||
                                       Has(RulerTraitFlags.Bloodlust) || pFacts.AtrocityCount > 0 ||
                                       pScores.War <= -2);
                case "幽":
                    return badRule && (Has(RulerTraitFlags.Paranoid) || Has(RulerTraitFlags.Madness) ||
                                       Has(RulerTraitFlags.Deceitful) || fallen);
                case "荒":
                    return badRule && (Has(RulerTraitFlags.Gluttonous) || Has(RulerTraitFlags.Lustful) ||
                                       Has(RulerTraitFlags.Greedy));
                case "废":
                    return fallen || string.Equals(pFacts.EndReason, "replaced", StringComparison.Ordinal) ||
                           string.Equals(pFacts.EndReason, "captured_slave", StringComparison.Ordinal);
                case "炀":
                    return badRule && pDerived.GraveCrime &&
                           (Has(RulerTraitFlags.Lustful) || Has(RulerTraitFlags.Gluttonous) ||
                            Has(RulerTraitFlags.Cruel));
                case "灵":
                    return badRule && (Has(RulerTraitFlags.Madness) || Has(RulerTraitFlags.Paranoid) ||
                                       Has(RulerTraitFlags.Deceitful));
                case "戾":
                    return badRule && (Has(RulerTraitFlags.Cruel) || Has(RulerTraitFlags.Hotheaded) ||
                                       Has(RulerTraitFlags.Kingslayer));
                case "刺":
                    return badRule && (Has(RulerTraitFlags.Deceitful) ||
                                       Has(RulerTraitFlags.Kingslayer));
                case "谬":
                    return badRule && (Has(RulerTraitFlags.Stupid) || pScores.Civil < 0);
                case "惑":
                    return badRule && (Has(RulerTraitFlags.Stupid) || Has(RulerTraitFlags.Madness) ||
                                       Has(RulerTraitFlags.Paranoid));
                case "蛊":
                    return badRule && (Has(RulerTraitFlags.Lustful) || Has(RulerTraitFlags.Madness) ||
                                       Has(RulerTraitFlags.Greedy));
                case "险":
                    return badRule && (Has(RulerTraitFlags.Paranoid) ||
                                       Has(RulerTraitFlags.Deceitful));
                case "悖":
                case "逆":
                    return badRule && (Has(RulerTraitFlags.Kingslayer) || Has(RulerTraitFlags.Evil));
                case "傲":
                    return badRule && (Has(RulerTraitFlags.Ambitious) ||
                                       Has(RulerTraitFlags.Hotheaded));
                case "暴":
                    return badRule && (Has(RulerTraitFlags.Bloodlust) || Has(RulerTraitFlags.Cruel) ||
                                       pFacts.AtrocityCount > 0);
                case "虐":
                    return badRule && (Has(RulerTraitFlags.Cruel) || pFacts.AtrocityCount > 0);
                case "昏":
                    return badRule && (Has(RulerTraitFlags.Stupid) || Has(RulerTraitFlags.Madness));
                case "愎":
                    return badRule &&
                           (Has(RulerTraitFlags.Hotheaded) &&
                            (Has(RulerTraitFlags.Deceitful) || Has(RulerTraitFlags.Paranoid)) ||
                            pFacts.WarLosses >= 3 && pFacts.WarLosses > pFacts.WarWins);
                default:
                    return false;
            }
        }

        private static int EligibilityStrength(string pChar, RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived, Scores pScores)
        {
            if (IsAgePriority(pChar, pFacts)) return 500 - Math.Max(0, pFacts.Age);
            if (pChar == "文" || pChar == "武") return 430;
            if (pChar == "明" || pChar == "睿" || pChar == "成" ||
                pChar == "仁" || pChar == "德") return 390;
            if (pScores.Grade == PosthumousGrade.BlameHigh &&
                IsBlameCharacter(pChar)) return 380;
            if (pChar == "思") return 370;
            if (pChar == "元" && (pFacts.IsFounder || pFacts.IsAutonomousRefounder)) return 360;
            int factMatches = CountTrue(
                pDerived.Diligent,
                pDerived.Just,
                pDerived.Administrator,
                pDerived.Strategist,
                pDerived.StableOrder,
                pDerived.GreatConquest);
            return 250 + factMatches * 4 + Math.Abs(DimensionScoreForChar(pChar, pScores));
        }

        private static Candidate FallbackCandidate(long pActorId)
        {
            PosthumousTitleChar definition = PosthumousTitleDefs.Pool
                .First(pItem => pItem.Char == "平");
            return new Candidate
            {
                Definition = definition,
                Eligibility = 0,
                DimensionMatch = 0,
                DimensionScore = 0,
                StableTie = StableTie(pActorId, definition.Char)
            };
        }

        private static Scores Evaluate(RulerTitleFacts pFacts, RulerTitleDerivedFacts pDerived)
        {
            int civil = PopulationScore(pFacts.StartPopulation, pFacts.EndPopulation);
            int civilAverage = (pFacts.Stewardship + pFacts.Intelligence + pFacts.Diplomacy) / 3;
            if (civilAverage >= RulerTitleFactRules.ExcellentStat) civil++;
            else if (civilAverage < RulerTitleFactRules.LowStat) civil--;
            if (pDerived.MajorReform) civil++;
            civil = Clamp(civil, -3, 3);

            int territory = TerritoryScore(pFacts.CityDelta, pFacts.EndReason);
            int war = WarScore(pFacts.WarWins, pFacts.WarLosses);
            int order = OrderScore(pFacts.ReignYears, pFacts.EndReason,
                pFacts.LostCapital, pFacts.OrderDelta);
            int ending = EndingScore(pFacts.EndReason, pFacts.DeathCause);
            return new Scores(civil, territory, war, order, ending);
        }

        private static int PopulationScore(int pStart, int pEnd)
        {
            if (pStart <= 0) return pEnd > 0 ? 1 : 0;
            double rate = (pEnd - pStart) / (double)pStart;
            if (rate >= 0.50) return 3;
            if (rate >= 0.25) return 2;
            if (rate >= 0.05) return 1;
            if (rate >= -0.05) return 0;
            if (rate >= -0.25) return -1;
            if (rate >= -0.50) return -2;
            return -3;
        }

        private static int TerritoryScore(int pCityDelta, string pEndReason)
        {
            if (string.Equals(pEndReason, "kingdom_fell", StringComparison.Ordinal)) return -3;
            if (pCityDelta >= 5) return 3;
            if (pCityDelta >= 2) return 2;
            if (pCityDelta >= 1) return 1;
            if (pCityDelta == 0) return 0;
            if (pCityDelta >= -2) return -1;
            if (pCityDelta >= -5) return -2;
            return -3;
        }

        private static int WarScore(int pWins, int pLosses)
        {
            int total = Math.Max(0, pWins) + Math.Max(0, pLosses);
            if (total <= 1) return 0;
            double rate = Math.Max(0, pWins) / (double)total;
            if (rate >= 0.80 && total >= 3) return 3;
            if (rate >= 0.60) return 2;
            if (rate >= 0.50) return 1;
            if (rate < 0.25 && total >= 3) return -3;
            if (rate < 0.40) return -2;
            return -1;
        }

        private static int OrderScore(int pYears, string pEndReason,
            bool pLostCapital, int pOrderDelta)
        {
            int score = Clamp(pOrderDelta, -1, 1);
            if (pYears >= 60) score += 2;
            else if (pYears >= 20) score++;
            else if (pYears > 0 && pYears < 3) score--;
            if (string.Equals(pEndReason, "abdicated", StringComparison.Ordinal)) score++;
            if (string.Equals(pEndReason, "replaced", StringComparison.Ordinal)) score--;
            if (pLostCapital) score--;
            return Clamp(score, -3, 3);
        }

        private static int EndingScore(string pEndReason, string pDeathCause)
        {
            if (string.Equals(pEndReason, "kingdom_fell", StringComparison.Ordinal) ||
                string.Equals(pEndReason, "captured_executed", StringComparison.Ordinal)) return -3;
            if (string.Equals(pEndReason, "captured_slave", StringComparison.Ordinal)) return -2;
            if (string.Equals(pEndReason, "abdicated", StringComparison.Ordinal)) return 1;
            string cause = pDeathCause ?? "";
            if (cause.Contains("自然老死")) return 1;
            if (cause.Contains("战斗") || cause.Contains("击杀") || cause.Contains("身亡")) return 1;
            if (cause.Contains("饥饿") || cause.Contains("疾病") || cause.Contains("中毒") ||
                cause.Contains("神力") || cause.Contains("神秘")) return -1;
            return 0;
        }

        private static PosthumousGrade GradeFor(int pTotal)
        {
            if (pTotal >= 6) return PosthumousGrade.PraiseHigh;
            if (pTotal >= 2) return PosthumousGrade.Praise;
            if (pTotal >= -1) return PosthumousGrade.Neutral;
            return pTotal >= -5 ? PosthumousGrade.Blame : PosthumousGrade.BlameHigh;
        }

        private static PosthumousDimension DominantFor(int pCivil, int pTerritory,
            int pWar, int pOrder, int pEnding)
        {
            int max = Math.Max(Math.Abs(pCivil), Math.Max(Math.Abs(pTerritory),
                Math.Max(Math.Abs(pWar), Math.Max(Math.Abs(pOrder), Math.Abs(pEnding)))));
            int near = CountTrue(max - Math.Abs(pCivil) <= 1,
                max - Math.Abs(pTerritory) <= 1,
                max - Math.Abs(pWar) <= 1,
                max - Math.Abs(pOrder) <= 1,
                max - Math.Abs(pEnding) <= 1);
            if (near >= 3) return PosthumousDimension.Balanced;
            if (Math.Abs(pEnding) == max) return PosthumousDimension.Ending;
            if (Math.Abs(pWar) == max) return PosthumousDimension.War;
            if (Math.Abs(pTerritory) == max) return PosthumousDimension.Territory;
            if (Math.Abs(pCivil) == max) return PosthumousDimension.Civil;
            return PosthumousDimension.Order;
        }

        private static bool GradeCompatible(PosthumousGrade pActual, PosthumousGrade pRequired)
        {
            return pActual switch
            {
                PosthumousGrade.PraiseHigh => pRequired == PosthumousGrade.PraiseHigh ||
                                              pRequired == PosthumousGrade.Praise ||
                                              pRequired == PosthumousGrade.Neutral,
                PosthumousGrade.Praise => pRequired == PosthumousGrade.Praise ||
                                          pRequired == PosthumousGrade.Neutral,
                PosthumousGrade.Neutral => pRequired == PosthumousGrade.Neutral,
                PosthumousGrade.Blame => pRequired == PosthumousGrade.Blame ||
                                         pRequired == PosthumousGrade.Neutral,
                PosthumousGrade.BlameHigh => pRequired == PosthumousGrade.BlameHigh ||
                                             pRequired == PosthumousGrade.Blame,
                _ => false
            };
        }

        private static bool IsAgePriority(string pChar, RulerTitleFacts pFacts)
        {
            if (pFacts == null || pFacts.Age <= 0) return false;
            return pChar switch
            {
                "殇" => pFacts.Age <= 18,
                "冲" => pFacts.Age <= 20,
                "怀" => pFacts.Age <= 30,
                "哀" => pFacts.Age <= 35,
                "悼" => pFacts.Age <= 35,
                "愍" => pFacts.Age <= 35 && IsViolentEnd(pFacts),
                "闵" => pFacts.Age <= 35 && IsViolentEnd(pFacts),
                _ => false
            };
        }

        private static bool IsViolentEnd(RulerTitleFacts pFacts)
        {
            string reason = pFacts?.EndReason ?? "";
            if (reason == "kingdom_fell" || reason == "captured_slave" ||
                reason == "captured_executed") return true;
            string cause = pFacts?.DeathCause ?? "";
            return cause.Contains("战斗") || cause.Contains("击杀") || cause.Contains("身亡");
        }

        private static bool IsBlameCharacter(string pChar)
        {
            return "厉幽荒废炀灵戾刺谬惑蛊险悖傲逆暴虐昏愎".Contains(pChar);
        }

        private static int DimensionScore(PosthumousDimension pDimension, Scores pScores)
        {
            return pDimension switch
            {
                PosthumousDimension.Civil => pScores.Civil,
                PosthumousDimension.Territory => pScores.Territory,
                PosthumousDimension.War => pScores.War,
                PosthumousDimension.Order => pScores.Order,
                PosthumousDimension.Ending => pScores.Ending,
                _ => pScores.Total
            };
        }

        private static int DimensionScoreForChar(string pChar, Scores pScores)
        {
            PosthumousTitleChar definition = PosthumousTitleDefs.Pool
                .FirstOrDefault(pItem => pItem.Char == pChar);
            return DimensionScore(definition.Dimension, pScores);
        }

        private static string GradeKey(PosthumousGrade pGrade)
        {
            return pGrade switch
            {
                PosthumousGrade.PraiseHigh => "praise_high",
                PosthumousGrade.Praise => "praise",
                PosthumousGrade.Blame => "blame",
                PosthumousGrade.BlameHigh => "blame_high",
                _ => "neutral"
            };
        }

        private static string DimensionKey(PosthumousDimension pDimension)
        {
            return pDimension switch
            {
                PosthumousDimension.Civil => "civil",
                PosthumousDimension.Territory => "territory",
                PosthumousDimension.War => "war",
                PosthumousDimension.Order => "order",
                PosthumousDimension.Ending => "ending",
                _ => "balanced"
            };
        }

        private static uint StableTie(long pActorId, string pValue)
        {
            unchecked
            {
                uint hash = 2166136261;
                hash = (hash ^ (uint)pActorId) * 16777619;
                hash = (hash ^ (uint)(pActorId >> 32)) * 16777619;
                foreach (char character in pValue ?? "")
                    hash = (hash ^ character) * 16777619;
                return hash;
            }
        }

        private static int CountTrue(params bool[] pValues)
        {
            int count = 0;
            foreach (bool value in pValues)
                if (value) count++;
            return count;
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            if (pValue < pMinimum) return pMinimum;
            return pValue > pMaximum ? pMaximum : pValue;
        }
    }
}
