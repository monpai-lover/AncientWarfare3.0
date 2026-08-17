namespace AncientWarfare3.ui
{
    internal static class AW_LineageWindowIds
    {
        public const string OVERVIEW = "aw_lineage_overview";
        public const string SHI_LIST = "aw_shi_list";
        public const string FAMILY_TREE = "aw_family_tree";
        public const string HISTORY = "aw_history";
        internal const string KINGDOM_ATLAS = "aw_kingdom_atlas";
        public const string KINGDOM_ROSTER = "aw_kingdom_roster";
        public const string POLICY_TREE = "aw_policy_tree";
        public const string ANCESTRY = "aw_ancestry_analysis";
        public const string MANDATE_DYNASTY = "aw_mandate_dynasty";
        public const string MANDATE_CYCLE = "aw_mandate_cycle";
        public const string MANDATE_DECISIONS = "aw_mandate_decisions";
        public const string VASSAL_RELATIONS = "aw_vassal_relations";
        public const string WAR_TARGETS = "aw_war_targets";
        public const string COURT = "aw_court";
        public const string CUSTOM_COURT_WORKFLOW = "aw_custom_court_workflow";
        public const string HAREM = "aw_ruler_household";
        public const string HOUSEHOLD_OFFER = "aw_ruler_household_offer";
        public const string CIVIL_SERVICE_EXAM = "aw_civil_service_exam";
        public const string COURT_APPOINTMENT = "aw_court_appointment";
        public const string COURT_DISPOSITION = "aw_court_disposition";
        public const string COURT_AUXILIARY_LAWS = "aw_court_auxiliary_laws";
        public const string INHERITANCE_LAWS = "aw_inheritance_laws";
        public const string SCHOOL = "aw_school_browser";
        public const string SCHOOL_ROSTER = "aw_school_roster";
        public const string NAME_DECISION = "aw_name_decision";
        public const string CONFERRED_POSTHUMOUS = "aw_conferred_posthumous";
        public const string CENTRAL_POWER = "aw_central_power";
        public const string FEUDATORIES = "aw_feudatories";
        public const string DIPLOMACY_CONVERSATIONS = "aw_diplomacy_conversations";
        public const string DIPLOMATIC_WAR_DECLARATION = "aw_diplomatic_war_declaration";
        public const string DIPLOMATIC_MARRIAGE = "aw_diplomatic_marriage";
        public const string SUPPORTERS = "aw_supporters";
        public const string VIRTUAL_TITLES = "aw_virtual_titles";
        public const string MILITARY_GOVERNORATE =
            "aw_military_governorate_window";

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

        public static bool ShowKingdom(long pKingdomId)
        {
            Kingdom kingdom = World.world?.kingdoms?.get(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt()) return false;
            ScrollWindow.finishAnimations();
            MetaType.Kingdom.getAsset().selectAndInspect(kingdom, pFromNameplate: false, pCheckNameplate: false);
            return true;
        }
    }
}
