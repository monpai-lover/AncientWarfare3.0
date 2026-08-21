using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MandateAnnualDeltaFacts
    {
        public bool AtWar;
        public int StrongestPowerPenalty;
        public float CoreControl;
        public float VassalLoyalty;
        public int ChildScarcityPenalty;
        public int SacrificeAnnualDelta;
        public bool KingIsHistoricalFigure;
        public bool KingIsYoung;
        public bool KingHasLowIntelligence;
        public bool KingHasHighDiplomacy;
        public bool KingHasHighStewardship;
        public string EraId = "";
    }

    internal readonly struct MandateAnnualDeltaEntry
    {
        internal MandateAnnualDeltaEntry(string pSourceId, int pDelta)
        {
            SourceId = pSourceId ?? "";
            Delta = pDelta;
        }

        public string SourceId { get; }
        public int Delta { get; }
    }

    internal sealed class MandateAnnualDeltaBreakdown
    {
        internal MandateAnnualDeltaBreakdown(
            IReadOnlyList<MandateAnnualDeltaEntry> pEntries, int pTotal)
        {
            Entries = pEntries ?? Array.Empty<MandateAnnualDeltaEntry>();
            Total = pTotal;
        }

        public IReadOnlyList<MandateAnnualDeltaEntry> Entries { get; }
        public int Total { get; }
    }

    internal static class MandateAnnualDeltaRules
    {
        internal static MandateAnnualDeltaBreakdown Calculate(
            MandateAnnualDeltaFacts pFacts)
        {
            pFacts ??= new MandateAnnualDeltaFacts();
            var entries = new List<MandateAnnualDeltaEntry>(12);
            Add(entries, pFacts.AtWar ? "war" : "peace",
                pFacts.AtWar ? -2 : 1);
            Add(entries, "strongest_power",
                pFacts.StrongestPowerPenalty);

            if (pFacts.CoreControl >= 0.85f)
                Add(entries, "core_control_high", 2);
            else if (pFacts.CoreControl < 0.5f)
                Add(entries, "core_control_low", -4);

            if (pFacts.VassalLoyalty >= 0.7f)
                Add(entries, "vassal_loyalty_high", 1);
            else if (pFacts.VassalLoyalty < 0.35f)
                Add(entries, "vassal_loyalty_low", -2);

            Add(entries, "heir_scarcity",
                pFacts.ChildScarcityPenalty);
            Add(entries, "sacrifice", pFacts.SacrificeAnnualDelta);
            if (pFacts.KingIsHistoricalFigure)
                Add(entries, "historical_emperor", 5);
            if (pFacts.KingIsYoung)
                Add(entries, "young_emperor", -1);
            if (pFacts.KingHasLowIntelligence)
                Add(entries, "low_intelligence", -1);
            if (pFacts.KingHasHighDiplomacy)
                Add(entries, "high_diplomacy", 1);
            if (pFacts.KingHasHighStewardship)
                Add(entries, "high_stewardship", 1);

            if (pFacts.EraId == "age_hope" ||
                pFacts.EraId == "age_wonders")
                Add(entries, "prosperous_era", 2);
            else if (pFacts.EraId == "age_despair" ||
                     pFacts.EraId == "age_ash" ||
                     pFacts.EraId == "age_chaos")
                Add(entries, "dark_era", -12);

            int total = 0;
            for (int index = 0; index < entries.Count; index++)
                total += entries[index].Delta;
            return new MandateAnnualDeltaBreakdown(entries, total);
        }

        private static void Add(List<MandateAnnualDeltaEntry> pEntries,
            string pSourceId, int pDelta)
        {
            if (pDelta == 0) return;
            pEntries.Add(new MandateAnnualDeltaEntry(pSourceId, pDelta));
        }
    }
}
