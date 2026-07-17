using System;

namespace AncientWarfare3.core.lineage
{
    [Flags]
    public enum RulerTraitFlags : long
    {
        None = 0,
        Ambitious = 1L << 0,
        Content = 1L << 1,
        Honest = 1L << 2,
        Deceitful = 1L << 3,
        Greedy = 1L << 4,
        Lustful = 1L << 5,
        Gluttonous = 1L << 6,
        Paranoid = 1L << 7,
        Peaceful = 1L << 8,
        Evil = 1L << 9,
        Psychopath = 1L << 10,
        Bloodlust = 1L << 11,
        Strong = 1L << 12,
        Weak = 1L << 13,
        FragileHealth = 1L << 14,
        Genius = 1L << 15,
        Wise = 1L << 16,
        Stupid = 1L << 17,
        Veteran = 1L << 18,
        Kingslayer = 1L << 19,
        Madness = 1L << 20,
        Attractive = 1L << 21,
        Hotheaded = 1L << 22,
        Patient = 1L << 23,
        Compassionate = 1L << 24,
        Generous = 1L << 25,
        Diligent = 1L << 26,
        Just = 1L << 27,
        Tough = 1L << 28,
        Fertile = 1L << 29,
        Cruel = 1L << 30,
        Crippled = 1L << 31,
        Slow = 1L << 32,
        StrongMinded = 1L << 33,
        Pacifist = 1L << 34
    }

    public sealed class RulerTitleFacts
    {
        public long ActorId = -1;
        public long KingdomId = -1;
        public long ReignId = -1;
        public long ShiId = -1;
        public long DynastyId = -1;
        public long MandatePeriodId = -1;
        public string ActorName = "";
        public string StateName = "";
        public string KingdomColor = "";
        public string EndReason = "";
        public string DeathCause = "";
        public int HighestTitle;
        public int Age;
        public int ReignYears;
        public int ReignIndex;
        public int StartYear;
        public int EndYear;
        public int Diplomacy;
        public int Warfare;
        public int Stewardship;
        public int Intelligence;
        public int Health;
        public int Combat;
        public int StartPopulation;
        public int EndPopulation;
        public int EndCityCount;
        public int CityDelta;
        public int WarWins;
        public int WarLosses;
        public int CapitalMoves;
        public int MajorReforms;
        public int OrderDelta;
        public int ImperialAuthority;
        public int MandateValue;
        public int OffensiveWars;
        public int AtrocityCount;
        public bool IsMandate;
        public bool IsFounder;
        public bool IsLowOrigin;
        public bool IsAutonomousRefounder;
        public bool IsFounderDirectHeir;
        public bool LostCapital;
        public bool HasBiologicalChildren;
        public bool HasKnownPatriline;
        public bool WasFormerMandateShi;
        public bool RegainedMandate;
        public bool RestoredLegalCore;
        public bool CentralizationRaised;
        public bool HasSchoolIdentity;
        public bool ForeignLineAdoption;
        public bool CollateralSuccession;
        public bool FoundedCadetBranch;
        public bool RitualPolicyComplete;
        public RulerTraitFlags Traits;

        public RulerTitleFacts Clone()
        {
            return (RulerTitleFacts)MemberwiseClone();
        }
    }

    public sealed class RulerTitleDerivedFacts
    {
        public bool Diligent;
        public bool Just;
        public bool Ambitious;
        public bool Compassionate;
        public bool Generous;
        public bool Patient;
        public bool Scholar;
        public bool Administrator;
        public bool Strategist;
        public bool Brave;
        public bool FamilyFirst;
        public bool Healthy;
        public bool Frail;
        public bool StableOrder;
        public bool MajorReform;
        public bool GreatConquest;
        public bool SmallRealm;
        public bool GraveCrime;
        public int CivilScore;
        public int MartialScore;
    }

    public sealed class RulerPersonalFacts
    {
        public long ActorId = -1;
        public int Diplomacy;
        public int Warfare;
        public int Stewardship;
        public int Intelligence;
        public int Health;
        public int Combat;
        public RulerTraitFlags Traits;
        public double DecidedTime;
    }
}
