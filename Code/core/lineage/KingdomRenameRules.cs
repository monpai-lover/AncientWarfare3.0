namespace AncientWarfare3.core.lineage
{
    public static class KingdomRenameRules
    {
        public static bool ShouldRecordRename(string pOldName, string pNewName, bool pTrack,
            bool pArchivable, bool pSuppressed)
        {
            if (!pTrack || !pArchivable || pSuppressed) return false;
            string oldName = (pOldName ?? "").Trim();
            string newName = (pNewName ?? "").Trim();
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return false;
            return oldName != newName;
        }
    }
}
