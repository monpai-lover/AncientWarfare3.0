using System;

namespace AncientWarfare3.core.policy
{
    public static class CityMaintenanceBenchmarkRules
    {
        public const string Group = "game_total";
        public const string Total = "aw3_city_army_maint_total";
        public const string Retirements = "aw3_city_retirements";
        public const string SlaveLabor = "aw3_city_slave_labor";
        public const string SlaveCatchers = "aw3_city_slave_catchers";
        public const string RoyalGuard = "aw3_city_royal_guard";
        public const string FiefCommand = "aw3_city_fief_command";
        public const string ArmyCleanup = "aw3_city_army_cleanup";
        public const string Food = "aw3_city_food";
        public const string Status = "aw3_city_status";
        public const string Citizens = "aw3_city_citizens";
        public const string Capture = "aw3_city_capture";

        public static readonly string[] EntryIds =
        {
            Total,
            Retirements,
            SlaveLabor,
            SlaveCatchers,
            RoyalGuard,
            FiefCommand,
            ArmyCleanup,
            Food,
            Status,
            Citizens,
            Capture
        };

        public static bool Contains(string pId)
        {
            return Array.IndexOf(EntryIds, pId) >= 0;
        }
    }
}
