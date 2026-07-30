namespace AncientWarfare3.core.lineage
{
    public enum RulerHouseholdConceptionKind
    {
        None = 0,
        PrincipalWife = 1,
        Consort = 2
    }

    public static class RulerHouseholdPregnancyRules
    {
        public const int MaximumPregnancyStartsPerKingdomMonth = 1;

        public static int ToMonthKey(int pYear, int pMonth)
        {
            int month = System.Math.Max(1, System.Math.Min(12, pMonth));
            return checked(pYear * 12 + month - 1);
        }

        public static bool ShouldProcessMonth(int currentMonthKey,
            int lastProcessedMonthKey)
        {
            return currentMonthKey != lastProcessedMonthKey;
        }

        public static int PregnancyStartsForMonth(int eligibleConsorts)
        {
            return System.Math.Min(System.Math.Max(0, eligibleConsorts),
                MaximumPregnancyStartsPerKingdomMonth);
        }

        public static int RotatingCandidateIndex(int monthKey,
            long rulerActorId, int candidateCount)
        {
            if (candidateCount <= 0) return 0;
            long monthOffset = monthKey % (long)candidateCount;
            long rulerOffset = rulerActorId % candidateCount;
            long index = (monthOffset + rulerOffset) % candidateCount;
            if (index < 0L) index += candidateCount;
            return (int)index;
        }

        public static RulerHouseholdConceptionKind ResolveConceptionKind(
            bool hasMutualSpouse, bool hasActiveConsort)
        {
            if (hasMutualSpouse)
                return RulerHouseholdConceptionKind.PrincipalWife;
            return hasActiveConsort
                ? RulerHouseholdConceptionKind.Consort
                : RulerHouseholdConceptionKind.None;
        }

        public static bool IsLegitimateBirth(
            RulerHouseholdConceptionKind pKind)
        {
            return pKind != RulerHouseholdConceptionKind.Consort;
        }

        public static string KindId(RulerHouseholdConceptionKind pKind)
        {
            return pKind switch
            {
                RulerHouseholdConceptionKind.PrincipalWife =>
                    "principal_wife",
                RulerHouseholdConceptionKind.Consort => "consort",
                _ => ""
            };
        }

        public static RulerHouseholdConceptionKind ParseKind(
            string pKind)
        {
            return pKind switch
            {
                "principal_wife" =>
                    RulerHouseholdConceptionKind.PrincipalWife,
                "consort" => RulerHouseholdConceptionKind.Consort,
                _ => RulerHouseholdConceptionKind.None
            };
        }
    }
}
