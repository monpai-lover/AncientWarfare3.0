using System;

namespace AncientWarfare3.core.lineage
{
    public sealed class ConferredPosthumousFactInput
    {
        public RulerTitleFacts ReignFacts;
        public long ActorId = -1;
        public long KingdomId = -1;
        public long ShiId = -1;
        public long DynastyId = -1;
        public string ActorName = "";
        public string StateName = "";
        public string KingdomColor = "";
        public string DeathCause = "";
        public int Age;
        public int NobleRank;
        public int ServiceYears;
        public int HighestOfficeRank;
        public int CivilMerit;
        public int GeneralMerit;
        public int TroopPower;
        public int Diplomacy;
        public int Warfare;
        public int Stewardship;
        public int Intelligence;
        public int Health;
        public int Combat;
        public bool HasPersonalSnapshot;
        public bool HasKnownPatriline;
        public bool HasBiologicalChildren;
        public bool HasSchoolIdentity;
        public RulerTraitFlags Traits;
    }

    public static class ConferredPosthumousFactRules
    {
        public static RulerTitleFacts MapArchivedFacts(
            ConferredPosthumousFactInput pInput)
        {
            pInput ??= new ConferredPosthumousFactInput();
            if (pInput.ReignFacts != null)
                return CompleteReignFacts(pInput.ReignFacts.Clone(), pInput);

            return new RulerTitleFacts
            {
                ActorId = pInput.ActorId,
                KingdomId = pInput.KingdomId,
                ReignId = -1,
                ShiId = pInput.ShiId,
                DynastyId = pInput.DynastyId,
                MandatePeriodId = -1,
                ActorName = Normalize(pInput.ActorName),
                StateName = Normalize(pInput.StateName),
                KingdomColor = Normalize(pInput.KingdomColor),
                EndReason = "died",
                DeathCause = Normalize(pInput.DeathCause),
                HighestTitle = HighestRulerTitleForNobleRank(pInput.NobleRank),
                Age = Math.Max(0, pInput.Age),
                ReignYears = Math.Max(1, pInput.ServiceYears),
                Diplomacy = Math.Max(0, pInput.Diplomacy),
                Warfare = Math.Max(0, pInput.Warfare),
                Stewardship = Math.Max(0, pInput.Stewardship),
                Intelligence = Math.Max(0, pInput.Intelligence),
                Health = Math.Max(0, pInput.Health),
                Combat = Math.Max(0, pInput.Combat),
                WarWins = MeritCredit(pInput.GeneralMerit),
                MajorReforms = MeritCredit(pInput.CivilMerit),
                OrderDelta = pInput.CivilMerit > 0 ? 1 : 0,
                EndCityCount = 1,
                HasKnownPatriline = pInput.HasKnownPatriline,
                HasBiologicalChildren = pInput.HasBiologicalChildren,
                HasSchoolIdentity = pInput.HasSchoolIdentity,
                Traits = pInput.Traits
            };
        }

        public static int HighestRulerTitleForNobleRank(int pNobleRank)
        {
            int rank = Math.Max(0, Math.Min(8, pNobleRank));
            if (rank >= 8) return 3;
            if (rank >= 5) return 2;
            if (rank >= 4) return 1;
            return 0;
        }

        private static RulerTitleFacts CompleteReignFacts(
            RulerTitleFacts pFacts, ConferredPosthumousFactInput pInput)
        {
            if (pFacts.ActorId < 0) pFacts.ActorId = pInput.ActorId;
            if (pFacts.KingdomId < 0) pFacts.KingdomId = pInput.KingdomId;
            if (pFacts.ShiId < 0) pFacts.ShiId = pInput.ShiId;
            if (pFacts.DynastyId < 0) pFacts.DynastyId = pInput.DynastyId;
            if (!string.IsNullOrWhiteSpace(pInput.ActorName))
                pFacts.ActorName = pInput.ActorName.Trim();
            if (string.IsNullOrWhiteSpace(pFacts.StateName))
                pFacts.StateName = Normalize(pInput.StateName);
            if (string.IsNullOrWhiteSpace(pFacts.KingdomColor))
                pFacts.KingdomColor = Normalize(pInput.KingdomColor);
            if (!string.IsNullOrWhiteSpace(pInput.DeathCause))
                pFacts.DeathCause = pInput.DeathCause.Trim();
            if (pInput.Age > 0) pFacts.Age = pInput.Age;
            if (pInput.HasPersonalSnapshot)
            {
                pFacts.Diplomacy = Math.Max(0, pInput.Diplomacy);
                pFacts.Warfare = Math.Max(0, pInput.Warfare);
                pFacts.Stewardship = Math.Max(0, pInput.Stewardship);
                pFacts.Intelligence = Math.Max(0, pInput.Intelligence);
                pFacts.Health = Math.Max(0, pInput.Health);
                pFacts.Combat = Math.Max(0, pInput.Combat);
                pFacts.Traits = pInput.Traits;
            }
            if (pFacts.ReignYears <= 0)
                pFacts.ReignYears = Math.Max(1, pInput.ServiceYears);
            return pFacts;
        }

        private static int MeritCredit(int pMerit)
        {
            return Math.Max(0, pMerit) / 100;
        }

        private static string Normalize(string pValue)
        {
            return pValue?.Trim() ?? "";
        }
    }
}
