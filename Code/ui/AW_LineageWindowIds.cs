namespace AncientWarfare3.ui
{
    internal static class AW_LineageWindowIds
    {
        public const string OVERVIEW = "aw_lineage_overview";
        public const string SHI_LIST = "aw_shi_list";
        public const string FAMILY_TREE = "aw_family_tree";
        public const string HISTORY = "aw_history";
        public const string KINGDOM_ROSTER = "aw_kingdom_roster";
        public const string POLICY_TREE = "aw_policy_tree";
        public const string ANCESTRY = "aw_ancestry_analysis";
        public const string MANDATE_DYNASTY = "aw_mandate_dynasty";
        public const string MANDATE_DECISIONS = "aw_mandate_decisions";
        public const string VASSAL_RELATIONS = "aw_vassal_relations";
        public const string WAR_TARGETS = "aw_war_targets";
        public const string COURT = "aw_court";
        public const string SCHOOL = "aw_school_browser";
        public const string SCHOOL_ROSTER = "aw_school_roster";

        public static void SafeShow(string pWindowId, System.Action pRefreshIfCurrent = null)
        {
            if (ScrollWindow.isCurrentWindow(pWindowId))
            {
                pRefreshIfCurrent?.Invoke();
                return;
            }

            ScrollWindow.finishAnimations();

            bool hasCurrent = ScrollWindow.getCurrentWindow() != null;
            ScrollWindow.showWindow(pWindowId, false, hasCurrent);
        }
    }
}
