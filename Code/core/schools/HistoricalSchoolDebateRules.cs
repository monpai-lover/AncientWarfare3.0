using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolDebateRules
    {
        public const double MaxDebateScore = 200d;
        public const float MaxMomentumDelta = 0.2f;
        public const float MaxActivePresenceDelta = 0.15f;
        public const float MaxTraditionDelta = 0.05f;

        private const double DrawMargin = 0.0001d;
        private const double DecisiveMargin = 40d;

        public static string SelectTopic(HistoricalSchoolMasterDefinition pFirst,
            HistoricalSchoolMasterDefinition pSecond, IEnumerable<string> pCityTopics)
        {
            if (pFirst == null || pSecond == null ||
                string.IsNullOrWhiteSpace(pFirst.SchoolId) ||
                string.IsNullOrWhiteSpace(pSecond.SchoolId) ||
                string.Equals(pFirst.SchoolId, pSecond.SchoolId, StringComparison.Ordinal))
                return null;

            HashSet<string> secondTopics = ToSet(pSecond.DebateTopics);
            HashSet<string> cityTopics = ToSet(pCityTopics);
            if (secondTopics.Count == 0 || cityTopics.Count == 0) return null;

            foreach (string topic in pFirst.DebateTopics ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(topic) && secondTopics.Contains(topic) &&
                    cityTopics.Contains(topic)) return topic;
            }

            // Definitions normally provide a stable order, but sorting the remaining
            // intersection keeps hand-authored content deterministic as well.
            return secondTopics.Intersect(cityTopics, StringComparer.Ordinal)
                .Intersect(ToSet(pFirst.DebateTopics), StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        public static HistoricalSchoolDebatePair SelectPair(
            IEnumerable<HistoricalSchoolDebateCandidate> pCandidates)
        {
            HistoricalSchoolDebateCandidate[] candidates = (pCandidates ??
                    Array.Empty<HistoricalSchoolDebateCandidate>())
                .Where(IsEligible)
                .OrderBy(p => p.ActorId)
                .ThenBy(p => p.SchoolId, StringComparer.Ordinal)
                .ToArray();

            for (int first = 0; first < candidates.Length; first++)
            {
                for (int second = first + 1; second < candidates.Length; second++)
                {
                    HistoricalSchoolDebateCandidate left = candidates[first];
                    HistoricalSchoolDebateCandidate right = candidates[second];
                    if (left.ActorId == right.ActorId ||
                        string.Equals(left.SchoolId, right.SchoolId, StringComparison.Ordinal))
                        continue;
                    return new HistoricalSchoolDebatePair(left, right);
                }
            }
            return null;
        }

        public static HistoricalSchoolDebateScore Score(
            HistoricalSchoolDebateCandidate pCandidate, string pTopic,
            HistoricalSchoolLedgerSnapshot pLedger)
        {
            if (pCandidate == null || !pCandidate.Alive || !pCandidate.Present ||
                pCandidate.ActorId < 0) return new HistoricalSchoolDebateScore(-1, 0d);

            double topicAbility = TopicAbility(pCandidate, pTopic);
            double otherAbility = (pCandidate.Diplomacy + pCandidate.Intelligence +
                pCandidate.Warfare + pCandidate.Stewardship) / 4d;
            double expertise = ContainsTopic(pCandidate.Topics, pTopic) ? 10d : 0d;
            double disciples = Math.Min(20, pCandidate.DirectDiscipleCount);
            double wins = Math.Min(20, pCandidate.DebateWins);
            double tradition = pLedger?.Tradition ?? 0d;
            double presence = pLedger?.ActivePresence ?? 0d;
            double momentum = pLedger?.Momentum ?? 0d;

            double value = pCandidate.Reputation * 0.22d + pCandidate.Learning * 0.23d +
                           topicAbility * 0.25d + otherAbility * 0.10d + expertise +
                           disciples + wins + pCandidate.ProblemMatch * 15d +
                           Bound01(tradition) * 5d + Bound01(presence) * 5d +
                           Bound01(momentum) * 10d;
            return new HistoricalSchoolDebateScore(pCandidate.ActorId,
                Math.Max(0d, Math.Min(MaxDebateScore, value)));
        }

        public static SchoolDebateOutcome ResolveOutcome(double pFirstScore,
            double pSecondScore)
        {
            double first = FiniteScore(pFirstScore);
            double second = FiniteScore(pSecondScore);
            double difference = first - second;
            if (Math.Abs(difference) <= DrawMargin) return SchoolDebateOutcome.Draw;
            if (difference > 0d)
                return difference >= DecisiveMargin
                    ? SchoolDebateOutcome.DecisiveFirstWin
                    : SchoolDebateOutcome.NarrowFirstWin;
            return difference <= -DecisiveMargin
                ? SchoolDebateOutcome.DecisiveSecondWin
                : SchoolDebateOutcome.NarrowSecondWin;
        }

        public static HistoricalSchoolLedgerDelta LedgerDelta(
            SchoolDebateOutcome pOutcome, bool pFirstWon, int pYear)
        {
            bool firstWinner = pOutcome == SchoolDebateOutcome.NarrowFirstWin ||
                               pOutcome == SchoolDebateOutcome.DecisiveFirstWin;
            bool secondWinner = pOutcome == SchoolDebateOutcome.NarrowSecondWin ||
                                pOutcome == SchoolDebateOutcome.DecisiveSecondWin;
            bool draw = pOutcome == SchoolDebateOutcome.Draw;
            bool targetWon = pFirstWon ? firstWinner : secondWinner;
            bool targetLost = pFirstWon ? secondWinner : firstWinner;

            float tradition;
            float active;
            float momentum;
            if (draw)
            {
                tradition = 0.02f;
                active = 0.015f;
                momentum = 0.01f;
            }
            else if (targetWon)
            {
                bool decisive = pOutcome == SchoolDebateOutcome.DecisiveFirstWin ||
                                pOutcome == SchoolDebateOutcome.DecisiveSecondWin;
                tradition = decisive ? 0.02f : 0.01f;
                active = decisive ? 0.10f : 0.06f;
                momentum = decisive ? 0.16f : 0.09f;
            }
            else if (targetLost)
            {
                tradition = 0.005f;
                active = -0.025f;
                momentum = -0.04f;
            }
            else
            {
                tradition = 0f;
                active = 0f;
                momentum = 0f;
            }

            return new HistoricalSchoolLedgerDelta(
                Clamp(tradition, MaxTraditionDelta),
                Clamp(active, MaxActivePresenceDelta),
                Clamp(momentum, MaxMomentumDelta), 0f,
                pYear < 0 ? -1 : pYear);
        }

        public static float ReputationDelta(SchoolDebateOutcome pOutcome, bool pFirst)
        {
            if (pOutcome == SchoolDebateOutcome.Draw) return 0.25f;
            bool firstWon = pOutcome == SchoolDebateOutcome.NarrowFirstWin ||
                            pOutcome == SchoolDebateOutcome.DecisiveFirstWin;
            bool secondWon = pOutcome == SchoolDebateOutcome.NarrowSecondWin ||
                             pOutcome == SchoolDebateOutcome.DecisiveSecondWin;
            bool won = pFirst ? firstWon : secondWon;
            bool decisive = pOutcome == SchoolDebateOutcome.DecisiveFirstWin ||
                            pOutcome == SchoolDebateOutcome.DecisiveSecondWin;
            return won ? (decisive ? 2.5f : 1.25f) : (decisive ? -0.5f : -0.2f);
        }

        private static bool IsEligible(HistoricalSchoolDebateCandidate pCandidate)
        {
            return pCandidate != null && pCandidate.ActorId >= 0 && pCandidate.Alive &&
                   pCandidate.Present && !string.IsNullOrWhiteSpace(pCandidate.SchoolId);
        }

        private static HashSet<string> ToSet(IEnumerable<string> pValues)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (pValues == null) return result;
            foreach (string value in pValues)
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            return result;
        }

        private static bool ContainsTopic(IEnumerable<string> pTopics, string pTopic)
        {
            return !string.IsNullOrWhiteSpace(pTopic) && ToSet(pTopics).Contains(pTopic);
        }

        private static double TopicAbility(HistoricalSchoolDebateCandidate pCandidate,
            string pTopic)
        {
            switch (pTopic ?? "")
            {
                case HistoricalDebateTopicId.War:
                case HistoricalDebateTopicId.Defense:
                case HistoricalDebateTopicId.Aggression:
                    return pCandidate.Warfare;
                case HistoricalDebateTopicId.Peace:
                case HistoricalDebateTopicId.Diplomacy:
                    return pCandidate.Diplomacy;
                case HistoricalDebateTopicId.Livelihood:
                case HistoricalDebateTopicId.Famine:
                case HistoricalDebateTopicId.Commerce:
                    return pCandidate.Stewardship;
                case HistoricalDebateTopicId.Order:
                case HistoricalDebateTopicId.Institutions:
                    return (pCandidate.Intelligence + pCandidate.Stewardship) / 2d;
                case HistoricalDebateTopicId.Technology:
                case HistoricalDebateTopicId.Medicine:
                case HistoricalDebateTopicId.Epidemic:
                    return pCandidate.Intelligence;
                default:
                    return (pCandidate.Diplomacy + pCandidate.Intelligence +
                            pCandidate.Warfare + pCandidate.Stewardship) / 4d;
            }
        }

        private static double FiniteScore(double pValue)
        {
            if (double.IsNaN(pValue) || double.IsInfinity(pValue)) return 0d;
            return Math.Max(0d, Math.Min(MaxDebateScore, pValue));
        }

        private static float Bound01(double pValue)
        {
            if (double.IsNaN(pValue) || double.IsInfinity(pValue)) return 0f;
            return (float)Math.Max(0d, Math.Min(1d, pValue));
        }

        private static float Clamp(float pValue, float pMaximum)
        {
            return Math.Max(-pMaximum, Math.Min(pMaximum, pValue));
        }
    }
}
