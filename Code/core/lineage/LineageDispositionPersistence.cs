using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class LineageDispositionPersistence
    {
        private const string ActorArchive = "ActorArchive";
        private const string FamilyEdge = "FamilyEdge";
        private const string ShiBranch = "ShiBranch";

        public static bool GrantSurname(SQLiteConnection pDb,
            IReadOnlyList<long> pActorIds, long pLineageId, long pShiId,
            string pFamilyName, string pClanName, double pWorldTime)
        {
            if (!Valid(pDb, pActorIds) || pLineageId < 0 || pShiId < 0 ||
                string.IsNullOrWhiteSpace(pFamilyName) ||
                string.IsNullOrWhiteSpace(pClanName)) return false;
            try
            {
                using SQLiteTransaction transaction = pDb.BeginTransaction();
                foreach (long actorId in DistinctIds(pActorIds))
                {
                    using var update = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    update.CommandText = "UPDATE " + ActorArchive +
                        " SET FAMILY_NAME=@family,CLAN_NAME=@clan," +
                        "LINEAGE_ID=@lineage,SHI_ID=@shi,STATUS='noble'," +
                        "NOBLE_DISTANCE=0 WHERE ID=@actor AND IS_ALIVE=1";
                    update.Parameters.AddWithValue("@family", pFamilyName);
                    update.Parameters.AddWithValue("@clan", pClanName);
                    update.Parameters.AddWithValue("@lineage", pLineageId);
                    update.Parameters.AddWithValue("@shi", pShiId);
                    update.Parameters.AddWithValue("@actor", actorId);
                    RequireOne(update.ExecuteNonQuery(),
                        "surname grant archive update");

                    using var edge = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    edge.CommandText = "UPDATE " + FamilyEdge +
                        " SET CHILD_LINEAGE_ID=@lineage WHERE CHILD_ID=@actor";
                    edge.Parameters.AddWithValue("@lineage", pLineageId);
                    edge.Parameters.AddWithValue("@actor", actorId);
                    edge.ExecuteNonQuery();
                }
                HistoricalContentRevision
                    .AdvanceAfterSuccessfulSynchronousWrite(
                        transaction.Commit);
                return true;
            }
            catch (SQLiteException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static bool Expel(SQLiteConnection pDb,
            IReadOnlyList<long> pActorIds, long newShiId, long lineageId,
            long parentShiId, string clanName, long founderActorId,
            long originKingdomId, long originCityId,
            long originOriginalClanId, int year, double worldTime)
        {
            if (!Valid(pDb, pActorIds) || newShiId < 0 || lineageId < 0 ||
                parentShiId < 0 || founderActorId < 0 ||
                string.IsNullOrWhiteSpace(clanName)) return false;
            try
            {
                using SQLiteTransaction transaction = pDb.BeginTransaction();
                using (var insert = new SQLiteCommand(pDb)
                       { Transaction = transaction })
                {
                    insert.CommandText = "INSERT INTO " + ShiBranch +
                        " (SHI_ID,LINEAGE_ID,CLAN_NAME,PARENT_SHI_ID," +
                        "SOURCE_TYPE,FOUNDER_ACTOR_ID,ORIGIN_KINGDOM_ID," +
                        "ORIGIN_CITY_ID,ORIGIN_ORIGINAL_CLAN_ID," +
                        "CREATED_TIME,IS_EXTINCT) VALUES " +
                        "(@shi,@lineage,@clan,@parent,'court_expulsion'," +
                        "@founder,@kingdom,@city,@original,@time,0)";
                    insert.Parameters.AddWithValue("@shi", newShiId);
                    insert.Parameters.AddWithValue("@lineage", lineageId);
                    insert.Parameters.AddWithValue("@clan", clanName);
                    insert.Parameters.AddWithValue("@parent", parentShiId);
                    insert.Parameters.AddWithValue("@founder", founderActorId);
                    insert.Parameters.AddWithValue("@kingdom", originKingdomId);
                    insert.Parameters.AddWithValue("@city", originCityId);
                    insert.Parameters.AddWithValue("@original",
                        originOriginalClanId);
                    insert.Parameters.AddWithValue("@time", worldTime);
                    RequireOne(insert.ExecuteNonQuery(),
                        "expulsion Shi insert");
                }

                IReadOnlyList<long> actorIds = DistinctIds(pActorIds);
                for (int i = 0; i < actorIds.Count; i++)
                {
                    using var update = new SQLiteCommand(pDb)
                        { Transaction = transaction };
                    update.CommandText = "UPDATE " + ActorArchive +
                        " SET CLAN_NAME=@clan,SHI_ID=@shi," +
                        "STATUS='common_lineage',NOBLE_DISTANCE=99" +
                        " WHERE ID=@actor AND IS_ALIVE=1";
                    update.Parameters.AddWithValue("@clan", clanName);
                    update.Parameters.AddWithValue("@shi", newShiId);
                    update.Parameters.AddWithValue("@actor", actorIds[i]);
                    RequireOne(update.ExecuteNonQuery(),
                        "expulsion archive update");
                }

                NobleRankRevocationPersistence.StageRevoke(pDb, transaction,
                    actorIds, year, worldTime, "court_expulsion");
                HistoricalContentRevision
                    .AdvanceAfterSuccessfulSynchronousWrite(
                        transaction.Commit);
                return true;
            }
            catch (SQLiteException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool Valid(SQLiteConnection pDb,
            IReadOnlyList<long> pActorIds)
        {
            return pDb != null && pActorIds != null && pActorIds.Count > 0 &&
                   pActorIds.Count <= LineageDispositionRules.MaximumMigrants;
        }

        private static IReadOnlyList<long> DistinctIds(
            IReadOnlyList<long> pActorIds)
        {
            var ids = new List<long>(pActorIds.Count);
            var seen = new HashSet<long>();
            for (int i = 0; i < pActorIds.Count; i++)
                if (pActorIds[i] >= 0 && seen.Add(pActorIds[i]))
                    ids.Add(pActorIds[i]);
            if (ids.Count != pActorIds.Count)
                throw new InvalidOperationException(
                    "lineage disposition actor IDs must be unique and valid");
            return ids;
        }

        private static void RequireOne(int pAffected, string pOperation)
        {
            if (pAffected != 1)
                throw new InvalidOperationException(pOperation +
                    " did not affect exactly one row");
        }
    }
}
