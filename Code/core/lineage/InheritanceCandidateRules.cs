using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct InheritanceCandidateFacts
    {
        public InheritanceCandidateFacts(long actorId, bool alive, bool male,
            bool adult, bool royal, bool king, bool enslaved, bool mad,
            bool domestic, int agnaticDistance,
            int existingSuccessionLegitimacy, int warfare, int combat,
            int courage, int generalExperience, int stewardship,
            int intelligence, int diplomacy, int officialRank,
            int officialMerit, int evaluationGrade, int groupSupport,
            bool legitimateBirth = true, bool directLine = false,
            double birthTime = 0.0)
        {
            ActorId = actorId;
            Alive = alive;
            Male = male;
            Adult = adult;
            Royal = royal;
            King = king;
            Enslaved = enslaved;
            Mad = mad;
            Domestic = domestic;
            AgnaticDistance = Math.Max(0, agnaticDistance);
            ExistingSuccessionLegitimacy = Math.Max(0,
                existingSuccessionLegitimacy);
            Warfare = Math.Max(0, warfare);
            Combat = Math.Max(0, combat);
            Courage = Math.Max(0, courage);
            GeneralExperience = Math.Max(0, generalExperience);
            Stewardship = Math.Max(0, stewardship);
            Intelligence = Math.Max(0, intelligence);
            Diplomacy = Math.Max(0, diplomacy);
            OfficialRank = Math.Max(0, officialRank);
            OfficialMerit = Math.Max(0, officialMerit);
            EvaluationGrade = Math.Max(0, evaluationGrade);
            GroupSupport = Math.Max(0, groupSupport);
            LegitimateBirth = legitimateBirth;
            DirectLine = directLine;
            BirthTime = birthTime;
        }

        public long ActorId { get; }
        public bool Alive { get; }
        public bool Male { get; }
        public bool Adult { get; }
        public bool Royal { get; }
        public bool King { get; }
        public bool Enslaved { get; }
        public bool Mad { get; }
        public bool Domestic { get; }
        public int AgnaticDistance { get; }
        public int ExistingSuccessionLegitimacy { get; }
        public int Warfare { get; }
        public int Combat { get; }
        public int Courage { get; }
        public int GeneralExperience { get; }
        public int Stewardship { get; }
        public int Intelligence { get; }
        public int Diplomacy { get; }
        public int OfficialRank { get; }
        public int OfficialMerit { get; }
        public int EvaluationGrade { get; }
        public int GroupSupport { get; }
        public bool LegitimateBirth { get; }
        /// <summary>直系(君主的后裔)。旁支排在顺位池末尾,是兜底不是竞争者。</summary>
        public bool DirectLine { get; }
        /// <summary>出生时刻,越小越年长。同支同档内比长幼用它。</summary>
        public double BirthTime { get; }

        public InheritanceCandidateFacts WithActorId(long pActorId)
        {
            return new InheritanceCandidateFacts(pActorId, Alive, Male, Adult,
                Royal, King, Enslaved, Mad, Domestic, AgnaticDistance,
                ExistingSuccessionLegitimacy, Warfare, Combat, Courage,
                GeneralExperience, Stewardship, Intelligence, Diplomacy,
                OfficialRank, OfficialMerit, EvaluationGrade, GroupSupport,
                LegitimateBirth, DirectLine, BirthTime);
        }

        /// <summary>
        ///     只换派系支持票数。军功/文治那两条法在算完支持度之后要重排一次,
        ///     原来是把整个 BuildFacts 重跑一遍 —— 而除了这一个字段,其余全是
        ///     同一个 actor 的同一批读取(七项属性、官阶、功勋、将领/奴隶/疯癫判定、
        ///     还有一趟父系链求亲缘)。每个候选人白算一遍。
        /// </summary>
        public InheritanceCandidateFacts WithGroupSupport(int pGroupSupport)
        {
            return new InheritanceCandidateFacts(ActorId, Alive, Male, Adult,
                Royal, King, Enslaved, Mad, Domestic, AgnaticDistance,
                ExistingSuccessionLegitimacy, Warfare, Combat, Courage,
                GeneralExperience, Stewardship, Intelligence, Diplomacy,
                OfficialRank, OfficialMerit, EvaluationGrade, pGroupSupport,
                LegitimateBirth, DirectLine, BirthTime);
        }
    }

    public static class InheritanceCandidateRules
    {
        public const int MaximumArchiveIds = 32;
        public const int MaximumLiveResolutions = 32;
        public const int MaximumFinalists = 8;
        public const int MaximumOfficerSupporters = 96;

        /// <summary>
        ///     收继承池时往下走几辈直系后裔(子/孙/曾孙/玄孙…)。
        ///
        ///     必须**穿过已故的一辈**才能收到下一辈 —— 嫡长孙承重正是这个情形:
        ///     嫡长子先卒,他的儿子仍在直系顺位的最前面。所以不能"这一辈有活人
        ///     就停",得按辈数封顶。
        /// </summary>
        public const int MaximumDescendantGenerations = 6;

        /// <summary>
        ///     后裔遍历允许的族谱子女查询次数上限。每次查询是一趟 SQLite
        ///     (FamilyEdge + 档案),而继承池按「王国 + 参照君主」只建一次,
        ///     所以给得比 <see cref="MaximumLiveResolutions"/> 宽:遍历要经过
        ///     已故的中间辈,那些人不占池子名额但要花一次查询。
        /// </summary>
        public const int MaximumDescendantLookups = 48;

        public static bool IsEligible(InheritanceCandidateFacts pFacts,
            InheritanceLaw pLaw)
        {
            if (pFacts.ActorId < 0 || !pFacts.Alive || !pFacts.Male ||
                !pFacts.Royal || pFacts.King || pFacts.Enslaved ||
                pFacts.Mad || !pFacts.Domestic)
                return false;
            return pLaw == InheritanceLaw.Primogeniture || pFacts.Adult;
        }

        public static bool IsFastAdultRoyalCandidate(bool alive, bool male,
            bool adult, bool king, bool enslaved, bool mad, bool domestic,
            bool sameLineage, bool sameShi)
        {
            return alive && male && adult && !king && !enslaved && !mad &&
                   domestic && (sameLineage || sameShi);
        }

        public static int Score(InheritanceCandidateFacts pFacts,
            InheritanceLaw pLaw)
        {
            if (!IsEligible(pFacts, pLaw)) return int.MinValue;
            int legitimacy = Math.Max(0, 40 - pFacts.AgnaticDistance * 6) +
                             Math.Min(30,
                                 pFacts.ExistingSuccessionLegitimacy);
            if (pLaw == InheritanceLaw.MilitaryAcclaim)
            {
                return legitimacy + pFacts.Warfare * 3 +
                       pFacts.Combat * 2 + pFacts.Courage +
                       Math.Min(20, pFacts.GeneralExperience * 10) +
                       Math.Min(50, pFacts.GroupSupport);
            }
            if (pLaw == InheritanceLaw.CivilAcclaim)
            {
                return legitimacy + pFacts.Stewardship * 3 +
                       pFacts.Intelligence * 2 + pFacts.Diplomacy * 2 +
                       Math.Min(20, pFacts.OfficialRank) +
                       Math.Min(30, pFacts.OfficialMerit) +
                       Math.Min(18, pFacts.EvaluationGrade * 2) +
                       Math.Min(50, pFacts.GroupSupport);
            }
            return legitimacy + (pFacts.LegitimateBirth ? 1000 : 0);
        }

        public static long[] BoundArchiveIds(IReadOnlyList<long> pIds)
        {
            if (pIds == null || pIds.Count == 0) return Array.Empty<long>();
            var result = new List<long>(Math.Min(MaximumArchiveIds, pIds.Count));
            var seen = new HashSet<long>();
            for (int i = 0; i < pIds.Count && result.Count < MaximumArchiveIds; i++)
            {
                long actorId = pIds[i];
                if (actorId < 0 || !seen.Add(actorId)) continue;
                result.Add(actorId);
            }
            return result.ToArray();
        }

        public static InheritanceCandidateFacts[] SelectFinalists(
            IReadOnlyList<InheritanceCandidateFacts> pCandidates,
            InheritanceLaw pLaw)
        {
            if (pCandidates == null || pCandidates.Count == 0)
                return Array.Empty<InheritanceCandidateFacts>();
            var eligible = new List<InheritanceCandidateFacts>(
                Math.Min(MaximumFinalists, pCandidates.Count));
            for (int i = 0; i < pCandidates.Count; i++)
            {
                InheritanceCandidateFacts candidate = pCandidates[i];
                if (IsEligible(candidate, pLaw)) eligible.Add(candidate);
            }
            eligible.Sort((left, right) => Compare(left, right, pLaw));
            if (eligible.Count > MaximumFinalists)
                eligible.RemoveRange(MaximumFinalists,
                    eligible.Count - MaximumFinalists);
            return eligible.ToArray();
        }

        public static InheritanceCandidateFacts SelectBest(
            IReadOnlyList<InheritanceCandidateFacts> pCandidates,
            InheritanceLaw pLaw)
        {
            InheritanceCandidateFacts[] finalists = SelectFinalists(
                pCandidates, pLaw);
            return finalists.Length > 0 ? finalists[0] : default;
        }

        private static int Compare(InheritanceCandidateFacts pLeft,
            InheritanceCandidateFacts pRight, InheritanceLaw pLaw)
        {
            // 正统继承用统一的顺位规则:直系在前、嫡压长、同档比齿。
            //
            // 原来它和军功/文治一样走加权求和,靠 LegitimateBirth 的 +1000 把嫡子
            // 顶上去。权重够大时行为大致等价,但那是「大数压小数」而不是硬保证 ——
            // AgnaticDistance 的 40 分档配上 ExistingSuccessionLegitimacy 的 30 分档
            // 仍可能在边界上翻盘,而且长幼根本没进过排序键(只能靠 ActorId 兜底,
            // 那是出生顺序的巧合,不是规则)。分层比较才是全序。
            //
            // 军功/文治是「拥立」,比的本来就是能力与支持,不是嫡长,保持原样。
            if (pLaw == InheritanceLaw.Primogeniture)
            {
                if (pLeft.ActorId == pRight.ActorId) return 0;
                return SuccessionOrderRules.SortsBefore(
                    SuccessionOrderBasis.Bloodline,
                    Branch(pLeft), pLeft.LegitimateBirth, pLeft.BirthTime, 0,
                    pLeft.ActorId,
                    Branch(pRight), pRight.LegitimateBirth, pRight.BirthTime,
                    0, pRight.ActorId)
                    ? -1
                    : 1;
            }

            int scoreComparison = Score(pRight, pLaw).CompareTo(
                Score(pLeft, pLaw));
            return scoreComparison != 0
                ? scoreComparison
                : pLeft.ActorId.CompareTo(pRight.ActorId);
        }

        private static int Branch(InheritanceCandidateFacts pFacts)
        {
            return pFacts.DirectLine
                ? SuccessionOrderRules.DirectLine
                : SuccessionOrderRules.CollateralLine;
        }
    }
}
