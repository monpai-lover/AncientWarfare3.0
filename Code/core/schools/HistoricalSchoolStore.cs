using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
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

        public static long NextMembershipId()
        {
            return DB == null ? -1L : TableIdAllocator.Next(DB, MembershipTable,
                "MEMBERSHIP_ID");
        }

        public static bool RecordSchoolEvent(string pEventType, long pActorId,
            long pTargetActorId, string pSchoolId, long pCityId, long pKingdomId, int pYear,
            string pPayload, int pImportance, double pWorldTime)
        {
            if (DB == null || string.IsNullOrWhiteSpace(pEventType) || pActorId < 0) return false;
            long eventId = TableIdAllocator.Next(DB, EventTable, "EVENT_ID");
            if (eventId < 0) return false;
            try
            {
                DB.Insert(EventTable,
                    ColumnVal.Create("EVENT_ID", eventId),
                    ColumnVal.Create("EVENT_TYPE", pEventType),
                    ColumnVal.Create("ACTOR_ID", pActorId),
                    ColumnVal.Create("TARGET_ACTOR_ID", pTargetActorId),
                    ColumnVal.Create("SCHOOL_ID", pSchoolId ?? ""),
                    ColumnVal.Create("CITY_ID", pCityId),
                    ColumnVal.Create("KINGDOM_ID", pKingdomId),
                    ColumnVal.Create("EVENT_YEAR", pYear),
                    ColumnVal.Create("PAYLOAD", pPayload ?? ""),
                    ColumnVal.Create("IMPORTANCE", Math.Max(0, pImportance)),
                    ColumnVal.Create("WORLD_TIME", pWorldTime));
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore insert event failed: " +
                                    error.Message);
                return false;
            }
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

        public static bool MarkAffiliationDead(long pActorId, double pTime)
        {
            if (DB == null || pActorId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + AffiliationTable +
                    " SET LIFECYCLE_STATE=@state,SERVICE_KINGDOM_ID=-1," +
                    "DESTINATION_CITY_ID=-1,UPDATED_TIME=@time WHERE ACTOR_ID=@actor";
                command.Parameters.AddWithValue("@state",
                    HistoricalSchoolLifecycleState.Dead.ToString());
                command.Parameters.AddWithValue("@time", pTime);
                command.Parameters.AddWithValue("@actor", pActorId);
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore mark affiliation dead failed: " +
                                    error.Message);
                return false;
            }
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
                        " (EVENT_ID,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID,CITY_ID," +
                        "KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME) VALUES " +
                        " (@id,'work_authored',@actor,-1,@school,@city,@kingdom,@year,@payload,2,@time)";
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
                transaction.Commit();
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

        public static List<SchoolMembershipRecord> LoadActiveMemberships()
        {
            var result = new List<SchoolMembershipRecord>();
            if (DB == null) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT MEMBERSHIP_ID, ACTOR_ID, SCHOOL_ID, " +
                    "SOURCE_TYPE, SOURCE_ID, TEACHER_ACTOR_ID, CITY_ID, GENERATION, " +
                    "REPUTATION, START_YEAR FROM " + MembershipTable +
                    " WHERE ACTIVE=1 ORDER BY ACTOR_ID, START_YEAR DESC, MEMBERSHIP_ID DESC";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (!Enum.TryParse(ValueString(reader, 3), ignoreCase: false,
                            out SchoolMembershipSource source)) continue;
                    result.Add(new SchoolMembershipRecord(ValueLong(reader, 0),
                        ValueLong(reader, 1), ValueString(reader, 2), source,
                        ValueString(reader, 4), ValueLong(reader, 5, -1),
                        ValueLong(reader, 6, -1), ValueInt(reader, 7),
                        (float)ValueDouble(reader, 8), ValueInt(reader, 9)));
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
            try
            {
                DB.Insert(MembershipTable,
                    ColumnVal.Create("MEMBERSHIP_ID", pRecord.MembershipId),
                    ColumnVal.Create("ACTOR_ID", pRecord.ActorId),
                    ColumnVal.Create("SCHOOL_ID", pRecord.SchoolId),
                    ColumnVal.Create("SOURCE_TYPE", pRecord.Source.ToString()),
                    ColumnVal.Create("SOURCE_ID", pRecord.SourceId),
                    ColumnVal.Create("TEACHER_ACTOR_ID", pRecord.TeacherActorId),
                    ColumnVal.Create("CITY_ID", pRecord.CityId),
                    ColumnVal.Create("GENERATION", pRecord.Generation),
                    ColumnVal.Create("REPUTATION", (double)pRecord.Reputation),
                    ColumnVal.Create("START_YEAR", pRecord.StartYear),
                    ColumnVal.Create("END_YEAR", -1),
                    ColumnVal.Create("ACTIVE", 1),
                    ColumnVal.Create("END_REASON", ""),
                    ColumnVal.Create("UPDATED_TIME", pTime));
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore insert membership failed: " +
                                    error.Message);
                return false;
            }
        }

        public static bool ConvertMembership(SchoolMembershipRecord pCurrent,
            SchoolMembershipRecord pReplacement, int pYear, double pTime)
        {
            if (DB == null || pCurrent == null || pReplacement == null ||
                !pReplacement.IsValid) return false;
            using SQLiteTransaction transaction = DB.BeginTransaction();
            try
            {
                CloseMembershipCommand(pCurrent.MembershipId, pYear, "converted", pTime,
                    transaction);
                InsertMembershipCommand(pReplacement, pTime, transaction);
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore convert membership failed: " +
                                    error.Message);
                return false;
            }
        }

        public static bool CloseMembership(SchoolMembershipRecord pCurrent, int pYear,
            string pReason, double pTime)
        {
            if (DB == null || pCurrent == null) return false;
            try
            {
                CloseMembershipCommand(pCurrent.MembershipId, pYear, pReason, pTime, null);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore close membership failed: " +
                                    error.Message);
                return false;
            }
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
                    "HOME_KINGDOM_ID,HOMETOWN_CITY_ID,SPAWN_YEAR FROM " + MasterTable +
                    " WHERE SPAWNED=1";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                    result.Add(new HistoricalSchoolMasterStoreRecord(ValueString(reader, 0),
                        ValueLong(reader, 1, -1), ValueInt(reader, 2) != 0,
                        ValueInt(reader, 3) != 0, ValueLong(reader, 4, -1),
                        ValueLong(reader, 5, -1), ValueInt(reader, 6, -1)));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load masters failed: " + error.Message);
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
                    "TRANSPORT_FAILURES=@failures,UPDATED_TIME=@time WHERE ACTOR_ID=@actor";
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
                return command.ExecuteNonQuery() == 1;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore save affiliation failed: " +
                                    error.Message);
                return false;
            }
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

        public static void SaveRuntimeState(int pEligibleYear, int pLastWorldYear, double pTime)
        {
            if (DB == null) return;
            if (DB.CheckKeyExist(RuntimeTable,
                    SimpleColumnConstraint.CreateEq("STATE_ID", 1L)))
            {
                DB.UpdateValue(RuntimeTable,
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("STATE_ID", 1L)
                    },
                    ColumnVal.Create("ELIGIBLE_YEAR", Math.Max(0, pEligibleYear)),
                    ColumnVal.Create("LAST_WORLD_YEAR", pLastWorldYear),
                    ColumnVal.Create("UPDATED_TIME", pTime));
                return;
            }
            DB.Insert(RuntimeTable, ColumnVal.Create("STATE_ID", 1L),
                ColumnVal.Create("ELIGIBLE_YEAR", Math.Max(0, pEligibleYear)),
                ColumnVal.Create("LAST_WORLD_YEAR", pLastWorldYear),
                ColumnVal.Create("UPDATED_TIME", pTime));
        }

        public static bool TryRecordDescent(HistoricalSchoolMasterDefinition pMaster,
            long pActorId, long pHomeKingdomId, string pHomeKingdomName, long pHometownCityId,
            int pYear, double pTime)
        {
            if (DB == null || pMaster == null || pActorId < 0 || pHomeKingdomId < 0 ||
                pHometownCityId < 0) return false;
            using SQLiteTransaction transaction = DB.BeginTransaction();
            try
            {
                using (var master = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    master.CommandText = "INSERT INTO " + MasterTable +
                        " (MASTER_ID,ACTOR_ID,SCHOOL_ID,CANONICAL_NAME,SPAWNED,DEAD," +
                        "HOME_KINGDOM_ID,HOME_KINGDOM_NAME,HOMETOWN_CITY_ID,SPAWN_YEAR," +
                        "DEATH_YEAR,LIFECYCLE_STATE,DEATH_CAUSE,DEATH_CITY_ID,UPDATED_TIME)" +
                        " VALUES (@master,@actor,@school,@name,1,0,@kingdom,@kingdomName," +
                        "@city,@year,-1,@state,'',-1,@time)";
                    master.Parameters.AddWithValue("@master", pMaster.Id);
                    master.Parameters.AddWithValue("@actor", pActorId);
                    master.Parameters.AddWithValue("@school", pMaster.SchoolId);
                    master.Parameters.AddWithValue("@name", pMaster.CanonicalName);
                    master.Parameters.AddWithValue("@kingdom", pHomeKingdomId);
                    master.Parameters.AddWithValue("@kingdomName", pHomeKingdomName ?? "");
                    master.Parameters.AddWithValue("@city", pHometownCityId);
                    master.Parameters.AddWithValue("@year", pYear);
                    master.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.AtHome.ToString());
                    master.Parameters.AddWithValue("@time", pTime);
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
                    affiliation.Parameters.AddWithValue("@actor", pActorId);
                    affiliation.Parameters.AddWithValue("@kingdom", pHomeKingdomId);
                    affiliation.Parameters.AddWithValue("@kingdomName", pHomeKingdomName ?? "");
                    affiliation.Parameters.AddWithValue("@city", pHometownCityId);
                    affiliation.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.AtHome.ToString());
                    affiliation.Parameters.AddWithValue("@time", pTime);
                    if (affiliation.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("master affiliation insert failed");
                }
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore record descent failed: " +
                                    error.Message);
                return false;
            }
        }

        public static void MarkMasterDead(string pMasterId, long pActorId, int pYear,
            long pCityId, string pCause, double pTime)
        {
            if (DB == null || string.IsNullOrEmpty(pMasterId)) return;
            try
            {
                using var transaction = DB.BeginTransaction();
                using (var master = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    master.CommandText = "UPDATE " + MasterTable +
                        " SET DEAD=1,DEATH_YEAR=@year,DEATH_CITY_ID=@city,DEATH_CAUSE=@cause," +
                        "LIFECYCLE_STATE=@state,UPDATED_TIME=@time WHERE MASTER_ID=@master" +
                        " AND ACTOR_ID=@actor AND SPAWNED=1";
                    master.Parameters.AddWithValue("@year", pYear);
                    master.Parameters.AddWithValue("@city", pCityId);
                    master.Parameters.AddWithValue("@cause", pCause ?? "death");
                    master.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.Dead.ToString());
                    master.Parameters.AddWithValue("@time", pTime);
                    master.Parameters.AddWithValue("@master", pMasterId);
                    master.Parameters.AddWithValue("@actor", pActorId);
                    master.ExecuteNonQuery();
                }
                using (var affiliation = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    affiliation.CommandText = "UPDATE " + AffiliationTable +
                        " SET LIFECYCLE_STATE=@state,SERVICE_KINGDOM_ID=-1," +
                        "DESTINATION_CITY_ID=-1,UPDATED_TIME=@time WHERE ACTOR_ID=@actor";
                    affiliation.Parameters.AddWithValue("@state",
                        HistoricalSchoolLifecycleState.Dead.ToString());
                    affiliation.Parameters.AddWithValue("@time", pTime);
                    affiliation.Parameters.AddWithValue("@actor", pActorId);
                    affiliation.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore mark death failed: " + error.Message);
            }
        }

        public static void RollbackDescent(string pMasterId, long pActorId)
        {
            if (DB == null || string.IsNullOrEmpty(pMasterId) || pActorId < 0) return;
            using SQLiteTransaction transaction = DB.BeginTransaction();
            try
            {
                using (var affiliation = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    affiliation.CommandText = "DELETE FROM " + AffiliationTable +
                                              " WHERE ACTOR_ID=@actor";
                    affiliation.Parameters.AddWithValue("@actor", pActorId);
                    affiliation.ExecuteNonQuery();
                }
                using (var master = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    master.CommandText = "DELETE FROM " + MasterTable +
                                         " WHERE MASTER_ID=@master AND ACTOR_ID=@actor";
                    master.Parameters.AddWithValue("@master", pMasterId);
                    master.Parameters.AddWithValue("@actor", pActorId);
                    master.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore rollback descent failed: " +
                                    error.Message);
            }
        }

        private static void InsertMembershipCommand(SchoolMembershipRecord pRecord, double pTime,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + MembershipTable +
                " (MEMBERSHIP_ID,ACTOR_ID,SCHOOL_ID,SOURCE_TYPE,SOURCE_ID,TEACHER_ACTOR_ID," +
                "CITY_ID,GENERATION,REPUTATION,START_YEAR,END_YEAR,ACTIVE,END_REASON,UPDATED_TIME)" +
                " VALUES (@id,@actor,@school,@source,@sourceId,@teacher,@city,@generation," +
                "@reputation,@start,-1,1,'',@time)";
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
            command.ExecuteNonQuery();
        }

        private static void CloseMembershipCommand(long pMembershipId, int pYear, string pReason,
            double pTime, SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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
    }

    internal sealed class HistoricalSchoolMasterStoreRecord
    {
        public HistoricalSchoolMasterStoreRecord(string pMasterId, long pActorId, bool pSpawned,
            bool pDead, long pHomeKingdomId, long pHometownCityId, int pSpawnYear)
        {
            MasterId = pMasterId ?? "";
            ActorId = pActorId;
            Spawned = pSpawned;
            Dead = pDead;
            HomeKingdomId = pHomeKingdomId;
            HometownCityId = pHometownCityId;
            SpawnYear = pSpawnYear;
        }

        public string MasterId { get; }
        public long ActorId { get; }
        public bool Spawned { get; }
        public bool Dead { get; }
        public long HomeKingdomId { get; }
        public long HometownCityId { get; }
        public int SpawnYear { get; }
    }
}
