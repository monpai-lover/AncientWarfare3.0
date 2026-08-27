using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.court
{
    public static class OfficialCareerHistoryReadService
    {
        public static IReadOnlyList<OfficialCareerHistoryRow> Read(
            OfficialCareerHistoryScope pScope, int limit = 64)
        {
            SQLiteConnection db =
                LineageArchiveManager.Instance?.OperatingDB;
            return OfficialCareerHistoryRules.CollapseTechnicalTransitions(
                OfficialCareerHistoryQuery.Read(db, pScope, limit));
        }
    }
}
