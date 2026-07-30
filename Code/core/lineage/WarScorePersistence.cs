using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal sealed class WarScoreControlState
    {
        public string Key = "";
        public long WarId = -1;
        public string Kind = "";
        public string SubjectId = "";
        public long HomeKingdomId = -1;
        public long ControllerKingdomId = -1;
        public WarScoreSide HomeSide;
        public WarScoreSide ControllerSide;
        public int Value;
        public int Contribution;
        public bool VerifiedGoal;
        public bool Decisive;
        public int Occurrence;
        public int HomeCityCount;
        public double StartedTime = -1d;
        public double UpdatedTime = -1d;

        public WarScoreControlState Clone()
        {
            return (WarScoreControlState)MemberwiseClone();
        }
    }

    internal sealed class WarScoreReliefEventState
    {
        public string Key = "";
        public long WarId = -1;
        public string Kind = "";
        public string SubjectId = "";
        public WarScoreSide BeneficiarySide;
        public int Amount;
        public double WorldTime = -1d;
    }

    internal sealed class WarScorePersistence
    {
        internal const string SnapshotTable = "WarScoreSnapshot";
        internal const string ControlTable = "WarScoreControl";
        internal const string ReliefEventTable = "WarScoreReliefEvent";

        private readonly SQLiteConnection _db;

        public WarScorePersistence(SQLiteConnection pDb)
        {
            _db = pDb ?? throw new ArgumentNullException(nameof(pDb));
            if (_db.State != ConnectionState.Open)
                throw new InvalidOperationException(
                    "war score database connection must be open");
            EnsureSchema();
        }

        public IReadOnlyList<WarScoreSnapshot> LoadActive()
        {
            var result = new List<WarScoreSnapshot>();
            using var command = new SQLiteCommand(
                "SELECT " + SnapshotColumns + " FROM " + SnapshotTable +
                " WHERE ACTIVE=1 ORDER BY WAR_ID", _db);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadSnapshot(reader));
            return result;
        }

        public IReadOnlyList<WarScoreControlState> LoadControls(long pWarId)
        {
            var result = new List<WarScoreControlState>();
            using var command = new SQLiteCommand(
                "SELECT CONTROL_KEY,WAR_ID,CONTROL_KIND,SUBJECT_ID," +
                "HOME_KINGDOM_ID,CONTROLLER_KINGDOM_ID," +
                "HOME_SIDE,CONTROLLER_SIDE,VALUE,CONTRIBUTION," +
                "VERIFIED_GOAL,DECISIVE,OCCURRENCE,HOME_CITY_COUNT," +
                "STARTED_TIME,UPDATED_TIME FROM " +
                ControlTable +
                " WHERE WAR_ID=@war ORDER BY CONTROL_KEY", _db);
            command.Parameters.AddWithValue("@war", pWarId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadControl(reader));
            return result;
        }

        public WarScoreSnapshot Read(long pWarId)
        {
            using var command = new SQLiteCommand(
                "SELECT " + SnapshotColumns + " FROM " + SnapshotTable +
                " WHERE WAR_ID=@war LIMIT 1", _db);
            command.Parameters.AddWithValue("@war", pWarId);
            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadSnapshot(reader) : null;
        }

        public IReadOnlyList<WarScoreSnapshot> ReadHistory(
            long pKingdomId, int pLimit)
        {
            int limit = Math.Max(1, Math.Min(200, pLimit));
            var result = new List<WarScoreSnapshot>();
            using var command = new SQLiteCommand(
                "SELECT " + SnapshotColumns + " FROM " + SnapshotTable +
                " WHERE ACTIVE=0 AND (ATTACKER_KINGDOM_ID=@kingdom OR " +
                "DEFENDER_KINGDOM_ID=@kingdom) ORDER BY ENDED_TIME DESC," +
                "WAR_ID DESC LIMIT @limit", _db);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@limit", limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadSnapshot(reader));
            return result;
        }

        public IReadOnlyList<WarScoreOccupiedCitySnapshot> ReadOccupiedCities(
            long pWarId, long pControllerKingdomId, int pLimit)
        {
            int limit = Math.Max(1, Math.Min(64, pLimit));
            var result = new List<WarScoreOccupiedCitySnapshot>();
            using var command = new SQLiteCommand(
                "SELECT WAR_ID,SUBJECT_ID,HOME_KINGDOM_ID," +
                "CONTROLLER_KINGDOM_ID,HOME_SIDE,CONTROLLER_SIDE," +
                "CONTRIBUTION,VERIFIED_GOAL FROM " + ControlTable +
                " WHERE WAR_ID=@war AND CONTROLLER_KINGDOM_ID=@controller" +
                " AND CONTROL_KIND='city' AND CONTRIBUTION<>0" +
                " ORDER BY CONTROL_KEY LIMIT @limit", _db);
            command.Parameters.AddWithValue("@war", pWarId);
            command.Parameters.AddWithValue("@controller", pControllerKingdomId);
            command.Parameters.AddWithValue("@limit", limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!long.TryParse(Convert.ToString(reader["SUBJECT_ID"]),
                        out long cityId)) continue;
                result.Add(new WarScoreOccupiedCitySnapshot(
                    Convert.ToInt64(reader["WAR_ID"]), cityId,
                    Convert.ToInt64(reader["HOME_KINGDOM_ID"]),
                    Convert.ToInt64(reader["CONTROLLER_KINGDOM_ID"]),
                    (WarScoreSide)Convert.ToInt32(reader["HOME_SIDE"]),
                    (WarScoreSide)Convert.ToInt32(reader["CONTROLLER_SIDE"]),
                    Convert.ToInt32(reader["CONTRIBUTION"]),
                    Convert.ToInt32(reader["VERIFIED_GOAL"]) != 0));
            }
            return result;
        }

        public IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadOccupiedCitiesForWar(long pWarId, int pLimit)
        {
            int limit = Math.Max(1, Math.Min(128, pLimit));
            var result = new List<WarScoreOccupiedCitySnapshot>();
            using var command = new SQLiteCommand(
                "SELECT WAR_ID,SUBJECT_ID,HOME_KINGDOM_ID," +
                "CONTROLLER_KINGDOM_ID,HOME_SIDE,CONTROLLER_SIDE," +
                "CONTRIBUTION,VERIFIED_GOAL FROM " + ControlTable +
                " WHERE WAR_ID=@war AND CONTROL_KIND='city'" +
                " AND CONTRIBUTION<>0 ORDER BY CONTROL_KEY LIMIT @limit", _db);
            command.Parameters.AddWithValue("@war", pWarId);
            command.Parameters.AddWithValue("@limit", limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!long.TryParse(Convert.ToString(reader["SUBJECT_ID"]),
                        out long cityId)) continue;
                result.Add(new WarScoreOccupiedCitySnapshot(
                    Convert.ToInt64(reader["WAR_ID"]), cityId,
                    Convert.ToInt64(reader["HOME_KINGDOM_ID"]),
                    Convert.ToInt64(reader["CONTROLLER_KINGDOM_ID"]),
                    (WarScoreSide)Convert.ToInt32(reader["HOME_SIDE"]),
                    (WarScoreSide)Convert.ToInt32(reader["CONTROLLER_SIDE"]),
                    Convert.ToInt32(reader["CONTRIBUTION"]),
                    Convert.ToInt32(reader["VERIFIED_GOAL"]) != 0));
            }
            return result;
        }

        public IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadOccupiedCitiesByHomeKingdom(long pWarId,
                long pHomeKingdomId, string pAfterControlKey, int pLimit)
        {
            int limit = Math.Max(1, Math.Min(32, pLimit));
            var result = new List<WarScoreOccupiedCitySnapshot>();
            using var command = new SQLiteCommand(
                "SELECT WAR_ID,SUBJECT_ID,HOME_KINGDOM_ID," +
                "CONTROLLER_KINGDOM_ID,HOME_SIDE,CONTROLLER_SIDE," +
                "CONTRIBUTION,VERIFIED_GOAL FROM " + ControlTable +
                " WHERE WAR_ID=@war AND HOME_KINGDOM_ID=@home" +
                " AND CONTROL_KIND='city' AND CONTROL_KEY>@after" +
                " ORDER BY CONTROL_KEY LIMIT @limit", _db);
            command.Parameters.AddWithValue("@war", pWarId);
            command.Parameters.AddWithValue("@home", pHomeKingdomId);
            command.Parameters.AddWithValue("@after", pAfterControlKey ?? "");
            command.Parameters.AddWithValue("@limit", limit);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!long.TryParse(Convert.ToString(reader["SUBJECT_ID"]),
                        out long cityId)) continue;
                result.Add(new WarScoreOccupiedCitySnapshot(
                    Convert.ToInt64(reader["WAR_ID"]), cityId,
                    Convert.ToInt64(reader["HOME_KINGDOM_ID"]),
                    Convert.ToInt64(reader["CONTROLLER_KINGDOM_ID"]),
                    (WarScoreSide)Convert.ToInt32(reader["HOME_SIDE"]),
                    (WarScoreSide)Convert.ToInt32(reader["CONTROLLER_SIDE"]),
                    Convert.ToInt32(reader["CONTRIBUTION"]),
                    Convert.ToInt32(reader["VERIFIED_GOAL"]) != 0));
            }
            return result;
        }

        public IReadOnlyList<WarScoreOccupiedCitySnapshot>
            ReadAllOccupiedCitiesForWarCleanup(long pWarId)
        {
            var result = new List<WarScoreOccupiedCitySnapshot>();
            using var command = new SQLiteCommand(
                "SELECT WAR_ID,SUBJECT_ID,HOME_KINGDOM_ID," +
                "CONTROLLER_KINGDOM_ID,HOME_SIDE,CONTROLLER_SIDE," +
                "CONTRIBUTION,VERIFIED_GOAL FROM " + ControlTable +
                " WHERE WAR_ID=@war AND CONTROL_KIND='city'" +
                " AND CONTRIBUTION<>0 ORDER BY CONTROL_KEY", _db);
            command.Parameters.AddWithValue("@war", pWarId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!long.TryParse(Convert.ToString(reader["SUBJECT_ID"]),
                        out long cityId)) continue;
                result.Add(new WarScoreOccupiedCitySnapshot(
                    Convert.ToInt64(reader["WAR_ID"]), cityId,
                    Convert.ToInt64(reader["HOME_KINGDOM_ID"]),
                    Convert.ToInt64(reader["CONTROLLER_KINGDOM_ID"]),
                    (WarScoreSide)Convert.ToInt32(reader["HOME_SIDE"]),
                    (WarScoreSide)Convert.ToInt32(reader["CONTROLLER_SIDE"]),
                    Convert.ToInt32(reader["CONTRIBUTION"]),
                    Convert.ToInt32(reader["VERIFIED_GOAL"]) != 0));
            }
            return result;
        }

        public bool TryReadFrozenOccupation(long pWarId, long pCityId,
            out long pControllerKingdomId)
        {
            pControllerKingdomId = -1;
            using var command = new SQLiteCommand(
                "SELECT CONTROLLER_KINGDOM_ID FROM " + ControlTable +
                " WHERE CONTROL_KEY=@key AND WAR_ID=@war" +
                " AND CONTROL_KIND='city' AND CONTRIBUTION<>0 LIMIT 1", _db);
            command.Parameters.AddWithValue("@key",
                pWarId + ":city:" + pCityId);
            command.Parameters.AddWithValue("@war", pWarId);
            object value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value) return false;
            pControllerKingdomId = Convert.ToInt64(value);
            return pControllerKingdomId >= 0;
        }

        public void Save(WarScoreSnapshot pSnapshot)
        {
            using SQLiteTransaction transaction = _db.BeginTransaction();
            WriteSnapshot(pSnapshot, transaction);
            transaction.Commit();
        }

        public void Save(WarScoreSnapshot pSnapshot,
            WarScoreControlState pControl)
        {
            using SQLiteTransaction transaction = _db.BeginTransaction();
            WriteControl(pControl, transaction);
            WriteSnapshot(pSnapshot, transaction);
            transaction.Commit();
        }

        public bool SaveWithReliefEvent(WarScoreSnapshot pBase,
            WarScoreSnapshot pRewarded, WarScoreReliefEventState pEvent)
        {
            using SQLiteTransaction transaction = _db.BeginTransaction();
            bool inserted = InsertReliefEvent(pEvent, transaction);
            WriteSnapshot(inserted ? pRewarded : pBase, transaction);
            transaction.Commit();
            return inserted;
        }

        public bool SaveControlWithReliefEvent(WarScoreSnapshot pBase,
            WarScoreSnapshot pRewarded,
            WarScoreControlState pControl,
            WarScoreReliefEventState pEvent)
        {
            using SQLiteTransaction transaction = _db.BeginTransaction();
            WriteControl(pControl, transaction);
            bool inserted = InsertReliefEvent(pEvent, transaction);
            WriteSnapshot(inserted ? pRewarded : pBase, transaction);
            transaction.Commit();
            return inserted;
        }

        public void End(WarScoreSnapshot pSnapshot)
        {
            using SQLiteTransaction transaction = _db.BeginTransaction();
            WriteSnapshot(pSnapshot, transaction);
            using var delete = new SQLiteCommand(
                "DELETE FROM " + ControlTable + " WHERE WAR_ID=@war",
                _db, transaction);
            delete.Parameters.AddWithValue("@war", pSnapshot.WarId);
            delete.ExecuteNonQuery();
            transaction.Commit();
        }

        public void DeleteControl(WarScoreSnapshot pSnapshot,
            string pControlKey)
        {
            using SQLiteTransaction transaction = _db.BeginTransaction();
            using (var delete = new SQLiteCommand(
                "DELETE FROM " + ControlTable +
                " WHERE WAR_ID=@war AND CONTROL_KEY=@key",
                _db, transaction))
            {
                delete.Parameters.AddWithValue("@war", pSnapshot.WarId);
                delete.Parameters.AddWithValue("@key", pControlKey);
                delete.ExecuteNonQuery();
            }
            WriteSnapshot(pSnapshot, transaction);
            transaction.Commit();
        }

        public bool DeleteHistory(long pWarId)
        {
            using SQLiteTransaction transaction = _db.BeginTransaction();
            int deleted;
            using (var snapshot = new SQLiteCommand(
                       "DELETE FROM " + SnapshotTable +
                       " WHERE WAR_ID=@war AND ACTIVE=0", _db,
                       transaction))
            {
                snapshot.Parameters.AddWithValue("@war", pWarId);
                deleted = snapshot.ExecuteNonQuery();
            }
            if (deleted == 1)
            {
                using var events = new SQLiteCommand(
                    "DELETE FROM " + ReliefEventTable +
                    " WHERE WAR_ID=@war", _db, transaction);
                events.Parameters.AddWithValue("@war", pWarId);
                events.ExecuteNonQuery();
            }
            transaction.Commit();
            return deleted == 1;
        }

        private void EnsureSchema()
        {
            using (var snapshot = new SQLiteCommand(
                "CREATE TABLE IF NOT EXISTS " + SnapshotTable + "(" +
                "WAR_ID INTEGER PRIMARY KEY," +
                "ATTACKER_KINGDOM_ID INTEGER NOT NULL," +
                "DEFENDER_KINGDOM_ID INTEGER NOT NULL," +
                "SCORE INTEGER NOT NULL," +
                "CITY_SCORE INTEGER NOT NULL," +
                "BATTLE_SCORE INTEGER NOT NULL," +
                "GOAL_SCORE INTEGER NOT NULL," +
                "LOSS_SCORE INTEGER NOT NULL," +
                "DECISIVE_SCORE INTEGER NOT NULL," +
                 "ATTACKER_LOSSES INTEGER NOT NULL," +
                 "DEFENDER_LOSSES INTEGER NOT NULL," +
                 "ATTACKER_MOBILIZATION_BASELINE INTEGER NOT NULL," +
                 "DEFENDER_MOBILIZATION_BASELINE INTEGER NOT NULL," +
                 "DURATION_YEARS INTEGER NOT NULL," +
                 "LAST_CALIBRATED_YEAR INTEGER NOT NULL," +
                 "ATTACKER_EXHAUSTION_RELIEF INTEGER NOT NULL," +
                 "DEFENDER_EXHAUSTION_RELIEF INTEGER NOT NULL," +
                 "ATTACKER_EXHAUSTION INTEGER NOT NULL," +
                "DEFENDER_EXHAUSTION INTEGER NOT NULL," +
                "ACTIVE INTEGER NOT NULL," +
                "WINNER TEXT NOT NULL," +
                "STARTED_TIME REAL NOT NULL," +
                "UPDATED_TIME REAL NOT NULL," +
                "ENDED_TIME REAL NOT NULL," +
                "REVISION INTEGER NOT NULL)", _db))
                snapshot.ExecuteNonQuery();
            using (var control = new SQLiteCommand(
                "CREATE TABLE IF NOT EXISTS " + ControlTable + "(" +
                "CONTROL_KEY TEXT PRIMARY KEY," +
                "WAR_ID INTEGER NOT NULL," +
                "CONTROL_KIND TEXT NOT NULL," +
                "SUBJECT_ID TEXT NOT NULL," +
                "HOME_KINGDOM_ID INTEGER NOT NULL," +
                "CONTROLLER_KINGDOM_ID INTEGER NOT NULL," +
                "HOME_SIDE INTEGER NOT NULL," +
                "CONTROLLER_SIDE INTEGER NOT NULL," +
                "VALUE INTEGER NOT NULL," +
                "CONTRIBUTION INTEGER NOT NULL," +
                 "VERIFIED_GOAL INTEGER NOT NULL," +
                 "DECISIVE INTEGER NOT NULL," +
                 "OCCURRENCE INTEGER NOT NULL," +
                 "HOME_CITY_COUNT INTEGER NOT NULL," +
                "STARTED_TIME REAL NOT NULL," +
                 "UPDATED_TIME REAL NOT NULL)", _db))
                control.ExecuteNonQuery();
            using (var reliefEvent = new SQLiteCommand(
                       "CREATE TABLE IF NOT EXISTS " + ReliefEventTable + "(" +
                       "EVENT_KEY TEXT PRIMARY KEY," +
                       "WAR_ID INTEGER NOT NULL," +
                       "EVENT_KIND TEXT NOT NULL," +
                       "SUBJECT_ID TEXT NOT NULL," +
                       "BENEFICIARY_SIDE INTEGER NOT NULL," +
                       "AMOUNT INTEGER NOT NULL," +
                       "WORLD_TIME REAL NOT NULL)", _db))
                reliefEvent.ExecuteNonQuery();
            EnsureColumn(SnapshotTable, "LAST_CALIBRATED_YEAR",
                "INTEGER NOT NULL DEFAULT -2147483648");
            EnsureColumn(SnapshotTable, "DECISIVE_SCORE",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(SnapshotTable, "ATTACKER_EXHAUSTION_RELIEF",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(SnapshotTable, "DEFENDER_EXHAUSTION_RELIEF",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(SnapshotTable, "ATTACKER_MOBILIZATION_BASELINE",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(SnapshotTable, "DEFENDER_MOBILIZATION_BASELINE",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(ControlTable, "HOME_KINGDOM_ID",
                "INTEGER NOT NULL DEFAULT -1");
            EnsureColumn(ControlTable, "CONTROLLER_KINGDOM_ID",
                "INTEGER NOT NULL DEFAULT -1");
            EnsureColumn(ControlTable, "DECISIVE",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(ControlTable, "OCCURRENCE",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(ControlTable, "HOME_CITY_COUNT",
                "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(ControlTable, "STARTED_TIME",
                "REAL NOT NULL DEFAULT -1");
            Execute("UPDATE " + ControlTable +
                    " SET STARTED_TIME=UPDATED_TIME WHERE STARTED_TIME<0");
            Execute("UPDATE " + ControlTable +
                    " SET CONTROL_KEY=CAST(WAR_ID AS TEXT)||':'||CONTROL_KEY" +
                    " WHERE CONTROL_KEY NOT LIKE CAST(WAR_ID AS TEXT)||':%'");
            Execute("CREATE INDEX IF NOT EXISTS idx_WarScoreSnapshot_active " +
                    "ON " + SnapshotTable + "(ACTIVE,UPDATED_TIME,WAR_ID)");
            Execute("CREATE INDEX IF NOT EXISTS idx_WarScoreSnapshot_attacker_history " +
                    "ON " + SnapshotTable +
                    "(ATTACKER_KINGDOM_ID,ACTIVE,ENDED_TIME,WAR_ID)");
            Execute("CREATE INDEX IF NOT EXISTS idx_WarScoreSnapshot_defender_history " +
                    "ON " + SnapshotTable +
                    "(DEFENDER_KINGDOM_ID,ACTIVE,ENDED_TIME,WAR_ID)");
            Execute("CREATE INDEX IF NOT EXISTS idx_WarScoreControl_war " +
                    "ON " + ControlTable + "(WAR_ID,CONTROL_KEY)");
            Execute("CREATE INDEX IF NOT EXISTS idx_WarScoreControl_controller " +
                    "ON " + ControlTable +
                    "(WAR_ID,CONTROLLER_KINGDOM_ID,CONTROL_KIND,CONTROL_KEY)");
            Execute("CREATE INDEX IF NOT EXISTS idx_WarScoreControl_home " +
                    "ON " + ControlTable +
                    "(WAR_ID,HOME_KINGDOM_ID,CONTROL_KIND,CONTROL_KEY)");
            Execute("CREATE INDEX IF NOT EXISTS idx_WarScoreReliefEvent_war " +
                    "ON " + ReliefEventTable + "(WAR_ID,EVENT_KEY)");
        }

        private void Execute(string pSql)
        {
            using var command = new SQLiteCommand(pSql, _db);
            command.ExecuteNonQuery();
        }

        private void EnsureColumn(string pTable, string pColumn,
            string pDefinition)
        {
            bool found = false;
            using (var info = new SQLiteCommand(
                "PRAGMA table_info(" + pTable + ")", _db))
            using (SQLiteDataReader reader = info.ExecuteReader())
                while (reader.Read())
                    if (string.Equals(Convert.ToString(reader["name"]),
                            pColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
            if (found) return;
            Execute("ALTER TABLE " + pTable + " ADD COLUMN " + pColumn +
                    " " + pDefinition);
        }

        private void WriteSnapshot(WarScoreSnapshot pValue,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(
                "INSERT OR REPLACE INTO " + SnapshotTable + "(" +
                SnapshotColumns + ") VALUES(" +
                "@war,@attacker,@defender,@score,@city,@battle,@goal,@loss,@decisive," +
                 "@attackerLosses,@defenderLosses," +
                 "@attackerMobilization,@defenderMobilization,@duration," +
                 "@lastCalibratedYear," +
                 "@attackerRelief,@defenderRelief," +
                 "@attackerExhaustion,@defenderExhaustion,@active,@winner," +
                "@started,@updated,@ended,@revision)", _db, pTransaction);
            command.Parameters.AddWithValue("@war", pValue.WarId);
            command.Parameters.AddWithValue("@attacker", pValue.AttackerKingdomId);
            command.Parameters.AddWithValue("@defender", pValue.DefenderKingdomId);
            command.Parameters.AddWithValue("@score", pValue.Score);
            command.Parameters.AddWithValue("@city", pValue.CityScore);
            command.Parameters.AddWithValue("@battle", pValue.BattleScore);
            command.Parameters.AddWithValue("@goal", pValue.GoalScore);
            command.Parameters.AddWithValue("@loss", pValue.LossScore);
            command.Parameters.AddWithValue("@decisive", pValue.DecisiveScore);
            command.Parameters.AddWithValue("@attackerLosses", pValue.AttackerLosses);
            command.Parameters.AddWithValue("@defenderLosses", pValue.DefenderLosses);
            command.Parameters.AddWithValue("@attackerMobilization",
                pValue.AttackerMobilizationBaseline);
            command.Parameters.AddWithValue("@defenderMobilization",
                pValue.DefenderMobilizationBaseline);
            command.Parameters.AddWithValue("@duration", pValue.DurationYears);
            command.Parameters.AddWithValue("@lastCalibratedYear",
                pValue.LastCalibratedYear);
            command.Parameters.AddWithValue("@attackerRelief",
                pValue.AttackerExhaustionRelief);
            command.Parameters.AddWithValue("@defenderRelief",
                pValue.DefenderExhaustionRelief);
            command.Parameters.AddWithValue("@attackerExhaustion", pValue.AttackerExhaustion);
            command.Parameters.AddWithValue("@defenderExhaustion", pValue.DefenderExhaustion);
            command.Parameters.AddWithValue("@active", pValue.Active ? 1 : 0);
            command.Parameters.AddWithValue("@winner", pValue.Winner ?? "");
            command.Parameters.AddWithValue("@started", pValue.StartedTime);
            command.Parameters.AddWithValue("@updated", pValue.UpdatedTime);
            command.Parameters.AddWithValue("@ended", pValue.EndedTime);
            command.Parameters.AddWithValue("@revision", pValue.Revision);
            command.ExecuteNonQuery();
        }

        private void WriteControl(WarScoreControlState pValue,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(
                "INSERT OR REPLACE INTO " + ControlTable +
                "(CONTROL_KEY,WAR_ID,CONTROL_KIND,SUBJECT_ID,HOME_SIDE," +
                "CONTROLLER_SIDE,VALUE,CONTRIBUTION,VERIFIED_GOAL," +
                "DECISIVE,OCCURRENCE,HOME_CITY_COUNT,HOME_KINGDOM_ID," +
                "CONTROLLER_KINGDOM_ID," +
                "STARTED_TIME,UPDATED_TIME) " +
                "VALUES(@key,@war,@kind,@subject,@home," +
                "@controller,@value,@contribution,@verified,@decisive," +
                "@occurrence,@homeCityCount,@homeKingdom," +
                "@controllerKingdom,@started,@updated)",
                _db, pTransaction);
            command.Parameters.AddWithValue("@key", pValue.Key);
            command.Parameters.AddWithValue("@war", pValue.WarId);
            command.Parameters.AddWithValue("@kind", pValue.Kind);
            command.Parameters.AddWithValue("@subject", pValue.SubjectId);
            command.Parameters.AddWithValue("@homeKingdom", pValue.HomeKingdomId);
            command.Parameters.AddWithValue("@controllerKingdom", pValue.ControllerKingdomId);
            command.Parameters.AddWithValue("@home", (int)pValue.HomeSide);
            command.Parameters.AddWithValue("@controller", (int)pValue.ControllerSide);
            command.Parameters.AddWithValue("@value", pValue.Value);
            command.Parameters.AddWithValue("@contribution", pValue.Contribution);
            command.Parameters.AddWithValue("@verified", pValue.VerifiedGoal ? 1 : 0);
            command.Parameters.AddWithValue("@decisive", pValue.Decisive ? 1 : 0);
            command.Parameters.AddWithValue("@occurrence", pValue.Occurrence);
            command.Parameters.AddWithValue("@homeCityCount",
                pValue.HomeCityCount);
            command.Parameters.AddWithValue("@started", pValue.StartedTime);
            command.Parameters.AddWithValue("@updated", pValue.UpdatedTime);
            command.ExecuteNonQuery();
        }

        private bool InsertReliefEvent(WarScoreReliefEventState pValue,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(
                "INSERT OR IGNORE INTO " + ReliefEventTable +
                "(EVENT_KEY,WAR_ID,EVENT_KIND,SUBJECT_ID," +
                "BENEFICIARY_SIDE,AMOUNT,WORLD_TIME) VALUES(" +
                "@key,@war,@kind,@subject,@side,@amount,@time)",
                _db, pTransaction);
            command.Parameters.AddWithValue("@key", pValue.Key);
            command.Parameters.AddWithValue("@war", pValue.WarId);
            command.Parameters.AddWithValue("@kind", pValue.Kind);
            command.Parameters.AddWithValue("@subject", pValue.SubjectId);
            command.Parameters.AddWithValue("@side",
                (int)pValue.BeneficiarySide);
            command.Parameters.AddWithValue("@amount", pValue.Amount);
            command.Parameters.AddWithValue("@time", pValue.WorldTime);
            return command.ExecuteNonQuery() == 1;
        }

        private static WarScoreSnapshot ReadSnapshot(SQLiteDataReader pReader)
        {
            return new WarScoreSnapshot
            {
                WarId = Convert.ToInt64(pReader["WAR_ID"]),
                AttackerKingdomId = Convert.ToInt64(pReader["ATTACKER_KINGDOM_ID"]),
                DefenderKingdomId = Convert.ToInt64(pReader["DEFENDER_KINGDOM_ID"]),
                Score = Convert.ToInt32(pReader["SCORE"]),
                CityScore = Convert.ToInt32(pReader["CITY_SCORE"]),
                BattleScore = Convert.ToInt32(pReader["BATTLE_SCORE"]),
                GoalScore = Convert.ToInt32(pReader["GOAL_SCORE"]),
                LossScore = Convert.ToInt32(pReader["LOSS_SCORE"]),
                DecisiveScore = Convert.ToInt32(pReader["DECISIVE_SCORE"]),
                AttackerLosses = Convert.ToInt32(pReader["ATTACKER_LOSSES"]),
                DefenderLosses = Convert.ToInt32(pReader["DEFENDER_LOSSES"]),
                AttackerMobilizationBaseline = Convert.ToInt32(
                    pReader["ATTACKER_MOBILIZATION_BASELINE"]),
                DefenderMobilizationBaseline = Convert.ToInt32(
                    pReader["DEFENDER_MOBILIZATION_BASELINE"]),
                DurationYears = Convert.ToInt32(pReader["DURATION_YEARS"]),
                LastCalibratedYear = Convert.ToInt32(
                    pReader["LAST_CALIBRATED_YEAR"]),
                AttackerExhaustionRelief = Convert.ToInt32(
                    pReader["ATTACKER_EXHAUSTION_RELIEF"]),
                DefenderExhaustionRelief = Convert.ToInt32(
                    pReader["DEFENDER_EXHAUSTION_RELIEF"]),
                AttackerExhaustion = Convert.ToInt32(pReader["ATTACKER_EXHAUSTION"]),
                DefenderExhaustion = Convert.ToInt32(pReader["DEFENDER_EXHAUSTION"]),
                Active = Convert.ToInt32(pReader["ACTIVE"]) != 0,
                Winner = Convert.ToString(pReader["WINNER"]) ?? "",
                StartedTime = Convert.ToDouble(pReader["STARTED_TIME"]),
                UpdatedTime = Convert.ToDouble(pReader["UPDATED_TIME"]),
                EndedTime = Convert.ToDouble(pReader["ENDED_TIME"]),
                Revision = Convert.ToInt64(pReader["REVISION"]),
                Perspective = WarScoreSide.Attackers
            };
        }

        private static WarScoreControlState ReadControl(SQLiteDataReader pReader)
        {
            return new WarScoreControlState
            {
                Key = Convert.ToString(pReader["CONTROL_KEY"]) ?? "",
                WarId = Convert.ToInt64(pReader["WAR_ID"]),
                Kind = Convert.ToString(pReader["CONTROL_KIND"]) ?? "",
                SubjectId = Convert.ToString(pReader["SUBJECT_ID"]) ?? "",
                HomeKingdomId = Convert.ToInt64(pReader["HOME_KINGDOM_ID"]),
                ControllerKingdomId = Convert.ToInt64(
                    pReader["CONTROLLER_KINGDOM_ID"]),
                HomeSide = (WarScoreSide)Convert.ToInt32(pReader["HOME_SIDE"]),
                ControllerSide = (WarScoreSide)Convert.ToInt32(pReader["CONTROLLER_SIDE"]),
                Value = Convert.ToInt32(pReader["VALUE"]),
                Contribution = Convert.ToInt32(pReader["CONTRIBUTION"]),
                VerifiedGoal = Convert.ToInt32(pReader["VERIFIED_GOAL"]) != 0,
                Decisive = Convert.ToInt32(pReader["DECISIVE"]) != 0,
                Occurrence = Convert.ToInt32(pReader["OCCURRENCE"]),
                HomeCityCount = Convert.ToInt32(
                    pReader["HOME_CITY_COUNT"]),
                StartedTime = Convert.ToDouble(pReader["STARTED_TIME"]),
                UpdatedTime = Convert.ToDouble(pReader["UPDATED_TIME"])
            };
        }

        private const string SnapshotColumns =
            "WAR_ID,ATTACKER_KINGDOM_ID,DEFENDER_KINGDOM_ID,SCORE," +
            "CITY_SCORE,BATTLE_SCORE,GOAL_SCORE,LOSS_SCORE,DECISIVE_SCORE," +
            "ATTACKER_LOSSES,DEFENDER_LOSSES," +
            "ATTACKER_MOBILIZATION_BASELINE," +
            "DEFENDER_MOBILIZATION_BASELINE," +
            "DURATION_YEARS,LAST_CALIBRATED_YEAR," +
            "ATTACKER_EXHAUSTION_RELIEF,DEFENDER_EXHAUSTION_RELIEF," +
            "ATTACKER_EXHAUSTION,DEFENDER_EXHAUSTION,ACTIVE,WINNER," +
            "STARTED_TIME,UPDATED_TIME,ENDED_TIME,REVISION";
    }
}
