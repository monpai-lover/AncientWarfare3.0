using System;

namespace AncientWarfare3.core.lineage
{
    public static class MandateAccessionCoordinator
    {
        public static bool TrySettle(Func<bool> pEnsureOpenReign,
            Func<bool> pIsInstalledKing, Func<bool> pCommitMandate)
        {
            return TrySettle(pEnsureOpenReign, pIsInstalledKing,
                pCommitMandate, null);
        }

        public static bool TrySettle(Func<bool> pEnsureOpenReign,
            Func<bool> pIsInstalledKing, Func<bool> pCommitMandate,
            Action pCommitProjection)
        {
            if (pEnsureOpenReign == null || !pEnsureOpenReign())
                return false;
            if (pIsInstalledKing == null || !pIsInstalledKing())
                return false;
            if (pCommitMandate == null || !pCommitMandate()) return false;
            pCommitProjection?.Invoke();
            return true;
        }
    }
}
