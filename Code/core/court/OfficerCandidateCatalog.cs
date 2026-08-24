using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// Per-kingdom yearly actor catalog used by vacancy repair. Qualification
    /// remains live at appointment time; this only avoids repeated unit scans.
    /// </summary>
    internal static class OfficerCandidateCatalog
    {
        private sealed class Entry
        {
            internal int Year;
            internal List<Actor> Actors = new List<Actor>();
        }

        private static readonly Dictionary<long, Entry> Entries =
            new Dictionary<long, Entry>();

        internal static List<Actor> GetOrBuild(Kingdom pKingdom, int pYear)
        {
            if (pKingdom?.data == null) return new List<Actor>();
            if (Entries.TryGetValue(pKingdom.id, out Entry existing) &&
                existing.Year == pYear)
                return existing.Actors;
            var actors = new List<Actor>();
            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                    if (actor?.data != null) actors.Add(actor);
            }
            catch { }
            actors = actors.OrderByDescending(p =>
                    OfficialCareerStateService.ReadRankFast(p))
                .ThenBy(p => p.data.id).ToList();
            Entries[pKingdom.id] = new Entry { Year = pYear, Actors = actors };
            return actors;
        }

        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data != null) Entries.Remove(pKingdom.id);
        }

        internal static void ClearRuntime() => Entries.Clear();
    }
}
