using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolAcademyRepairPersistence
    {
        internal static bool MarkRepairPending(SQLiteConnection db,
            string institutionTable, string ticketTable, long institutionId,
            long cityId, long buildingId, int tileX, int tileY,
            long ownerKingdomId, double updatedTime)
        {
            if (!Valid(db, institutionTable, ticketTable, institutionId, cityId))
                return false;
            using SQLiteTransaction transaction = db.BeginTransaction();
            try
            {
                using (var update = new SQLiteCommand(db) { Transaction = transaction })
                {
                    update.CommandText = "UPDATE " + institutionTable +
                        " SET ACTIVE=0,BUILDING_ID=@building,TILE_X=@x,TILE_Y=@y," +
                        "PHYSICAL_STATE='repair_pending',UPDATED_TIME=@time " +
                        "WHERE INSTITUTION_ID=@institution AND CITY_ID=@city";
                    AddIdentity(update, institutionId, cityId);
                    update.Parameters.AddWithValue("@building", buildingId);
                    update.Parameters.AddWithValue("@x", tileX);
                    update.Parameters.AddWithValue("@y", tileY);
                    update.Parameters.AddWithValue("@time", Finite(updatedTime));
                    if (update.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }

                using (var insert = new SQLiteCommand(db) { Transaction = transaction })
                {
                    insert.CommandText = "INSERT OR IGNORE INTO " + ticketTable +
                        " (INSTITUTION_ID,CITY_ID,BUILDING_ID,TILE_X,TILE_Y,STATE," +
                        "OWNER_KINGDOM_ID,OPERATION_KEY,UPDATED_TIME) VALUES " +
                        "(@institution,@city,@building,@x,@y,'repair_pending'," +
                        "@owner,@operation,@time)";
                    AddIdentity(insert, institutionId, cityId);
                    insert.Parameters.AddWithValue("@building", buildingId);
                    insert.Parameters.AddWithValue("@x", tileX);
                    insert.Parameters.AddWithValue("@y", tileY);
                    insert.Parameters.AddWithValue("@owner", ownerKingdomId);
                    insert.Parameters.AddWithValue("@operation",
                        HistoricalSchoolAcademyRepairRules.OperationKey(
                            institutionId, cityId));
                    insert.Parameters.AddWithValue("@time", Finite(updatedTime));
                    insert.ExecuteNonQuery();
                }
                using (var refresh = new SQLiteCommand(db) { Transaction = transaction })
                {
                    refresh.CommandText = "UPDATE " + ticketTable +
                        " SET CITY_ID=@city,BUILDING_ID=@building,TILE_X=@x," +
                        "TILE_Y=@y,STATE='repair_pending',OWNER_KINGDOM_ID=@owner," +
                        "UPDATED_TIME=@time WHERE INSTITUTION_ID=@institution";
                    AddIdentity(refresh, institutionId, cityId);
                    refresh.Parameters.AddWithValue("@building", buildingId);
                    refresh.Parameters.AddWithValue("@x", tileX);
                    refresh.Parameters.AddWithValue("@y", tileY);
                    refresh.Parameters.AddWithValue("@owner", ownerKingdomId);
                    refresh.Parameters.AddWithValue("@time", Finite(updatedTime));
                    if (refresh.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                return false;
            }
        }

        internal static int RestoreMissingTickets(SQLiteConnection db,
            string institutionTable, string ticketTable, double updatedTime)
        {
            if (db == null || !Identifier(institutionTable) ||
                !Identifier(ticketTable)) return 0;
            using var command = new SQLiteCommand(db);
            command.CommandText = "INSERT OR IGNORE INTO " + ticketTable +
                " (INSTITUTION_ID,CITY_ID,BUILDING_ID,TILE_X,TILE_Y,STATE," +
                "OWNER_KINGDOM_ID,OPERATION_KEY,UPDATED_TIME) SELECT " +
                "I.INSTITUTION_ID,I.CITY_ID,I.BUILDING_ID,I.TILE_X,I.TILE_Y," +
                "I.PHYSICAL_STATE,-1,'school_academy_repair:' || " +
                "I.INSTITUTION_ID || ':' || I.CITY_ID,@time FROM " +
                institutionTable + " I WHERE I.PHYSICAL_STATE IN " +
                "('repair_pending','rebuilding') AND NOT EXISTS (SELECT 1 FROM " +
                ticketTable + " T WHERE T.INSTITUTION_ID=I.INSTITUTION_ID)";
            command.Parameters.AddWithValue("@time", Finite(updatedTime));
            return command.ExecuteNonQuery();
        }

        internal static bool MarkRebuilding(SQLiteConnection db,
            string institutionTable, string ticketTable, long institutionId,
            long buildingId, long ownerKingdomId, double updatedTime)
        {
            if (db == null || !Identifier(institutionTable) ||
                !Identifier(ticketTable) || institutionId < 0 || buildingId < 0)
                return false;
            using SQLiteTransaction transaction = db.BeginTransaction();
            try
            {
                int ticketRows;
                using (var ticket = new SQLiteCommand(db) { Transaction = transaction })
                {
                    ticket.CommandText = "UPDATE " + ticketTable +
                        " SET BUILDING_ID=@building,STATE='rebuilding'," +
                        "OWNER_KINGDOM_ID=@owner,UPDATED_TIME=@time " +
                        "WHERE INSTITUTION_ID=@institution";
                    ticket.Parameters.AddWithValue("@building", buildingId);
                    ticket.Parameters.AddWithValue("@owner", ownerKingdomId);
                    ticket.Parameters.AddWithValue("@time", Finite(updatedTime));
                    ticket.Parameters.AddWithValue("@institution", institutionId);
                    ticketRows = ticket.ExecuteNonQuery();
                }
                if (ticketRows != 1)
                {
                    transaction.Rollback();
                    return false;
                }
                using (var institution = new SQLiteCommand(db)
                       { Transaction = transaction })
                {
                    institution.CommandText = "UPDATE " + institutionTable +
                        " SET BUILDING_ID=@building,PHYSICAL_STATE='rebuilding'," +
                        "UPDATED_TIME=@time WHERE INSTITUTION_ID=@institution";
                    institution.Parameters.AddWithValue("@building", buildingId);
                    institution.Parameters.AddWithValue("@time", Finite(updatedTime));
                    institution.Parameters.AddWithValue("@institution", institutionId);
                    if (institution.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                return false;
            }
        }

        internal static bool Complete(SQLiteConnection db, string institutionTable,
            string ticketTable, long institutionId, long buildingId,
            int tileX, int tileY, double updatedTime)
        {
            if (db == null || !Identifier(institutionTable) ||
                !Identifier(ticketTable) || institutionId < 0 || buildingId < 0)
                return false;
            using SQLiteTransaction transaction = db.BeginTransaction();
            try
            {
                using (var delete = new SQLiteCommand(db) { Transaction = transaction })
                {
                    delete.CommandText = "DELETE FROM " + ticketTable +
                        " WHERE INSTITUTION_ID=@institution AND STATE='rebuilding'";
                    delete.Parameters.AddWithValue("@institution", institutionId);
                    if (delete.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                using (var update = new SQLiteCommand(db) { Transaction = transaction })
                {
                    update.CommandText = "UPDATE " + institutionTable +
                        " SET ACTIVE=1,BUILDING_ID=@building,TILE_X=@x,TILE_Y=@y," +
                        "PHYSICAL_STATE='active',UPDATED_TIME=@time " +
                        "WHERE INSTITUTION_ID=@institution";
                    update.Parameters.AddWithValue("@building", buildingId);
                    update.Parameters.AddWithValue("@x", tileX);
                    update.Parameters.AddWithValue("@y", tileY);
                    update.Parameters.AddWithValue("@time", Finite(updatedTime));
                    update.Parameters.AddWithValue("@institution", institutionId);
                    if (update.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                return false;
            }
        }

        internal static bool Cancel(SQLiteConnection db, string institutionTable,
            string ticketTable, long institutionId, double updatedTime)
        {
            if (db == null || !Identifier(institutionTable) ||
                !Identifier(ticketTable) || institutionId < 0) return false;
            using SQLiteTransaction transaction = db.BeginTransaction();
            try
            {
                using (var delete = new SQLiteCommand(db) { Transaction = transaction })
                {
                    delete.CommandText = "DELETE FROM " + ticketTable +
                        " WHERE INSTITUTION_ID=@institution";
                    delete.Parameters.AddWithValue("@institution", institutionId);
                    if (delete.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                using (var update = new SQLiteCommand(db) { Transaction = transaction })
                {
                    update.CommandText = "UPDATE " + institutionTable +
                        " SET ACTIVE=0,PHYSICAL_STATE='inactive',UPDATED_TIME=@time " +
                        "WHERE INSTITUTION_ID=@institution";
                    update.Parameters.AddWithValue("@time", Finite(updatedTime));
                    update.Parameters.AddWithValue("@institution", institutionId);
                    if (update.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                return false;
            }
        }

        private static bool Valid(SQLiteConnection db, string institutionTable,
            string ticketTable, long institutionId, long cityId)
        {
            return db != null && Identifier(institutionTable) && Identifier(ticketTable) &&
                   institutionId >= 0 && cityId >= 0;
        }

        private static bool Identifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            for (int i = 0; i < value.Length; i++)
                if (!char.IsLetterOrDigit(value[i]) && value[i] != '_') return false;
            return true;
        }

        private static void AddIdentity(SQLiteCommand command,
            long institutionId, long cityId)
        {
            command.Parameters.AddWithValue("@institution", institutionId);
            command.Parameters.AddWithValue("@city", cityId);
        }

        private static double Finite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? 0d
                : Math.Max(0d, value);
        }
    }
}
