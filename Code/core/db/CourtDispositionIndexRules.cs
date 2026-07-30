using System.Collections.Generic;

namespace AncientWarfare3.core.db
{
    public sealed class CourtDispositionIndexSpec
    {
        public string Name = "";
        public string Table = "";
        public string Columns = "";

        public string BuildSql()
        {
            return "CREATE INDEX IF NOT EXISTS " + Name + " ON " +
                   Table + " (" + Columns + ")";
        }
    }

    public static class CourtDispositionIndexRules
    {
        public static IReadOnlyList<CourtDispositionIndexSpec>
            GetRequiredIndexes()
        {
            return new[]
            {
                new CourtDispositionIndexSpec
                {
                    Name = "idx_SocialAction_kingdom_time",
                    Table = "SocialAction",
                    Columns = "KINGDOM_ID, START_TIME, ACTION_ID"
                },
                new CourtDispositionIndexSpec
                {
                    Name = "idx_SocialAction_target_time",
                    Table = "SocialAction",
                    Columns = "TARGET_ACTOR_ID, START_TIME, ACTION_ID"
                }
            };
        }
    }
}
