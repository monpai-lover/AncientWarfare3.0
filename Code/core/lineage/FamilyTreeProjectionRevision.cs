using System.Threading;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    public enum FamilyTreeProjectionChange
    {
        None = 0,
        RulerAccession = 1,
        RankOrMandate = 2,
        Era = 3,
        DynastyOrStateName = 4,
        Heir = 5,
        LifeStatus = 6,
        PosthumousTitle = 7
    }

    public static class FamilyTreeProjectionRevisionRules
    {
        public static bool ShouldAdvance(FamilyTreeProjectionChange change)
        {
            return change != FamilyTreeProjectionChange.None;
        }
    }

    internal static class FamilyTreeProjectionRevision
    {
        private static long _revision = 1L;

        public static long Current => Interlocked.Read(ref _revision);

        public static long Advance(FamilyTreeProjectionChange change)
        {
            if (!FamilyTreeProjectionRevisionRules.ShouldAdvance(change))
                return Current;

            HistoricalContentRevision.Advance();
            while (true)
            {
                long current = Interlocked.Read(ref _revision);
                if (current == long.MaxValue) return current;
                long next = current + 1L;
                if (Interlocked.CompareExchange(ref _revision, next,
                        current) == current)
                    return next;
            }
        }
    }
}
