using System;

namespace AncientWarfare3.core.lineage
{
    internal static class KingdomRenameSyncService
    {
        [ThreadStatic]
        private static int _suppressDepth;

        public static bool IsSuppressed => _suppressDepth > 0;

        public static void Suppress(Action pAction)
        {
            if (pAction == null) return;
            _suppressDepth++;
            try { pAction(); }
            finally { _suppressDepth = Math.Max(0, _suppressDepth - 1); }
        }

        public static void OnKingdomNameChanged(Kingdom pKingdom,
            string pOldName, string pNewName, bool pTrack)
        {
            bool archivable = KingdomArchiveWriter.IsArchivable(pKingdom);
            if (archivable) KingdomArchiveWriter.Upsert(pKingdom);
            if (!KingdomRenameRules.ShouldRecordRename(pOldName, pNewName,
                    pTrack, archivable, IsSuppressed)) return;
            RecordRenameEvent(pKingdom, pOldName, pNewName);
        }

        private static void RecordRenameEvent(Kingdom pKingdom,
            string pOldName, string pNewName)
        {
            string color = HistoryColors.FromKingdom(pKingdom);
            HistoryText oldText = HistoryText.Colored(pOldName ?? "", color);
            HistoryText newText = HistoryText.Colored(
                pNewName ?? pKingdom.name ?? "", color);
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RENAMED,
                oldText + HistoryLocalizationRules.H("aw_hist_kingdom_renamed_mid") +
                newText, HistoryTarget.Kingdom(pKingdom));
        }
    }
}
