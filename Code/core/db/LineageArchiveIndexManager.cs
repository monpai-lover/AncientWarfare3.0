using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.db
{
    internal static class LineageArchiveIndexManager
    {
        public static void EnsureIndexes(SQLiteConnection pDb)
        {
            if (pDb == null) return;

            foreach (LineageArchiveIndexSpec spec in LineageArchiveIndexRules.GetRequiredIndexes())
            {
                if (spec == null) continue;
                try
                {
                    using var cmd = new SQLiteCommand(pDb);
                    cmd.CommandText = spec.BuildSql();
                    cmd.ExecuteNonQuery();
                }
                catch (Exception e)
                {
                    ModClass.LogWarning("LineageArchiveIndexManager: failed to create index " +
                                        spec.name + ": " + e.Message);
                }
            }
        }
    }
}
