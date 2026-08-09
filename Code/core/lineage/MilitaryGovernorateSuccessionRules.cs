using System;

namespace AncientWarfare3.core.lineage
{
    public static class MilitaryGovernorateSuccessionRules
    {
        public const int GraceYears = 2;
        public const int CandidateLimit = 32;

        public static bool UseDesignated(bool pAlive, bool pValidRealm)
        {
            return pAlive && pValidRealm;
        }

        public static bool CanDesignate(bool pActiveGeneral, bool pAlive,
            bool pAdult, bool pKing, bool pFromSubject,
            bool pFromDirectSuzerain)
        {
            return pActiveGeneral && pAlive && pAdult && !pKing &&
                   (pFromSubject || pFromDirectSuzerain);
        }

        public static bool CanCommitDesignated(bool pAlive,
            bool pEligibleGeneral, bool pCurrentSubjectKing)
        {
            return pAlive && (pEligibleGeneral || pCurrentSubjectKing);
        }

        public static bool ShouldMoveAtCommit(long pCandidateKingdomId,
            long pSubjectKingdomId)
        {
            return pCandidateKingdomId >= 0 && pSubjectKingdomId >= 0 &&
                   pCandidateKingdomId != pSubjectKingdomId;
        }

        public static bool ShouldWaitForSuzerain(int pCurrentYear,
            int pPendingSinceYear, bool pSuzerainStable)
        {
            return pSuzerainStable && pCurrentYear >= 0 &&
                   pPendingSinceYear >= 0 &&
                   pCurrentYear - pPendingSinceYear < GraceYears;
        }

        public static int ElectionScore(int pMerit, int pProwess,
            int pArmySupport, int pLocalServiceYears)
        {
            long score = Math.Max(0L, pMerit) * 8L +
                         Math.Max(0L, pProwess) * 6L +
                         Math.Max(0L, pArmySupport) * 5L +
                         Math.Max(0L, pLocalServiceYears) * 4L;
            return score >= int.MaxValue ? int.MaxValue : (int)score;
        }

        public static int CompareCandidate(int pLeftScore, long pLeftId,
            int pRightScore, long pRightId)
        {
            int score = pRightScore.CompareTo(pLeftScore);
            return score != 0 ? score : pLeftId.CompareTo(pRightId);
        }

        public static string ChronicleProjectionKey(long pStateId,
            long pGovernorActorId, string pTarget)
        {
            return "military_governorate_succession:" + pStateId + ":" +
                   pGovernorActorId + ":" + (pTarget ?? "");
        }
    }
}
