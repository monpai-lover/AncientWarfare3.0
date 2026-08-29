using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HereditaryTitleSuccessionCandidate
    {
        public HereditaryTitleSuccessionCandidate(long actorId, bool eligible,
            bool directSon, bool legitimateBirth, bool adult, bool agnatic,
            int kinDistance, double birthTime)
        {
            ActorId = actorId;
            Eligible = eligible;
            DirectSon = directSon;
            LegitimateBirth = legitimateBirth;
            Adult = adult;
            Agnatic = agnatic;
            KinDistance = Math.Max(0, kinDistance);
            BirthTime = birthTime;
        }

        public long ActorId { get; }
        public bool Eligible { get; }
        public bool DirectSon { get; }
        public bool LegitimateBirth { get; }
        public bool Adult { get; }
        public bool Agnatic { get; }
        public int KinDistance { get; }
        public double BirthTime { get; }
    }

    public static class HereditaryTitleSuccessionRules
    {
        public static bool CanTransfer(bool hereditary, bool holderMale,
            bool maleLineIdentity)
        {
            return hereditary && holderMale && maleLineIdentity;
        }

        /// <summary>
        ///     世袭爵位/分封的继承人 —— 顺位池第一席。
        ///
        ///     和王位、宗族、学派共用 <see cref="SuccessionOrderRules"/> 那一条比较器,
        ///     差别只在怎么填「支系」这个键:
        ///       直系子   → <see cref="SuccessionOrderRules.DirectLine"/>(0)
        ///       旁支     → 1 + 亲等,于是「旁支必在直系之后」和「亲等近的在前」
        ///                  由同一个键一起表达,不需要第二套比较逻辑。
        ///
        ///     旁支仍然要求成年(幼主只在直系里兜底),这是**入池**条件,不是排序,
        ///     所以留在筛选这一侧。
        /// </summary>
        public static long SelectSuccessor(
            IReadOnlyList<HereditaryTitleSuccessionCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return -1L;
            HereditaryTitleSuccessionCandidate? best = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                HereditaryTitleSuccessionCandidate candidate = candidates[i];
                if (!CanEnter(candidate)) continue;
                if (best.HasValue && !SortsBefore(candidate, best.Value))
                    continue;
                best = candidate;
            }

            return best?.ActorId ?? -1L;
        }

        private static bool CanEnter(
            HereditaryTitleSuccessionCandidate pCandidate)
        {
            if (pCandidate.ActorId < 0 || !pCandidate.Eligible ||
                !pCandidate.Agnatic) return false;
            // 旁支入继必须成年;直系不受此限,幼主由直系兜底。
            return pCandidate.DirectSon || pCandidate.Adult;
        }

        private static bool SortsBefore(
            HereditaryTitleSuccessionCandidate left,
            HereditaryTitleSuccessionCandidate right)
        {
            return SuccessionOrderRules.SortsBefore(
                SuccessionOrderBasis.Bloodline,
                Branch(left), left.LegitimateBirth, SafeBirth(left), 0,
                left.ActorId,
                Branch(right), right.LegitimateBirth, SafeBirth(right), 0,
                right.ActorId);
        }

        private static int Branch(
            HereditaryTitleSuccessionCandidate pCandidate)
        {
            return pCandidate.DirectSon
                ? SuccessionOrderRules.DirectLine
                : SuccessionOrderRules.CollateralLine + pCandidate.KinDistance;
        }

        private static double SafeBirth(
            HereditaryTitleSuccessionCandidate pCandidate)
        {
            return double.IsNaN(pCandidate.BirthTime)
                ? double.MaxValue
                : pCandidate.BirthTime;
        }
    }
}
