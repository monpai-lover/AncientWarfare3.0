using System;

namespace AncientWarfare3.core.court
{
    public readonly struct FeudatoryOfficeCandidateFacts
    {
        public FeudatoryOfficeCandidateFacts(bool alive, bool adult, bool male,
            bool sameKingdom, bool king, bool heir, bool prince, bool slave,
            bool madness, bool asylum, bool hasIncompatibleOffice,
            bool cityLeader = false, bool general = false)
        {
            Alive = alive;
            Adult = adult;
            Male = male;
            SameKingdom = sameKingdom;
            King = king;
            Heir = heir;
            Prince = prince;
            Slave = slave;
            Madness = madness;
            Asylum = asylum;
            HasIncompatibleOffice = hasIncompatibleOffice;
            CityLeader = cityLeader;
            General = general;
        }

        public bool Alive { get; }
        public bool Adult { get; }
        public bool Male { get; }
        public bool SameKingdom { get; }
        public bool King { get; }
        public bool Heir { get; }
        public bool Prince { get; }
        public bool Slave { get; }
        public bool Madness { get; }
        public bool Asylum { get; }
        public bool HasIncompatibleOffice { get; }
        public bool CityLeader { get; }
        public bool General { get; }
    }

    public static class FeudatoryOfficeRules
    {
        public const int MaxCandidateScan = 32;
        public const int PrinceRank = 50;
        public const int InspectorRank = 60;

        public static bool CanServe(FeudatoryOfficeCandidateFacts pFacts)
        {
            return pFacts.Alive && pFacts.Adult && pFacts.Male &&
                   pFacts.SameKingdom && !pFacts.King && !pFacts.Heir &&
                   !pFacts.Prince && !pFacts.Slave && !pFacts.Madness &&
                   !pFacts.Asylum && !pFacts.HasIncompatibleOffice &&
                   !pFacts.CityLeader && !pFacts.General;
        }

        public static float CandidateScore(float stewardship, float diplomacy,
            float intelligence, float warfare)
        {
            return stewardship * 0.45f + diplomacy * 0.30f +
                   intelligence * 0.20f + warfare * 0.05f;
        }

        public static int NextCandidateCursor(int currentCursor,
            int residentCount, int scannedCount)
        {
            if (residentCount <= 0) return 0;
            int current = currentCursor % residentCount;
            if (current < 0) current += residentCount;
            int next = (current + Math.Max(0, scannedCount)) % residentCount;
            return next;
        }
    }
}
