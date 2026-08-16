namespace AncientWarfare3.core.lineage
{
    internal enum RoyalHouseholdOwnerRole
    {
        None = 0,
        King = 1,
        Heir = 2,
        Prince = 3
    }

    internal readonly struct RoyalHouseholdRecipientCandidate
    {
        internal RoyalHouseholdRecipientCandidate(long actorId,
            RoyalHouseholdOwnerRole role, bool eligible,
            int activeConsorts, int capacity, bool legitimateBirth,
            double birthTime)
        {
            ActorId = actorId;
            Role = role;
            Eligible = eligible;
            ActiveConsorts = activeConsorts;
            Capacity = capacity;
            LegitimateBirth = legitimateBirth;
            BirthTime = birthTime;
        }

        internal long ActorId { get; }
        internal RoyalHouseholdOwnerRole Role { get; }
        internal bool Eligible { get; }
        internal int ActiveConsorts { get; }
        internal int Capacity { get; }
        internal bool LegitimateBirth { get; }
        internal double BirthTime { get; }
        internal bool HasVacancy => Eligible && Capacity > 0 &&
                                    ActiveConsorts < Capacity;
    }

    internal static class RoyalHouseholdRecipientRules
    {
        internal static int Capacity(RoyalHouseholdOwnerRole role,
            RulerHouseholdRealmTier tier)
        {
            return role switch
            {
                RoyalHouseholdOwnerRole.King =>
                    RulerHouseholdRules.ConsortCapacity(tier),
                RoyalHouseholdOwnerRole.Heir => 2,
                RoyalHouseholdOwnerRole.Prince => 1,
                _ => 0
            };
        }

        internal static int Compare(RoyalHouseholdRecipientCandidate left,
            RoyalHouseholdRecipientCandidate right)
        {
            int role = left.Role.CompareTo(right.Role);
            if (role != 0) return role;
            if (left.Role == RoyalHouseholdOwnerRole.Prince &&
                left.LegitimateBirth != right.LegitimateBirth)
                return left.LegitimateBirth ? -1 : 1;
            int birth = left.BirthTime.CompareTo(right.BirthTime);
            return birth != 0 ? birth : left.ActorId.CompareTo(right.ActorId);
        }
    }
}
