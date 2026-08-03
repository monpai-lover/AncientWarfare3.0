using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateAccessionCoordinator
    {
        public static bool TrySettle(Func<bool> pEnsureOpenReign,
            Func<bool> pIsInstalledKing, Func<bool> pCommitMandate)
        {
            if (pEnsureOpenReign == null || !pEnsureOpenReign())
                return false;
            if (pIsInstalledKing == null || !pIsInstalledKing())
                return false;
            return pCommitMandate != null && pCommitMandate();
        }
    }
}
