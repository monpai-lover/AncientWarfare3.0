using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal static class NobleRankRevocationPersistence
    {
        private const string Table = "Enfeoffment";

        public static int StageRevoke(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, IReadOnlyList<long> pActorIds,
            int pEndYear, double pEndTime, string pReason)
        {
            if (pDb == null || pTransaction == null || pActorIds == null)
                throw new ArgumentException("invalid noble revocation stage");
            int closed = 0;
            var seen = new HashSet<long>();
            for (int i = 0; i < pActorIds.Count; i++)
            {
                long actorId = pActorIds[i];
                if (actorId < 0 || !seen.Add(actorId)) continue;
                using var update = new SQLiteCommand(pDb)
                    { Transaction = pTransaction };
                update.CommandText = "UPDATE " + Table +
                    " SET ACTIVE=0,END_YEAR=@year,END_TIME=@time," +
                    "END_REASON=@reason WHERE ACTOR_ID=@actor AND ACTIVE=1";
                update.Parameters.AddWithValue("@year", pEndYear);
                update.Parameters.AddWithValue("@time", pEndTime);
                update.Parameters.AddWithValue("@reason", pReason ?? "");
                update.Parameters.AddWithValue("@actor", actorId);
                int affected = update.ExecuteNonQuery();
                if (affected > 1)
                    throw new InvalidOperationException(
                        "one actor owns multiple active noble grants");
                closed += affected;
            }
            return closed;
        }
    }
}
