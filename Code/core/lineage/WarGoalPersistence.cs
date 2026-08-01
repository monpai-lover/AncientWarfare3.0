using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal sealed class WarGoalSnapshot
    {
        public long WarId { get; set; } = -1;
        public long AttackerKingdomId { get; set; } = -1;
        public string AttackerName { get; set; } = "";
        public string AttackerColor { get; set; } = "";
        public long DefenderKingdomId { get; set; } = -1;
        public string DefenderName { get; set; } = "";
        public string DefenderColor { get; set; } = "";
        public string WarType { get; set; } = "";
        public string GoalType { get; set; } = "";
        public int RequiredWarScore { get; set; } = -1;
        public string CompletionKind { get; set; } = "";
        public long TargetCityId { get; set; } = -1;
        public string TargetCityName { get; set; } = "";
        public long TargetKingdomId { get; set; } = -1;
        public string TargetKingdomName { get; set; } = "";
        public long SourceClaimId { get; set; } = -1;
        public long SourceCoreId { get; set; } = -1;
        public long SourceProjectId { get; set; } = -1;
        public long ClaimantActorId { get; set; } = -1;
        public string ClaimantName { get; set; } = "";
        public double CreatedTime { get; set; } = -1d;

        public WarGoalIdentity Identity => new WarGoalIdentity(
            GoalType, TargetCityId, TargetKingdomId, SourceClaimId,
            SourceCoreId, SourceProjectId, ClaimantActorId);
    }

    internal readonly struct WarGoalCreateResult
    {
        public WarGoalCreateResult(bool pSuccess, long pWarGoalId,
            string pReason)
        {
            Success = pSuccess;
            WarGoalId = pWarGoalId;
            Reason = pReason ?? "";
        }

        public bool Success { get; }
        public long WarGoalId { get; }
        public string Reason { get; }
    }

    internal sealed class WarGoalSettlementSnapshot
    {
        public long WarGoalId { get; set; } = -1;
        public int Position { get; set; } = -1;
        public long AttackerKingdomId { get; set; } = -1;
        public long DefenderKingdomId { get; set; } = -1;
        public string GoalType { get; set; } = "";
        public int RequiredWarScore { get; set; } = -1;
        public string CompletionKind { get; set; } = "";
        public long TargetCityId { get; set; } = -1;
        public long TargetKingdomId { get; set; } = -1;
        public long SourceClaimId { get; set; } = -1;
        public bool Completed { get; set; }
        public int CompletionScore { get; set; } = -101;
        public long CompletionRevision { get; set; } = -1;
    }

    internal static class WarGoalPersistence
    {
        private const string TableName = "WarGoal";

        public static WarGoalCreateResult TryCreate(SQLiteConnection pDb,
            WarGoalSnapshot pSnapshot)
        {
            if (pDb == null)
                return Failed("war_goal_database_unavailable");
            if (pSnapshot == null || pSnapshot.WarId < 0)
                return Failed("invalid_war_goal_snapshot");

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                List<WarGoalIdentity> existing = ReadPersistedIdentities(
                    pDb, transaction, pSnapshot.WarId);
                WarGoalIdentity candidate = pSnapshot.Identity;
                for (int index = 0; index < existing.Count; index++)
                {
                    if (existing[index].Equals(candidate))
                    {
                        transaction.Rollback();
                        return Failed("duplicate_war_goal");
                    }
                }
                if (existing.Count >= WarGoalSettlementRules.MaximumPersistedGoals)
                {
                    transaction.Rollback();
                    return Failed("war_goal_limit_reached");
                }

                long goalId = NextGoalId(pDb, transaction);
                Insert(pDb, transaction, goalId, existing.Count, pSnapshot);
                transaction.Commit();
                return new WarGoalCreateResult(true, goalId, "");
            }
            catch
            {
                try { transaction?.Rollback(); }
                catch { }
                return Failed("war_goal_insert_failed");
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public static int MarkCityControlCompleted(SQLiteConnection pDb,
            long warId, long cityDataId, long cityObjectId,
            int completionScore, double completedTime,
            long completionRevision)
        {
            if (pDb == null || warId < 0 ||
                cityDataId < 0 && cityObjectId < 0) return 0;
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "UPDATE " + TableName + " SET " +
                        "COMPLETED=1,COMPLETED_TIME=@time," +
                        "COMPLETION_SCORE=@score," +
                        "COMPLETION_REVISION=@revision " +
                        "WHERE WAR_ID=@war AND RESOLVED=0 AND COMPLETED=0 " +
                        "AND COMPLETION_KIND IN " +
                        "('city_control','capital_control') AND " +
                        "(TARGET_CITY_ID=@dataCity OR " +
                        "TARGET_CITY_ID=@objectCity)"
                };
                command.Parameters.AddWithValue("@time", completedTime);
                command.Parameters.AddWithValue("@score", completionScore);
                command.Parameters.AddWithValue("@revision",
                    completionRevision);
                command.Parameters.AddWithValue("@war", warId);
                command.Parameters.AddWithValue("@dataCity", cityDataId);
                command.Parameters.AddWithValue("@objectCity", cityObjectId);
                return command.ExecuteNonQuery();
            }
            catch { return 0; }
        }

        public static bool MarkGoalCompleted(SQLiteConnection pDb,
            long pWarId, long pWarGoalId, int pCompletionScore,
            double pCompletedTime, long pCompletionRevision)
        {
            if (pDb == null || pWarId < 0 || pWarGoalId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "UPDATE " + TableName + " SET " +
                        "COMPLETED=1,COMPLETED_TIME=@time," +
                        "COMPLETION_SCORE=@score," +
                        "COMPLETION_REVISION=@revision " +
                        "WHERE WAR_ID=@war AND WAR_GOAL_ID=@goal " +
                        "AND RESOLVED=0 AND COMPLETED=0"
                };
                command.Parameters.AddWithValue("@time", pCompletedTime);
                command.Parameters.AddWithValue("@score", pCompletionScore);
                command.Parameters.AddWithValue("@revision",
                    pCompletionRevision);
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@goal", pWarGoalId);
                return command.ExecuteNonQuery() > 0;
            }
            catch { return false; }
        }

        public static IReadOnlyList<WarGoalSettlementSnapshot>
            ReadOpenSettlementGoals(SQLiteConnection pDb, long pWarId)
        {
            var result = new List<WarGoalSettlementSnapshot>(
                WarGoalSettlementRules.MaximumPersistedGoals);
            if (pDb == null || pWarId < 0) return result;
            try
            {
                using var command = new SQLiteCommand(pDb)
                {
                    CommandText = "SELECT WAR_GOAL_ID,POSITION," +
                        "ATTACKER_KINGDOM_ID,DEFENDER_KINGDOM_ID," +
                        "GOAL_TYPE,REQUIRED_WAR_SCORE,COMPLETION_KIND," +
                        "TARGET_CITY_ID,TARGET_KINGDOM_ID,SOURCE_CLAIM_ID," +
                        "COMPLETED," +
                        "COMPLETION_SCORE,COMPLETION_REVISION FROM " +
                        TableName + " WHERE WAR_ID=@war AND RESOLVED=0 " +
                        "ORDER BY POSITION,WAR_GOAL_ID LIMIT 3"
                };
                command.Parameters.AddWithValue("@war", pWarId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result.Add(new WarGoalSettlementSnapshot
                    {
                        WarGoalId = reader.GetInt64(0),
                        Position = reader.IsDBNull(1) ? -1 :
                            reader.GetInt32(1),
                        AttackerKingdomId = reader.IsDBNull(2) ? -1L :
                            reader.GetInt64(2),
                        DefenderKingdomId = reader.IsDBNull(3) ? -1L :
                            reader.GetInt64(3),
                        GoalType = reader.IsDBNull(4) ? "" :
                            reader.GetString(4),
                        RequiredWarScore = reader.IsDBNull(5) ? -1 :
                            reader.GetInt32(5),
                        CompletionKind = reader.IsDBNull(6) ? "" :
                            reader.GetString(6),
                        TargetCityId = reader.IsDBNull(7) ? -1L :
                            reader.GetInt64(7),
                        TargetKingdomId = reader.IsDBNull(8) ? -1L :
                            reader.GetInt64(8),
                        SourceClaimId = reader.IsDBNull(9) ? -1L :
                            reader.GetInt64(9),
                        Completed = !reader.IsDBNull(10) &&
                            reader.GetInt32(10) != 0,
                        CompletionScore = reader.IsDBNull(11) ? -101 :
                            reader.GetInt32(11),
                        CompletionRevision = reader.IsDBNull(12) ? -1L :
                            reader.GetInt64(12)
                    });
            }
            catch { }
            return result;
        }

        private static List<WarGoalIdentity> ReadPersistedIdentities(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            long pWarId)
        {
            var result = new List<WarGoalIdentity>(
                WarGoalSettlementRules.MaximumPersistedGoals);
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT GOAL_TYPE,TARGET_CITY_ID," +
                    "TARGET_KINGDOM_ID,SOURCE_CLAIM_ID,SOURCE_CORE_ID," +
                    "SOURCE_PROJECT_ID,CLAIMANT_ACTOR_ID FROM " + TableName +
                    " WHERE WAR_ID=@war " +
                    "ORDER BY POSITION,WAR_GOAL_ID LIMIT 3"
            };
            command.Parameters.AddWithValue("@war", pWarId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new WarGoalIdentity(
                    reader.IsDBNull(0) ? "" : reader.GetString(0),
                    reader.IsDBNull(1) ? -1L : reader.GetInt64(1),
                    reader.IsDBNull(2) ? -1L : reader.GetInt64(2),
                    reader.IsDBNull(3) ? -1L : reader.GetInt64(3),
                    reader.IsDBNull(4) ? -1L : reader.GetInt64(4),
                    reader.IsDBNull(5) ? -1L : reader.GetInt64(5),
                    reader.IsDBNull(6) ? -1L : reader.GetInt64(6)));
            }
            return result;
        }

        private static long NextGoalId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT IFNULL(MAX(WAR_GOAL_ID),0)+1 FROM " +
                    TableName
            };
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static void Insert(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pGoalId, int pPosition,
            WarGoalSnapshot pSnapshot)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " + TableName + " (" +
                    "WAR_GOAL_ID,WAR_ID,ATTACKER_KINGDOM_ID,ATTACKER_NAME," +
                    "ATTACKER_COLOR,DEFENDER_KINGDOM_ID,DEFENDER_NAME," +
                    "DEFENDER_COLOR,WAR_TYPE,GOAL_TYPE,POSITION," +
                    "REQUIRED_WAR_SCORE,TARGET_CITY_ID,TARGET_CITY_NAME," +
                    "TARGET_KINGDOM_ID,TARGET_KINGDOM_NAME,SOURCE_CLAIM_ID," +
                    "SOURCE_CORE_ID,SOURCE_PROJECT_ID,CLAIMANT_ACTOR_ID," +
                    "CLAIMANT_NAME,CREATED_TIME,RESOLVED_TIME,RESOLVED," +
                    "RESULT,COMPLETION_KIND,COMPLETED,COMPLETED_TIME," +
                    "COMPLETION_SCORE,COMPLETION_REVISION) VALUES (" +
                    "@id,@war,@attacker,@attackerName,@attackerColor," +
                    "@defender,@defenderName,@defenderColor,@warType," +
                    "@goalType,@position,@required,@city,@cityName," +
                    "@targetKingdom,@targetKingdomName,@claim,@core," +
                    "@project,@claimant,@claimantName,@created,-1,0,''," +
                    "@completionKind,0,-1,-101,-1)"
            };
            command.Parameters.AddWithValue("@id", pGoalId);
            command.Parameters.AddWithValue("@war", pSnapshot.WarId);
            command.Parameters.AddWithValue("@attacker",
                pSnapshot.AttackerKingdomId);
            command.Parameters.AddWithValue("@attackerName",
                pSnapshot.AttackerName ?? "");
            command.Parameters.AddWithValue("@attackerColor",
                pSnapshot.AttackerColor ?? "");
            command.Parameters.AddWithValue("@defender",
                pSnapshot.DefenderKingdomId);
            command.Parameters.AddWithValue("@defenderName",
                pSnapshot.DefenderName ?? "");
            command.Parameters.AddWithValue("@defenderColor",
                pSnapshot.DefenderColor ?? "");
            command.Parameters.AddWithValue("@warType",
                pSnapshot.WarType ?? "");
            command.Parameters.AddWithValue("@goalType",
                pSnapshot.GoalType ?? "");
            command.Parameters.AddWithValue("@position", pPosition);
            command.Parameters.AddWithValue("@required",
                pSnapshot.RequiredWarScore);
            command.Parameters.AddWithValue("@city", pSnapshot.TargetCityId);
            command.Parameters.AddWithValue("@cityName",
                pSnapshot.TargetCityName ?? "");
            command.Parameters.AddWithValue("@targetKingdom",
                pSnapshot.TargetKingdomId);
            command.Parameters.AddWithValue("@targetKingdomName",
                pSnapshot.TargetKingdomName ?? "");
            command.Parameters.AddWithValue("@claim", pSnapshot.SourceClaimId);
            command.Parameters.AddWithValue("@core", pSnapshot.SourceCoreId);
            command.Parameters.AddWithValue("@project",
                pSnapshot.SourceProjectId);
            command.Parameters.AddWithValue("@claimant",
                pSnapshot.ClaimantActorId);
            command.Parameters.AddWithValue("@claimantName",
                pSnapshot.ClaimantName ?? "");
            command.Parameters.AddWithValue("@created", pSnapshot.CreatedTime);
            command.Parameters.AddWithValue("@completionKind",
                pSnapshot.CompletionKind ?? "");
            command.ExecuteNonQuery();
        }

        private static WarGoalCreateResult Failed(string pReason)
        {
            return new WarGoalCreateResult(false, -1L, pReason);
        }
    }
}
