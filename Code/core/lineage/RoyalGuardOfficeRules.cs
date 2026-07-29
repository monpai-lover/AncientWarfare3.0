using System;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     A royal guard appointment is a lifetime military commission. It is
    ///     not a temporary job that civilian, succession, or maintenance code
    ///     may exchange for another role.
    /// </summary>
    public static class RoyalGuardOfficeRules
    {
        public static bool CanAcceptOfficeAppointment(bool pIsRoyalGuard)
        {
            return !pIsRoyalGuard;
        }

        public static bool CanAppearInOfficeCandidateList(bool pIsRoyalGuard)
        {
            return !pIsRoyalGuard;
        }

        public static bool CanAcceptNewCityLeadership(bool pIsRoyalGuard,
            bool pIsNewAppointment)
        {
            return !pIsNewAppointment || !pIsRoyalGuard;
        }

        public static bool CanAcceptNewKingship(bool pIsRoyalGuard,
            bool pFromLoad)
        {
            return !pIsRoyalGuard;
        }

        public static bool CanBecomeSuccessionCandidate(bool pIsRoyalGuard)
        {
            return !pIsRoyalGuard;
        }

        public static bool CanReplaceLifetimeGuardIdentity(bool pIsRoyalGuard)
        {
            return !pIsRoyalGuard;
        }

        public static bool CanLeaveMilitaryService(bool pIsRoyalGuard,
            bool pActorIsDead)
        {
            return !pIsRoyalGuard || pActorIsDead;
        }

        public static bool CanTrimLifetimeGuard()
        {
            return false;
        }

        public static bool CanEndLifetimeGuardService(string pReason)
        {
            return string.Equals(pReason, "died", StringComparison.Ordinal) ||
                   string.Equals(pReason, "became_heir",
                       StringComparison.Ordinal) ||
                   string.Equals(pReason, "became_king",
                       StringComparison.Ordinal) ||
                   string.Equals(pReason, "kingdom_fell",
                       StringComparison.Ordinal) ||
                   string.Equals(pReason, "kingdom_extinct",
                       StringComparison.Ordinal);
        }
    }
}
