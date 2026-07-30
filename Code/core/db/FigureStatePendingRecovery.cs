using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.db
{
    public static class FigureStatePendingRecovery
    {
        private readonly struct PendingOwner
        {
            public PendingOwner(long pIndex, long pActorId,
                long pLineageId, long pShiId)
            {
                Index = pIndex;
                ActorId = pActorId;
                LineageId = pLineageId;
                ShiId = pShiId;
            }

            public long Index { get; }
            public long ActorId { get; }
            public long LineageId { get; }
            public long ShiId { get; }
        }

        public static void Recover(SQLiteConnection pDb, string pFigureTable,
            string pShiTable, string pLineageTable, string pSourceType)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            List<PendingOwner> owners = ReadPendingOwners(pDb, transaction,
                pFigureTable);
            foreach (PendingOwner owner in owners)
            {
                CleanupOwnedLineage(pDb, transaction, pShiTable,
                    pLineageTable, pSourceType, owner);
                if (ResetPending(pDb, transaction, pFigureTable, owner) != 1)
                    throw new InvalidOperationException(
                        "FigureState pending recovery lost reservation ownership.");
            }
            transaction.Commit();
        }

        public static bool TryAbort(SQLiteConnection pDb, string pFigureTable,
            string pShiTable, string pLineageTable, string pSourceType,
            long pIndex, long pActorId)
        {
            if (pDb == null || pIndex < 0 || pActorId < 0) return false;
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            PendingOwner? owner = ReadPendingOwner(pDb, transaction,
                pFigureTable, pIndex, pActorId);
            if (!owner.HasValue)
            {
                transaction.Rollback();
                return false;
            }

            CleanupOwnedLineage(pDb, transaction, pShiTable, pLineageTable,
                pSourceType, owner.Value);
            if (ResetPending(pDb, transaction, pFigureTable, owner.Value) != 1)
            {
                transaction.Rollback();
                return false;
            }
            transaction.Commit();
            return true;
        }

        private static List<PendingOwner> ReadPendingOwners(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            string pFigureTable)
        {
            var result = new List<PendingOwner>();
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT FIGURE_INDEX,ACTOR_ID," +
                "PENDING_LINEAGE_ID,PENDING_SHI_ID FROM " + pFigureTable +
                " WHERE SPAWNED=2";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(new PendingOwner(reader.GetInt64(0),
                    reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
            return result;
        }

        private static PendingOwner? ReadPendingOwner(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pFigureTable,
            long pIndex, long pActorId)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT PENDING_LINEAGE_ID,PENDING_SHI_ID" +
                " FROM " + pFigureTable +
                " WHERE FIGURE_INDEX=@index AND SPAWNED=2" +
                " AND ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@index", pIndex);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new PendingOwner(pIndex, pActorId, reader.GetInt64(0),
                    reader.GetInt64(1))
                : null;
        }

        private static void CleanupOwnedLineage(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pShiTable,
            string pLineageTable, string pSourceType, PendingOwner pOwner)
        {
            if (pOwner.ActorId < 0 || pOwner.LineageId < 0 || pOwner.ShiId < 0)
                return;

            using (var deleteShi = new SQLiteCommand(pDb)
                   { Transaction = pTransaction })
            {
                deleteShi.CommandText = "DELETE FROM " + pShiTable +
                    " WHERE SHI_ID=@shi AND LINEAGE_ID=@lineage" +
                    " AND FOUNDER_ACTOR_ID=@actor AND SOURCE_TYPE=@source";
                AddOwnerParameters(deleteShi, pOwner);
                deleteShi.Parameters.AddWithValue("@source", pSourceType ?? "");
                deleteShi.ExecuteNonQuery();
            }

            using var deleteLineage = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            deleteLineage.CommandText = "DELETE FROM " + pLineageTable +
                " WHERE LINEAGE_ID=@lineage AND FOUNDER_ACTOR_ID=@actor" +
                " AND NOT EXISTS (SELECT 1 FROM " + pShiTable +
                " WHERE LINEAGE_ID=@lineage)";
            AddOwnerParameters(deleteLineage, pOwner);
            deleteLineage.ExecuteNonQuery();
        }

        private static int ResetPending(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pFigureTable,
            PendingOwner pOwner)
        {
            using var reset = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            reset.CommandText = "UPDATE " + pFigureTable +
                " SET SPAWNED=0,ACTOR_ID=-1,DEAD=0,KINGDOM_ID=-1," +
                "KINGDOM_NAME_APPLIED='',SPAWN_TIME=0," +
                "PENDING_LINEAGE_ID=-1,PENDING_SHI_ID=-1" +
                " WHERE FIGURE_INDEX=@index AND SPAWNED=2" +
                " AND ACTOR_ID=@actor AND PENDING_LINEAGE_ID=@lineage" +
                " AND PENDING_SHI_ID=@shi";
            reset.Parameters.AddWithValue("@index", pOwner.Index);
            AddOwnerParameters(reset, pOwner);
            return reset.ExecuteNonQuery();
        }

        private static void AddOwnerParameters(SQLiteCommand pCommand,
            PendingOwner pOwner)
        {
            pCommand.Parameters.AddWithValue("@actor", pOwner.ActorId);
            pCommand.Parameters.AddWithValue("@lineage", pOwner.LineageId);
            pCommand.Parameters.AddWithValue("@shi", pOwner.ShiId);
        }
    }
}
