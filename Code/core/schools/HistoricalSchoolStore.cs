using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolStore
    {
        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static string MembershipTable => SchoolMembershipTableItem.GetTableName();

        public static long NextMembershipId()
        {
            return DB == null ? -1L : TableIdAllocator.Next(DB, MembershipTable,
                "MEMBERSHIP_ID");
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
}
