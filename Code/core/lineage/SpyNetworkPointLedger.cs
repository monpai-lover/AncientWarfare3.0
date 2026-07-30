using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public readonly struct SpyNetworkPointSnapshot
    {
        public SpyNetworkPointSnapshot(bool active, int points,
            int lastAccrualYear, double lastAccrualTime)
        {
            Active = active;
            Points = points;
            LastAccrualYear = lastAccrualYear;
            LastAccrualTime = lastAccrualTime;
        }

        public bool Active { get; }
        public int Points { get; }
        public int LastAccrualYear { get; }
        public double LastAccrualTime { get; }
    }

    public readonly struct SpyClaimPurchaseResult
    {
        public SpyClaimPurchaseResult(bool success, string reason,
            int remainingPoints)
        {
            Success = success;
            Reason = reason ?? "";
            RemainingPoints = remainingPoints;
        }

        public bool Success { get; }
        public string Reason { get; }
        public int RemainingPoints { get; }
    }

    public static class SpyNetworkPointLedger
    {
        public const string NetworkTable = "SpyNetwork";
        public const string PurchaseTable = "SpyNetworkClaimPurchase";

        public static void EnsureSchema(SQLiteConnection db)
        {
            Execute(db, "CREATE TABLE IF NOT EXISTS SpyNetwork(" +
                        "SOURCE_KINGDOM_ID INTEGER NOT NULL," +
                        "TARGET_KINGDOM_ID INTEGER NOT NULL," +
                        "POINTS INTEGER NOT NULL DEFAULT 0," +
                        "LAST_ACCRUAL_YEAR INTEGER NOT NULL DEFAULT -1," +
                        "LAST_ACCRUAL_TIME REAL NOT NULL DEFAULT -1," +
                        "ACTIVE INTEGER NOT NULL DEFAULT 1," +
                        "PRIMARY KEY(SOURCE_KINGDOM_ID,TARGET_KINGDOM_ID))");
            Execute(db, "CREATE INDEX IF NOT EXISTS " +
                        "idx_SpyNetwork_pair_active_accrual ON SpyNetwork(" +
                        "SOURCE_KINGDOM_ID,TARGET_KINGDOM_ID,ACTIVE," +
                        "LAST_ACCRUAL_YEAR)");
            Execute(db, "CREATE TABLE IF NOT EXISTS SpyNetworkClaimPurchase(" +
                        "SOURCE_KINGDOM_ID INTEGER NOT NULL," +
                        "TARGET_KINGDOM_ID INTEGER NOT NULL," +
                        "PURCHASE_KEY TEXT NOT NULL," +
                        "PURCHASE_YEAR INTEGER NOT NULL," +
                        "COST INTEGER NOT NULL," +
                        "PRIMARY KEY(SOURCE_KINGDOM_ID,TARGET_KINGDOM_ID," +
                        "PURCHASE_KEY))");
        }

        public static void UpsertNetwork(SQLiteConnection db,
            long sourceId, long targetId, int points,
            int lastAccrualYear, double lastAccrualTime)
        {
            using var command = new SQLiteCommand(
                "UPDATE SpyNetwork SET ACTIVE=1 WHERE " +
                "SOURCE_KINGDOM_ID=@source AND TARGET_KINGDOM_ID=@target",
                db);
            command.Parameters.AddWithValue("@source", sourceId);
            command.Parameters.AddWithValue("@target", targetId);
            if (command.ExecuteNonQuery() == 1) return;
            command.CommandText = "INSERT INTO SpyNetwork(" +
                "SOURCE_KINGDOM_ID,TARGET_KINGDOM_ID,POINTS," +
                "LAST_ACCRUAL_YEAR,LAST_ACCRUAL_TIME,ACTIVE) VALUES(" +
                "@source,@target,@points,@year,@time,1)";
            command.Parameters.AddWithValue("@points",
                Math.Max(0, Math.Min(SpyNetworkPointRules.MaximumPoints,
                    points)));
            command.Parameters.AddWithValue("@year", lastAccrualYear);
            command.Parameters.AddWithValue("@time", lastAccrualTime);
            command.ExecuteNonQuery();
        }

        public static SpyNetworkPointSnapshot Read(SQLiteConnection db,
            long sourceId, long targetId, int currentYear,
            double currentTime)
        {
            using SQLiteTransaction transaction = db.BeginTransaction();
            SpyNetworkPointSnapshot snapshot = ReadAndSettle(db, transaction,
                sourceId, targetId, currentYear, currentTime);
            transaction.Commit();
            return snapshot;
        }

        public static SpyClaimPurchaseResult TryPurchase(
            SQLiteConnection db, long sourceId, long targetId,
            int currentYear, double currentTime, SpyClaimKind kind,
            string purchaseKey,
            Func<SQLiteConnection, SQLiteTransaction, bool> createClaim)
        {
            if (db == null || string.IsNullOrEmpty(purchaseKey) ||
                createClaim == null)
                return new SpyClaimPurchaseResult(false, "invalid", 0);
            using SQLiteTransaction transaction = db.BeginTransaction();
            try
            {
                SpyNetworkPointSnapshot snapshot = ReadAndSettle(db,
                    transaction, sourceId, targetId, currentYear, currentTime);
                if (!snapshot.Active)
                {
                    transaction.Rollback();
                    return new SpyClaimPurchaseResult(false,
                        "spy_network_required", 0);
                }
                if (PurchaseExists(db, transaction, sourceId, targetId,
                        purchaseKey))
                {
                    transaction.Rollback();
                    return new SpyClaimPurchaseResult(false,
                        "claim_already_purchased", snapshot.Points);
                }
                int cost = SpyNetworkPointRules.Cost(kind);
                if (snapshot.Points < cost)
                {
                    transaction.Rollback();
                    return new SpyClaimPurchaseResult(false,
                        "insufficient_spy_points", snapshot.Points);
                }
                InsertPurchase(db, transaction, sourceId, targetId,
                    purchaseKey, currentYear, cost);
                int remaining = snapshot.Points - cost;
                UpdatePoints(db, transaction, sourceId, targetId, remaining,
                    currentYear, currentTime);
                if (!createClaim(db, transaction))
                {
                    transaction.Rollback();
                    return new SpyClaimPurchaseResult(false,
                        "claim_creation_failed", snapshot.Points);
                }
                transaction.Commit();
                return new SpyClaimPurchaseResult(true, "", remaining);
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                return new SpyClaimPurchaseResult(false,
                    "claim_creation_failed", 0);
            }
        }

        private static SpyNetworkPointSnapshot ReadAndSettle(
            SQLiteConnection db, SQLiteTransaction transaction,
            long sourceId, long targetId, int currentYear,
            double currentTime)
        {
            using var command = new SQLiteCommand(
                "SELECT POINTS,LAST_ACCRUAL_YEAR,LAST_ACCRUAL_TIME,ACTIVE " +
                "FROM SpyNetwork WHERE SOURCE_KINGDOM_ID=@source AND " +
                "TARGET_KINGDOM_ID=@target LIMIT 1", db, transaction);
            command.Parameters.AddWithValue("@source", sourceId);
            command.Parameters.AddWithValue("@target", targetId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return new SpyNetworkPointSnapshot(false, 0, currentYear,
                    currentTime);
            int stored = reader.GetInt32(0);
            int lastYear = reader.GetInt32(1);
            double lastTime = reader.GetDouble(2);
            bool active = reader.GetInt32(3) != 0;
            reader.Close();
            if (!active)
                return new SpyNetworkPointSnapshot(false, stored, lastYear,
                    lastTime);
            SpyNetworkAccrual accrued = SpyNetworkPointRules.Accrue(stored,
                lastYear, currentYear);
            if (accrued.Points != stored ||
                accrued.LastAccrualYear != lastYear)
                UpdatePoints(db, transaction, sourceId, targetId,
                    accrued.Points, accrued.LastAccrualYear, currentTime);
            return new SpyNetworkPointSnapshot(true, accrued.Points,
                accrued.LastAccrualYear,
                accrued.LastAccrualYear == lastYear ? lastTime : currentTime);
        }

        private static bool PurchaseExists(SQLiteConnection db,
            SQLiteTransaction transaction, long sourceId, long targetId,
            string purchaseKey)
        {
            using var command = new SQLiteCommand(
                "SELECT 1 FROM SpyNetworkClaimPurchase WHERE " +
                "SOURCE_KINGDOM_ID=@source AND TARGET_KINGDOM_ID=@target " +
                "AND PURCHASE_KEY=@key LIMIT 1", db, transaction);
            command.Parameters.AddWithValue("@source", sourceId);
            command.Parameters.AddWithValue("@target", targetId);
            command.Parameters.AddWithValue("@key", purchaseKey);
            return command.ExecuteScalar() != null;
        }

        private static void InsertPurchase(SQLiteConnection db,
            SQLiteTransaction transaction, long sourceId, long targetId,
            string purchaseKey, int year, int cost)
        {
            using var command = new SQLiteCommand(
                "INSERT INTO SpyNetworkClaimPurchase(SOURCE_KINGDOM_ID," +
                "TARGET_KINGDOM_ID,PURCHASE_KEY,PURCHASE_YEAR,COST) " +
                "VALUES(@source,@target,@key,@year,@cost)", db, transaction);
            command.Parameters.AddWithValue("@source", sourceId);
            command.Parameters.AddWithValue("@target", targetId);
            command.Parameters.AddWithValue("@key", purchaseKey);
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@cost", cost);
            command.ExecuteNonQuery();
        }

        private static void UpdatePoints(SQLiteConnection db,
            SQLiteTransaction transaction, long sourceId, long targetId,
            int points, int year, double time)
        {
            using var command = new SQLiteCommand(
                "UPDATE SpyNetwork SET POINTS=@points," +
                "LAST_ACCRUAL_YEAR=@year,LAST_ACCRUAL_TIME=@time WHERE " +
                "SOURCE_KINGDOM_ID=@source AND TARGET_KINGDOM_ID=@target " +
                "AND ACTIVE=1", db, transaction);
            command.Parameters.AddWithValue("@points", points);
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@time", time);
            command.Parameters.AddWithValue("@source", sourceId);
            command.Parameters.AddWithValue("@target", targetId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("spy_network_changed");
        }

        private static void Execute(SQLiteConnection db, string sql)
        {
            using var command = new SQLiteCommand(sql, db);
            command.ExecuteNonQuery();
        }
    }
}
