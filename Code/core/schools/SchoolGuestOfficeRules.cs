using System;

namespace AncientWarfare3.core.schools
{
    public static class SchoolGuestOfficeRules
    {
        public const int MinTermYears = 8;
        public const int MaxTermYears = 20;

        public static bool CanInvite(bool realScholar, bool alive, bool foreignHome,
            bool residenceInHost, bool available, bool serviceFree, bool forbidden,
            bool centralOfficeMale, bool reputationFit, bool officeFit)
        {
            return realScholar && alive && foreignHome && residenceInHost && available &&
                   serviceFree && !forbidden && centralOfficeMale && reputationFit && officeFit;
        }

        public static int TermYears(long pActorId, long pHostKingdomId, int pYear)
        {
            unchecked
            {
                long value = pActorId * 6364136223846793005L +
                             pHostKingdomId * 1442695040888963407L + pYear * 31L;
                value ^= value >> 33;
                int span = MaxTermYears - MinTermYears + 1;
                int offset = (int)(Math.Abs(value == long.MinValue ? long.MaxValue : value) %
                                   span);
                return MinTermYears + offset;
            }
        }

        public static bool ShouldRenew(float pReputation, float pHostReceptiveness,
            int pRemainingYears, bool pHostAlive, bool pActorAlive)
        {
            if (!pHostAlive || !pActorAlive || pRemainingYears > 0) return false;
            float reputation = Bound01(pReputation / 100f);
            float receptiveness = Bound01(pHostReceptiveness);
            return reputation * 0.65f + receptiveness * 0.35f >= 0.6f;
        }

        private static float Bound01(float pValue)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue)) return 0f;
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }
}
