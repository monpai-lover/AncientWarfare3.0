namespace AncientWarfare3.core.lineage
{
    public enum AristocraticVacancyDecision
    {
        Defer,
        InstallHouse,
        ElectRepublic
    }

    public readonly struct AristocraticRulerScore
    {
        public readonly long ActorId;
        public readonly bool IsChief;
        public readonly int Diplomacy;
        public readonly int Warfare;
        public readonly int Stewardship;
        public readonly int Level;
        public readonly float CombatStrength;
        public readonly int Age;

        public AristocraticRulerScore(long actorId, bool isChief, int diplomacy,
            int warfare, int stewardship, int level, float combatStrength, int age)
        {
            ActorId = actorId;
            IsChief = isChief;
            Diplomacy = diplomacy;
            Warfare = warfare;
            Stewardship = stewardship;
            Level = level;
            CombatStrength = combatStrength;
            Age = age;
        }

        public int GoverningScore => Diplomacy + Warfare + Stewardship;
    }

    public readonly struct AristocraticHouseScore
    {
        public readonly long ClanId;
        public readonly int Renown;
        public readonly int OfficeHolders;
        public readonly int RealmMembers;
        public readonly int EligibleAdultMales;
        public readonly AristocraticRulerScore BestRuler;

        public AristocraticHouseScore(long clanId, int renown, int officeHolders,
            int realmMembers, int eligibleAdultMales, AristocraticRulerScore bestRuler)
        {
            ClanId = clanId;
            Renown = renown;
            OfficeHolders = officeHolders;
            RealmMembers = realmMembers;
            EligibleAdultMales = eligibleAdultMales;
            BestRuler = bestRuler;
        }
    }

    public static class AristocraticSuccessionRules
    {
        public static AristocraticVacancyDecision DecideVacancy(bool successionPending,
            bool hasHereditaryHeir, bool hasHouseCandidate, int electableCount,
            bool monarchyEstablished)
        {
            if (successionPending || hasHereditaryHeir || !monarchyEstablished)
                return AristocraticVacancyDecision.Defer;
            if (hasHouseCandidate) return AristocraticVacancyDecision.InstallHouse;
            return electableCount > 0
                ? AristocraticVacancyDecision.ElectRepublic
                : AristocraticVacancyDecision.Defer;
        }

        public static bool IsEligibleRuler(bool inLineageSystem, bool hasVisibleClan,
            bool isMale, bool isAdult, bool isAlive, bool isSlave, bool isKing)
        {
            return inLineageSystem && hasVisibleClan && isMale && isAdult &&
                   isAlive && !isSlave && !isKing;
        }

        public static int CompareRulers(AristocraticRulerScore left,
            AristocraticRulerScore right)
        {
            int result = right.IsChief.CompareTo(left.IsChief);
            if (result != 0) return result;
            result = right.GoverningScore.CompareTo(left.GoverningScore);
            if (result != 0) return result;
            result = right.Level.CompareTo(left.Level);
            if (result != 0) return result;
            result = right.CombatStrength.CompareTo(left.CombatStrength);
            if (result != 0) return result;
            result = right.Age.CompareTo(left.Age);
            if (result != 0) return result;
            return left.ActorId.CompareTo(right.ActorId);
        }

        public static int CompareHouses(AristocraticHouseScore left,
            AristocraticHouseScore right)
        {
            int result = right.Renown.CompareTo(left.Renown);
            if (result != 0) return result;
            result = right.OfficeHolders.CompareTo(left.OfficeHolders);
            if (result != 0) return result;
            result = right.RealmMembers.CompareTo(left.RealmMembers);
            if (result != 0) return result;
            result = right.EligibleAdultMales.CompareTo(left.EligibleAdultMales);
            if (result != 0) return result;
            result = CompareRulers(left.BestRuler, right.BestRuler);
            if (result != 0) return result;
            return left.ClanId.CompareTo(right.ClanId);
        }
    }
}
