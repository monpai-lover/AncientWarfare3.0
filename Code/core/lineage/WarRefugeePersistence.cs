using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Runtime.CompilerServices;

namespace AncientWarfare3.core.lineage
{
    internal static class WarRefugeePersistence
    {
        private const string JourneyTable = "AW_WarRefugeeJourney";
        private const string MemberTable = "AW_WarRefugeeMember";
        private const string OriginTable = "AW_WarRefugeeOrigin";
        private static readonly ConditionalWeakTable<SQLiteConnection, SchemaState>
            SchemaStates = new ConditionalWeakTable<SQLiteConnection, SchemaState>();

        private sealed class SchemaState
        {
            internal bool Ready;
        }

        internal static void EnsureSchema(SQLiteConnection pDb)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));
            SchemaState state = SchemaStates.GetValue(pDb,
                _ => new SchemaState());
            lock (state)
            {
                if (state.Ready) return;
                using var command = new SQLiteCommand(pDb);
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS " + JourneyTable + " (" +
                    "JOURNEY_ID INTEGER PRIMARY KEY,ORIGIN_KINGDOM_ID INTEGER NOT NULL," +
                    "ORIGIN_CITY_ID INTEGER NOT NULL,DESTINATION_KINGDOM_ID INTEGER NOT NULL," +
                    "DESTINATION_CITY_ID INTEGER NOT NULL,STATE INTEGER NOT NULL," +
                    "DEPARTURE_YEAR INTEGER NOT NULL,ARRIVAL_YEAR INTEGER NOT NULL," +
                    "RESERVED_CAPACITY INTEGER NOT NULL,SAFE_MONTHS INTEGER NOT NULL," +
                    "LAST_ASSIMILATION_YEAR INTEGER NOT NULL,ROUTE_RETRIES INTEGER NOT NULL " +
                    "DEFAULT 0,LAST_DISTANCE INTEGER NOT NULL DEFAULT 2147483647," +
                    "CONSECUTIVE_DANGER_MONTHS INTEGER NOT NULL DEFAULT 0," +
                    "NEXT_RETRY_MONTH INTEGER NOT NULL DEFAULT -1);" +
                    "CREATE TABLE IF NOT EXISTS " + MemberTable + " (" +
                    "JOURNEY_ID INTEGER NOT NULL,ACTOR_ID INTEGER NOT NULL," +
                    "IS_LEADER INTEGER NOT NULL,ACTIVE INTEGER NOT NULL," +
                    "ORIGIN_CULTURE TEXT NOT NULL,PRIMARY KEY(JOURNEY_ID,ACTOR_ID));" +
                    "CREATE UNIQUE INDEX IF NOT EXISTS AW_WarRefugeeActiveActor " +
                    "ON " + MemberTable + "(ACTOR_ID) WHERE ACTIVE=1;" +
                    "CREATE TABLE IF NOT EXISTS " + OriginTable + " (" +
                    "ACTOR_ID INTEGER PRIMARY KEY,JOURNEY_ID INTEGER NOT NULL," +
                    "ORIGIN_KINGDOM_ID INTEGER NOT NULL,ORIGIN_CITY_ID INTEGER NOT NULL," +
                    "ORIGIN_CULTURE TEXT NOT NULL,SETTLED_YEAR INTEGER NOT NULL);";
                command.ExecuteNonQuery();
                EnsureColumn(pDb, JourneyTable, "ROUTE_RETRIES",
                    "INTEGER NOT NULL DEFAULT 0");
                EnsureColumn(pDb, JourneyTable, "LAST_DISTANCE",
                    "INTEGER NOT NULL DEFAULT 2147483647");
                EnsureColumn(pDb, JourneyTable, "CONSECUTIVE_DANGER_MONTHS",
                    "INTEGER NOT NULL DEFAULT 0");
                EnsureColumn(pDb, JourneyTable, "NEXT_RETRY_MONTH",
                    "INTEGER NOT NULL DEFAULT -1");
                state.Ready = true;
            }
        }

        internal static bool UpsertJourney(SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney)
        {
            if (pDb == null || pJourney == null || pJourney.JourneyId < 0L)
                return false;
            EnsureSchema(pDb);
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                using (var update = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "UPDATE " + JourneyTable +
                        " SET ORIGIN_KINGDOM_ID=@ok,ORIGIN_CITY_ID=@oc," +
                        "DESTINATION_KINGDOM_ID=@dk,DESTINATION_CITY_ID=@dc," +
                        "STATE=@state,DEPARTURE_YEAR=@dy,ARRIVAL_YEAR=@ay," +
                        "RESERVED_CAPACITY=@capacity,SAFE_MONTHS=@safe," +
                        "LAST_ASSIMILATION_YEAR=@last,ROUTE_RETRIES=@retries," +
                        "LAST_DISTANCE=@distance,CONSECUTIVE_DANGER_MONTHS=@danger," +
                        "NEXT_RETRY_MONTH=@retry " +
                        "WHERE JOURNEY_ID=@id"
                })
                {
                    BindJourney(update, pJourney);
                    if (update.ExecuteNonQuery() == 0)
                    {
                        update.CommandText = "INSERT INTO " + JourneyTable +
                            " (JOURNEY_ID,ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID," +
                            "DESTINATION_KINGDOM_ID,DESTINATION_CITY_ID,STATE," +
                            "DEPARTURE_YEAR,ARRIVAL_YEAR,RESERVED_CAPACITY," +
                            "SAFE_MONTHS,LAST_ASSIMILATION_YEAR,ROUTE_RETRIES," +
                            "LAST_DISTANCE,CONSECUTIVE_DANGER_MONTHS,NEXT_RETRY_MONTH) VALUES " +
                            "(@id,@ok,@oc,@dk,@dc,@state,@dy,@ay,@capacity,@safe,@last," +
                            "@retries,@distance,@danger,@retry)";
                        update.ExecuteNonQuery();
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

        internal static bool TryLoadJourney(SQLiteConnection pDb, long pJourneyId,
            out WarRefugeeJourneySnapshot pJourney)
        {
            pJourney = null;
            if (pDb == null || pJourneyId < 0L) return false;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID," +
                "DESTINATION_KINGDOM_ID,DESTINATION_CITY_ID,STATE,DEPARTURE_YEAR," +
                "ARRIVAL_YEAR,RESERVED_CAPACITY,SAFE_MONTHS,LAST_ASSIMILATION_YEAR," +
                "ROUTE_RETRIES,LAST_DISTANCE,CONSECUTIVE_DANGER_MONTHS," +
                "NEXT_RETRY_MONTH " +
                "FROM " + JourneyTable + " WHERE JOURNEY_ID=@id";
            command.Parameters.AddWithValue("@id", pJourneyId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            pJourney = new WarRefugeeJourneySnapshot
            {
                JourneyId = pJourneyId,
                OriginKingdomId = reader.GetInt64(0),
                OriginCityId = reader.GetInt64(1),
                DestinationKingdomId = reader.GetInt64(2),
                DestinationCityId = reader.GetInt64(3),
                State = (WarRefugeeJourneyState)reader.GetInt32(4),
                DepartureYear = reader.GetInt32(5),
                ArrivalYear = reader.GetInt32(6),
                ReservedCapacity = reader.GetInt32(7),
                SafeMonths = reader.GetInt32(8),
                LastAssimilationYear = reader.GetInt32(9),
                RouteRetries = reader.GetInt32(10),
                LastDistance = reader.GetInt32(11),
                ConsecutiveDangerMonths = reader.GetInt32(12),
                NextRetryMonth = reader.GetInt32(13)
            };
            return true;
        }

        internal static bool InsertMember(SQLiteConnection pDb,
            WarRefugeeMemberSnapshot pMember)
        {
            if (pDb == null || pMember == null || pMember.JourneyId < 0L ||
                pMember.ActorId < 0L) return false;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "INSERT INTO " + MemberTable +
                " (JOURNEY_ID,ACTOR_ID,IS_LEADER,ACTIVE,ORIGIN_CULTURE) " +
                "VALUES (@journey,@actor,@leader,@active,@culture)";
            command.Parameters.AddWithValue("@journey", pMember.JourneyId);
            command.Parameters.AddWithValue("@actor", pMember.ActorId);
            command.Parameters.AddWithValue("@leader", pMember.IsLeader ? 1 : 0);
            command.Parameters.AddWithValue("@active", pMember.Active ? 1 : 0);
            command.Parameters.AddWithValue("@culture", pMember.OriginCulture ?? "");
            try
            {
                command.ExecuteNonQuery();
                return true;
            }
            catch (SQLiteException) { return false; }
        }

        internal static bool TryCreateHouseholdAtomic(SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney,
            IReadOnlyList<WarRefugeeMemberSnapshot> pMembers)
        {
            if (pDb == null || pJourney == null || pJourney.JourneyId < 0L ||
                pMembers == null || pMembers.Count == 0) return false;
            EnsureSchema(pDb);
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                using (var journey = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "INSERT INTO " + JourneyTable +
                        " (JOURNEY_ID,ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID," +
                        "DESTINATION_KINGDOM_ID,DESTINATION_CITY_ID,STATE," +
                        "DEPARTURE_YEAR,ARRIVAL_YEAR,RESERVED_CAPACITY,SAFE_MONTHS," +
                        "LAST_ASSIMILATION_YEAR,ROUTE_RETRIES,LAST_DISTANCE," +
                        "CONSECUTIVE_DANGER_MONTHS,NEXT_RETRY_MONTH) VALUES " +
                        "(@id,@ok,@oc,@dk,@dc,@state,@dy,@ay,@capacity,@safe,@last," +
                        "@retries,@distance,@danger,@retry)"
                })
                {
                    BindJourney(journey, pJourney);
                    journey.ExecuteNonQuery();
                }
                for (int i = 0; i < pMembers.Count; i++)
                {
                    WarRefugeeMemberSnapshot member = pMembers[i];
                    if (member == null || member.JourneyId != pJourney.JourneyId ||
                        member.ActorId < 0L) throw new InvalidOperationException();
                    using var command = new SQLiteCommand(pDb)
                    {
                        Transaction = transaction,
                        CommandText = "INSERT INTO " + MemberTable +
                            " (JOURNEY_ID,ACTOR_ID,IS_LEADER,ACTIVE,ORIGIN_CULTURE) " +
                            "VALUES (@journey,@actor,@leader,@active,@culture)"
                    };
                    command.Parameters.AddWithValue("@journey", member.JourneyId);
                    command.Parameters.AddWithValue("@actor", member.ActorId);
                    command.Parameters.AddWithValue("@leader", member.IsLeader ? 1 : 0);
                    command.Parameters.AddWithValue("@active", member.Active ? 1 : 0);
                    command.Parameters.AddWithValue("@culture", member.OriginCulture ?? "");
                    command.ExecuteNonQuery();
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

        internal static bool TryCommitHouseholdArrival(SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney,
            IReadOnlyList<WarRefugeeOriginSnapshot> pOrigins, bool pReleaseMembers)
        {
            if (pDb == null || pJourney == null || pJourney.JourneyId < 0L ||
                pOrigins == null || pOrigins.Count == 0) return false;
            EnsureSchema(pDb);
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                for (int i = 0; i < pOrigins.Count; i++)
                {
                    WarRefugeeOriginSnapshot origin = pOrigins[i];
                    using var archive = new SQLiteCommand(pDb)
                    {
                        Transaction = transaction,
                        CommandText = "INSERT OR IGNORE INTO " + OriginTable +
                            " (ACTOR_ID,JOURNEY_ID,ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID," +
                            "ORIGIN_CULTURE,SETTLED_YEAR) VALUES " +
                            "(@actor,@journey,@kingdom,@city,@culture,@year)"
                    };
                    archive.Parameters.AddWithValue("@actor", origin.ActorId);
                    archive.Parameters.AddWithValue("@journey", origin.JourneyId);
                    archive.Parameters.AddWithValue("@kingdom", origin.OriginKingdomId);
                    archive.Parameters.AddWithValue("@city", origin.OriginCityId);
                    archive.Parameters.AddWithValue("@culture", origin.OriginCulture ?? "");
                    archive.Parameters.AddWithValue("@year", origin.SettledYear);
                    archive.ExecuteNonQuery();
                }
                using (var update = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "UPDATE " + JourneyTable +
                        " SET STATE=@state,ARRIVAL_YEAR=@ay,RESERVED_CAPACITY=0," +
                        "SAFE_MONTHS=@safe,ROUTE_RETRIES=@retries,LAST_DISTANCE=@distance," +
                        "CONSECUTIVE_DANGER_MONTHS=@danger,NEXT_RETRY_MONTH=@retry " +
                        "WHERE JOURNEY_ID=@id"
                })
                {
                    BindJourney(update, pJourney);
                    if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException();
                }
                if (pReleaseMembers)
                {
                    using var release = new SQLiteCommand(pDb)
                    {
                        Transaction = transaction,
                        CommandText = "UPDATE " + MemberTable +
                            " SET ACTIVE=0 WHERE JOURNEY_ID=@journey AND ACTIVE=1"
                    };
                    release.Parameters.AddWithValue("@journey", pJourney.JourneyId);
                    release.ExecuteNonQuery();
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

        internal static bool TryTransitionHouseholdTerminalAtomic(
            SQLiteConnection pDb, long pJourneyId, WarRefugeeJourneyState pState)
        {
            if (pDb == null || pJourneyId < 0L ||
                (pState != WarRefugeeJourneyState.Settled &&
                 pState != WarRefugeeJourneyState.Cancelled)) return false;
            EnsureSchema(pDb);
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                using var journey = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "UPDATE " + JourneyTable +
                        " SET STATE=@state,RESERVED_CAPACITY=0 WHERE JOURNEY_ID=@id"
                };
                journey.Parameters.AddWithValue("@state", (int)pState);
                journey.Parameters.AddWithValue("@id", pJourneyId);
                if (journey.ExecuteNonQuery() != 1) throw new InvalidOperationException();
                using var members = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "UPDATE " + MemberTable +
                        " SET ACTIVE=0 WHERE JOURNEY_ID=@id"
                };
                members.Parameters.AddWithValue("@id", pJourneyId);
                members.ExecuteNonQuery();
                transaction.Commit();
                return true;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                return false;
            }
        }

        internal static int CountActiveMembers(SQLiteConnection pDb,
            long pJourneyId)
        {
            if (pDb == null || pJourneyId < 0L) return 0;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT COUNT(*) FROM " + MemberTable +
                " WHERE JOURNEY_ID=@journey AND ACTIVE=1";
            command.Parameters.AddWithValue("@journey", pJourneyId);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        internal static IReadOnlyList<WarRefugeeJourneySnapshot>
            LoadActiveJourneys(SQLiteConnection pDb, int pLimit,
                long pAfterJourneyId = -1L)
        {
            var result = new List<WarRefugeeJourneySnapshot>();
            if (pDb == null || pLimit <= 0) return result;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT JOURNEY_ID,ORIGIN_KINGDOM_ID," +
                "ORIGIN_CITY_ID,DESTINATION_KINGDOM_ID,DESTINATION_CITY_ID," +
                "STATE,DEPARTURE_YEAR,ARRIVAL_YEAR,RESERVED_CAPACITY," +
                "SAFE_MONTHS,LAST_ASSIMILATION_YEAR,ROUTE_RETRIES,LAST_DISTANCE," +
                "CONSECUTIVE_DANGER_MONTHS,NEXT_RETRY_MONTH FROM " + JourneyTable +
                " WHERE STATE IN (@traveling,@returning,@arrived) AND JOURNEY_ID>@after " +
                "ORDER BY JOURNEY_ID " +
                "LIMIT @limit";
            command.Parameters.AddWithValue("@traveling",
                (int)WarRefugeeJourneyState.Traveling);
            command.Parameters.AddWithValue("@returning",
                (int)WarRefugeeJourneyState.Returning);
            command.Parameters.AddWithValue("@arrived",
                (int)WarRefugeeJourneyState.Arrived);
            command.Parameters.AddWithValue("@limit", Math.Min(4096, pLimit));
            command.Parameters.AddWithValue("@after", pAfterJourneyId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadJourney(reader));
            return result;
        }

        internal static IReadOnlyList<WarRefugeeActiveMember>
            LoadActiveMembers(SQLiteConnection pDb, long pJourneyId, int pLimit)
        {
            var result = new List<WarRefugeeActiveMember>();
            if (pDb == null || pJourneyId < 0L || pLimit <= 0) return result;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT ACTOR_ID,IS_LEADER,ACTIVE," +
                "ORIGIN_CULTURE FROM " + MemberTable +
                " WHERE JOURNEY_ID=@journey AND ACTIVE=1 " +
                "ORDER BY IS_LEADER DESC,ACTOR_ID " +
                "LIMIT @limit";
            command.Parameters.AddWithValue("@journey", pJourneyId);
            command.Parameters.AddWithValue("@limit", Math.Min(256, pLimit));
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(new WarRefugeeActiveMember(
                pJourneyId, reader.GetInt64(0), reader.GetInt32(1) != 0,
                reader.GetInt32(2) != 0,
                reader.IsDBNull(3) ? "" : reader.GetString(3)));
            return result;
        }

        internal static bool SetMemberActive(SQLiteConnection pDb,
            long pJourneyId, long pActorId, bool pActive)
        {
            if (pDb == null || pJourneyId < 0L || pActorId < 0L) return false;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "UPDATE " + MemberTable +
                " SET ACTIVE=@active WHERE JOURNEY_ID=@journey AND ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@active", pActive ? 1 : 0);
            command.Parameters.AddWithValue("@journey", pJourneyId);
            command.Parameters.AddWithValue("@actor", pActorId);
            return command.ExecuteNonQuery() > 0;
        }

        internal static bool SetMemberLeader(SQLiteConnection pDb,
            long pJourneyId, long pActorId)
        {
            if (pDb == null || pJourneyId < 0L || pActorId < 0L) return false;
            EnsureSchema(pDb);
            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
                using (var clear = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "UPDATE " + MemberTable +
                        " SET IS_LEADER=0 WHERE JOURNEY_ID=@journey"
                })
                {
                    clear.Parameters.AddWithValue("@journey", pJourneyId);
                    clear.ExecuteNonQuery();
                }
                using (var promote = new SQLiteCommand(pDb)
                {
                    Transaction = transaction,
                    CommandText = "UPDATE " + MemberTable +
                        " SET IS_LEADER=1 WHERE JOURNEY_ID=@journey AND " +
                        "ACTOR_ID=@actor AND ACTIVE=1"
                })
                {
                    promote.Parameters.AddWithValue("@journey", pJourneyId);
                    promote.Parameters.AddWithValue("@actor", pActorId);
                    bool changed = promote.ExecuteNonQuery() > 0;
                    transaction.Commit();
                    return changed;
                }
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                return false;
            }
        }

        internal static bool TryGetActiveJourneyForActor(SQLiteConnection pDb,
            long pActorId, out long pJourneyId)
        {
            pJourneyId = -1L;
            if (pDb == null || pActorId < 0L) return false;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT m.JOURNEY_ID FROM " + MemberTable +
                " m INNER JOIN " + JourneyTable + " j ON j.JOURNEY_ID=m.JOURNEY_ID" +
                " WHERE m.ACTOR_ID=@actor AND m.ACTIVE=1 AND j.STATE IN " +
                "(@traveling,@returning,@arrived) LIMIT 1";
            command.Parameters.AddWithValue("@actor", pActorId);
            command.Parameters.AddWithValue("@traveling",
                (int)WarRefugeeJourneyState.Traveling);
            command.Parameters.AddWithValue("@returning",
                (int)WarRefugeeJourneyState.Returning);
            command.Parameters.AddWithValue("@arrived",
                (int)WarRefugeeJourneyState.Arrived);
            object value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value) return false;
            pJourneyId = Convert.ToInt64(value);
            return pJourneyId >= 0L;
        }

        internal static int CountActiveReservations(SQLiteConnection pDb,
            long pDestinationCityId)
        {
            if (pDb == null || pDestinationCityId < 0L) return 0;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT COALESCE(SUM(RESERVED_CAPACITY),0) FROM " +
                JourneyTable + " WHERE DESTINATION_CITY_ID=@city AND STATE IN " +
                "(@traveling,@returning)";
            command.Parameters.AddWithValue("@city", pDestinationCityId);
            command.Parameters.AddWithValue("@traveling",
                (int)WarRefugeeJourneyState.Traveling);
            command.Parameters.AddWithValue("@returning",
                (int)WarRefugeeJourneyState.Returning);
            return Math.Max(0, Convert.ToInt32(command.ExecuteScalar()));
        }

        internal static IReadOnlyDictionary<long, int> LoadActiveReservations(
            SQLiteConnection pDb)
        {
            var result = new Dictionary<long, int>();
            if (pDb == null) return result;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT DESTINATION_CITY_ID," +
                "COALESCE(SUM(RESERVED_CAPACITY),0) FROM " + JourneyTable +
                " WHERE STATE IN (@traveling,@returning) " +
                "GROUP BY DESTINATION_CITY_ID";
            command.Parameters.AddWithValue("@traveling",
                (int)WarRefugeeJourneyState.Traveling);
            command.Parameters.AddWithValue("@returning",
                (int)WarRefugeeJourneyState.Returning);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                result[reader.GetInt64(0)] = Math.Max(0, reader.GetInt32(1));
            return result;
        }

        internal static bool InsertOrigin(SQLiteConnection pDb,
            WarRefugeeOriginSnapshot pOrigin)
        {
            if (pDb == null || pOrigin == null || pOrigin.ActorId < 0L)
                return false;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "INSERT OR IGNORE INTO " + OriginTable +
                " (ACTOR_ID,JOURNEY_ID,ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID," +
                "ORIGIN_CULTURE,SETTLED_YEAR) VALUES " +
                "(@actor,@journey,@kingdom,@city,@culture,@year)";
            command.Parameters.AddWithValue("@actor", pOrigin.ActorId);
            command.Parameters.AddWithValue("@journey", pOrigin.JourneyId);
            command.Parameters.AddWithValue("@kingdom", pOrigin.OriginKingdomId);
            command.Parameters.AddWithValue("@city", pOrigin.OriginCityId);
            command.Parameters.AddWithValue("@culture", pOrigin.OriginCulture ?? "");
            command.Parameters.AddWithValue("@year", pOrigin.SettledYear);
            command.ExecuteNonQuery();
            return true;
        }

        internal static bool HasOrigin(SQLiteConnection pDb, long pActorId)
        {
            if (pDb == null || pActorId < 0L) return false;
            EnsureSchema(pDb);
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT 1 FROM " + OriginTable +
                " WHERE ACTOR_ID=@actor LIMIT 1";
            command.Parameters.AddWithValue("@actor", pActorId);
            return command.ExecuteScalar() != null;
        }

        private static void BindJourney(SQLiteCommand pCommand,
            WarRefugeeJourneySnapshot pJourney)
        {
            pCommand.Parameters.Clear();
            pCommand.Parameters.AddWithValue("@id", pJourney.JourneyId);
            pCommand.Parameters.AddWithValue("@ok", pJourney.OriginKingdomId);
            pCommand.Parameters.AddWithValue("@oc", pJourney.OriginCityId);
            pCommand.Parameters.AddWithValue("@dk", pJourney.DestinationKingdomId);
            pCommand.Parameters.AddWithValue("@dc", pJourney.DestinationCityId);
            pCommand.Parameters.AddWithValue("@state", (int)pJourney.State);
            pCommand.Parameters.AddWithValue("@dy", pJourney.DepartureYear);
            pCommand.Parameters.AddWithValue("@ay", pJourney.ArrivalYear);
            pCommand.Parameters.AddWithValue("@capacity", pJourney.ReservedCapacity);
            pCommand.Parameters.AddWithValue("@safe", pJourney.SafeMonths);
            pCommand.Parameters.AddWithValue("@last", pJourney.LastAssimilationYear);
            pCommand.Parameters.AddWithValue("@retries", pJourney.RouteRetries);
            pCommand.Parameters.AddWithValue("@distance", pJourney.LastDistance);
            pCommand.Parameters.AddWithValue("@danger", pJourney.ConsecutiveDangerMonths);
            pCommand.Parameters.AddWithValue("@retry", pJourney.NextRetryMonth);
        }

        private static WarRefugeeJourneySnapshot ReadJourney(
            SQLiteDataReader pReader)
        {
            return new WarRefugeeJourneySnapshot
            {
                JourneyId = pReader.GetInt64(0),
                OriginKingdomId = pReader.GetInt64(1),
                OriginCityId = pReader.GetInt64(2),
                DestinationKingdomId = pReader.GetInt64(3),
                DestinationCityId = pReader.GetInt64(4),
                State = (WarRefugeeJourneyState)pReader.GetInt32(5),
                DepartureYear = pReader.GetInt32(6),
                ArrivalYear = pReader.GetInt32(7),
                ReservedCapacity = pReader.GetInt32(8),
                SafeMonths = pReader.GetInt32(9),
                LastAssimilationYear = pReader.GetInt32(10),
                RouteRetries = pReader.GetInt32(11),
                LastDistance = pReader.GetInt32(12),
                ConsecutiveDangerMonths = pReader.GetInt32(13),
                NextRetryMonth = pReader.GetInt32(14)
            };
        }

        private static void EnsureColumn(SQLiteConnection pDb, string pTable,
            string pColumn, string pDefinition)
        {
            using var inspect = new SQLiteCommand("PRAGMA table_info(" + pTable + ")", pDb);
            using SQLiteDataReader reader = inspect.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader.GetString(1), pColumn,
                    StringComparison.OrdinalIgnoreCase)) return;
            reader.Close();
            using var alter = new SQLiteCommand("ALTER TABLE " + pTable +
                " ADD COLUMN " + pColumn + " " + pDefinition, pDb);
            alter.ExecuteNonQuery();
        }
    }
}
