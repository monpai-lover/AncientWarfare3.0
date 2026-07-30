using System;

namespace AncientWarfare3.core.lineage
{
    internal static class DiplomacyExtinctionService
    {
        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.id < 0) return;
            var manager = core.db.LineageArchiveManager.Instance;
            if (manager?.OperatingDB == null ||
                !manager.InitializeSuccessful) return;
            int year;
            double time;
            try { year = Date.getCurrentYear(); }
            catch { year = 0; }
            try { time = LineageService.CurTime(); }
            catch { time = -1d; }
            if (!DiplomacyExtinctionPersistence.CloseRealm(
                    manager.OperatingDB, pKingdom.id, year, time))
            {
                ModClass.LogWarning(
                    "Diplomatic extinction cleanup failed for kingdom " +
                    pKingdom.id);
                return;
            }
            DiplomacyProposalService.OnKingdomDestroyed(pKingdom.id);
            DiplomaticRelationModifierService.ClearRuntime();
        }
    }
}
