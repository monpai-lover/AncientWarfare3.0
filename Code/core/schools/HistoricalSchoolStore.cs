using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolStore
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static string MembershipTable => SchoolMembershipTableItem.GetTableName();
        private static string MasterTable => HistoricalSchoolMasterTableItem.GetTableName();
        private static string AffiliationTable => SchoolAffiliationTableItem.GetTableName();
        private static string RuntimeTable => HistoricalSchoolRuntimeStateTableItem.GetTableName();
        private static string EventTable => SchoolEventTableItem.GetTableName();
        private static string WorkTable => SchoolWorkTableItem.GetTableName();
        private static string DebateTable => SchoolDebateTableItem.GetTableName();
        private static string InstitutionTable => SchoolInstitutionTableItem.GetTableName();
        private static string LedgerTable => CitySchoolLedgerTableItem.GetTableName();

        private const double MaxLedgerValue = 1d;
        private const double MaxLedgerInstitutions = 100d;
        private const int LedgerCityQueryChunkSize = 128;
        private const int LedgerReadCacheCapacity = 128;
        private static readonly HistoricalSchoolYearCityCache<
                Dictionary<string, HistoricalSchoolLedgerSnapshot>> LedgerReadCache =
            new HistoricalSchoolYearCityCache<
                Dictionary<string, HistoricalSchoolLedgerSnapshot>>(
                LedgerReadCacheCapacity);
        public static long NextMembershipId()
        {
            return DB == null ? -1L : TableIdAllocator.Next(DB, MembershipTable,
                "MEMBERSHIP_ID");
        }

        internal static HistoricalSchoolTeachingHistory LoadTeachingHistory()
        {
            try
            {
                return HistoricalSchoolTeachingPersistenceDb.LoadHistory(DB);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load teaching history failed: " +
                                    error.Message);
                return new HistoricalSchoolTeachingHistory();
            }
        }

        internal static HistoricalSchoolTeachingDbResult RecordTeaching(
            HistoricalSchoolTeachingPlan pPlan, string pActorName, long pTargetActorId,
            string pTargetActorName, double pWorldTime)
        {
            if (DB == null || !pPlan.IsValid)
                return new HistoricalSchoolTeachingDbResult(
                    HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            try
            {
                var request = new HistoricalSchoolTeachingDbRequest(pPlan, pActorName,
                    pTargetActorId, pTargetActorName, pWorldTime);
                HistoricalSchoolTeachingDbResult result =
                    HistoricalSchoolTeachingPersistenceDb.Record(DB, request);
                if (result.PersistedNew)
                    InvalidateLedgerCaches(pPlan.Candidate.CityId);
                return result;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore teaching transaction failed: " +
                                    error.Message);
                return new HistoricalSchoolTeachingDbResult(
                    HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            }
        }

        internal static HistoricalSchoolTeachingDbResult RecordTeachingInTransaction(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            HistoricalSchoolTeachingDbRequest pRequest)
        {
            return HistoricalSchoolTeachingPersistenceDb.RecordInTransaction(pDb,
                pTransaction, pRequest);
        }

        internal static void InvalidateTeachingCommit(long pCityId)
        {
            InvalidateLedgerCaches(pCityId);
        }

        public static bool RecordSchoolEvent(string pEventType, long pActorId,
            long pTargetActorId, string pSchoolId, long pCityId, long pKingdomId, int pYear,
            string pPayload, int pImportance, double pWorldTime)
        {
            if (DB == null || string.IsNullOrWhiteSpace(pEventType) || pActorId < 0) return false;
            long eventId = TableIdAllocator.Next(DB, EventTable, "EVENT_ID");
            if (eventId < 0) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                using (var command = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    command.CommandText = "INSERT INTO " + EventTable +
                        " (EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID,CITY_ID," +
                        "KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME) VALUES " +
                        " (@id,@operationKey,@type,@actor,@target,@school,@city,@kingdom,@year,@payload," +
                        "@importance,@time)";
                    command.Parameters.AddWithValue("@id", eventId);
                    command.Parameters.AddWithValue("@operationKey", "");
                    command.Parameters.AddWithValue("@type", pEventType);
                    command.Parameters.AddWithValue("@actor", pActorId);
                    command.Parameters.AddWithValue("@target", pTargetActorId);
                    command.Parameters.AddWithValue("@school", pSchoolId ?? "");
                    command.Parameters.AddWithValue("@city", pCityId);
                    command.Parameters.AddWithValue("@kingdom", pKingdomId);
                    command.Parameters.AddWithValue("@year", pYear);
                    command.Parameters.AddWithValue("@payload", pPayload ?? "");
                    command.Parameters.AddWithValue("@importance", Math.Max(0, pImportance));
                    command.Parameters.AddWithValue("@time", FiniteNonNegative(pWorldTime));
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("school event insert failed");
                }
                if (EventLedgerDelta(pEventType, pYear,
                        out HistoricalSchoolLedgerDelta ledgerDelta,
                        out double membershipDelta) && pCityId >= 0 &&
                    !string.IsNullOrWhiteSpace(pSchoolId))
                    UpsertLedgerCommand(transaction, pCityId, pSchoolId, ledgerDelta,
                        pWorldTime, membershipDelta);
                transaction.Commit();
                InvalidateLedgerCaches(pCityId);
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore insert event failed: " +
                                    error.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome
            RecordSchoolEventInTransaction(SQLiteConnection pDb,
                SQLiteTransaction pTransaction, string pOperationKey, string pEventType,
                long pActorId, long pTargetActorId, string pSchoolId, long pCityId,
                long pKingdomId, int pYear, string pPayload, int pImportance,
                double pWorldTime)
        {
            if (pDb == null || pTransaction == null ||
                string.IsNullOrWhiteSpace(pOperationKey) ||
                string.IsNullOrWhiteSpace(pEventType) || pActorId < 0)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            int rows = 0;
            bool exact = false;
            using (var read = new SQLiteCommand(pDb) { Transaction = pTransaction })
            {
                read.CommandText = "SELECT EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID," +
                    "CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME FROM " +
                    EventTable + " WHERE OPERATION_KEY=@operation";
                read.Parameters.AddWithValue("@operation", pOperationKey);
                using SQLiteDataReader reader = read.ExecuteReader();
                while (reader.Read())
                {
                    rows++;
                    exact |= ValueString(reader, 0) == pEventType &&
                             ValueLong(reader, 1, -1L) == pActorId &&
                             ValueLong(reader, 2, -1L) == pTargetActorId &&
                             ValueString(reader, 3) == (pSchoolId ?? "") &&
                             ValueLong(reader, 4, -1L) == pCityId &&
                             ValueLong(reader, 5, -1L) == pKingdomId &&
                             ValueInt(reader, 6, -1) == pYear &&
                             ValueString(reader, 7) == (pPayload ?? "") &&
                             ValueInt(reader, 8) == Math.Max(0, pImportance) &&
                             ValueDouble(reader, 9, -1d).Equals(
                                 FiniteNonNegative(pWorldTime));
                }
            }
            if (rows > 0) return rows == 1 && exact
                ? HistoricalSchoolTeachingPersistenceOutcome.Replayed
                : HistoricalSchoolTeachingPersistenceOutcome.Unknown;

            long eventId = NextIdInTransaction(pTransaction, EventTable, "EVENT_ID");
            using (var command = new SQLiteCommand(pDb) { Transaction = pTransaction })
            {
                command.CommandText = "INSERT INTO " + EventTable +
                    " (EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID," +
                    "SCHOOL_ID,CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME)" +
                    " VALUES (@id,@operation,@type,@actor,@target,@school,@city,@kingdom," +
                    "@year,@payload,@importance,@time)";
                command.Parameters.AddWithValue("@id", eventId);
                command.Parameters.AddWithValue("@operation", pOperationKey);
                command.Parameters.AddWithValue("@type", pEventType);
                command.Parameters.AddWithValue("@actor", pActorId);
                command.Parameters.AddWithValue("@target", pTargetActorId);
                command.Parameters.AddWithValue("@school", pSchoolId ?? "");
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@payload", pPayload ?? "");
                command.Parameters.AddWithValue("@importance", Math.Max(0, pImportance));
                command.Parameters.AddWithValue("@time", FiniteNonNegative(pWorldTime));
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("school event insert failed");
            }
            if (EventLedgerDelta(pEventType, pYear,
                    out HistoricalSchoolLedgerDelta ledgerDelta,
                    out double membershipDelta) && pCityId >= 0 &&
                !string.IsNullOrWhiteSpace(pSchoolId))
                UpsertLedgerCommand(pTransaction, pCityId, pSchoolId, ledgerDelta,
                    pWorldTime, membershipDelta);
            return HistoricalSchoolTeachingPersistenceOutcome.Committed;
        }

        public static Dictionary<long, long> LoadLineageSuccessors()
        {
            var result = new Dictionary<long, long>();
            if (DB == null) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT TARGET_ACTOR_ID,ACTOR_ID FROM " + EventTable +
                    " WHERE EVENT_TYPE=@type AND TARGET_ACTOR_ID>=0 AND ACTOR_ID>=0" +
                    " ORDER BY EVENT_YEAR,EVENT_ID";
                command.Parameters.AddWithValue("@type", "lineage_successor");
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result[ValueLong(reader, 0, -1L)] = ValueLong(reader, 1, -1L);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load successors failed: " +
                                    error.Message);
            }
            return result;
        }

        public static bool EnsureMemberAffiliation(long pActorId, long pHomeKingdomId,
            string pHomeKingdomName, long pHometownCityId, int pYear, double pTime)
        {
            if (DB == null || pActorId < 0 || pHomeKingdomId < 0 || pHometownCityId < 0)
                return false;
            try
            {
                if (DB.CheckKeyExist(AffiliationTable,
                        SimpleColumnConstraint.CreateEq("ACTOR_ID", pActorId))) return true;
                DB.Insert(AffiliationTable,
                    ColumnVal.Create("ACTOR_ID", pActorId),
                    ColumnVal.Create("HOME_KINGDOM_ID", pHomeKingdomId),
                    ColumnVal.Create("HOME_KINGDOM_NAME", pHomeKingdomName ?? ""),
                    ColumnVal.Create("HOMETOWN_CITY_ID", pHometownCityId),
                    ColumnVal.Create("RESIDENCE_CITY_ID", pHometownCityId),
                    ColumnVal.Create("PREVIOUS_RESIDENCE_CITY_ID", -1L),
                    ColumnVal.Create("DESTINATION_CITY_ID", -1L),
                    ColumnVal.Create("SERVICE_KINGDOM_ID", -1L),
                    ColumnVal.Create("LIFECYCLE_STATE",
                        HistoricalSchoolLifecycleState.AtHome.ToString()),
                    ColumnVal.Create("SERVICE_START_YEAR", -1),
                    ColumnVal.Create("SERVICE_END_YEAR", -1),
                    ColumnVal.Create("LAST_TRAVEL_YEAR", pYear),
                    ColumnVal.Create("TRAVEL_WAIT_START_YEAR", -1),
                    ColumnVal.Create("VOYAGE_START_YEAR", -1),
                    ColumnVal.Create("VOYAGE_ARRIVAL_YEAR", -1),
                    ColumnVal.Create("TRANSPORT_FAILURES", 0),
                    ColumnVal.Create("UPDATED_TIME", pTime));
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore ensure affiliation failed: " +
                                    error.Message);
                return false;
            }
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome
            EnsureMemberAffiliationInTransaction(SQLiteConnection pDb,
                SQLiteTransaction pTransaction,
                HistoricalSchoolAffiliationSnapshot pState, double pTime)
        {
            if (pDb == null || pTransaction == null || pState == null ||
                pState.ActorId < 0 || pState.HomeKingdomId < 0 ||
                pState.HometownCityId < 0)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            int rows = 0;
            bool exact = false;
            using (var read = new SQLiteCommand(pDb) { Transaction = pTransaction })
            {
                read.CommandText = "SELECT HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                    "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                    "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE," +
                    "SERVICE_START_YEAR,SERVICE_END_YEAR,LAST_TRAVEL_YEAR," +
                    "TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR," +
                    "TRANSPORT_FAILURES,UPDATED_TIME FROM " + AffiliationTable +
                    " WHERE ACTOR_ID=@actor";
                read.Parameters.AddWithValue("@actor", pState.ActorId);
                using SQLiteDataReader reader = read.ExecuteReader();
                while (reader.Read())
                {
                    rows++;
                    exact |= ValueLong(reader, 0, -1L) == pState.HomeKingdomId &&
                             ValueString(reader, 1) == pState.HomeKingdomName &&
                             ValueLong(reader, 2, -1L) == pState.HometownCityId &&
                             ValueLong(reader, 3, -1L) == pState.ResidenceCityId &&
                             ValueLong(reader, 4, -1L) ==
                             pState.PreviousResidenceCityId &&
                             ValueLong(reader, 5, -1L) == pState.DestinationCityId &&
                             ValueLong(reader, 6, -1L) == pState.ServiceKingdomId &&
                             ValueString(reader, 7) == pState.LifecycleState.ToString() &&
                             ValueInt(reader, 8, -1) == pState.ServiceStartYear &&
                             ValueInt(reader, 9, -1) == pState.ServiceEndYear &&
                             ValueInt(reader, 10, -1) == pState.LastTravelYear &&
                             ValueInt(reader, 11, -1) == pState.TravelWaitStartYear &&
                             ValueInt(reader, 12, -1) == pState.VoyageStartYear &&
                             ValueInt(reader, 13, -1) == pState.VoyageArrivalYear &&
                             ValueInt(reader, 14) == pState.TransportFailures &&
                             ValueDouble(reader, 15, -1d).Equals(
                                 FiniteNonNegative(pTime));
                }
            }
            if (rows > 0) return rows == 1 && exact
                ? HistoricalSchoolTeachingPersistenceOutcome.Replayed
                : HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + AffiliationTable +
                " (ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID," +
                "RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID,DESTINATION_CITY_ID," +
                "SERVICE_KINGDOM_ID,LIFECYCLE_STATE,SERVICE_START_YEAR,SERVICE_END_YEAR," +
                "LAST_TRAVEL_YEAR,TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR," +
                "VOYAGE_ARRIVAL_YEAR,TRANSPORT_FAILURES,UPDATED_TIME) VALUES " +
                "(@actor,@home,@homeName,@hometown,@residence,@previous,@destination," +
                "@service,@state,@serviceStart,@serviceEnd,@lastTravel,@wait,@voyage," +
                "@arrival,@failures,@time)";
            command.Parameters.AddWithValue("@actor", pState.ActorId);
            command.Parameters.AddWithValue("@home", pState.HomeKingdomId);
            command.Parameters.AddWithValue("@homeName", pState.HomeKingdomName ?? "");
            command.Parameters.AddWithValue("@hometown", pState.HometownCityId);
            command.Parameters.AddWithValue("@residence", pState.ResidenceCityId);
            command.Parameters.AddWithValue("@previous", pState.PreviousResidenceCityId);
            command.Parameters.AddWithValue("@destination", pState.DestinationCityId);
            command.Parameters.AddWithValue("@service", pState.ServiceKingdomId);
            command.Parameters.AddWithValue("@state", pState.LifecycleState.ToString());
            command.Parameters.AddWithValue("@serviceStart", pState.ServiceStartYear);
            command.Parameters.AddWithValue("@serviceEnd", pState.ServiceEndYear);
            command.Parameters.AddWithValue("@lastTravel", pState.LastTravelYear);
            command.Parameters.AddWithValue("@wait", pState.TravelWaitStartYear);
            command.Parameters.AddWithValue("@voyage", pState.VoyageStartYear);
            command.Parameters.AddWithValue("@arrival", pState.VoyageArrivalYear);
            command.Parameters.AddWithValue("@failures", pState.TransportFailures);
            command.Parameters.AddWithValue("@time", FiniteNonNegative(pTime));
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("member affiliation insert failed");
            return HistoricalSchoolTeachingPersistenceOutcome.Committed;
        }

        public static bool HasPreservedWork(string pWorkKey, string pSchoolId)
        {
            if (DB == null || string.IsNullOrWhiteSpace(pWorkKey) || string.IsNullOrWhiteSpace(pSchoolId))
                return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT 1 FROM " + WorkTable +
                    " WHERE WORK_KEY=@key AND SCHOOL_ID=@school AND PRESERVED=1 LIMIT 1";
                command.Parameters.AddWithValue("@key", pWorkKey);
                command.Parameters.AddWithValue("@school", pSchoolId);
                return command.ExecuteScalar() != null;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore work lookup failed: " + error.Message);
                return false;
            }
        }

        public static long PreservedWorkCity(string pWorkKey, string pSchoolId)
        {
            if (DB == null || string.IsNullOrWhiteSpace(pWorkKey) ||
                string.IsNullOrWhiteSpace(pSchoolId)) return -1L;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT CITY_ID FROM " + WorkTable +
                    " WHERE WORK_KEY=@key AND SCHOOL_ID=@school AND PRESERVED=1" +
                    " ORDER BY WORK_ID LIMIT 1";
                command.Parameters.AddWithValue("@key", pWorkKey);
                command.Parameters.AddWithValue("@school", pSchoolId);
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? -1L : Convert.ToInt64(value);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore work city lookup failed: " +
                                    error.Message);
                return -1L;
            }
        }

        public static bool RecordSchoolWork(string pWorkKey, string pDisplayName,
            string pSchoolId, long pAuthorActorId, long pCityId, int pYear,
            long pKingdomId = -1L)
        {
            if (DB == null || string.IsNullOrWhiteSpace(pWorkKey) ||
                string.IsNullOrWhiteSpace(pSchoolId) || pAuthorActorId < 0) return false;
            if (HasPreservedWork(pWorkKey, pSchoolId)) return false;
            long workId = TableIdAllocator.Next(DB, WorkTable, "WORK_ID");
            long eventId = TableIdAllocator.Next(DB, EventTable, "EVENT_ID");
            if (workId < 0 || eventId < 0) return false;
            double worldTime = World.world?.getCurWorldTime() ?? 0d;
            using SQLiteTransaction transaction = DB.BeginTransaction();
            try
            {
                using (var work = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    work.CommandText = "INSERT INTO " + WorkTable +
                        " (WORK_ID,WORK_KEY,DISPLAY_NAME,SCHOOL_ID,AUTHOR_ACTOR_ID,CITY_ID," +
                        "INSTITUTION_ID,WRITTEN_YEAR,PRESERVED,CONDITION,UPDATED_TIME) VALUES " +
                        " (@id,@key,@name,@school,@author,@city,-1,@year,1,100,@time)";
                    work.Parameters.AddWithValue("@id", workId);
                    work.Parameters.AddWithValue("@key", pWorkKey);
                    work.Parameters.AddWithValue("@name", pDisplayName ?? pWorkKey);
                    work.Parameters.AddWithValue("@school", pSchoolId);
                    work.Parameters.AddWithValue("@author", pAuthorActorId);
                    work.Parameters.AddWithValue("@city", pCityId);
                    work.Parameters.AddWithValue("@year", pYear);
                    work.Parameters.AddWithValue("@time", worldTime);
                    if (work.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("school work insert failed");
                }
                using (var schoolEvent = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    schoolEvent.CommandText = "INSERT INTO " + EventTable +
                        " (EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID,CITY_ID," +
                        "KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME) VALUES " +
                        " (@id,'','work_authored',@actor,-1,@school,@city,@kingdom,@year,@payload,2,@time)";
                    schoolEvent.Parameters.AddWithValue("@id", eventId);
                    schoolEvent.Parameters.AddWithValue("@actor", pAuthorActorId);
                    schoolEvent.Parameters.AddWithValue("@school", pSchoolId);
                    schoolEvent.Parameters.AddWithValue("@city", pCityId);
                    schoolEvent.Parameters.AddWithValue("@kingdom", pKingdomId);
                    schoolEvent.Parameters.AddWithValue("@year", pYear);
                    schoolEvent.Parameters.AddWithValue("@payload", pWorkKey);
                    schoolEvent.Parameters.AddWithValue("@time", worldTime);
                    if (schoolEvent.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("school work event insert failed");
                }
                if (pCityId >= 0 && EventLedgerDelta("work_authored", pYear,
                        out HistoricalSchoolLedgerDelta ledgerDelta,
                        out double membershipDelta))
                    UpsertLedgerCommand(transaction, pCityId, pSchoolId, ledgerDelta, worldTime,
                        membershipDelta);
                transaction.Commit();
                InvalidateLedgerCaches(pCityId);
                return true;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore insert work/event failed: " +
                                    error.Message);
                return false;
            }
        }

        /// <summary>
        ///     Persists one debate, its history event, and both city-school ledger
        ///     deltas atomically.  The duplicate check is repeated after the
        ///     transaction starts so a caller cannot race a second yearly result.
        /// </summary>
        internal static HistoricalSchoolTeachingPersistenceOutcome RecordDebateAndLedger(
            HistoricalSchoolDebateRecord pDebate,
            HistoricalSchoolLedgerDelta pFirstDelta, HistoricalSchoolLedgerDelta pSecondDelta,
            double pWorldTime)
        {
            if (DB == null) return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            if (pDebate == null || pFirstDelta == null || pSecondDelta == null ||
                pDebate.CityId < 0 || pDebate.DebateYear < 0 || pDebate.FirstActorId < 0 ||
                pDebate.SecondActorId < 0 ||
                pDebate.FirstActorId == pDebate.SecondActorId ||
                string.IsNullOrWhiteSpace(pDebate.FirstSchoolId) ||
                string.IsNullOrWhiteSpace(pDebate.SecondSchoolId) ||
                string.Equals(pDebate.FirstSchoolId, pDebate.SecondSchoolId,
                    StringComparison.Ordinal) || string.IsNullOrWhiteSpace(pDebate.TopicId) ||
                !Enum.IsDefined(typeof(SchoolDebateOutcome), pDebate.Outcome))
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;

            string firstSchool = ResolveLedgerSchool(pFirstDelta, pDebate.FirstSchoolId);
            string secondSchool = ResolveLedgerSchool(pSecondDelta, pDebate.SecondSchoolId);
            if (string.IsNullOrWhiteSpace(firstSchool) || string.IsNullOrWhiteSpace(secondSchool) ||
                !string.Equals(firstSchool, pDebate.FirstSchoolId, StringComparison.Ordinal) ||
                !string.Equals(secondSchool, pDebate.SecondSchoolId, StringComparison.Ordinal) ||
                string.Equals(firstSchool, secondSchool, StringComparison.Ordinal))
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;

            try
            {
                HistoricalSchoolTeachingPersistenceOutcome existing =
                    ReadExistingDebateOutcome(null, pDebate, out bool exists);
                if (exists) return existing;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore read debate state failed: " +
                                    error.Message);
                return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            }

            // IDs are allocated before opening the transaction.  Ledger rows use
            // city+school keys and therefore do not consume an allocator id.
            long debateId = TableIdAllocator.Next(DB, DebateTable, "DEBATE_ID");
            long firstEventId = TableIdAllocator.Next(DB, EventTable, "EVENT_ID");
            if (debateId < 0 || firstEventId < 0 || firstEventId >= long.MaxValue - 1L)
                return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            long secondEventId = firstEventId + 1L;

            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                HistoricalSchoolTeachingPersistenceOutcome existing =
                    ReadExistingDebateOutcome(transaction, pDebate, out bool exists);
                if (exists)
                {
                    transaction.Rollback();
                    return existing;
                }

                InsertDebateCommand(transaction, debateId, pDebate);
                InsertDebateEventCommand(transaction, firstEventId, pDebate, firstSchool,
                    pDebate.FirstActorId, pDebate.SecondActorId, pWorldTime);
                InsertDebateEventCommand(transaction, secondEventId, pDebate, secondSchool,
                    pDebate.SecondActorId, pDebate.FirstActorId, pWorldTime);
                UpsertLedgerCommand(transaction, pDebate.CityId, firstSchool, pFirstDelta,
                    pWorldTime);
                UpsertLedgerCommand(transaction, pDebate.CityId, secondSchool, pSecondDelta,
                    pWorldTime);
                UpdateMembershipReputationCommand(transaction, pDebate.FirstActorId,
                    ReputationDelta(pDebate.Outcome, pFirst: true), pWorldTime);
                UpdateMembershipReputationCommand(transaction, pDebate.SecondActorId,
                    ReputationDelta(pDebate.Outcome, pFirst: false), pWorldTime);
                transaction.Commit();
                InvalidateLedgerCaches(pDebate.CityId);
                return HistoricalSchoolTeachingPersistenceOutcome.Committed;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore record debate failed: " +
                                    error.Message);
                try
                {
                    HistoricalSchoolTeachingPersistenceOutcome recovered =
                        ReadExistingDebateOutcome(null, pDebate, out bool exists);
                    if (exists) return recovered;
                }
                catch (Exception recoveryError)
                {
                    ModClass.LogWarning("HistoricalSchoolStore recover debate failed: " +
                                        recoveryError.Message);
                }
                return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome
            RecordDebateAndLedgerInTransaction(SQLiteConnection pDb,
                SQLiteTransaction pTransaction, HistoricalSchoolDebateRecord pDebate,
                HistoricalSchoolLedgerDelta pFirstDelta,
                HistoricalSchoolLedgerDelta pSecondDelta, double pWorldTime)
        {
            if (pDb == null || pTransaction == null || pDebate == null ||
                pFirstDelta == null || pSecondDelta == null || pDebate.CityId < 0 ||
                pDebate.DebateYear < 0 || pDebate.FirstActorId < 0 ||
                pDebate.SecondActorId < 0 ||
                pDebate.FirstActorId == pDebate.SecondActorId ||
                string.IsNullOrWhiteSpace(pDebate.FirstSchoolId) ||
                string.IsNullOrWhiteSpace(pDebate.SecondSchoolId) ||
                string.Equals(pDebate.FirstSchoolId, pDebate.SecondSchoolId,
                    StringComparison.Ordinal) || string.IsNullOrWhiteSpace(pDebate.TopicId) ||
                !Enum.IsDefined(typeof(SchoolDebateOutcome), pDebate.Outcome))
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;

            string firstSchool = ResolveLedgerSchool(pFirstDelta, pDebate.FirstSchoolId);
            string secondSchool = ResolveLedgerSchool(pSecondDelta, pDebate.SecondSchoolId);
            if (string.IsNullOrWhiteSpace(firstSchool) ||
                string.IsNullOrWhiteSpace(secondSchool) ||
                !string.Equals(firstSchool, pDebate.FirstSchoolId,
                    StringComparison.Ordinal) ||
                !string.Equals(secondSchool, pDebate.SecondSchoolId,
                    StringComparison.Ordinal) ||
                string.Equals(firstSchool, secondSchool, StringComparison.Ordinal))
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;

            HistoricalSchoolTeachingPersistenceOutcome existing =
                ReadExistingDebateOutcome(pTransaction, pDebate, out bool exists);
            if (exists) return existing;

            long debateId = NextIdInTransaction(pTransaction, DebateTable, "DEBATE_ID");
            long firstEventId = NextIdInTransaction(pTransaction, EventTable, "EVENT_ID");
            if (debateId < 0 || firstEventId < 0 || firstEventId >= long.MaxValue - 1L)
                return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            long secondEventId = firstEventId + 1L;
            InsertDebateCommand(pTransaction, debateId, pDebate);
            InsertDebateEventCommand(pTransaction, firstEventId, pDebate, firstSchool,
                pDebate.FirstActorId, pDebate.SecondActorId, pWorldTime);
            InsertDebateEventCommand(pTransaction, secondEventId, pDebate, secondSchool,
                pDebate.SecondActorId, pDebate.FirstActorId, pWorldTime);
            UpsertLedgerCommand(pTransaction, pDebate.CityId, firstSchool, pFirstDelta,
                pWorldTime);
            UpsertLedgerCommand(pTransaction, pDebate.CityId, secondSchool, pSecondDelta,
                pWorldTime);
            UpdateMembershipReputationCommand(pTransaction, pDebate.FirstActorId,
                ReputationDelta(pDebate.Outcome, pFirst: true), pWorldTime);
            UpdateMembershipReputationCommand(pTransaction, pDebate.SecondActorId,
                ReputationDelta(pDebate.Outcome, pFirst: false), pWorldTime);
            return HistoricalSchoolTeachingPersistenceOutcome.Committed;
        }

        internal static void InvalidateDebateCommit(long pCityId)
        {
            InvalidateLedgerCaches(pCityId);
        }

        /// <summary>
        ///     Founds the smallest durable historical institution unit.  Only a canonical
        ///     historical master can found one, and the actor must be a real, living unit
        ///     whose persisted residence is the requested city.  Lecture/debate evidence is kept
        ///     deliberately cheap: one event is enough for the first institution.  The
        ///     institution, founding event, and ledger bonus are one transaction so a
        ///     failed write cannot leave a phantom institution or influence.
        /// </summary>
        public static bool TryFoundInstitution(HistoricalSchoolMasterDefinition pMaster,
            long pFounderActorId, long pCityId, int pYear, double pWorldTime)
        {
            HistoricalSchoolMasterDefinition canonical = pMaster == null
                ? null
                : HistoricalSchoolMasterRegistry.Find(pMaster.Id);
            if (DB == null || pMaster == null || canonical == null ||
                !ReferenceEquals(canonical, pMaster) ||
                string.IsNullOrWhiteSpace(pMaster.SchoolId) ||
                string.IsNullOrWhiteSpace(pMaster.InstitutionId) ||
                !string.Equals(canonical.SchoolId, pMaster.SchoolId,
                    StringComparison.Ordinal) ||
                !string.Equals(canonical.InstitutionId, pMaster.InstitutionId,
                    StringComparison.Ordinal) ||
                CourtSchoolRegistry.Find(pMaster.SchoolId) == null ||
                pFounderActorId < 0 || pCityId < 0 || pYear < 0) return false;

            // IDs are resolved from the live world rather than accepting a synthetic row.
            Actor founder = World.world?.units?.get(pFounderActorId);
            City city = World.world?.cities?.get(pCityId);
            if (founder?.data == null || city?.data == null || city.isRekt() ||
                founder.isRekt() || !founder.isAlive()) return false;
            if (!HistoricalSchoolDescentService.IsCanonicalMaster(founder)) return false;
            HistoricalSchoolMasterDefinition founderDefinition =
                HistoricalSchoolDescentService.DefinitionFor(founder);
            if (founderDefinition == null ||
                !ReferenceEquals(founderDefinition, canonical) ||
                !string.Equals(founderDefinition.SchoolId, pMaster.SchoolId,
                    StringComparison.Ordinal)) return false;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                pFounderActorId);
            if (membership == null ||
                !string.Equals(membership.SchoolId, pMaster.SchoolId,
                    StringComparison.Ordinal)) return false;

            City residence = HistoricalAffiliationService.ResidenceCity(founder);
            if (residence?.data == null || residence.data.id != pCityId) return false;

            long institutionId = TableIdAllocator.Next(DB, InstitutionTable, "INSTITUTION_ID");
            long eventId = TableIdAllocator.Next(DB, EventTable, "EVENT_ID");
            if (institutionId < 0 || eventId < 0) return false;

            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                // Idempotent: an active institution is unique within a city/school.
                if (HasActiveInstitutionCommand(transaction, pMaster, pCityId) ||
                    !HasFounderEvidenceCommand(transaction, pFounderActorId, pCityId,
                        pMaster.SchoolId))
                {
                    transaction.Rollback();
                    return false;
                }

                InsertSchoolInstitutionCommand(transaction, institutionId, pMaster,
                    pFounderActorId, pCityId, pYear, pWorldTime);
                InsertSchoolEventCommand(transaction, eventId, pMaster, pFounderActorId,
                    pCityId, pYear, residence.kingdom?.data?.id ??
                    founder.kingdom?.data?.id ?? -1L, pWorldTime);
                var ledgerDelta = new HistoricalSchoolLedgerDelta(pMaster.SchoolId,
                    pTradition: 0f, pActivePresence: 0f, pMomentum: 0f,
                    pInstitutions: 1f, pLastActiveYear: pYear);
                UpsertLedgerCommand(transaction, pCityId, pMaster.SchoolId, ledgerDelta,
                    pWorldTime);
                transaction.Commit();
                InvalidateLedgerCaches(pCityId);
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore found institution failed: " +
                                    error.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        public static HistoricalSchoolLedgerSnapshot LoadLedger(long pCityId,
            string pSchoolId)
        {
            string school = pSchoolId ?? "";
            if (DB == null || pCityId < 0 || string.IsNullOrWhiteSpace(school))
                return new HistoricalSchoolLedgerSnapshot(school, 0f, 0f, 0f, -1);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT SCHOOL_ID,TRADITION,MEMBERSHIP,INSTITUTIONS," +
                                      "ACTIVE_PRESENCE,MOMENTUM,LAST_ACTIVE_YEAR," +
                                      "LAST_DECAY_YEAR FROM " + LedgerTable +
                                      " WHERE CITY_ID=@city AND SCHOOL_ID=@school LIMIT 1";
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@school", school);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                    return new HistoricalSchoolLedgerSnapshot(school, 0f, 0f, 0f, -1);
                HistoricalSchoolEffectiveLedger effective =
                    HistoricalSchoolLedgerDecayRules.Effective(
                        ValueDouble(reader, 1), ValueDouble(reader, 2),
                        ValueDouble(reader, 3), ValueDouble(reader, 4),
                        ValueDouble(reader, 5), ValueInt(reader, 6, -1),
                        ValueInt(reader, 7, -1), CurrentLedgerYear());
                return Snapshot(ValueString(reader, 0), effective);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load ledger failed: " +
                                    error.Message);
                return new HistoricalSchoolLedgerSnapshot(school, 0f, 0f, 0f, -1);
            }
        }

        public static Dictionary<string, HistoricalSchoolLedgerSnapshot> LoadLedgersForCity(
            long pCityId)
        {
            if (!TryLoadLedgersForCities(new[] { pCityId },
                    out Dictionary<long,
                        Dictionary<string, HistoricalSchoolLedgerSnapshot>> batches,
                    out string failure))
                ModClass.LogWarning("HistoricalSchoolStore load city ledgers failed for city " +
                                    pCityId + ": " + failure);
            return batches.TryGetValue(pCityId,
                out Dictionary<string, HistoricalSchoolLedgerSnapshot> result)
                ? result
                : new Dictionary<string, HistoricalSchoolLedgerSnapshot>(
                    StringComparer.Ordinal);
        }

        public static bool TryLoadLedgersForCities(IReadOnlyList<long> pCityIds,
            out Dictionary<long, Dictionary<string, HistoricalSchoolLedgerSnapshot>> pResult,
            out string pFailure)
        {
            pResult =
                new Dictionary<long, Dictionary<string, HistoricalSchoolLedgerSnapshot>>();
            pFailure = "";
            if (pCityIds == null || pCityIds.Count == 0) return true;

            int ledgerYear = CurrentLedgerYear();
            long[] missingCityIds = LedgerReadCache.CollectMisses(
                pCityIds, ledgerYear, pResult);
            if (missingCityIds.Length == 0) return true;
            if (DB == null)
            {
                pFailure = "database unavailable";
                return false;
            }

            var loaded =
                new Dictionary<long, Dictionary<string, HistoricalSchoolLedgerSnapshot>>();
            foreach (long cityId in missingCityIds)
                loaded[cityId] = new Dictionary<string, HistoricalSchoolLedgerSnapshot>(
                    StringComparer.Ordinal);

            for (int offset = 0; offset < missingCityIds.Length;
                 offset += LedgerCityQueryChunkSize)
            {
                int count = Math.Min(LedgerCityQueryChunkSize,
                    missingCityIds.Length - offset);
                if (!TryLoadLedgerChunk(missingCityIds, offset, count, loaded,
                        out string chunkFailure))
                {
                    pFailure = chunkFailure;
                    return false;
                }
            }
            foreach (KeyValuePair<long,
                         Dictionary<string, HistoricalSchoolLedgerSnapshot>> entry in loaded)
            {
                LedgerReadCache.Set(entry.Key, ledgerYear, entry.Value);
                pResult[entry.Key] = entry.Value;
            }
            return true;
        }

        internal static void ClearLedgerReadCache()
        {
            LedgerReadCache.Clear();
        }

        private static bool TryLoadLedgerChunk(IReadOnlyList<long> pCityIds, int pOffset,
            int pCount,
            Dictionary<long, Dictionary<string, HistoricalSchoolLedgerSnapshot>> pResult,
            out string pFailure)
        {
            pFailure = "";
            if (pCityIds == null || pResult == null || pCount <= 0)
            {
                pFailure = "invalid ledger batch chunk at offset " + pOffset;
                return false;
            }
            try
            {
                using var command = new SQLiteCommand(DB);
                var parameters = new List<string>(pCount);
                for (int i = 0; i < pCount; i++)
                {
                    string parameterName = "@city" + i;
                    parameters.Add(parameterName);
                    command.Parameters.AddWithValue(parameterName, pCityIds[pOffset + i]);
                }
                command.CommandText = "SELECT CITY_ID,SCHOOL_ID,TRADITION,MEMBERSHIP,INSTITUTIONS," +
                                      "ACTIVE_PRESENCE,MOMENTUM,LAST_ACTIVE_YEAR," +
                                      "LAST_DECAY_YEAR FROM " + LedgerTable +
                                      " WHERE CITY_ID IN (" +
                                      string.Join(",", parameters.ToArray()) + ")";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long cityId = ValueLong(reader, 0, -1L);
                    string school = ValueString(reader, 1);
                    if (cityId < 0 || string.IsNullOrWhiteSpace(school) ||
                        !pResult.TryGetValue(cityId,
                            out Dictionary<string, HistoricalSchoolLedgerSnapshot> ledgers))
                        continue;
                    HistoricalSchoolEffectiveLedger effective =
                        HistoricalSchoolLedgerDecayRules.Effective(
                            ValueDouble(reader, 2), ValueDouble(reader, 3),
                            ValueDouble(reader, 4), ValueDouble(reader, 5),
                            ValueDouble(reader, 6), ValueInt(reader, 7, -1),
                            ValueInt(reader, 8, -1), CurrentLedgerYear());
                    ledgers[school] = Snapshot(school, effective);
                }
                return true;
            }
            catch (Exception error)
            {
                pFailure = "ledger batch chunk failed at offset " + pOffset + ": " +
                           error.GetType().Name + ": " + error.Message;
                return false;
            }
        }

        public static bool HasDebateForYear(long pCityId, long pFirstActorId,
            long pSecondActorId, int pYear)
        {
            if (DB == null || pCityId < 0 || pFirstActorId < 0 || pSecondActorId < 0 ||
                pFirstActorId == pSecondActorId || pYear < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT 1 FROM " + DebateTable +
                                      " WHERE CITY_ID=@city AND DEBATE_YEAR=@year AND " +
                                      "((FIRST_ACTOR_ID=@first AND SECOND_ACTOR_ID=@second) OR " +
                                      "(FIRST_ACTOR_ID=@second AND SECOND_ACTOR_ID=@first)) LIMIT 1";
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@first", pFirstActorId);
                command.Parameters.AddWithValue("@second", pSecondActorId);
                return command.ExecuteScalar() != null;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore debate lookup failed: " +
                                    error.Message);
                return false;
            }
        }

        public static Dictionary<long, int> LoadDebateWins()
        {
            var result = new Dictionary<long, int>();
            if (DB == null) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT FIRST_ACTOR_ID,SECOND_ACTOR_ID,RESULT FROM " +
                                      DebateTable + " WHERE RESOLVED=1";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long first = ValueLong(reader, 0, -1L);
                    long second = ValueLong(reader, 1, -1L);
                    if (!Enum.TryParse(ValueString(reader, 2), out SchoolDebateOutcome outcome))
                        continue;
                    long winner = outcome == SchoolDebateOutcome.NarrowFirstWin ||
                                  outcome == SchoolDebateOutcome.DecisiveFirstWin
                        ? first
                        : outcome == SchoolDebateOutcome.NarrowSecondWin ||
                          outcome == SchoolDebateOutcome.DecisiveSecondWin
                            ? second
                            : -1L;
                    if (winner < 0) continue;
                    result.TryGetValue(winner, out int count);
                    result[winner] = Math.Min(100000, count + 1);
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load debate wins failed: " +
                                    error.Message);
            }
            return result;
        }

        public static List<SchoolMembershipRecord> LoadActiveMemberships()
        {
            var result = new List<SchoolMembershipRecord>();
            if (DB == null) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT MEMBERSHIP_ID, ACTOR_ID, SCHOOL_ID, " +
                    "SOURCE_TYPE, SOURCE_ID, TEACHER_ACTOR_ID, CITY_ID, GENERATION, " +
                    "REPUTATION, START_YEAR, STANDING, LOYALTY_UNTIL_YEAR FROM " +
                    MembershipTable +
                    " WHERE ACTIVE=1 ORDER BY ACTOR_ID, START_YEAR DESC, MEMBERSHIP_ID DESC";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (!Enum.TryParse(ValueString(reader, 3), ignoreCase: false,
                            out SchoolMembershipSource source)) continue;
                    if (!Enum.TryParse(ValueString(reader, 10), ignoreCase: false,
                            out HistoricalSchoolStanding standing)) continue;
                    result.Add(new SchoolMembershipRecord(ValueLong(reader, 0),
                        ValueLong(reader, 1), ValueString(reader, 2), source,
                        ValueString(reader, 4), ValueLong(reader, 5, -1),
                        ValueLong(reader, 6, -1), ValueInt(reader, 7),
                        (float)ValueDouble(reader, 8), ValueInt(reader, 9),
                        pStanding: standing,
                        pLoyaltyUntilYear: ValueInt(reader, 11, -1)));
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load memberships failed: " +
                                    error.Message);
            }
            return result;
        }

        public static bool InsertMembership(SchoolMembershipRecord pRecord, double pTime)
        {
            if (DB == null || pRecord == null || !pRecord.IsValid || !pRecord.Active) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                HistoricalSchoolTeachingPersistenceOutcome outcome =
                    InsertMembershipInTransaction(DB, transaction, pRecord, pTime);
                if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                    outcome == HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                {
                    transaction.Commit();
                    return true;
                }
                transaction.Rollback();
                return false;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore insert membership failed: " +
                                    error.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome
            InsertMembershipInTransaction(SQLiteConnection pDb,
                SQLiteTransaction pTransaction, SchoolMembershipRecord pRecord,
                double pTime)
        {
            if (pDb == null || pTransaction == null || pRecord == null ||
                !pRecord.IsValid || !pRecord.Active)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            int rows = 0;
            bool exact = false;
            using (var command = new SQLiteCommand(pDb) { Transaction = pTransaction })
            {
                command.CommandText = MembershipSelect() +
                    " WHERE MEMBERSHIP_ID=@id OR (ACTOR_ID=@actor AND ACTIVE=1)";
                command.Parameters.AddWithValue("@id", pRecord.MembershipId);
                command.Parameters.AddWithValue("@actor", pRecord.ActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows++;
                    exact |= MembershipRowMatches(reader, pRecord, pActive: true,
                        pEndYear: -1, pEndReason: "", pTime, pRequireTime: true);
                }
            }
            if (rows > 0) return rows == 1 && exact
                ? HistoricalSchoolTeachingPersistenceOutcome.Replayed
                : HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            InsertMembershipCommand(pRecord, pTime, pTransaction);
            return HistoricalSchoolTeachingPersistenceOutcome.Committed;
        }

        public static bool ConvertMembership(SchoolMembershipRecord pCurrent,
            SchoolMembershipRecord pReplacement, int pYear, double pTime)
        {
            if (DB == null || pCurrent == null || pReplacement == null ||
                !pReplacement.IsValid) return false;
            using SQLiteTransaction transaction = DB.BeginTransaction();
            try
            {
                HistoricalSchoolTeachingPersistenceOutcome outcome =
                    ConvertMembershipInTransaction(DB, transaction, pCurrent,
                        pReplacement, pYear, pTime);
                if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                    outcome == HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                {
                    transaction.Commit();
                    return true;
                }
                transaction.Rollback();
                return false;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore convert membership failed: " +
                                    error.Message);
                return false;
            }
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome
            ConvertMembershipInTransaction(SQLiteConnection pDb,
                SQLiteTransaction pTransaction, SchoolMembershipRecord pCurrent,
                SchoolMembershipRecord pReplacement, int pYear, double pTime)
        {
            if (pDb == null || pTransaction == null || pCurrent == null ||
                pReplacement == null || !pCurrent.IsValid || !pReplacement.IsValid ||
                pCurrent.ActorId != pReplacement.ActorId || pYear < pCurrent.StartYear)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            bool currentActive = false;
            bool currentClosed = false;
            bool replacementActive = false;
            bool conflict = false;
            using (var command = new SQLiteCommand(pDb) { Transaction = pTransaction })
            {
                command.CommandText = MembershipSelect() +
                    " WHERE MEMBERSHIP_ID=@current OR MEMBERSHIP_ID=@replacement" +
                    " OR (ACTOR_ID=@actor AND ACTIVE=1)";
                command.Parameters.AddWithValue("@current", pCurrent.MembershipId);
                command.Parameters.AddWithValue("@replacement", pReplacement.MembershipId);
                command.Parameters.AddWithValue("@actor", pCurrent.ActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long membershipId = ValueLong(reader, 0, -1L);
                    if (membershipId == pCurrent.MembershipId)
                    {
                        currentActive |= MembershipRowMatches(reader, pCurrent,
                            pActive: true, pEndYear: -1, pEndReason: "", pTime,
                            pRequireTime: false);
                        currentClosed |= MembershipRowMatches(reader, pCurrent,
                            pActive: false, pEndYear: pYear, pEndReason: "converted",
                            pTime, pRequireTime: true);
                        if (!currentActive && !currentClosed) conflict = true;
                    }
                    else if (membershipId == pReplacement.MembershipId)
                    {
                        bool exact = MembershipRowMatches(reader, pReplacement,
                            pActive: true, pEndYear: -1, pEndReason: "", pTime,
                            pRequireTime: true);
                        replacementActive |= exact;
                        if (!exact) conflict = true;
                    }
                    else
                    {
                        conflict = true;
                    }
                }
            }
            if (currentClosed && replacementActive && !conflict)
                return HistoricalSchoolTeachingPersistenceOutcome.Replayed;
            if (!currentActive || currentClosed || replacementActive || conflict)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            CloseMembershipCommand(pCurrent.MembershipId, pYear, "converted", pTime,
                pTransaction);
            InsertMembershipCommand(pReplacement, pTime, pTransaction);
            return HistoricalSchoolTeachingPersistenceOutcome.Committed;
        }

        public static bool UpdateMembershipStanding(
            SchoolMembershipRecord pCurrent,
            SchoolMembershipRecord pNext,
            double pTime)
        {
            if (DB == null || pCurrent == null || pNext == null ||
                pCurrent.MembershipId != pNext.MembershipId ||
                pCurrent.ActorId != pNext.ActorId || !pCurrent.Active || !pNext.Active)
                return false;
            if (pCurrent.Standing == pNext.Standing) return true;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + MembershipTable +
                    " SET STANDING=@next,UPDATED_TIME=@time" +
                    " WHERE MEMBERSHIP_ID=@id AND ACTOR_ID=@actor AND ACTIVE=1" +
                    " AND STANDING=@current AND LOYALTY_UNTIL_YEAR=@loyalty";
                command.Parameters.AddWithValue("@next", pNext.Standing.ToString());
                command.Parameters.AddWithValue("@time", FiniteNonNegative(pTime));
                command.Parameters.AddWithValue("@id", pCurrent.MembershipId);
                command.Parameters.AddWithValue("@actor", pCurrent.ActorId);
                command.Parameters.AddWithValue("@current", pCurrent.Standing.ToString());
                command.Parameters.AddWithValue("@loyalty", pCurrent.LoyaltyUntilYear);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore update standing failed: " +
                                    error.Message);
                return false;
            }
        }

        public static bool UpdateSchoolLeader(
            string pSchoolId,
            long pLeaderActorId,
            double pTime)
        {
            if (DB == null || string.IsNullOrEmpty(pSchoolId)) return false;
            using SQLiteTransaction transaction = DB.BeginTransaction();
            try
            {
                using (var demote = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    demote.CommandText = "UPDATE " + MembershipTable +
                        " SET STANDING=@teacher,UPDATED_TIME=@time" +
                        " WHERE SCHOOL_ID=@school AND ACTIVE=1 AND STANDING=@leader" +
                        " AND ACTOR_ID<>@target";
                    demote.Parameters.AddWithValue(
                        "@teacher", HistoricalSchoolStanding.Teacher.ToString());
                    demote.Parameters.AddWithValue("@time", FiniteNonNegative(pTime));
                    demote.Parameters.AddWithValue("@school", pSchoolId);
                    demote.Parameters.AddWithValue(
                        "@leader", HistoricalSchoolStanding.Leader.ToString());
                    demote.Parameters.AddWithValue("@target", pLeaderActorId);
                    demote.ExecuteNonQuery();
                }

                if (pLeaderActorId >= 0)
                {
                    using var promote = new SQLiteCommand(DB) { Transaction = transaction };
                    promote.CommandText = "UPDATE " + MembershipTable +
                        " SET STANDING=@leader,UPDATED_TIME=@time" +
                        " WHERE SCHOOL_ID=@school AND ACTOR_ID=@target AND ACTIVE=1" +
                        " AND (STANDING=@teacher OR STANDING=@leader)";
                    promote.Parameters.AddWithValue(
                        "@leader", HistoricalSchoolStanding.Leader.ToString());
                    promote.Parameters.AddWithValue("@time", FiniteNonNegative(pTime));
                    promote.Parameters.AddWithValue("@school", pSchoolId);
                    promote.Parameters.AddWithValue("@target", pLeaderActorId);
                    promote.Parameters.AddWithValue(
                        "@teacher", HistoricalSchoolStanding.Teacher.ToString());
                    if (promote.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "school leader candidate is no longer eligible");
                }

                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore update leader failed: " +
                                    error.Message);
                return false;
            }
        }

        public static bool RollbackConversion(SchoolMembershipRecord pCurrent,
            SchoolMembershipRecord pReplacement, double pTime)
        {
            if (DB == null || pCurrent == null || pReplacement == null ||
                pCurrent.ActorId < 0 || pReplacement.ActorId != pCurrent.ActorId)
                return false;
            using SQLiteTransaction transaction = DB.BeginTransaction();
            try
            {
                using (var delete = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    delete.CommandText = "DELETE FROM " + MembershipTable +
                                          " WHERE MEMBERSHIP_ID=@replacement AND " +
                                          "ACTOR_ID=@actor AND ACTIVE=1";
                    delete.Parameters.AddWithValue("@replacement", pReplacement.MembershipId);
                    delete.Parameters.AddWithValue("@actor", pCurrent.ActorId);
                    if (delete.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("replacement membership rollback failed");
                }
                using (var restore = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    restore.CommandText = "UPDATE " + MembershipTable +
                                          " SET ACTIVE=1,END_YEAR=-1,END_REASON=''," +
                                          "UPDATED_TIME=@time WHERE MEMBERSHIP_ID=@current AND " +
                                          "ACTOR_ID=@actor AND ACTIVE=0";
                    restore.Parameters.AddWithValue("@time", FiniteNonNegative(pTime));
                    restore.Parameters.AddWithValue("@current", pCurrent.MembershipId);
                    restore.Parameters.AddWithValue("@actor", pCurrent.ActorId);
                    if (restore.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("original membership restore failed");
                }
                transaction.Commit();
                InvalidateLedgerCaches(pCurrent.CityId);
                InvalidateLedgerCaches(pReplacement.CityId);
                return true;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore rollback conversion failed: " +
                                    error.Message);
                return false;
            }
        }

        public static bool CloseMembership(SchoolMembershipRecord pCurrent, int pYear,
            string pReason, double pTime)
        {
            if (DB == null || pCurrent == null) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                HistoricalSchoolTeachingPersistenceOutcome outcome =
                    CloseMembershipInTransaction(DB, transaction, pCurrent, pYear,
                        pReason, pTime);
                if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                    outcome == HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                {
                    transaction.Commit();
                    return true;
                }
                transaction.Rollback();
                return false;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore close membership failed: " +
                                    error.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome
            CloseMembershipInTransaction(SQLiteConnection pDb,
                SQLiteTransaction pTransaction, SchoolMembershipRecord pCurrent,
                int pYear, string pReason, double pTime)
        {
            if (pDb == null || pTransaction == null || pCurrent == null ||
                !pCurrent.IsValid || pYear < pCurrent.StartYear)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            int rows = 0;
            bool active = false;
            bool closed = false;
            using (var command = new SQLiteCommand(pDb) { Transaction = pTransaction })
            {
                command.CommandText = MembershipSelect() +
                    " WHERE MEMBERSHIP_ID=@id AND ACTOR_ID=@actor";
                command.Parameters.AddWithValue("@id", pCurrent.MembershipId);
                command.Parameters.AddWithValue("@actor", pCurrent.ActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows++;
                    active |= MembershipRowMatches(reader, pCurrent, pActive: true,
                        pEndYear: -1, pEndReason: "", pTime, pRequireTime: false);
                    closed |= MembershipRowMatches(reader, pCurrent, pActive: false,
                        pEndYear: pYear, pEndReason: pReason ?? "", pTime,
                        pRequireTime: true);
                }
            }
            if (rows != 1)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            if (closed) return HistoricalSchoolTeachingPersistenceOutcome.Replayed;
            if (!active) return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            CloseMembershipCommand(pCurrent.MembershipId, pYear, pReason, pTime,
                pTransaction);
            return HistoricalSchoolTeachingPersistenceOutcome.Committed;
        }

        public static bool DeleteMembership(SchoolMembershipRecord pCurrent)
        {
            if (DB == null || pCurrent == null) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "DELETE FROM " + MembershipTable +
                                      " WHERE MEMBERSHIP_ID=@id AND ACTOR_ID=@actor";
                command.Parameters.AddWithValue("@id", pCurrent.MembershipId);
                command.Parameters.AddWithValue("@actor", pCurrent.ActorId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore delete membership failed: " +
                                    error.Message);
                return false;
            }
        }

        public static List<HistoricalSchoolMasterStoreRecord> LoadMasterStates()
        {
            var result = new List<HistoricalSchoolMasterStoreRecord>();
            if (DB == null) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT MASTER_ID,ACTOR_ID,SPAWNED,DEAD," +
                    "HOME_KINGDOM_ID,HOMETOWN_CITY_ID,SPAWN_YEAR,LINEAGE_ID,SHI_ID," +
                    "UPDATED_TIME FROM " + MasterTable +
                    " WHERE SPAWNED=1";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result.Add(new HistoricalSchoolMasterStoreRecord(ValueString(reader, 0),
                        ValueLong(reader, 1, -1), ValueInt(reader, 2) != 0,
                        ValueInt(reader, 3) != 0, ValueLong(reader, 4, -1),
                        ValueLong(reader, 5, -1), ValueInt(reader, 6, -1),
                        ValueLong(reader, 7, -1), ValueLong(reader, 8, -1),
                        ValueDouble(reader, 9, -1d)));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load masters failed: " + error.Message);
            }
            return result;
        }

        public static List<SchoolInstitutionReadModel> LoadInstitutions(string pSchoolId,
            long pCityId = -1L, int pLimit = 32)
        {
            var result = new List<SchoolInstitutionReadModel>();
            if (DB == null || string.IsNullOrWhiteSpace(pSchoolId)) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT INSTITUTION_ID,INSTITUTION_TYPE,SCHOOL_ID," +
                    "CITY_ID,FOUNDER_ACTOR_ID,FOUNDING_YEAR,LEVEL,CONDITION,ACTIVE FROM " +
                    InstitutionTable + " WHERE SCHOOL_ID=@school AND ACTIVE=1" +
                    (pCityId >= 0 ? " AND CITY_ID=@city" : "") +
                    " ORDER BY LEVEL DESC,FOUNDING_YEAR,INSTITUTION_ID LIMIT @limit";
                command.Parameters.AddWithValue("@school", pSchoolId);
                if (pCityId >= 0) command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@limit", Math.Max(1, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new SchoolInstitutionReadModel
                    {
                        InstitutionId = ValueLong(reader, 0, -1L),
                        InstitutionType = ValueString(reader, 1),
                        SchoolId = ValueString(reader, 2),
                        CityId = ValueLong(reader, 3, -1L),
                        FounderActorId = ValueLong(reader, 4, -1L),
                        FoundingYear = ValueInt(reader, 5, -1),
                        Level = Math.Max(1, ValueInt(reader, 6, 1)),
                        Condition = Math.Max(0d, Math.Min(100d, ValueDouble(reader, 7))),
                        Active = ValueInt(reader, 8)
                    });
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load institutions failed: " +
                                    error.Message);
            }
            return result;
        }

        public static SchoolInstitutionReadModel LoadLeadingInstitution(long pCityId)
        {
            if (DB == null || pCityId < 0) return null;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT INSTITUTION_ID,INSTITUTION_TYPE,SCHOOL_ID," +
                    "CITY_ID,FOUNDER_ACTOR_ID,FOUNDING_YEAR,LEVEL,CONDITION,ACTIVE FROM " +
                    InstitutionTable + " WHERE CITY_ID=@city AND ACTIVE=1" +
                    " ORDER BY LEVEL DESC,CONDITION DESC,FOUNDING_YEAR,INSTITUTION_ID LIMIT 1";
                command.Parameters.AddWithValue("@city", pCityId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return null;
                return new SchoolInstitutionReadModel
                {
                    InstitutionId = ValueLong(reader, 0, -1L),
                    InstitutionType = ValueString(reader, 1),
                    SchoolId = ValueString(reader, 2),
                    CityId = ValueLong(reader, 3, -1L),
                    FounderActorId = ValueLong(reader, 4, -1L),
                    FoundingYear = ValueInt(reader, 5, -1),
                    Level = Math.Max(1, ValueInt(reader, 6, 1)),
                    Condition = Math.Max(0d, Math.Min(100d, ValueDouble(reader, 7))),
                    Active = ValueInt(reader, 8)
                };
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load leading institution failed: " +
                                    error.Message);
                return null;
            }
        }

        public static List<SchoolWorkReadModel> LoadWorks(string pSchoolId, int pLimit = 32)
        {
            var result = new List<SchoolWorkReadModel>();
            if (DB == null || string.IsNullOrWhiteSpace(pSchoolId)) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT WORK_ID,WORK_KEY,DISPLAY_NAME,SCHOOL_ID," +
                    "AUTHOR_ACTOR_ID,CITY_ID,WRITTEN_YEAR,CONDITION FROM " + WorkTable +
                    " WHERE SCHOOL_ID=@school AND PRESERVED=1" +
                    " ORDER BY WRITTEN_YEAR DESC,WORK_ID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@school", pSchoolId);
                command.Parameters.AddWithValue("@limit", Math.Max(1, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new SchoolWorkReadModel
                    {
                        WorkId = ValueLong(reader, 0, -1L),
                        WorkKey = ValueString(reader, 1),
                        DisplayName = ValueString(reader, 2),
                        SchoolId = ValueString(reader, 3),
                        AuthorActorId = ValueLong(reader, 4, -1L),
                        CityId = ValueLong(reader, 5, -1L),
                        WrittenYear = ValueInt(reader, 6, -1),
                        Condition = Math.Max(0d, Math.Min(100d, ValueDouble(reader, 7)))
                    });
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load works failed: " + error.Message);
            }
            return result;
        }

        public static List<SchoolDebateReadModel> LoadDebates(string pSchoolId,
            long pCityId = -1L, int pLimit = 16)
        {
            var result = new List<SchoolDebateReadModel>();
            if (DB == null || string.IsNullOrWhiteSpace(pSchoolId)) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT DEBATE_ID,CITY_ID,DEBATE_YEAR,TOPIC_ID," +
                    "FIRST_ACTOR_ID,FIRST_SCHOOL_ID,SECOND_ACTOR_ID,SECOND_SCHOOL_ID," +
                    "RESULT,PRESENTED FROM " + DebateTable +
                    " WHERE (FIRST_SCHOOL_ID=@school OR SECOND_SCHOOL_ID=@school)" +
                    (pCityId >= 0 ? " AND CITY_ID=@city" : "") +
                    " ORDER BY DEBATE_YEAR DESC,DEBATE_ID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@school", pSchoolId);
                if (pCityId >= 0) command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@limit", Math.Max(1, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new SchoolDebateReadModel
                    {
                        DebateId = ValueLong(reader, 0, -1L),
                        CityId = ValueLong(reader, 1, -1L),
                        DebateYear = ValueInt(reader, 2, -1),
                        TopicId = ValueString(reader, 3),
                        FirstActorId = ValueLong(reader, 4, -1L),
                        FirstSchoolId = ValueString(reader, 5),
                        SecondActorId = ValueLong(reader, 6, -1L),
                        SecondSchoolId = ValueString(reader, 7),
                        Result = ValueString(reader, 8),
                        Presented = ValueInt(reader, 9) != 0
                    });
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load debates failed: " + error.Message);
            }
            return result;
        }

        public static List<SchoolEventReadModel> LoadRecentSchoolEvents(string pSchoolId,
            long pCityId = -1L, int pLimit = 16)
        {
            var result = new List<SchoolEventReadModel>();
            if (DB == null || string.IsNullOrWhiteSpace(pSchoolId)) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID," +
                    "CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE FROM " + EventTable +
                    " WHERE SCHOOL_ID=@school" + (pCityId >= 0 ? " AND CITY_ID=@city" : "") +
                    " ORDER BY EVENT_YEAR DESC,EVENT_ID DESC LIMIT @limit";
                command.Parameters.AddWithValue("@school", pSchoolId);
                if (pCityId >= 0) command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@limit", Math.Max(1, pLimit));
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new SchoolEventReadModel
                    {
                        EventType = ValueString(reader, 0),
                        ActorId = ValueLong(reader, 1, -1L),
                        TargetActorId = ValueLong(reader, 2, -1L),
                        SchoolId = ValueString(reader, 3),
                        CityId = ValueLong(reader, 4, -1L),
                        KingdomId = ValueLong(reader, 5, -1L),
                        EventYear = ValueInt(reader, 6, -1),
                        Payload = ValueString(reader, 7),
                        Importance = Math.Max(0, ValueInt(reader, 8))
                    });
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load school events failed: " +
                                    error.Message);
            }
            return result;
        }

        internal static Dictionary<long, SchoolLectureSeniority>
            LoadEarliestLectureSeniority(string pSchoolId)
        {
            var result = new Dictionary<long, SchoolLectureSeniority>();
            if (DB == null || string.IsNullOrWhiteSpace(pSchoolId)) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ACTOR_ID,MIN(EVENT_YEAR),MIN(WORLD_TIME) FROM " +
                    EventTable + " WHERE SCHOOL_ID=@school AND EVENT_TYPE='lecture'" +
                    " AND ACTOR_ID>=0 GROUP BY ACTOR_ID";
                command.Parameters.AddWithValue("@school", pSchoolId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long actorId = ValueLong(reader, 0, -1L);
                    if (actorId < 0) continue;
                    result[actorId] = new SchoolLectureSeniority(
                        ValueInt(reader, 1, int.MaxValue),
                        ValueDouble(reader, 2, double.MaxValue));
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load lecture seniority failed: " +
                                    error.Message);
            }
            return result;
        }

        public static List<HistoricalSchoolAffiliationSnapshot> LoadAffiliations()
        {
            var result = new List<HistoricalSchoolAffiliationSnapshot>();
            if (DB == null) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                    "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                    "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE," +
                    "SERVICE_START_YEAR,SERVICE_END_YEAR,LAST_TRAVEL_YEAR," +
                    "TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR," +
                    "TRANSPORT_FAILURES FROM " + AffiliationTable;
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (!Enum.TryParse(ValueString(reader, 8), out
                            HistoricalSchoolLifecycleState state))
                        state = HistoricalSchoolLifecycleState.AtHome;
                    result.Add(new HistoricalSchoolAffiliationSnapshot(
                        ValueLong(reader, 0, -1), ValueLong(reader, 1, -1),
                        ValueString(reader, 2), ValueLong(reader, 3, -1),
                        ValueLong(reader, 4, -1), ValueLong(reader, 5, -1),
                        ValueLong(reader, 6, -1), ValueLong(reader, 7, -1), state,
                        ValueInt(reader, 9, -1), ValueInt(reader, 10, -1),
                        ValueInt(reader, 11, -1), ValueInt(reader, 12, -1),
                        ValueInt(reader, 13, -1), ValueInt(reader, 14, -1),
                        ValueInt(reader, 15)));
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load affiliations failed: " +
                                    error.Message);
            }
            return result;
        }

        public static bool SaveAffiliation(HistoricalSchoolAffiliationSnapshot pState,
            double pTime)
        {
            if (DB == null || pState == null || pState.ActorId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + AffiliationTable +
                    " SET RESIDENCE_CITY_ID=@residence," +
                    "PREVIOUS_RESIDENCE_CITY_ID=@previous,DESTINATION_CITY_ID=@destination," +
                    "SERVICE_KINGDOM_ID=@service,LIFECYCLE_STATE=@state," +
                    "SERVICE_START_YEAR=@serviceStart,SERVICE_END_YEAR=@serviceEnd," +
                    "LAST_TRAVEL_YEAR=@lastTravel,TRAVEL_WAIT_START_YEAR=@waitStart," +
                    "VOYAGE_START_YEAR=@voyageStart,VOYAGE_ARRIVAL_YEAR=@voyageArrival," +
                    "TRANSPORT_FAILURES=@failures,UPDATED_TIME=@time WHERE ACTOR_ID=@actor" +
                    " AND LIFECYCLE_STATE<>@deadState";
                command.Parameters.AddWithValue("@residence", pState.ResidenceCityId);
                command.Parameters.AddWithValue("@previous", pState.PreviousResidenceCityId);
                command.Parameters.AddWithValue("@destination", pState.DestinationCityId);
                command.Parameters.AddWithValue("@service", pState.ServiceKingdomId);
                command.Parameters.AddWithValue("@state", pState.LifecycleState.ToString());
                command.Parameters.AddWithValue("@serviceStart", pState.ServiceStartYear);
                command.Parameters.AddWithValue("@serviceEnd", pState.ServiceEndYear);
                command.Parameters.AddWithValue("@lastTravel", pState.LastTravelYear);
                command.Parameters.AddWithValue("@waitStart", pState.TravelWaitStartYear);
                command.Parameters.AddWithValue("@voyageStart", pState.VoyageStartYear);
                command.Parameters.AddWithValue("@voyageArrival", pState.VoyageArrivalYear);
                command.Parameters.AddWithValue("@failures", pState.TransportFailures);
                command.Parameters.AddWithValue("@time", pTime);
                command.Parameters.AddWithValue("@actor", pState.ActorId);
                command.Parameters.AddWithValue("@deadState",
                    HistoricalSchoolLifecycleState.Dead.ToString());
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore save affiliation failed: " +
                                    error.Message);
                return false;
            }
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome
            SaveAffiliationTransitionInTransaction(SQLiteConnection pDb,
                SQLiteTransaction pTransaction,
                HistoricalSchoolAffiliationSnapshot pExpected,
                HistoricalSchoolAffiliationSnapshot pDesired,
                double pTime)
        {
            if (pDb == null || pTransaction == null || pExpected == null ||
                pDesired == null || pExpected.ActorId < 0 ||
                pExpected.ActorId != pDesired.ActorId)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;

            using (var read = new SQLiteCommand(pDb)
                   {
                       Transaction = pTransaction
                   })
            {
                read.CommandText = "SELECT RESIDENCE_CITY_ID," +
                    "PREVIOUS_RESIDENCE_CITY_ID,DESTINATION_CITY_ID," +
                    "SERVICE_KINGDOM_ID,LIFECYCLE_STATE,SERVICE_START_YEAR," +
                    "SERVICE_END_YEAR,LAST_TRAVEL_YEAR,TRAVEL_WAIT_START_YEAR," +
                    "VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR,TRANSPORT_FAILURES " +
                    "FROM " + AffiliationTable + " WHERE ACTOR_ID=@actor";
                read.Parameters.AddWithValue("@actor", pExpected.ActorId);
                using SQLiteDataReader reader = read.ExecuteReader();
                if (!reader.Read())
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
                if (AffiliationMutableExact(reader, pDesired))
                    return HistoricalSchoolTeachingPersistenceOutcome.Replayed;
                if (!AffiliationMutableExact(reader, pExpected))
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            }

            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction
            };
            command.CommandText = "UPDATE " + AffiliationTable +
                " SET RESIDENCE_CITY_ID=@residence," +
                "PREVIOUS_RESIDENCE_CITY_ID=@previous," +
                "DESTINATION_CITY_ID=@destination,SERVICE_KINGDOM_ID=@service," +
                "LIFECYCLE_STATE=@state,SERVICE_START_YEAR=@serviceStart," +
                "SERVICE_END_YEAR=@serviceEnd,LAST_TRAVEL_YEAR=@lastTravel," +
                "TRAVEL_WAIT_START_YEAR=@waitStart,VOYAGE_START_YEAR=@voyageStart," +
                "VOYAGE_ARRIVAL_YEAR=@voyageArrival," +
                "TRANSPORT_FAILURES=@failures,UPDATED_TIME=@time " +
                "WHERE ACTOR_ID=@actor AND LIFECYCLE_STATE=@expectedState " +
                "AND DESTINATION_CITY_ID=@expectedDestination";
            command.Parameters.AddWithValue("@residence", pDesired.ResidenceCityId);
            command.Parameters.AddWithValue("@previous", pDesired.PreviousResidenceCityId);
            command.Parameters.AddWithValue("@destination", pDesired.DestinationCityId);
            command.Parameters.AddWithValue("@service", pDesired.ServiceKingdomId);
            command.Parameters.AddWithValue("@state", pDesired.LifecycleState.ToString());
            command.Parameters.AddWithValue("@serviceStart", pDesired.ServiceStartYear);
            command.Parameters.AddWithValue("@serviceEnd", pDesired.ServiceEndYear);
            command.Parameters.AddWithValue("@lastTravel", pDesired.LastTravelYear);
            command.Parameters.AddWithValue("@waitStart", pDesired.TravelWaitStartYear);
            command.Parameters.AddWithValue("@voyageStart", pDesired.VoyageStartYear);
            command.Parameters.AddWithValue("@voyageArrival", pDesired.VoyageArrivalYear);
            command.Parameters.AddWithValue("@failures", pDesired.TransportFailures);
            command.Parameters.AddWithValue("@time", FiniteNonNegative(pTime));
            command.Parameters.AddWithValue("@actor", pDesired.ActorId);
            command.Parameters.AddWithValue("@expectedState",
                pExpected.LifecycleState.ToString());
            command.Parameters.AddWithValue("@expectedDestination",
                pExpected.DestinationCityId);
            return command.ExecuteNonQuery() == 1
                ? HistoricalSchoolTeachingPersistenceOutcome.Committed
                : HistoricalSchoolTeachingPersistenceOutcome.Unknown;
        }

        private static bool AffiliationMutableExact(SQLiteDataReader pReader,
            HistoricalSchoolAffiliationSnapshot pState)
        {
            return pReader != null && pState != null &&
                   ValueLong(pReader, 0, -1L) == pState.ResidenceCityId &&
                   ValueLong(pReader, 1, -1L) == pState.PreviousResidenceCityId &&
                   ValueLong(pReader, 2, -1L) == pState.DestinationCityId &&
                   ValueLong(pReader, 3, -1L) == pState.ServiceKingdomId &&
                   ValueString(pReader, 4) == pState.LifecycleState.ToString() &&
                   ValueInt(pReader, 5, -1) == pState.ServiceStartYear &&
                   ValueInt(pReader, 6, -1) == pState.ServiceEndYear &&
                   ValueInt(pReader, 7, -1) == pState.LastTravelYear &&
                   ValueInt(pReader, 8, -1) == pState.TravelWaitStartYear &&
                   ValueInt(pReader, 9, -1) == pState.VoyageStartYear &&
                   ValueInt(pReader, 10, -1) == pState.VoyageArrivalYear &&
                   ValueInt(pReader, 11, -1) == pState.TransportFailures;
        }

        public static void LoadRuntimeState(out int pEligibleYear, out int pLastWorldYear)
        {
            pEligibleYear = 0;
            pLastWorldYear = -1;
            if (DB == null) return;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ELIGIBLE_YEAR,LAST_WORLD_YEAR FROM " +
                                      RuntimeTable + " WHERE STATE_ID=1";
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return;
                pEligibleYear = Math.Max(0, ValueInt(reader, 0));
                pLastWorldYear = ValueInt(reader, 1, -1);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load runtime failed: " + error.Message);
            }
        }

        public static bool SaveRuntimeState(int pEligibleYear, int pLastWorldYear, double pTime)
        {
            if (DB == null) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "INSERT OR REPLACE INTO " + RuntimeTable +
                    " (STATE_ID,ELIGIBLE_YEAR,LAST_WORLD_YEAR,UPDATED_TIME)" +
                    " VALUES (1,@eligible,@world,@time)";
                command.Parameters.AddWithValue("@eligible", Math.Max(0, pEligibleYear));
                command.Parameters.AddWithValue("@world", pLastWorldYear);
                command.Parameters.AddWithValue("@time", FiniteNonNegative(pTime));
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore save runtime failed: " +
                                    error.ToString());
                return false;
            }
        }

        public static SchoolPersistenceOutcome CommitHistoricalDescent(
            HistoricalSchoolMasterDefinition pMaster, SchoolMembershipRecord pMembership,
            long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId, int pYear,
            double pTime, HistoricalMasterLineageCommitIdentity pIdentity)
        {
            double time = FiniteNonNegative(pTime);
            if (DB == null || pMaster == null || pMembership == null ||
                !pMembership.IsValid || !pMembership.Active ||
                pMembership.Source != SchoolMembershipSource.HistoricalDescent ||
                pMembership.SourceId != pMaster.Id || pMembership.SchoolId != pMaster.SchoolId ||
                pMembership.ActorId < 0 || pHomeKingdomId < 0 || pHometownCityId < 0 ||
                pIdentity == null || !pIdentity.IsValid ||
                pIdentity.ActorId != pMembership.ActorId ||
                pIdentity.CanonicalName != pMaster.CanonicalName ||
                pIdentity.ShiName != pMaster.CanonicalShiName ||
                pIdentity.GivenName != pMaster.CanonicalGivenName ||
                pIdentity.FamilyName != pMaster.CanonicalFamilyName ||
                pIdentity.FamilyEvidence != pMaster.FamilyEvidence ||
                pIdentity.HomeKingdomId != pHomeKingdomId ||
                pIdentity.HometownCityId != pHometownCityId ||
                !pIdentity.CreatedTime.Equals(time))
                return SchoolPersistenceOutcome.Unknown;

            long actorId = pMembership.ActorId;
            if (pIdentity.IdsFrozen)
            {
                try
                {
                    ReadHistoricalDescentState(pMaster, pMembership, pHomeKingdomId,
                        pHomeKingdomName, pHometownCityId, pYear, time, pIdentity,
                        out SchoolPersistenceRowState existingMembership,
                        out SchoolPersistenceRowState existingMaster,
                        out SchoolPersistenceRowState existingAffiliation,
                        out SchoolPersistenceRowState existingLineage,
                        out SchoolPersistenceRowState existingShi);
                    SchoolPersistenceOutcome existing =
                        HistoricalSchoolPersistenceRules.Resolve(pQuerySucceeded: true,
                            existingMembership, existingMaster, existingAffiliation,
                            existingLineage, existingShi);
                    if (existing != SchoolPersistenceOutcome.CleanFailure) return existing;
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("HistoricalSchoolStore descent preflight failed: " +
                                        error.Message);
                    return SchoolPersistenceOutcome.Unknown;
                }
            }

            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                HistoricalMasterLineagePersistence.FreezeIds(DB, transaction, pIdentity);
                InsertMembershipCommand(pMembership, time, transaction);
                using (var master = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    master.CommandText = "INSERT INTO " + MasterTable +
                        " (MASTER_ID,ACTOR_ID,SCHOOL_ID,CANONICAL_NAME,SPAWNED,DEAD," +
                        "HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID,LINEAGE_ID,SHI_ID," +
                        "SPAWN_YEAR,DEATH_YEAR,LIFECYCLE_STATE,DEATH_CAUSE,DEATH_CITY_ID,UPDATED_TIME)" +
                        " VALUES (@master,@actor,@school,@name,1,0,@kingdom,@kingdomName," +
                        "@city,@lineage,@shi,@year,-1,@state,'',-1,@time)";
                    master.Parameters.AddWithValue("@master", pMaster.Id);
                    master.Parameters.AddWithValue("@actor", actorId);
                    master.Parameters.AddWithValue("@school", pMaster.SchoolId);
                    master.Parameters.AddWithValue("@name", pMaster.CanonicalName);
                    master.Parameters.AddWithValue("@kingdom", pHomeKingdomId);
                    master.Parameters.AddWithValue("@kingdomName", pHomeKingdomName ?? "");
                    master.Parameters.AddWithValue("@city", pHometownCityId);
                    master.Parameters.AddWithValue("@lineage", pIdentity.LineageId);
                    master.Parameters.AddWithValue("@shi", pIdentity.ShiId);
                    master.Parameters.AddWithValue("@year", pYear);
                    master.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.AtHome.ToString());
                    master.Parameters.AddWithValue("@time", time);
                    if (master.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("master state insert failed");
                }
                using (var affiliation = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    affiliation.CommandText = "INSERT INTO " + AffiliationTable +
                        " (ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID," +
                        "RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID,DESTINATION_CITY_ID," +
                        "SERVICE_KINGDOM_ID,LIFECYCLE_STATE,SERVICE_START_YEAR,SERVICE_END_YEAR," +
                        "LAST_TRAVEL_YEAR,TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR," +
                        "VOYAGE_ARRIVAL_YEAR,TRANSPORT_FAILURES,UPDATED_TIME) VALUES " +
                        "(@actor,@kingdom,@kingdomName,@city,@city,-1,-1,-1,@state,-1,-1," +
                        "@year,-1,-1,-1,0,@time)";
                    affiliation.Parameters.AddWithValue("@actor", actorId);
                    affiliation.Parameters.AddWithValue("@kingdom", pHomeKingdomId);
                    affiliation.Parameters.AddWithValue("@kingdomName", pHomeKingdomName ?? "");
                    affiliation.Parameters.AddWithValue("@city", pHometownCityId);
                    affiliation.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.AtHome.ToString());
                    affiliation.Parameters.AddWithValue("@year", pYear);
                    affiliation.Parameters.AddWithValue("@time", time);
                    if (affiliation.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("master affiliation insert failed");
                }
                HistoricalMasterLineagePersistence.Stage(DB, transaction, pIdentity);
                HistoricalContentRevision
                    .AdvanceAfterSuccessfulSynchronousWrite(
                        transaction.Commit);
                return SchoolPersistenceOutcome.Committed;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore commit descent failed: " +
                                    error.Message);
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            try
            {
                ReadHistoricalDescentState(pMaster, pMembership, pHomeKingdomId,
                    pHomeKingdomName, pHometownCityId, pYear, time, pIdentity,
                    out SchoolPersistenceRowState membershipState,
                    out SchoolPersistenceRowState masterState,
                    out SchoolPersistenceRowState affiliationState,
                    out SchoolPersistenceRowState lineageState,
                    out SchoolPersistenceRowState shiState);
                return HistoricalSchoolPersistenceRules.Resolve(pQuerySucceeded: true,
                    membershipState, masterState, affiliationState, lineageState, shiState);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore descent readback failed: " +
                                    error.Message);
                return HistoricalSchoolPersistenceRules.Resolve(pQuerySucceeded: false,
                    SchoolPersistenceRowState.Missing, SchoolPersistenceRowState.Missing,
                    SchoolPersistenceRowState.Missing, SchoolPersistenceRowState.Missing,
                    SchoolPersistenceRowState.Missing);
            }
        }

        private static void ReadHistoricalDescentState(
            HistoricalSchoolMasterDefinition pMaster, SchoolMembershipRecord pMembership,
            long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId, int pYear,
            double pTime, HistoricalMasterLineageCommitIdentity pIdentity,
            out SchoolPersistenceRowState pMembershipState,
            out SchoolPersistenceRowState pMasterState,
            out SchoolPersistenceRowState pAffiliationState,
            out SchoolPersistenceRowState pLineageState,
            out SchoolPersistenceRowState pShiState)
        {
            pMembershipState = ReadMembershipState(pMembership, pTime);
            pMasterState = ReadMasterState(pMaster, pMembership.ActorId, pHomeKingdomId,
                pHomeKingdomName, pHometownCityId, pYear, pTime, pIdentity);
            pAffiliationState = ReadAffiliationState(pMembership.ActorId, pHomeKingdomId,
                pHomeKingdomName, pHometownCityId, pYear, pTime);
            HistoricalMasterLineagePersistence.ReadStates(DB, pIdentity,
                out HistoricalMasterLineageRowState lineageState,
                out HistoricalMasterLineageRowState shiState);
            pLineageState = ToSchoolRowState(lineageState);
            pShiState = ToSchoolRowState(shiState);
        }

        private static SchoolPersistenceRowState ReadMembershipState(
            SchoolMembershipRecord pExpected, double pTime)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT MEMBERSHIP_ID,ACTOR_ID,SCHOOL_ID,SOURCE_TYPE," +
                "SOURCE_ID,TEACHER_ACTOR_ID,CITY_ID,GENERATION,REPUTATION,START_YEAR," +
                "END_YEAR,ACTIVE,END_REASON,UPDATED_TIME,STANDING,LOYALTY_UNTIL_YEAR FROM " + MembershipTable +
                " WHERE MEMBERSHIP_ID=@id OR (ACTOR_ID=@actor AND ACTIVE=1)";
            command.Parameters.AddWithValue("@id", pExpected.MembershipId);
            command.Parameters.AddWithValue("@actor", pExpected.ActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            int rowCount = 0;
            bool exact = false;
            while (reader.Read())
            {
                rowCount++;
                exact |= ValueLong(reader, 0, -1L) == pExpected.MembershipId &&
                         ValueLong(reader, 1, -1L) == pExpected.ActorId &&
                         ValueString(reader, 2) == pExpected.SchoolId &&
                         ValueString(reader, 3) == pExpected.Source.ToString() &&
                         ValueString(reader, 4) == pExpected.SourceId &&
                         ValueLong(reader, 5, -1L) == pExpected.TeacherActorId &&
                         ValueLong(reader, 6, -1L) == pExpected.CityId &&
                         ValueInt(reader, 7, -1) == pExpected.Generation &&
                         ValueDouble(reader, 8).Equals((double)pExpected.Reputation) &&
                         ValueInt(reader, 9, -1) == pExpected.StartYear &&
                         ValueInt(reader, 10, int.MinValue) == pExpected.EndYear &&
                         ValueInt(reader, 11, -1) == 1 &&
                         ValueString(reader, 12) == pExpected.EndReason &&
                         ValueDouble(reader, 13, -1d).Equals(pTime) &&
                         ValueString(reader, 14) == pExpected.Standing.ToString() &&
                         ValueInt(reader, 15, int.MinValue) ==
                         pExpected.LoyaltyUntilYear;
            }
            if (rowCount == 0) return SchoolPersistenceRowState.Missing;
            return rowCount == 1 && exact
                ? SchoolPersistenceRowState.Exact
                : SchoolPersistenceRowState.Conflict;
        }

        private static SchoolPersistenceRowState ReadMasterState(
            HistoricalSchoolMasterDefinition pMaster, long pActorId, long pHomeKingdomId,
            string pHomeKingdomName, long pHometownCityId, int pYear, double pTime,
            HistoricalMasterLineageCommitIdentity pIdentity)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT MASTER_ID,ACTOR_ID,SCHOOL_ID,CANONICAL_NAME,SPAWNED," +
                "DEAD,HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID,SPAWN_YEAR," +
                "DEATH_YEAR,LIFECYCLE_STATE,DEATH_CAUSE,DEATH_CITY_ID,UPDATED_TIME," +
                "LINEAGE_ID,SHI_ID FROM " +
                MasterTable + " WHERE MASTER_ID=@master OR ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@master", pMaster.Id);
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            int rowCount = 0;
            bool exact = false;
            while (reader.Read())
            {
                rowCount++;
                exact |= ValueString(reader, 0) == pMaster.Id &&
                         ValueLong(reader, 1, -1L) == pActorId &&
                         ValueString(reader, 2) == pMaster.SchoolId &&
                         ValueString(reader, 3) == pMaster.CanonicalName &&
                         ValueInt(reader, 4, -1) == 1 && ValueInt(reader, 5, -1) == 0 &&
                         ValueLong(reader, 6, -1L) == pHomeKingdomId &&
                         ValueString(reader, 7) == (pHomeKingdomName ?? "") &&
                         ValueLong(reader, 8, -1L) == pHometownCityId &&
                         ValueInt(reader, 9, -1) == pYear &&
                         ValueInt(reader, 10, int.MinValue) == -1 &&
                         ValueString(reader, 11) ==
                         HistoricalSchoolLifecycleState.AtHome.ToString() &&
                         ValueString(reader, 12) == "" &&
                         ValueLong(reader, 13, long.MinValue) == -1L &&
                         ValueDouble(reader, 14, -1d).Equals(pTime) &&
                         ValueLong(reader, 15, -1L) == pIdentity.LineageId &&
                         ValueLong(reader, 16, -1L) == pIdentity.ShiId;
            }
            if (rowCount == 0) return SchoolPersistenceRowState.Missing;
            return rowCount == 1 && exact
                ? SchoolPersistenceRowState.Exact
                : SchoolPersistenceRowState.Conflict;
        }

        private static SchoolPersistenceRowState ReadAffiliationState(long pActorId,
            long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId, int pYear,
            double pTime)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE,SERVICE_START_YEAR," +
                "SERVICE_END_YEAR,LAST_TRAVEL_YEAR,TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR," +
                "VOYAGE_ARRIVAL_YEAR,TRANSPORT_FAILURES,UPDATED_TIME FROM " +
                AffiliationTable + " WHERE ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return SchoolPersistenceRowState.Missing;
            bool exact = ValueLong(reader, 0, -1L) == pActorId &&
                         ValueLong(reader, 1, -1L) == pHomeKingdomId &&
                         ValueString(reader, 2) == (pHomeKingdomName ?? "") &&
                         ValueLong(reader, 3, -1L) == pHometownCityId &&
                         ValueLong(reader, 4, -1L) == pHometownCityId &&
                         ValueLong(reader, 5, long.MinValue) == -1L &&
                         ValueLong(reader, 6, long.MinValue) == -1L &&
                         ValueLong(reader, 7, long.MinValue) == -1L &&
                         ValueString(reader, 8) ==
                         HistoricalSchoolLifecycleState.AtHome.ToString() &&
                         ValueInt(reader, 9, int.MinValue) == -1 &&
                         ValueInt(reader, 10, int.MinValue) == -1 &&
                         ValueInt(reader, 11, -1) == pYear &&
                         ValueInt(reader, 12, int.MinValue) == -1 &&
                         ValueInt(reader, 13, int.MinValue) == -1 &&
                         ValueInt(reader, 14, int.MinValue) == -1 &&
                         ValueInt(reader, 15, -1) == 0 &&
                         ValueDouble(reader, 16, -1d).Equals(pTime);
            return !reader.Read() && exact
                ? SchoolPersistenceRowState.Exact
                : SchoolPersistenceRowState.Conflict;
        }

        private static SchoolPersistenceRowState ToSchoolRowState(
            HistoricalMasterLineageRowState pState)
        {
            return pState switch
            {
                HistoricalMasterLineageRowState.Missing => SchoolPersistenceRowState.Missing,
                HistoricalMasterLineageRowState.Exact => SchoolPersistenceRowState.Exact,
                _ => SchoolPersistenceRowState.Conflict
            };
        }

        public static SchoolPersistenceOutcome CommitSchoolDeath(
            SchoolMembershipRecord pMembership,
            HistoricalSchoolAffiliationSnapshot pCachedAffiliation,
            HistoricalSchoolMasterDefinition pMaster, int pYear, long pCityId,
            string pCause, double pTime,
            out HistoricalSchoolAffiliationSnapshot pCommittedAffiliation,
            out long pEffectiveDeathCityId)
        {
            pCommittedAffiliation = null;
            pEffectiveDeathCityId = pCityId;
            bool historicalMaster = pMembership?.Source ==
                                    SchoolMembershipSource.HistoricalDescent;
            if (DB == null || pMembership == null || !pMembership.Active ||
                !pMembership.IsValid || pYear < pMembership.StartYear ||
                (pCachedAffiliation != null &&
                 pCachedAffiliation.ActorId != pMembership.ActorId) ||
                (!historicalMaster && pMaster != null) ||
                 (pMaster != null &&
                  (pMembership.Source != SchoolMembershipSource.HistoricalDescent ||
                   pMembership.SourceId != pMaster.Id ||
                   pMembership.SchoolId != pMaster.SchoolId)))
                return SchoolPersistenceOutcome.Unknown;

            double time = FiniteNonNegative(pTime);
            string cause = pCause ?? "death";
            double originalMembershipTime = double.NaN;
            HistoricalSchoolAffiliationDeathRow originalAffiliation = null;
            HistoricalSchoolMasterDeathRow originalMaster = null;
            bool affiliationExpected = pCachedAffiliation != null;
            long deathCityId = pCityId;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                originalMembershipTime = LoadMembershipTimeForDeath(pMembership, transaction);
                HistoricalSchoolAffiliationSnapshot authoritativeAffiliation =
                    LoadAffiliationForDeath(pMembership.ActorId, transaction,
                        out originalAffiliation);
                affiliationExpected |= authoritativeAffiliation != null;
                if (pCachedAffiliation != null && authoritativeAffiliation == null)
                    throw new InvalidOperationException("cached affiliation row not found");
                if (authoritativeAffiliation?.LifecycleState ==
                    HistoricalSchoolLifecycleState.Dead)
                    throw new InvalidOperationException("affiliation already dead");
                pCommittedAffiliation = authoritativeAffiliation;
                deathCityId = authoritativeAffiliation?.ResidenceCityId >= 0
                    ? authoritativeAffiliation.ResidenceCityId
                    : pCityId;
                pEffectiveDeathCityId = deathCityId;
                if (historicalMaster)
                    originalMaster = LoadMasterForDeath(pMembership, transaction);
                CloseSchoolDeathMembershipCommand(pMembership, pYear, "death", time,
                    transaction);
                if (authoritativeAffiliation != null)
                {
                    using var affiliation = new SQLiteCommand(DB) { Transaction = transaction };
                    affiliation.CommandText = "UPDATE " + AffiliationTable +
                        " SET LIFECYCLE_STATE=@state,SERVICE_KINGDOM_ID=-1," +
                        "SERVICE_START_YEAR=-1,SERVICE_END_YEAR=-1," +
                        "DESTINATION_CITY_ID=-1,TRAVEL_WAIT_START_YEAR=-1," +
                        "VOYAGE_START_YEAR=-1,VOYAGE_ARRIVAL_YEAR=-1,UPDATED_TIME=@time" +
                        " WHERE ACTOR_ID=@actor AND LIFECYCLE_STATE=@previousState" +
                        " AND LIFECYCLE_STATE<>@state";
                    affiliation.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.Dead.ToString());
                    affiliation.Parameters.AddWithValue("@previousState",
                        authoritativeAffiliation.LifecycleState.ToString());
                    affiliation.Parameters.AddWithValue("@time", time);
                    affiliation.Parameters.AddWithValue("@actor", pMembership.ActorId);
                    if (affiliation.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("active affiliation row not found");
                }
                if (historicalMaster)
                {
                    using var master = new SQLiteCommand(DB) { Transaction = transaction };
                    master.CommandText = "UPDATE " + MasterTable +
                        " SET DEAD=1,DEATH_YEAR=@year,DEATH_CITY_ID=@city,DEATH_CAUSE=@cause," +
                        "LIFECYCLE_STATE=@state,UPDATED_TIME=@time WHERE MASTER_ID=@master" +
                        " AND ACTOR_ID=@actor AND SCHOOL_ID=@school AND SPAWNED=1 AND DEAD=0";
                    master.Parameters.AddWithValue("@year", pYear);
                    master.Parameters.AddWithValue("@city", deathCityId);
                    master.Parameters.AddWithValue("@cause", cause);
                    master.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.Dead.ToString());
                    master.Parameters.AddWithValue("@time", time);
                    master.Parameters.AddWithValue("@master", pMembership.SourceId);
                    master.Parameters.AddWithValue("@actor", pMembership.ActorId);
                    master.Parameters.AddWithValue("@school", pMembership.SchoolId);
                    if (master.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("living master row not found");
                }
                transaction.Commit();
                pCommittedAffiliation = authoritativeAffiliation;
                return SchoolPersistenceOutcome.Committed;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore commit school death failed: " +
                                    error.Message);
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            if (double.IsNaN(originalMembershipTime))
                return SchoolPersistenceOutcome.Unknown;
            try
            {
                ReadSchoolDeathState(pMembership, originalMembershipTime,
                    originalAffiliation, affiliationExpected, originalMaster,
                    historicalMaster, pYear, deathCityId, cause, time,
                    out SchoolDeathPersistenceRowState membershipState,
                    out SchoolDeathPersistenceRowState affiliationState,
                    out SchoolDeathPersistenceRowState masterState);
                SchoolPersistenceOutcome outcome = HistoricalSchoolDeathPersistenceRules.Resolve(
                    pQuerySucceeded: true, membershipState, affiliationState, masterState);
                if (outcome == SchoolPersistenceOutcome.Committed)
                    pCommittedAffiliation = originalAffiliation?.State;
                return outcome;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore school death readback failed: " +
                                    error.Message);
                return HistoricalSchoolDeathPersistenceRules.Resolve(pQuerySucceeded: false,
                    SchoolDeathPersistenceRowState.Conflict,
                    SchoolDeathPersistenceRowState.Conflict,
                    SchoolDeathPersistenceRowState.Conflict);
            }
        }

        public static SchoolPersistenceOutcome ReconcileSchoolDeath(
            SchoolMembershipRecord pMembership,
            HistoricalSchoolAffiliationSnapshot pCachedAffiliation,
            HistoricalSchoolMasterDefinition pMaster, int pYear, long pCityId,
            string pCause, double pTime,
            out HistoricalSchoolAffiliationSnapshot pCommittedAffiliation)
        {
            pCommittedAffiliation = null;
            bool historicalMaster = pMembership?.Source ==
                                    SchoolMembershipSource.HistoricalDescent;
            if (DB == null || pMembership == null || !pMembership.Active ||
                !pMembership.IsValid || pYear < pMembership.StartYear ||
                (pCachedAffiliation != null &&
                 pCachedAffiliation.ActorId != pMembership.ActorId) ||
                (!historicalMaster && pMaster != null) ||
                (pMaster != null &&
                 (pMembership.SourceId != pMaster.Id ||
                  pMembership.SchoolId != pMaster.SchoolId)))
                return SchoolPersistenceOutcome.Unknown;

            double time = FiniteNonNegative(pTime);
            string cause = pCause ?? "death";
            try
            {
                SchoolDeathPersistenceRowState membershipState =
                    ReadAuthoritativeSchoolDeathMembershipState(pMembership, pYear, time);
                SchoolDeathPersistenceRowState affiliationState =
                    ReadAuthoritativeSchoolDeathAffiliationState(pMembership.ActorId,
                        pCachedAffiliation, time,
                        out HistoricalSchoolAffiliationSnapshot authoritativeAffiliation);
                SchoolDeathPersistenceRowState masterState =
                    ReadAuthoritativeSchoolDeathMasterState(pMembership, historicalMaster,
                        pYear, pCityId, cause, time);
                SchoolPersistenceOutcome outcome =
                    HistoricalSchoolDeathPersistenceRules.Resolve(pQuerySucceeded: true,
                        membershipState, affiliationState, masterState);
                pCommittedAffiliation = authoritativeAffiliation;
                return outcome;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore authoritative death readback failed: " +
                                    error.Message);
                return HistoricalSchoolDeathPersistenceRules.Resolve(pQuerySucceeded: false,
                    SchoolDeathPersistenceRowState.Conflict,
                    SchoolDeathPersistenceRowState.Conflict,
                    SchoolDeathPersistenceRowState.Conflict);
            }
        }

        private static SchoolDeathPersistenceRowState
            ReadAuthoritativeSchoolDeathMembershipState(SchoolMembershipRecord pExpected,
                int pYear, double pTime)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT MEMBERSHIP_ID,ACTOR_ID,SCHOOL_ID,SOURCE_TYPE," +
                "SOURCE_ID,TEACHER_ACTOR_ID,CITY_ID,GENERATION,REPUTATION,START_YEAR," +
                "END_YEAR,ACTIVE,END_REASON,UPDATED_TIME,STANDING,LOYALTY_UNTIL_YEAR FROM " + MembershipTable +
                " WHERE MEMBERSHIP_ID=@id OR (ACTOR_ID=@actor AND ACTIVE=1)";
            command.Parameters.AddWithValue("@id", pExpected.MembershipId);
            command.Parameters.AddWithValue("@actor", pExpected.ActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            int rowCount = 0;
            bool original = false;
            bool committed = false;
            while (reader.Read())
            {
                rowCount++;
                bool identity = MatchesMembershipIdentity(reader, pExpected);
                original |= identity &&
                            ValueInt(reader, 10, int.MinValue) == pExpected.EndYear &&
                            ValueInt(reader, 11, -1) == 1 &&
                            ValueString(reader, 12) == pExpected.EndReason;
                committed |= identity && ValueInt(reader, 10, int.MinValue) == pYear &&
                              ValueInt(reader, 11, -1) == 0 &&
                              ValueString(reader, 12) == "death" &&
                              ValueDouble(reader, 13, -1d).Equals(pTime);
            }
            if (rowCount != 1) return SchoolDeathPersistenceRowState.Conflict;
            if (committed) return SchoolDeathPersistenceRowState.Committed;
            if (original) return SchoolDeathPersistenceRowState.Original;
            return SchoolDeathPersistenceRowState.Conflict;
        }

        private static SchoolDeathPersistenceRowState
            ReadAuthoritativeSchoolDeathAffiliationState(long pActorId,
                HistoricalSchoolAffiliationSnapshot pExpected, double pTime,
                out HistoricalSchoolAffiliationSnapshot pAuthoritativeAffiliation)
        {
            pAuthoritativeAffiliation = null;
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE," +
                "SERVICE_START_YEAR,SERVICE_END_YEAR,LAST_TRAVEL_YEAR," +
                "TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR," +
                "TRANSPORT_FAILURES,UPDATED_TIME FROM " + AffiliationTable +
                " WHERE ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return pExpected == null
                    ? SchoolDeathPersistenceRowState.Unchanged
                    : SchoolDeathPersistenceRowState.Conflict;
            if (!Enum.TryParse(ValueString(reader, 8), out
                    HistoricalSchoolLifecycleState lifecycleState))
                return SchoolDeathPersistenceRowState.Conflict;
            var current = new HistoricalSchoolAffiliationSnapshot(
                ValueLong(reader, 0, -1L), ValueLong(reader, 1, -1L),
                ValueString(reader, 2), ValueLong(reader, 3, -1L),
                ValueLong(reader, 4, -1L), ValueLong(reader, 5, -1L),
                ValueLong(reader, 6, -1L), ValueLong(reader, 7, -1L), lifecycleState,
                ValueInt(reader, 9, -1), ValueInt(reader, 10, -1),
                ValueInt(reader, 11, -1), ValueInt(reader, 12, -1),
                ValueInt(reader, 13, -1), ValueInt(reader, 14, -1),
                ValueInt(reader, 15));
            double updatedTime = ValueDouble(reader, 16, -1d);
            if (current.ActorId != pActorId || reader.Read())
                return SchoolDeathPersistenceRowState.Conflict;

            bool fixedFields = pExpected == null ||
                current.HomeKingdomId == pExpected.HomeKingdomId &&
                current.HomeKingdomName == pExpected.HomeKingdomName &&
                current.HometownCityId == pExpected.HometownCityId &&
                current.ResidenceCityId == pExpected.ResidenceCityId &&
                current.PreviousResidenceCityId == pExpected.PreviousResidenceCityId &&
                current.LastTravelYear == pExpected.LastTravelYear &&
                current.TransportFailures == pExpected.TransportFailures;
            bool committed = fixedFields &&
                current.LifecycleState == HistoricalSchoolLifecycleState.Dead &&
                current.DestinationCityId == -1 && current.ServiceKingdomId == -1 &&
                current.ServiceStartYear == -1 && current.ServiceEndYear == -1 &&
                current.TravelWaitStartYear == -1 && current.VoyageStartYear == -1 &&
                current.VoyageArrivalYear == -1 && updatedTime.Equals(pTime);
            if (committed)
            {
                pAuthoritativeAffiliation = pExpected ??
                    CreateAffiliationForCommittedDeathAdoption(current);
                return SchoolDeathPersistenceRowState.Committed;
            }
            if (pExpected == null)
            {
                if (current.LifecycleState == HistoricalSchoolLifecycleState.Dead)
                    return SchoolDeathPersistenceRowState.Conflict;
                pAuthoritativeAffiliation = current;
                return SchoolDeathPersistenceRowState.Original;
            }
            if (current.LifecycleState == HistoricalSchoolLifecycleState.Dead)
                return SchoolDeathPersistenceRowState.Conflict;
            pAuthoritativeAffiliation = current;
            return SchoolDeathPersistenceRowState.Original;
        }

        private static HistoricalSchoolAffiliationSnapshot
            CreateAffiliationForCommittedDeathAdoption(
                HistoricalSchoolAffiliationSnapshot pCommitted)
        {
            return new HistoricalSchoolAffiliationSnapshot(pCommitted.ActorId,
                pCommitted.HomeKingdomId, pCommitted.HomeKingdomName,
                pCommitted.HometownCityId, pCommitted.ResidenceCityId,
                pCommitted.PreviousResidenceCityId, -1, -1,
                HistoricalSchoolLifecycleState.AtHome, -1, -1,
                pCommitted.LastTravelYear, -1, -1, -1,
                pCommitted.TransportFailures);
        }

        private static SchoolDeathPersistenceRowState ReadAuthoritativeSchoolDeathMasterState(
            SchoolMembershipRecord pMembership, bool pHistoricalMaster, int pYear,
            long pDeathCityId, string pCause, double pTime)
        {
            if (!pHistoricalMaster) return SchoolDeathPersistenceRowState.Unchanged;
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT MASTER_ID,ACTOR_ID,SCHOOL_ID,CANONICAL_NAME,SPAWNED," +
                "DEAD,HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID,SPAWN_YEAR," +
                "DEATH_YEAR,LIFECYCLE_STATE,DEATH_CAUSE,DEATH_CITY_ID,UPDATED_TIME FROM " +
                MasterTable + " WHERE MASTER_ID=@master OR ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@master", pMembership.SourceId);
            command.Parameters.AddWithValue("@actor", pMembership.ActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return SchoolDeathPersistenceRowState.Conflict;
            HistoricalSchoolMasterDeathRow current = HistoricalSchoolMasterDeathRow.Read(reader);
            if (reader.Read() || current.MasterId != pMembership.SourceId ||
                current.ActorId != pMembership.ActorId ||
                current.SchoolId != pMembership.SchoolId || !current.Spawned)
                return SchoolDeathPersistenceRowState.Conflict;
            bool committed = current.MatchesAuthoritativeCommittedDeath(pMembership,
                pYear, pDeathCityId, pCause, pTime);
            if (committed) return SchoolDeathPersistenceRowState.Committed;
            bool original = current.MatchesAuthoritativeOriginal(pMembership);
            return original
                ? SchoolDeathPersistenceRowState.Original
                : SchoolDeathPersistenceRowState.Conflict;
        }

        private static double LoadMembershipTimeForDeath(SchoolMembershipRecord pMembership,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "SELECT MEMBERSHIP_ID,ACTOR_ID,SCHOOL_ID,SOURCE_TYPE," +
                "SOURCE_ID,TEACHER_ACTOR_ID,CITY_ID,GENERATION,REPUTATION,START_YEAR," +
                "END_YEAR,ACTIVE,END_REASON,UPDATED_TIME,STANDING,LOYALTY_UNTIL_YEAR FROM " + MembershipTable +
                " WHERE MEMBERSHIP_ID=@id OR (ACTOR_ID=@actor AND ACTIVE=1)";
            command.Parameters.AddWithValue("@id", pMembership.MembershipId);
            command.Parameters.AddWithValue("@actor", pMembership.ActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read() || !MatchesMembershipIdentity(reader, pMembership) ||
                ValueInt(reader, 10, int.MinValue) != pMembership.EndYear ||
                ValueInt(reader, 11, -1) != 1 ||
                ValueString(reader, 12) != pMembership.EndReason)
                throw new InvalidOperationException("active membership row does not match runtime");
            double updatedTime = ValueDouble(reader, 13, -1d);
            if (reader.Read())
                throw new InvalidOperationException("conflicting active membership rows");
            return updatedTime;
        }

        private static HistoricalSchoolAffiliationSnapshot LoadAffiliationForDeath(
            long pActorId, SQLiteTransaction pTransaction,
            out HistoricalSchoolAffiliationDeathRow pLoadedRow)
        {
            pLoadedRow = null;
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "SELECT ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE," +
                "SERVICE_START_YEAR,SERVICE_END_YEAR,LAST_TRAVEL_YEAR," +
                "TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR," +
                "TRANSPORT_FAILURES,UPDATED_TIME FROM " + AffiliationTable +
                " WHERE ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            if (!Enum.TryParse(ValueString(reader, 8), out
                    HistoricalSchoolLifecycleState lifecycleState))
                throw new InvalidOperationException("invalid affiliation lifecycle state");
            var result = new HistoricalSchoolAffiliationSnapshot(
                ValueLong(reader, 0, -1L), ValueLong(reader, 1, -1L),
                ValueString(reader, 2), ValueLong(reader, 3, -1L),
                ValueLong(reader, 4, -1L), ValueLong(reader, 5, -1L),
                ValueLong(reader, 6, -1L), ValueLong(reader, 7, -1L), lifecycleState,
                ValueInt(reader, 9, -1), ValueInt(reader, 10, -1),
                ValueInt(reader, 11, -1), ValueInt(reader, 12, -1),
                ValueInt(reader, 13, -1), ValueInt(reader, 14, -1),
                ValueInt(reader, 15));
            double updatedTime = ValueDouble(reader, 16, -1d);
            if (result.ActorId != pActorId || reader.Read())
                throw new InvalidOperationException("conflicting affiliation rows");
            pLoadedRow = new HistoricalSchoolAffiliationDeathRow(result, updatedTime);
            return result;
        }

        private static HistoricalSchoolMasterDeathRow LoadMasterForDeath(
            SchoolMembershipRecord pMembership, SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "SELECT MASTER_ID,ACTOR_ID,SCHOOL_ID,CANONICAL_NAME,SPAWNED," +
                "DEAD,HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID,SPAWN_YEAR," +
                "DEATH_YEAR,LIFECYCLE_STATE,DEATH_CAUSE,DEATH_CITY_ID,UPDATED_TIME FROM " +
                MasterTable + " WHERE MASTER_ID=@master OR ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@master", pMembership.SourceId);
            command.Parameters.AddWithValue("@actor", pMembership.ActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("living master row not found");
            var result = HistoricalSchoolMasterDeathRow.Read(reader);
            if (result.MasterId != pMembership.SourceId ||
                result.ActorId != pMembership.ActorId ||
                result.SchoolId != pMembership.SchoolId || !result.Spawned || result.Dead ||
                reader.Read())
                throw new InvalidOperationException("living master row does not match membership");
            return result;
        }

        private static void ReadSchoolDeathState(SchoolMembershipRecord pMembership,
            double pOriginalMembershipTime,
            HistoricalSchoolAffiliationDeathRow pOriginalAffiliation,
            bool pAffiliationExpected, HistoricalSchoolMasterDeathRow pOriginalMaster,
            bool pHistoricalMaster, int pYear, long pDeathCityId, string pCause, double pTime,
            out SchoolDeathPersistenceRowState pMembershipState,
            out SchoolDeathPersistenceRowState pAffiliationState,
            out SchoolDeathPersistenceRowState pMasterState)
        {
            pMembershipState = ReadSchoolDeathMembershipState(pMembership,
                pOriginalMembershipTime, pYear, pTime);
            pAffiliationState = ReadSchoolDeathAffiliationState(pMembership.ActorId,
                pOriginalAffiliation, pAffiliationExpected, pTime);
            pMasterState = ReadSchoolDeathMasterState(pOriginalMaster, pHistoricalMaster,
                pYear, pDeathCityId, pCause, pTime);
        }

        private static SchoolDeathPersistenceRowState ReadSchoolDeathMembershipState(
            SchoolMembershipRecord pExpected, double pOriginalTime, int pYear, double pTime)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT MEMBERSHIP_ID,ACTOR_ID,SCHOOL_ID,SOURCE_TYPE," +
                "SOURCE_ID,TEACHER_ACTOR_ID,CITY_ID,GENERATION,REPUTATION,START_YEAR," +
                "END_YEAR,ACTIVE,END_REASON,UPDATED_TIME,STANDING,LOYALTY_UNTIL_YEAR FROM " + MembershipTable +
                " WHERE MEMBERSHIP_ID=@id OR (ACTOR_ID=@actor AND ACTIVE=1)";
            command.Parameters.AddWithValue("@id", pExpected.MembershipId);
            command.Parameters.AddWithValue("@actor", pExpected.ActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            int rowCount = 0;
            bool original = false;
            bool committed = false;
            while (reader.Read())
            {
                rowCount++;
                bool identity = MatchesMembershipIdentity(reader, pExpected);
                original |= identity &&
                            ValueInt(reader, 10, int.MinValue) == pExpected.EndYear &&
                            ValueInt(reader, 11, -1) == 1 &&
                            ValueString(reader, 12) == pExpected.EndReason &&
                            ValueDouble(reader, 13, -1d).Equals(pOriginalTime);
                committed |= identity && ValueInt(reader, 10, int.MinValue) == pYear &&
                              ValueInt(reader, 11, -1) == 0 &&
                              ValueString(reader, 12) == "death" &&
                              ValueDouble(reader, 13, -1d).Equals(pTime);
            }
            if (rowCount != 1) return SchoolDeathPersistenceRowState.Conflict;
            if (committed) return SchoolDeathPersistenceRowState.Committed;
            if (original) return SchoolDeathPersistenceRowState.Original;
            return SchoolDeathPersistenceRowState.Conflict;
        }

        private static bool MatchesMembershipIdentity(SQLiteDataReader pReader,
            SchoolMembershipRecord pExpected)
        {
            if (pReader == null || pExpected == null) return false;
            var persisted = new SchoolMembershipStableIdentity(
                ValueLong(pReader, 0, -1L), ValueLong(pReader, 1, -1L),
                ValueString(pReader, 2), ValueString(pReader, 3),
                ValueString(pReader, 4), ValueLong(pReader, 5, -1L),
                ValueLong(pReader, 6, -1L), ValueInt(pReader, 7, -1),
                ValueInt(pReader, 9, -1));
            var expected = new SchoolMembershipStableIdentity(
                pExpected.MembershipId, pExpected.ActorId,
                pExpected.SchoolId, pExpected.Source.ToString(),
                pExpected.SourceId, pExpected.TeacherActorId,
                pExpected.CityId, pExpected.Generation,
                pExpected.StartYear);
            return persisted.Equals(expected);
        }

        private static SchoolDeathPersistenceRowState ReadSchoolDeathAffiliationState(
            long pActorId, HistoricalSchoolAffiliationDeathRow pOriginal,
            bool pAffiliationExpected, double pTime)
        {
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE," +
                "SERVICE_START_YEAR,SERVICE_END_YEAR,LAST_TRAVEL_YEAR," +
                "TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR," +
                "TRANSPORT_FAILURES,UPDATED_TIME FROM " + AffiliationTable +
                " WHERE ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@actor", pActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
                return pOriginal == null && !pAffiliationExpected
                    ? SchoolDeathPersistenceRowState.Unchanged
                    : SchoolDeathPersistenceRowState.Conflict;
            bool extra = false;
            bool original = false;
            bool committed = false;
            if (pOriginal != null)
            {
                HistoricalSchoolAffiliationSnapshot state = pOriginal.State;
                bool fixedFields = ValueLong(reader, 0, -1L) == state.ActorId &&
                    ValueLong(reader, 1, -1L) == state.HomeKingdomId &&
                    ValueString(reader, 2) == state.HomeKingdomName &&
                    ValueLong(reader, 3, -1L) == state.HometownCityId &&
                    ValueLong(reader, 4, -1L) == state.ResidenceCityId &&
                    ValueLong(reader, 5, -1L) == state.PreviousResidenceCityId &&
                    ValueInt(reader, 11, int.MinValue) == state.LastTravelYear &&
                    ValueInt(reader, 15, -1) == state.TransportFailures;
                original = fixedFields &&
                    ValueLong(reader, 6, long.MinValue) == state.DestinationCityId &&
                    ValueLong(reader, 7, long.MinValue) == state.ServiceKingdomId &&
                    ValueString(reader, 8) == state.LifecycleState.ToString() &&
                    ValueInt(reader, 9, int.MinValue) == state.ServiceStartYear &&
                    ValueInt(reader, 10, int.MinValue) == state.ServiceEndYear &&
                    ValueInt(reader, 12, int.MinValue) == state.TravelWaitStartYear &&
                    ValueInt(reader, 13, int.MinValue) == state.VoyageStartYear &&
                    ValueInt(reader, 14, int.MinValue) == state.VoyageArrivalYear &&
                    ValueDouble(reader, 16, -1d).Equals(pOriginal.UpdatedTime);
                committed = fixedFields &&
                    ValueLong(reader, 6, long.MinValue) == -1L &&
                    ValueLong(reader, 7, long.MinValue) == -1L &&
                    ValueString(reader, 8) == HistoricalSchoolLifecycleState.Dead.ToString() &&
                    ValueInt(reader, 9, int.MinValue) == -1 &&
                    ValueInt(reader, 10, int.MinValue) == -1 &&
                    ValueInt(reader, 12, int.MinValue) == -1 &&
                    ValueInt(reader, 13, int.MinValue) == -1 &&
                    ValueInt(reader, 14, int.MinValue) == -1 &&
                    ValueDouble(reader, 16, -1d).Equals(pTime);
            }
            extra = reader.Read();
            if (extra || pOriginal == null) return SchoolDeathPersistenceRowState.Conflict;
            if (committed) return SchoolDeathPersistenceRowState.Committed;
            if (original) return SchoolDeathPersistenceRowState.Original;
            return SchoolDeathPersistenceRowState.Conflict;
        }

        private static SchoolDeathPersistenceRowState ReadSchoolDeathMasterState(
            HistoricalSchoolMasterDeathRow pOriginal, bool pHistoricalMaster, int pYear,
            long pDeathCityId, string pCause, double pTime)
        {
            if (pOriginal == null)
                return pHistoricalMaster ? SchoolDeathPersistenceRowState.Conflict :
                    SchoolDeathPersistenceRowState.Unchanged;
            using var command = new SQLiteCommand(DB);
            command.CommandText = "SELECT MASTER_ID,ACTOR_ID,SCHOOL_ID,CANONICAL_NAME,SPAWNED," +
                "DEAD,HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID,SPAWN_YEAR," +
                "DEATH_YEAR,LIFECYCLE_STATE,DEATH_CAUSE,DEATH_CITY_ID,UPDATED_TIME FROM " +
                MasterTable + " WHERE MASTER_ID=@master OR ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@master", pOriginal.MasterId);
            command.Parameters.AddWithValue("@actor", pOriginal.ActorId);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return SchoolDeathPersistenceRowState.Conflict;
            HistoricalSchoolMasterDeathRow current = HistoricalSchoolMasterDeathRow.Read(reader);
            if (reader.Read()) return SchoolDeathPersistenceRowState.Conflict;
            if (current.MatchesCommittedDeath(pOriginal, pYear, pDeathCityId, pCause, pTime))
                return SchoolDeathPersistenceRowState.Committed;
            if (current.MatchesExact(pOriginal)) return SchoolDeathPersistenceRowState.Original;
            return SchoolDeathPersistenceRowState.Conflict;
        }

        private static void CloseSchoolDeathMembershipCommand(
            SchoolMembershipRecord pMembership, int pYear, string pReason, double pTime,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + MembershipTable +
                " SET ACTIVE=0,END_YEAR=@year,END_REASON=@reason,UPDATED_TIME=@time" +
                " WHERE MEMBERSHIP_ID=@id AND ACTOR_ID=@actor AND SCHOOL_ID=@school" +
                " AND SOURCE_TYPE=@source AND SOURCE_ID=@sourceId AND ACTIVE=1";
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@reason", pReason ?? "");
            command.Parameters.AddWithValue("@time", pTime);
            command.Parameters.AddWithValue("@id", pMembership.MembershipId);
            command.Parameters.AddWithValue("@actor", pMembership.ActorId);
            command.Parameters.AddWithValue("@school", pMembership.SchoolId);
            command.Parameters.AddWithValue("@source", pMembership.Source.ToString());
            command.Parameters.AddWithValue("@sourceId", pMembership.SourceId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("active membership row not found");
        }

        private static void InsertMembershipCommand(SchoolMembershipRecord pRecord, double pTime,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + MembershipTable +
                " (MEMBERSHIP_ID,ACTOR_ID,SCHOOL_ID,SOURCE_TYPE,SOURCE_ID,TEACHER_ACTOR_ID," +
                "CITY_ID,GENERATION,REPUTATION,START_YEAR,END_YEAR,ACTIVE,END_REASON,UPDATED_TIME," +
                "STANDING,LOYALTY_UNTIL_YEAR)" +
                " VALUES (@id,@actor,@school,@source,@sourceId,@teacher,@city,@generation," +
                "@reputation,@start,-1,1,'',@time,@standing,@loyalty)";
            command.Parameters.AddWithValue("@id", pRecord.MembershipId);
            command.Parameters.AddWithValue("@actor", pRecord.ActorId);
            command.Parameters.AddWithValue("@school", pRecord.SchoolId);
            command.Parameters.AddWithValue("@source", pRecord.Source.ToString());
            command.Parameters.AddWithValue("@sourceId", pRecord.SourceId);
            command.Parameters.AddWithValue("@teacher", pRecord.TeacherActorId);
            command.Parameters.AddWithValue("@city", pRecord.CityId);
            command.Parameters.AddWithValue("@generation", pRecord.Generation);
            command.Parameters.AddWithValue("@reputation", pRecord.Reputation);
            command.Parameters.AddWithValue("@start", pRecord.StartYear);
            command.Parameters.AddWithValue("@time", pTime);
            command.Parameters.AddWithValue("@standing", pRecord.Standing.ToString());
            command.Parameters.AddWithValue("@loyalty", pRecord.LoyaltyUntilYear);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("membership insert failed");
        }

        private static string MembershipSelect()
        {
            return "SELECT MEMBERSHIP_ID,ACTOR_ID,SCHOOL_ID,SOURCE_TYPE,SOURCE_ID," +
                   "TEACHER_ACTOR_ID,CITY_ID,GENERATION,REPUTATION,START_YEAR," +
                   "END_YEAR,ACTIVE,END_REASON,UPDATED_TIME,STANDING," +
                   "LOYALTY_UNTIL_YEAR FROM " + MembershipTable;
        }

        private static bool MembershipRowMatches(SQLiteDataReader pReader,
            SchoolMembershipRecord pExpected, bool pActive, int pEndYear,
            string pEndReason, double pTime, bool pRequireTime)
        {
            return pReader != null && pExpected != null &&
                   ValueLong(pReader, 0, -1L) == pExpected.MembershipId &&
                   ValueLong(pReader, 1, -1L) == pExpected.ActorId &&
                   ValueString(pReader, 2) == pExpected.SchoolId &&
                   ValueString(pReader, 3) == pExpected.Source.ToString() &&
                   ValueString(pReader, 4) == pExpected.SourceId &&
                   ValueLong(pReader, 5, -1L) == pExpected.TeacherActorId &&
                   ValueLong(pReader, 6, -1L) == pExpected.CityId &&
                   ValueInt(pReader, 7, -1) == pExpected.Generation &&
                   SchoolMembershipPersistenceRules.ReputationMatches(
                       ValueDouble(pReader, 8), pExpected.Reputation) &&
                   ValueInt(pReader, 9, -1) == pExpected.StartYear &&
                   ValueInt(pReader, 10, int.MinValue) == pEndYear &&
                   (ValueInt(pReader, 11, -1) != 0) == pActive &&
                   ValueString(pReader, 12) == (pEndReason ?? "") &&
                   (!pRequireTime || ValueDouble(pReader, 13, -1d).Equals(
                       FiniteNonNegative(pTime))) &&
                   ValueString(pReader, 14) == pExpected.Standing.ToString() &&
                   ValueInt(pReader, 15, int.MinValue) ==
                   pExpected.LoyaltyUntilYear;
        }

        private static void CloseMembershipCommand(long pMembershipId, int pYear, string pReason,
            double pTime, SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + MembershipTable +
                " SET ACTIVE=0,END_YEAR=@year,END_REASON=@reason,UPDATED_TIME=@time" +
                " WHERE MEMBERSHIP_ID=@id AND ACTIVE=1";
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@reason", pReason ?? "");
            command.Parameters.AddWithValue("@time", pTime);
            command.Parameters.AddWithValue("@id", pMembershipId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("active membership row not found");
        }

        private static HistoricalSchoolTeachingPersistenceOutcome ReadExistingDebateOutcome(
            SQLiteTransaction pTransaction, HistoricalSchoolDebateRecord pDebate,
            out bool pExists)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "SELECT TOPIC_ID,FIRST_ACTOR_ID,FIRST_SCHOOL_ID," +
                                  "SECOND_ACTOR_ID,SECOND_SCHOOL_ID,SEED,FIRST_SCORE," +
                                  "SECOND_SCORE,RESULT,RESOLVED FROM " + DebateTable +
                                  " WHERE CITY_ID=@city AND DEBATE_YEAR=@year LIMIT 1";
            command.Parameters.AddWithValue("@city", pDebate.CityId);
            command.Parameters.AddWithValue("@year", pDebate.DebateYear);
            using SQLiteDataReader reader = command.ExecuteReader();
            pExists = reader.Read();
            if (!pExists) return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            bool exact = string.Equals(ValueString(reader, 0), pDebate.TopicId,
                             StringComparison.Ordinal) &&
                         ValueLong(reader, 1, -1L) == pDebate.FirstActorId &&
                         string.Equals(ValueString(reader, 2), pDebate.FirstSchoolId,
                             StringComparison.Ordinal) &&
                         ValueLong(reader, 3, -1L) == pDebate.SecondActorId &&
                         string.Equals(ValueString(reader, 4), pDebate.SecondSchoolId,
                             StringComparison.Ordinal) &&
                         ValueLong(reader, 5) == pDebate.Seed &&
                         SameStoredScore(ValueDouble(reader, 6), pDebate.FirstScore) &&
                         SameStoredScore(ValueDouble(reader, 7), pDebate.SecondScore) &&
                         string.Equals(ValueString(reader, 8), pDebate.Outcome.ToString(),
                             StringComparison.Ordinal) &&
                         (ValueInt(reader, 9) != 0) == pDebate.Resolved;
            return exact
                ? HistoricalSchoolTeachingPersistenceOutcome.Replayed
                : HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
        }

        private static bool SameStoredScore(double pStored, double pExpected)
        {
            return Math.Abs(pStored - pExpected) <= 0.000000001d;
        }

        private static bool HasActiveInstitutionCommand(SQLiteTransaction pTransaction,
            HistoricalSchoolMasterDefinition pMaster, long pCityId)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "SELECT 1 FROM " + InstitutionTable +
                                  " WHERE CITY_ID=@city AND SCHOOL_ID=@school AND " +
                                  "INSTITUTION_TYPE=@institution AND ACTIVE=1 LIMIT 1";
            command.Parameters.AddWithValue("@city", pCityId);
            command.Parameters.AddWithValue("@school", pMaster.SchoolId);
            command.Parameters.AddWithValue("@institution", pMaster.InstitutionId);
            if (command.ExecuteScalar() != null) return true;

            // A school may have only one active institution in a city even if an old
            // definition used a different institution_type.  This is the second guard
            // that keeps the operation idempotent across content revisions.
            command.CommandText = "SELECT 1 FROM " + InstitutionTable +
                                  " WHERE CITY_ID=@city AND SCHOOL_ID=@school AND ACTIVE=1 " +
                                  "LIMIT 1";
            command.Parameters.RemoveAt(command.Parameters.IndexOf("@institution"));
            return command.ExecuteScalar() != null;
        }

        private static bool HasFounderEvidenceCommand(SQLiteTransaction pTransaction,
            long pFounderActorId, long pCityId, string pSchoolId)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "SELECT COUNT(*) FROM " + EventTable +
                                  " WHERE CITY_ID=@city AND SCHOOL_ID=@school AND " +
                                  "EVENT_TYPE IN ('lecture','debate') " +
                                  "AND (ACTOR_ID=@actor OR TARGET_ACTOR_ID=@actor)";
            command.Parameters.AddWithValue("@city", pCityId);
            command.Parameters.AddWithValue("@school", pSchoolId ?? "");
            command.Parameters.AddWithValue("@actor", pFounderActorId);
            object value = command.ExecuteScalar();
            return value != null && value != DBNull.Value && Convert.ToInt64(value) >= 1L;
        }

        private static void InsertSchoolInstitutionCommand(SQLiteTransaction pTransaction,
            long pInstitutionId, HistoricalSchoolMasterDefinition pMaster,
            long pFounderActorId, long pCityId, int pYear, double pWorldTime)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + InstitutionTable +
                                  " (INSTITUTION_ID,INSTITUTION_TYPE,SCHOOL_ID,CITY_ID," +
                                  "FOUNDER_ACTOR_ID,FOUNDING_YEAR,LEVEL,CUSTODIAN_ACTOR_ID," +
                                  "CONDITION,ACTIVE,UPDATED_TIME) VALUES " +
                                  " (@id,@institution,@school,@city,@founder,@year,1,@custodian," +
                                  "100,1,@time)";
            command.Parameters.AddWithValue("@id", pInstitutionId);
            command.Parameters.AddWithValue("@institution", pMaster.InstitutionId);
            command.Parameters.AddWithValue("@school", pMaster.SchoolId);
            command.Parameters.AddWithValue("@city", pCityId);
            command.Parameters.AddWithValue("@founder", pFounderActorId);
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@custodian", pFounderActorId);
            command.Parameters.AddWithValue("@time", FiniteNonNegative(pWorldTime));
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("SchoolInstitution insert failed");
        }

        private static void InsertSchoolEventCommand(SQLiteTransaction pTransaction,
            long pEventId, HistoricalSchoolMasterDefinition pMaster, long pFounderActorId,
            long pCityId, int pYear, long pKingdomId, double pWorldTime)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + EventTable +
                                  " (EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID," +
                                  "CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME) " +
                                  "VALUES (@id,'','institution_founded',@actor,-1,@school,@city," +
                                  "@kingdom,@year,@payload,3,@time)";
            command.Parameters.AddWithValue("@id", pEventId);
            command.Parameters.AddWithValue("@actor", pFounderActorId);
            command.Parameters.AddWithValue("@school", pMaster.SchoolId);
            command.Parameters.AddWithValue("@city", pCityId);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@payload", pMaster.InstitutionId);
            command.Parameters.AddWithValue("@time", FiniteNonNegative(pWorldTime));
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("SchoolEvent institution insert failed");
        }

        private static void InsertDebateCommand(SQLiteTransaction pTransaction, long pDebateId,
            HistoricalSchoolDebateRecord pDebate)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + DebateTable +
                                  " (DEBATE_ID,CITY_ID,DEBATE_YEAR,TOPIC_ID,FIRST_ACTOR_ID," +
                                  "FIRST_SCHOOL_ID,SECOND_ACTOR_ID,SECOND_SCHOOL_ID,SEED," +
                                  "FIRST_SCORE,SECOND_SCORE,RESULT,RESOLVED,PRESENTED,UPDATED_TIME)" +
                                  " VALUES (@id,@city,@year,@topic,@first,@firstSchool,@second," +
                                  "@secondSchool,@seed,@firstScore,@secondScore,@result,@resolved," +
                                  "@presented,@time)";
            command.Parameters.AddWithValue("@id", pDebateId);
            command.Parameters.AddWithValue("@city", pDebate.CityId);
            command.Parameters.AddWithValue("@year", pDebate.DebateYear);
            command.Parameters.AddWithValue("@topic", pDebate.TopicId);
            command.Parameters.AddWithValue("@first", pDebate.FirstActorId);
            command.Parameters.AddWithValue("@firstSchool", pDebate.FirstSchoolId);
            command.Parameters.AddWithValue("@second", pDebate.SecondActorId);
            command.Parameters.AddWithValue("@secondSchool", pDebate.SecondSchoolId);
            command.Parameters.AddWithValue("@seed", pDebate.Seed);
            command.Parameters.AddWithValue("@firstScore", pDebate.FirstScore);
            command.Parameters.AddWithValue("@secondScore", pDebate.SecondScore);
            command.Parameters.AddWithValue("@result", pDebate.Outcome.ToString());
            command.Parameters.AddWithValue("@resolved", pDebate.Resolved ? 1 : 0);
            command.Parameters.AddWithValue("@presented", pDebate.Presented ? 1 : 0);
            command.Parameters.AddWithValue("@time", pDebate.UpdatedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("school debate insert failed");
        }

        private static void InsertDebateEventCommand(SQLiteTransaction pTransaction,
            long pEventId, HistoricalSchoolDebateRecord pDebate, string pSchoolId,
            long pActorId, long pTargetActorId, double pWorldTime)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + EventTable +
                                  " (EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID," +
                                  "CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME)" +
                                  " VALUES (@id,'','debate',@actor,@target,@school,@city,-1,@year," +
                                  "@payload,2,@time)";
            command.Parameters.AddWithValue("@id", pEventId);
            command.Parameters.AddWithValue("@actor", pActorId);
            command.Parameters.AddWithValue("@target", pTargetActorId);
            command.Parameters.AddWithValue("@school", pSchoolId);
            command.Parameters.AddWithValue("@city", pDebate.CityId);
            command.Parameters.AddWithValue("@year", pDebate.DebateYear);
            command.Parameters.AddWithValue("@payload", BuildDebatePayload(pDebate));
            command.Parameters.AddWithValue("@time", FiniteNonNegative(pWorldTime));
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("school debate event insert failed");
        }

        private static void UpsertLedgerCommand(SQLiteTransaction pTransaction, long pCityId,
            string pSchoolId, HistoricalSchoolLedgerDelta pDelta, double pWorldTime,
            double pMembershipDelta = 0d)
        {
            string key = BuildLedgerKey(pCityId, pSchoolId);
            double tradition = 0d;
            double membership = 0d;
            double institutions = 0d;
            double activePresence = 0d;
            double momentum = 0d;
            int lastActiveYear = -1;
            int lastDecayYear = -1;
            bool exists;
            using (var read = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction })
            {
                read.CommandText = "SELECT TRADITION,MEMBERSHIP,INSTITUTIONS,ACTIVE_PRESENCE," +
                                   "MOMENTUM,LAST_ACTIVE_YEAR,LAST_DECAY_YEAR FROM " + LedgerTable +
                                   " WHERE LEDGER_KEY=@key LIMIT 1";
                read.Parameters.AddWithValue("@key", key);
                using SQLiteDataReader reader = read.ExecuteReader();
                exists = reader.Read();
                if (exists)
                {
                    tradition = ClampLedger01(ValueDouble(reader, 0));
                    membership = ClampLedger01(ValueDouble(reader, 1));
                    institutions = ClampLedgerInstitutions(ValueDouble(reader, 2));
                    activePresence = ClampLedger01(ValueDouble(reader, 3));
                    momentum = ClampLedger01(ValueDouble(reader, 4));
                    lastActiveYear = ValueInt(reader, 5, -1);
                    lastDecayYear = ValueInt(reader, 6, -1);
                }
            }

            int effectiveYear = pDelta.LastActiveYear >= 0
                ? pDelta.LastActiveYear
                : CurrentLedgerYear();
            HistoricalSchoolEffectiveLedger effective =
                HistoricalSchoolLedgerDecayRules.Effective(tradition, membership,
                    institutions, activePresence, momentum, lastActiveYear,
                    lastDecayYear, effectiveYear);
            tradition = effective.Tradition;
            membership = effective.Membership;
            institutions = effective.Institutions;
            activePresence = effective.ActivePresence;
            momentum = effective.Momentum;
            lastDecayYear = effective.LastDecayYear;
            tradition = ClampLedger01(tradition + Finite(pDelta.Tradition));
            membership = ClampLedger01(membership + Finite(pMembershipDelta));
            activePresence = ClampLedger01(activePresence + Finite(pDelta.ActivePresence));
            momentum = ClampLedger01(momentum + Finite(pDelta.Momentum));
            institutions = ClampLedgerInstitutions(institutions +
                                                   Math.Max(0d, Math.Min(MaxLedgerInstitutions,
                                                       Finite(pDelta.Institutions))));
            if (pDelta.LastActiveYear >= 0)
                lastActiveYear = Math.Max(lastActiveYear, pDelta.LastActiveYear);
            lastDecayYear = Math.Max(lastDecayYear, effectiveYear);
            double updatedTime = FiniteNonNegative(pWorldTime);

            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            if (exists)
            {
                command.CommandText = "UPDATE " + LedgerTable +
                                      " SET TRADITION=@tradition,MEMBERSHIP=@membership," +
                                      "INSTITUTIONS=@institutions,ACTIVE_PRESENCE=@active," +
                                      "MOMENTUM=@momentum,LAST_ACTIVE_YEAR=@lastActive," +
                                      "LAST_DECAY_YEAR=@lastDecay,UPDATED_TIME=@time" +
                                      " WHERE LEDGER_KEY=@key";
            }
            else
            {
                command.CommandText = "INSERT INTO " + LedgerTable +
                                      " (LEDGER_KEY,CITY_ID,SCHOOL_ID,TRADITION,MEMBERSHIP," +
                                      "INSTITUTIONS,ACTIVE_PRESENCE,MOMENTUM,LAST_ACTIVE_YEAR," +
                                      "LAST_DECAY_YEAR,UPDATED_TIME) VALUES (@key,@city,@school," +
                                      "@tradition,@membership,@institutions,@active,@momentum," +
                                      "@lastActive,@lastDecay,@time)";
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@school", pSchoolId);
            }
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@tradition", tradition);
            command.Parameters.AddWithValue("@membership", membership);
            command.Parameters.AddWithValue("@institutions", institutions);
            command.Parameters.AddWithValue("@active", activePresence);
            command.Parameters.AddWithValue("@momentum", momentum);
            command.Parameters.AddWithValue("@lastActive", lastActiveYear);
            command.Parameters.AddWithValue("@lastDecay", lastDecayYear);
            command.Parameters.AddWithValue("@time", updatedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("city school ledger upsert failed");
        }

        private static bool EventLedgerDelta(string pEventType, int pYear,
            out HistoricalSchoolLedgerDelta pDelta, out double pMembershipDelta)
        {
            pDelta = null;
            pMembershipDelta = 0d;
            switch (pEventType ?? "")
            {
                case "lecture":
                    pDelta = new HistoricalSchoolLedgerDelta(0.005f, 0.01f, 0.02f,
                        0f, pYear);
                    pMembershipDelta = 0.01d;
                    return true;
                case "persuasion":
                    pDelta = new HistoricalSchoolLedgerDelta(0.003f, 0.015f, 0.03f,
                        0f, pYear);
                    pMembershipDelta = 0.01d;
                    return true;
                case "disciple_joined":
                case "school_rediscovery":
                    pDelta = new HistoricalSchoolLedgerDelta(0.01f, 0.02f, 0.015f,
                        0f, pYear);
                    pMembershipDelta = 0.03d;
                    return true;
                case "school_work_authored":
                case "work_authored":
                    pDelta = new HistoricalSchoolLedgerDelta(0.015f, 0.005f, 0.01f,
                        0f, pYear);
                    return true;
                case "school_conversion":
                    pDelta = new HistoricalSchoolLedgerDelta(0f, 0.01f, 0.01f,
                        0f, pYear);
                    pMembershipDelta = 0.01d;
                    return true;
                default:
                    return false;
            }
        }

        private static void UpdateMembershipReputationCommand(SQLiteTransaction pTransaction,
            long pActorId, double pDelta, double pWorldTime)
        {
            if (pActorId < 0 || Math.Abs(pDelta) < 0.0001d) return;
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + MembershipTable +
                                  " SET REPUTATION=MAX(0,MIN(100,REPUTATION+@delta))," +
                                  "UPDATED_TIME=@time WHERE ACTOR_ID=@actor AND ACTIVE=1";
            command.Parameters.AddWithValue("@delta", pDelta);
            command.Parameters.AddWithValue("@time", FiniteNonNegative(pWorldTime));
            command.Parameters.AddWithValue("@actor", pActorId);
            command.ExecuteNonQuery();
        }

        private static double ReputationDelta(SchoolDebateOutcome pOutcome, bool pFirst)
        {
            return HistoricalSchoolDebateRules.ReputationDelta(pOutcome, pFirst);
        }

        private static string ResolveLedgerSchool(HistoricalSchoolLedgerDelta pDelta,
            string pFallback)
        {
            return string.IsNullOrWhiteSpace(pDelta?.SchoolId) ? pFallback ?? "" :
                pDelta.SchoolId;
        }

        private static string BuildLedgerKey(long pCityId, string pSchoolId)
        {
            return pCityId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   (pSchoolId ?? "");
        }

        private static long NextIdInTransaction(SQLiteTransaction pTransaction,
            string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(pTransaction.Connection) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn + "),0)+1 FROM " +
                                  pTable;
            return Convert.ToInt64(command.ExecuteScalar(),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void InvalidateLedgerCaches(long pCityId)
        {
            if (pCityId < 0) return;
            LedgerReadCache.Remove(pCityId, CurrentLedgerYear());
            try
            {
                CitySchoolSnapshotService.MarkDirtyById(pCityId);
                SchoolLandmarkService.MarkDirty(pCityId);
                SchoolMapModeService.DirtyMapIfActive();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore cache invalidation failed: " +
                                    error.Message);
            }
        }

        private static string BuildDebatePayload(HistoricalSchoolDebateRecord pDebate)
        {
            return "topic=" + pDebate.TopicId + ";first_school=" + pDebate.FirstSchoolId +
                   ";second_school=" + pDebate.SecondSchoolId + ";result=" +
                   pDebate.Outcome + ";first_score=" + pDebate.FirstScore.ToString("R") +
                   ";second_score=" + pDebate.SecondScore.ToString("R");
        }

        private static double Finite(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue) ? 0d : pValue;
        }

        private static double FiniteNonNegative(double pValue)
        {
            return Math.Max(0d, Finite(pValue));
        }

        private static double ClampLedger01(double pValue)
        {
            return Math.Max(0d, Math.Min(MaxLedgerValue, Finite(pValue)));
        }

        private static double ClampLedgerInstitutions(double pValue)
        {
            return Math.Max(0d, Math.Min(MaxLedgerInstitutions, Finite(pValue)));
        }

        private static HistoricalSchoolLedgerSnapshot Snapshot(string pSchoolId,
            HistoricalSchoolEffectiveLedger pEffective)
        {
            return new HistoricalSchoolLedgerSnapshot(pSchoolId,
                (float)ClampLedger01(pEffective.Tradition),
                (float)ClampLedger01(pEffective.ActivePresence),
                (float)ClampLedger01(pEffective.Momentum), pEffective.LastActiveYear,
                (float)ClampLedger01(pEffective.Membership),
                (float)ClampLedgerInstitutions(pEffective.Institutions));
        }

        private static int CurrentLedgerYear()
        {
            try { return Math.Max(0, Date.getCurrentYear()); }
            catch { return 0; }
        }

        private static long ValueLong(SQLiteDataReader pReader, int pIndex, long pFallback = 0L)
        {
            return pReader.IsDBNull(pIndex) ? pFallback : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static int ValueInt(SQLiteDataReader pReader, int pIndex, int pFallback = 0)
        {
            return pReader.IsDBNull(pIndex) ? pFallback : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static double ValueDouble(SQLiteDataReader pReader, int pIndex,
            double pFallback = 0d)
        {
            return pReader.IsDBNull(pIndex) ? pFallback : Convert.ToDouble(pReader.GetValue(pIndex));
        }

        private static string ValueString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? "";
        }

        private sealed class HistoricalSchoolAffiliationDeathRow
        {
            public HistoricalSchoolAffiliationDeathRow(
                HistoricalSchoolAffiliationSnapshot pState, double pUpdatedTime)
            {
                State = pState;
                UpdatedTime = pUpdatedTime;
            }

            public HistoricalSchoolAffiliationSnapshot State { get; }
            public double UpdatedTime { get; }
        }

        private sealed class HistoricalSchoolMasterDeathRow
        {
            private HistoricalSchoolMasterDeathRow(string pMasterId, long pActorId,
                string pSchoolId, string pCanonicalName, bool pSpawned, bool pDead,
                long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId,
                int pSpawnYear, int pDeathYear, string pLifecycleState, string pDeathCause,
                long pDeathCityId, double pUpdatedTime)
            {
                MasterId = pMasterId;
                ActorId = pActorId;
                SchoolId = pSchoolId;
                CanonicalName = pCanonicalName;
                Spawned = pSpawned;
                Dead = pDead;
                HomeKingdomId = pHomeKingdomId;
                HomeKingdomName = pHomeKingdomName;
                HometownCityId = pHometownCityId;
                SpawnYear = pSpawnYear;
                DeathYear = pDeathYear;
                LifecycleState = pLifecycleState;
                DeathCause = pDeathCause;
                DeathCityId = pDeathCityId;
                UpdatedTime = pUpdatedTime;
            }

            public string MasterId { get; }
            public long ActorId { get; }
            public string SchoolId { get; }
            private string CanonicalName { get; }
            public bool Spawned { get; }
            public bool Dead { get; }
            private long HomeKingdomId { get; }
            private string HomeKingdomName { get; }
            private long HometownCityId { get; }
            private int SpawnYear { get; }
            private int DeathYear { get; }
            private string LifecycleState { get; }
            private string DeathCause { get; }
            private long DeathCityId { get; }
            private double UpdatedTime { get; }

            public static HistoricalSchoolMasterDeathRow Read(SQLiteDataReader pReader)
            {
                return new HistoricalSchoolMasterDeathRow(ValueString(pReader, 0),
                    ValueLong(pReader, 1, -1L), ValueString(pReader, 2),
                    ValueString(pReader, 3), ValueInt(pReader, 4) == 1,
                    ValueInt(pReader, 5) == 1, ValueLong(pReader, 6, -1L),
                    ValueString(pReader, 7), ValueLong(pReader, 8, -1L),
                    ValueInt(pReader, 9, -1), ValueInt(pReader, 10, -1),
                    ValueString(pReader, 11), ValueString(pReader, 12),
                    ValueLong(pReader, 13, -1L), ValueDouble(pReader, 14, -1d));
            }

            public bool MatchesExact(HistoricalSchoolMasterDeathRow pExpected)
            {
                return pExpected != null && MasterId == pExpected.MasterId &&
                       ActorId == pExpected.ActorId && SchoolId == pExpected.SchoolId &&
                       CanonicalName == pExpected.CanonicalName &&
                       Spawned == pExpected.Spawned && Dead == pExpected.Dead &&
                       HomeKingdomId == pExpected.HomeKingdomId &&
                       HomeKingdomName == pExpected.HomeKingdomName &&
                       HometownCityId == pExpected.HometownCityId &&
                       SpawnYear == pExpected.SpawnYear && DeathYear == pExpected.DeathYear &&
                       LifecycleState == pExpected.LifecycleState &&
                       DeathCause == pExpected.DeathCause &&
                       DeathCityId == pExpected.DeathCityId &&
                       UpdatedTime.Equals(pExpected.UpdatedTime);
            }

            public bool MatchesCommittedDeath(HistoricalSchoolMasterDeathRow pOriginal,
                int pYear, long pDeathCityId, string pCause, double pTime)
            {
                return pOriginal != null && MasterId == pOriginal.MasterId &&
                       ActorId == pOriginal.ActorId && SchoolId == pOriginal.SchoolId &&
                       CanonicalName == pOriginal.CanonicalName && Spawned && Dead &&
                       HomeKingdomId == pOriginal.HomeKingdomId &&
                       HomeKingdomName == pOriginal.HomeKingdomName &&
                       HometownCityId == pOriginal.HometownCityId &&
                       SpawnYear == pOriginal.SpawnYear && DeathYear == pYear &&
                       LifecycleState == HistoricalSchoolLifecycleState.Dead.ToString() &&
                       DeathCause == pCause && DeathCityId == pDeathCityId &&
                       UpdatedTime.Equals(pTime);
            }

            public bool MatchesAuthoritativeCommittedDeath(SchoolMembershipRecord pMembership,
                int pYear, long pDeathCityId, string pCause, double pTime)
            {
                return pMembership != null && MasterId == pMembership.SourceId &&
                       ActorId == pMembership.ActorId && SchoolId == pMembership.SchoolId &&
                       Spawned && Dead && DeathYear == pYear &&
                       LifecycleState == HistoricalSchoolLifecycleState.Dead.ToString() &&
                       DeathCause == pCause && DeathCityId == pDeathCityId &&
                       UpdatedTime.Equals(pTime);
            }

            public bool MatchesAuthoritativeOriginal(SchoolMembershipRecord pMembership)
            {
                return pMembership != null && MasterId == pMembership.SourceId &&
                       ActorId == pMembership.ActorId && SchoolId == pMembership.SchoolId &&
                       Spawned && !Dead && DeathYear == -1 &&
                       LifecycleState != HistoricalSchoolLifecycleState.Dead.ToString() &&
                       DeathCause == "" && DeathCityId == -1L;
            }
        }
    }

    internal sealed class HistoricalSchoolMasterStoreRecord
    {
        public HistoricalSchoolMasterStoreRecord(string pMasterId, long pActorId, bool pSpawned,
            bool pDead, long pHomeKingdomId, long pHometownCityId, int pSpawnYear,
            long pLineageId, long pShiId, double pCreatedTime)
        {
            MasterId = pMasterId ?? "";
            ActorId = pActorId;
            Spawned = pSpawned;
            Dead = pDead;
            HomeKingdomId = pHomeKingdomId;
            HometownCityId = pHometownCityId;
            SpawnYear = pSpawnYear;
            LineageId = pLineageId;
            ShiId = pShiId;
            CreatedTime = pCreatedTime;
        }

        public string MasterId { get; }
        public long ActorId { get; }
        public bool Spawned { get; }
        public bool Dead { get; }
        public long HomeKingdomId { get; }
        public long HometownCityId { get; }
        public int SpawnYear { get; }
        public long LineageId { get; }
        public long ShiId { get; }
        public double CreatedTime { get; }
    }
}
