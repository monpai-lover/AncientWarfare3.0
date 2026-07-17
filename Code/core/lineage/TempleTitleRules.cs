using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public enum TempleTitleGrade
    {
        Praise,
        Neutral,
        Martial,
        Lament,
        Blame
    }

    public readonly struct TempleTitleCandidate
    {
        public readonly string Name;
        public readonly TempleTitleGrade Grade;

        public TempleTitleCandidate(string pName, TempleTitleGrade pGrade)
        {
            Name = pName ?? "";
            Grade = pGrade;
        }
    }

    public readonly struct TempleTitleDecision
    {
        public readonly string Name;
        public readonly string QualificationKey;
        public readonly string Reason;
        public readonly int CycleNo;
        public readonly int Score;

        public TempleTitleDecision(string pName, string pQualificationKey,
            string pReason, int pCycleNo, int pScore)
        {
            Name = pName ?? "";
            QualificationKey = pQualificationKey ?? "";
            Reason = pReason ?? "";
            CycleNo = Math.Max(0, pCycleNo);
            Score = pScore;
        }
    }

    public static class TempleTitleRules
    {
        private static TempleTitleCandidate C(string pName, TempleTitleGrade pGrade)
        {
            return new TempleTitleCandidate(pName, pGrade);
        }

        public static readonly IReadOnlyList<TempleTitleCandidate> CommonCandidates =
            new[]
            {
                C("安宗", TempleTitleGrade.Neutral), C("哀宗", TempleTitleGrade.Lament),
                C("成宗", TempleTitleGrade.Praise), C("崇宗", TempleTitleGrade.Praise),
                C("纯宗", TempleTitleGrade.Neutral), C("代宗", TempleTitleGrade.Neutral),
                C("戴宗", TempleTitleGrade.Praise), C("道宗", TempleTitleGrade.Neutral),
                C("德宗", TempleTitleGrade.Praise), C("定宗", TempleTitleGrade.Neutral),
                C("度宗", TempleTitleGrade.Blame), C("端宗", TempleTitleGrade.Lament),
                C("高宗", TempleTitleGrade.Neutral), C("恭宗", TempleTitleGrade.Praise),
                C("光宗", TempleTitleGrade.Blame), C("怀宗", TempleTitleGrade.Lament),
                C("桓宗", TempleTitleGrade.Martial), C("徽宗", TempleTitleGrade.Neutral),
                C("惠宗", TempleTitleGrade.Praise), C("简宗", TempleTitleGrade.Lament),
                C("景宗", TempleTitleGrade.Blame), C("敬宗", TempleTitleGrade.Neutral),
                C("靖宗", TempleTitleGrade.Praise), C("康宗", TempleTitleGrade.Blame),
                C("礼宗", TempleTitleGrade.Lament), C("理宗", TempleTitleGrade.Praise),
                C("烈宗", TempleTitleGrade.Martial), C("明宗", TempleTitleGrade.Praise),
                C("穆宗", TempleTitleGrade.Neutral), C("宁宗", TempleTitleGrade.Neutral),
                C("钦宗", TempleTitleGrade.Lament), C("仁宗", TempleTitleGrade.Praise),
                C("睿宗", TempleTitleGrade.Praise), C("绍宗", TempleTitleGrade.Neutral),
                C("神宗", TempleTitleGrade.Blame), C("圣宗", TempleTitleGrade.Praise),
                C("世宗", TempleTitleGrade.Praise), C("思宗", TempleTitleGrade.Lament),
                C("顺宗", TempleTitleGrade.Neutral), C("肃宗", TempleTitleGrade.Praise),
                C("太宗", TempleTitleGrade.Praise), C("威宗", TempleTitleGrade.Blame),
                C("文宗", TempleTitleGrade.Neutral), C("武宗", TempleTitleGrade.Martial),
                C("熙宗", TempleTitleGrade.Neutral), C("熹宗", TempleTitleGrade.Neutral),
                C("僖宗", TempleTitleGrade.Blame), C("显宗", TempleTitleGrade.Praise),
                C("宪宗", TempleTitleGrade.Praise), C("献宗", TempleTitleGrade.Neutral),
                C("襄宗", TempleTitleGrade.Blame), C("孝宗", TempleTitleGrade.Praise),
                C("宣宗", TempleTitleGrade.Praise), C("玄宗", TempleTitleGrade.Neutral),
                C("义宗", TempleTitleGrade.Martial), C("毅宗", TempleTitleGrade.Martial),
                C("翼宗", TempleTitleGrade.Praise), C("懿宗", TempleTitleGrade.Blame),
                C("英宗", TempleTitleGrade.Praise), C("裕宗", TempleTitleGrade.Neutral),
                C("元宗", TempleTitleGrade.Blame), C("章宗", TempleTitleGrade.Praise),
                C("昭宗", TempleTitleGrade.Martial), C("哲宗", TempleTitleGrade.Neutral),
                C("真宗", TempleTitleGrade.Neutral), C("中宗", TempleTitleGrade.Praise),
                C("庄宗", TempleTitleGrade.Blame)
            };

        public static TempleTitleDecision Select(RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived, IEnumerable<string> pUsed,
            int pCycleNo = 0, string pPreviousName = "")
        {
            pFacts ??= new RulerTitleFacts();
            pDerived ??= RulerTitleFactRules.Derive(pFacts);
            var orderedUsed = new List<string>();
            if (pUsed != null)
            {
                foreach (string value in pUsed)
                {
                    string normalized = (value ?? "").Trim();
                    if (normalized.Length > 0) orderedUsed.Add(normalized);
                }
            }
            var used = new HashSet<string>(orderedUsed, StringComparer.Ordinal);
            string previous = string.IsNullOrWhiteSpace(pPreviousName)
                ? orderedUsed.LastOrDefault() ?? ""
                : pPreviousName.Trim();
            int cycleNo = Math.Max(0, pCycleNo);

            foreach (string special in SpecialOrder(pFacts))
            {
                if (!IsEligible(special, pFacts, pDerived) || used.Contains(special)) continue;
                return Decision(special, cycleNo, Score(special, pFacts, pDerived), "special");
            }

            TempleTitleCandidate? ageTitle = RankCommon(pFacts, pDerived, used,
                pCandidate => pCandidate.Grade == TempleTitleGrade.Lament &&
                              IsAgeLament(pCandidate.Name, pFacts),
                pBypassGrade: true);
            if (ageTitle.HasValue)
            {
                string name = ageTitle.Value.Name;
                return Decision(name, cycleNo, Score(name, pFacts, pDerived), "age_lament");
            }

            TempleTitleCandidate? common = RankCommon(pFacts, pDerived, used,
                _ => true, pBypassGrade: false);
            if (common.HasValue)
            {
                string name = common.Value.Name;
                return Decision(name, cycleNo, Score(name, pFacts, pDerived), "common");
            }

            List<string> generated = GeneratedCandidates(pFacts.ActorId, used);
            if (generated.Count > 0)
                return Decision(generated[0], cycleNo, 1, "generated");

            cycleNo++;
            string repeated = CycleFallback(pFacts, pDerived, previous);
            return Decision(repeated, cycleNo, Score(repeated, pFacts, pDerived), "cycle");
        }

        public static bool IsEligible(string pName, RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived)
        {
            if (string.IsNullOrEmpty(pName) || pFacts == null || pDerived == null) return false;
            bool Has(RulerTraitFlags pFlag) => (pFacts.Traits & pFlag) != 0;
            bool High(int pValue) => pValue >= RulerTitleFactRules.HighStat;
            bool Excellent(int pValue) => pValue >= RulerTitleFactRules.ExcellentStat;

            if (pName == "世祖")
                return pFacts.IsAutonomousRefounder && pFacts.WasFormerMandateShi &&
                       pFacts.RegainedMandate;
            if (pName == "太祖") return pFacts.IsFounder && pFacts.IsLowOrigin;
            if (pName == "高祖") return pFacts.IsFounder && !pFacts.IsLowOrigin;
            if (pName == "成祖")
                return !pFacts.IsFounder && pFacts.CapitalMoves > 0 &&
                       pFacts.RestoredLegalCore && pDerived.GreatConquest;

            return pName switch
            {
                "安宗" => Has(RulerTraitFlags.Content) || Has(RulerTraitFlags.Peaceful) ||
                          (pFacts.WarWins + pFacts.WarLosses == 0 && pFacts.ReignYears >= 10),
                "哀宗" => pFacts.Age <= 18,
                "成宗" => Has(RulerTraitFlags.Content) && pDerived.Patient && pDerived.StableOrder,
                "崇宗" => pDerived.Administrator && pDerived.MajorReform && pFacts.OrderDelta >= 1,
                "纯宗" => (pDerived.Scholar || Has(RulerTraitFlags.Honest) || High(pFacts.Stewardship)) &&
                          !pDerived.GraveCrime,
                "代宗" => pFacts.CollateralSuccession || pFacts.FoundedCadetBranch,
                "戴宗" => pDerived.Administrator || pDerived.Just || pDerived.Compassionate,
                "道宗" => Has(RulerTraitFlags.Honest) || Has(RulerTraitFlags.Content) ||
                          Has(RulerTraitFlags.Peaceful) || pFacts.OffensiveWars == 0,
                "德宗" => pDerived.Ambitious && pDerived.Diligent && Has(RulerTraitFlags.Honest),
                "定宗" => pDerived.StableOrder &&
                          (Has(RulerTraitFlags.StrongMinded) || Has(RulerTraitFlags.Hotheaded)),
                "度宗" => Has(RulerTraitFlags.Content) || Has(RulerTraitFlags.Lustful) ||
                          Has(RulerTraitFlags.Slow),
                "端宗" => pDerived.Frail || pFacts.Age <= 20,
                "高宗" => pDerived.Generous && Has(RulerTraitFlags.StrongMinded) &&
                          Has(RulerTraitFlags.Hotheaded),
                "恭宗" => pDerived.Compassionate || pDerived.Scholar || High(pFacts.Diplomacy),
                "光宗" => Has(RulerTraitFlags.Weak) || Has(RulerTraitFlags.Paranoid) ||
                          pFacts.Warfare <= RulerTitleFactRules.LowStat,
                "怀宗" => pFacts.Age <= 20 ||
                          (pFacts.Age <= 30 && pDerived.Compassionate),
                "桓宗" => Has(RulerTraitFlags.Hotheaded) || Has(RulerTraitFlags.Veteran) ||
                          High(pFacts.Warfare),
                "徽宗" => Has(RulerTraitFlags.Fertile) || pDerived.Scholar ||
                          Has(RulerTraitFlags.Weak),
                "惠宗" => pDerived.Administrator || pDerived.Scholar || pDerived.Compassionate,
                "简宗" => pFacts.Age <= 15 || pDerived.Frail,
                "景宗" => Has(RulerTraitFlags.Deceitful) || Has(RulerTraitFlags.Cruel) ||
                          pDerived.Brave || Has(RulerTraitFlags.Kingslayer),
                "敬宗" => High(pFacts.Diplomacy) || Has(RulerTraitFlags.Gluttonous) ||
                          Has(RulerTraitFlags.Veteran),
                "靖宗" => High(pFacts.Diplomacy) || pDerived.Administrator,
                "康宗" => Has(RulerTraitFlags.Greedy) ||
                          (pDerived.Ambitious && pFacts.EndPopulation < pFacts.StartPopulation),
                "礼宗" => pDerived.Frail || Has(RulerTraitFlags.Weak),
                "理宗" => pDerived.Scholar || pDerived.Diligent || pDerived.Compassionate,
                "烈宗" => pDerived.Ambitious || Has(RulerTraitFlags.Bloodlust) ||
                          Has(RulerTraitFlags.Attractive),
                "明宗" => Has(RulerTraitFlags.Genius) || pDerived.Administrator ||
                          pDerived.CivilScore >= 60,
                "穆宗" => pDerived.Just && pFacts.Age <= 40,
                "宁宗" => Has(RulerTraitFlags.Content) && Has(RulerTraitFlags.Peaceful),
                "钦宗" => Has(RulerTraitFlags.Paranoid) && pDerived.Frail,
                "仁宗" => pDerived.Generous && pDerived.Diligent && pDerived.Compassionate,
                "睿宗" => pDerived.FamilyFirst ||
                          (pDerived.Healthy && pFacts.Age >= 60) || Has(RulerTraitFlags.Genius),
                "绍宗" => pDerived.Brave || Has(RulerTraitFlags.Content),
                "神宗" => pDerived.Ambitious && Has(RulerTraitFlags.Paranoid) &&
                          Has(RulerTraitFlags.Greedy),
                "圣宗" => pDerived.Scholar && pDerived.Administrator && pDerived.Diligent,
                "世宗" => pDerived.Ambitious && pDerived.Brave && pDerived.Strategist,
                "思宗" => pDerived.Diligent && pFacts.Age <= 34 && pDerived.SmallRealm,
                "顺宗" => Has(RulerTraitFlags.Peaceful) && pDerived.Patient,
                "肃宗" => Has(RulerTraitFlags.Attractive) &&
                          (High(pFacts.Warfare) || High(pFacts.Stewardship)),
                "太宗" => IsTaizongEligible(pFacts, pDerived),
                "威宗" => Has(RulerTraitFlags.Gluttonous) && Has(RulerTraitFlags.Lustful),
                "文宗" => pDerived.Diligent &&
                          (Has(RulerTraitFlags.Weak) || Has(RulerTraitFlags.Peaceful)) &&
                          Has(RulerTraitFlags.Content),
                "武宗" => pDerived.Strategist && pDerived.Brave && Has(RulerTraitFlags.Strong),
                "熙宗" => Has(RulerTraitFlags.Content) || Has(RulerTraitFlags.Gluttonous),
                "熹宗" => pDerived.FamilyFirst && Has(RulerTraitFlags.Content) &&
                          Has(RulerTraitFlags.Honest),
                "僖宗" => Has(RulerTraitFlags.Genius) && Has(RulerTraitFlags.Gluttonous) &&
                          Has(RulerTraitFlags.Greedy),
                "显宗" => pDerived.Diligent && Has(RulerTraitFlags.Hotheaded) &&
                          pDerived.Administrator,
                "宪宗" => High(pFacts.Diplomacy) && pDerived.Diligent && pDerived.Patient,
                "献宗" => Has(RulerTraitFlags.Weak),
                "襄宗" => Has(RulerTraitFlags.Deceitful) && Has(RulerTraitFlags.Greedy) &&
                          Has(RulerTraitFlags.Hotheaded),
                "孝宗" => Has(RulerTraitFlags.Content) && pDerived.Just && pDerived.FamilyFirst,
                "宣宗" => pDerived.Diligent && pDerived.Just && pDerived.Healthy,
                "玄宗" => pDerived.Diligent && pDerived.Just && pDerived.Ambitious,
                "义宗" => Excellent(pFacts.Warfare) && Has(RulerTraitFlags.Peaceful) &&
                          pDerived.Generous,
                "毅宗" => pDerived.Strategist || pDerived.Scholar || Has(RulerTraitFlags.Tough),
                "翼宗" => Has(RulerTraitFlags.Attractive) || Has(RulerTraitFlags.Strong) ||
                          Has(RulerTraitFlags.Genius),
                "懿宗" => Has(RulerTraitFlags.Lustful) || Has(RulerTraitFlags.Gluttonous) ||
                          Has(RulerTraitFlags.Madness),
                "英宗" => High(pFacts.Diplomacy) && pDerived.Diligent && pDerived.Generous,
                "裕宗" => Has(RulerTraitFlags.Paranoid) && pDerived.Diligent && pDerived.Scholar,
                "元宗" => Has(RulerTraitFlags.Hotheaded) && pDerived.Scholar &&
                          Has(RulerTraitFlags.Greedy),
                "章宗" => pDerived.Just && Has(RulerTraitFlags.Honest) && pDerived.Generous,
                "昭宗" => pDerived.Ambitious && Has(RulerTraitFlags.Hotheaded) && pDerived.Brave,
                "哲宗" => pDerived.Just && Has(RulerTraitFlags.Content) && pDerived.Frail,
                "真宗" => Has(RulerTraitFlags.Honest) && pDerived.Scholar &&
                          High(pFacts.Intelligence),
                "中宗" => pDerived.Patient && pDerived.Just && pDerived.Administrator,
                "庄宗" => pDerived.Strategist && Excellent(pFacts.Warfare) &&
                          Has(RulerTraitFlags.Cruel) && Has(RulerTraitFlags.Greedy),
                _ => false
            };
        }

        public static int Score(string pName, RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived)
        {
            if (pFacts == null || pDerived == null || string.IsNullOrEmpty(pName))
                return int.MinValue;
            if (pName == "世祖") return 20000;
            if (pName == "太祖" || pName == "高祖") return 19000;
            if (pName == "成祖") return 18000;
            if (pName == "太宗") return 17000;

            TempleTitleGrade grade = CommonCandidates
                .FirstOrDefault(pCandidate => pCandidate.Name == pName).Grade;
            int combined = pDerived.CivilScore + pDerived.MartialScore + pFacts.OrderDelta * 4;
            int score = 1000;
            switch (grade)
            {
                case TempleTitleGrade.Praise:
                    score += pDerived.CivilScore * 3 + Math.Max(0, combined);
                    break;
                case TempleTitleGrade.Martial:
                    score += pDerived.MartialScore * 3 + Math.Max(0, pFacts.WarWins * 8);
                    break;
                case TempleTitleGrade.Lament:
                    score += 300 - Math.Max(0, pFacts.Age) * 4;
                    break;
                case TempleTitleGrade.Blame:
                    score += Math.Max(0, -combined) * 4 + (pDerived.GraveCrime ? 200 : 0);
                    break;
                default:
                    score += 100 - Math.Abs(combined - 40);
                    break;
            }
            if (pName == "礼宗" && pFacts.RitualPolicyComplete) score += 40;
            return score;
        }

        public static string SelectRetrospectiveAncestor(int civil, int martial, bool healthy)
        {
            if (healthy && civil >= 12 && civil >= martial) return "宣祖";
            if (martial >= 12 && martial >= civil + 4) return "景祖";
            return "德祖";
        }

        public static string BuildImperialAppellation(string pTempleName,
            string pPosthumousName)
        {
            return (pTempleName ?? "").Trim() +
                   (pPosthumousName ?? "").Trim() + "皇帝";
        }

        private static bool IsTaizongEligible(RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived)
        {
            return pFacts.IsFounderDirectHeir && pDerived.Administrator &&
                   pDerived.StableOrder && pFacts.CityDelta >= 0 &&
                   (pDerived.MajorReform || pFacts.WarWins > 0);
        }

        private static IEnumerable<string> SpecialOrder(RulerTitleFacts pFacts)
        {
            yield return "世祖";
            yield return pFacts.IsLowOrigin ? "太祖" : "高祖";
            yield return "成祖";
            yield return "太宗";
        }

        private static TempleTitleCandidate? RankCommon(RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived, HashSet<string> pUsed,
            Func<TempleTitleCandidate, bool> pFilter, bool pBypassGrade)
        {
            return CommonCandidates
                .Where(pCandidate => !pUsed.Contains(pCandidate.Name) &&
                                     pFilter(pCandidate) &&
                                     IsEligible(pCandidate.Name, pFacts, pDerived) &&
                                     (pBypassGrade || GradeCompatible(
                                         pCandidate.Grade, pFacts, pDerived)))
                .OrderByDescending(pCandidate => Score(pCandidate.Name, pFacts, pDerived))
                .ThenByDescending(pCandidate => StableTie(pFacts.ActorId, pCandidate.Name))
                .Cast<TempleTitleCandidate?>()
                .FirstOrDefault();
        }

        private static bool GradeCompatible(TempleTitleGrade pGrade,
            RulerTitleFacts pFacts, RulerTitleDerivedFacts pDerived)
        {
            if (pDerived.GraveCrime ||
                string.Equals(pFacts.EndReason, "kingdom_fell", StringComparison.Ordinal))
                return pGrade == TempleTitleGrade.Blame || pGrade == TempleTitleGrade.Lament;

            int combined = pDerived.CivilScore + pDerived.MartialScore + pFacts.OrderDelta * 4;
            if (combined >= 70)
                return pGrade == TempleTitleGrade.Praise ||
                       pGrade == TempleTitleGrade.Martial ||
                       pGrade == TempleTitleGrade.Neutral;
            if (combined >= 25)
                return pGrade == TempleTitleGrade.Praise ||
                       pGrade == TempleTitleGrade.Neutral ||
                       pGrade == TempleTitleGrade.Martial ||
                       pGrade == TempleTitleGrade.Lament;
            return pGrade == TempleTitleGrade.Neutral ||
                   pGrade == TempleTitleGrade.Lament ||
                   pGrade == TempleTitleGrade.Blame;
        }

        private static bool IsAgeLament(string pName, RulerTitleFacts pFacts)
        {
            return pName switch
            {
                "哀宗" => pFacts.Age <= 18,
                "端宗" => pFacts.Age <= 20,
                "怀宗" => pFacts.Age <= 30,
                "简宗" => pFacts.Age <= 15,
                "思宗" => pFacts.Age <= 34,
                _ => false
            };
        }

        private static List<string> GeneratedCandidates(long pActorId, HashSet<string> pUsed)
        {
            return PosthumousTitleDefs.Pool
                .Select(pDefinition => pDefinition.Char + "宗")
                .Where(pName => !pUsed.Contains(pName))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(pName => StableTie(pActorId, pName))
                .ToList();
        }

        private static string CycleFallback(RulerTitleFacts pFacts,
            RulerTitleDerivedFacts pDerived, string pPrevious)
        {
            IEnumerable<string> eligible = CommonCandidates
                .Where(pCandidate => IsEligible(pCandidate.Name, pFacts, pDerived))
                .OrderByDescending(pCandidate => Score(pCandidate.Name, pFacts, pDerived))
                .ThenByDescending(pCandidate => StableTie(pFacts.ActorId, pCandidate.Name))
                .Select(pCandidate => pCandidate.Name);
            string selected = eligible.FirstOrDefault(pName =>
                !string.Equals(pName, pPrevious, StringComparison.Ordinal));
            if (!string.IsNullOrEmpty(selected)) return selected;
            return string.Equals(pPrevious, "德宗", StringComparison.Ordinal) ? "安宗" : "德宗";
        }

        private static TempleTitleDecision Decision(string pName, int pCycleNo,
            int pScore, string pSource)
        {
            return new TempleTitleDecision(pName,
                "temple_qualification_" + pName,
                "source=" + pSource + ";score=" + pScore,
                pCycleNo, pScore);
        }

        private static uint StableTie(long pActorId, string pName)
        {
            unchecked
            {
                uint hash = 2166136261;
                hash = (hash ^ (uint)pActorId) * 16777619;
                hash = (hash ^ (uint)(pActorId >> 32)) * 16777619;
                foreach (char character in pName ?? "")
                    hash = (hash ^ character) * 16777619;
                return hash;
            }
        }
    }
}
