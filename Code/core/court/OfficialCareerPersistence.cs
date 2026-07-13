using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.court
{
    internal sealed class OfficialCareerAppointment
    {
        public long ActorId;
        public string ActorName = "";
        public long KingdomId;
        public long CityId;
        public string Layer = "";
        public string OfficeId = "";
        public string SchoolId = "";
        public double Influence;
        public int AppointedYear;
        public double AppointedTime;
    }

    internal static class OfficialCareerPersistence
    {
        private sealed class ActiveCareer
        {
            public long OfficerId;
            public long KingdomId;
            public long ActorId;
            public string ActorName = "";
            public long CityId;
            public string Layer = "";
            public string OfficeId = "";
            public string SchoolId = "";
            public double Influence;
            public int AppointedYear;
            public double AppointedTime;
            public int EndedYear;
            public double EndedTime;
            public int Active;
            public string EndReason = "";
            public double UpdatedTime;

            public ActiveCareer Copy()
            {
                return (ActiveCareer)MemberwiseClone();
            }

            public bool Exact(ActiveCareer pOther, bool pRequireOfficerId = true)
            {
                if (pOther == null) return false;
                return (!pRequireOfficerId || OfficerId == pOther.OfficerId) &&
                       KingdomId == pOther.KingdomId && ActorId == pOther.ActorId &&
                       ActorName == pOther.ActorName && CityId == pOther.CityId &&
                       Layer == pOther.Layer && OfficeId == pOther.OfficeId &&
                       SchoolId == pOther.SchoolId && Influence.Equals(pOther.Influence) &&
                       AppointedYear == pOther.AppointedYear &&
                       AppointedTime.Equals(pOther.AppointedTime) &&
                       EndedYear == pOther.EndedYear && EndedTime.Equals(pOther.EndedTime) &&
                       Active == pOther.Active && EndReason == pOther.EndReason &&
                       UpdatedTime.Equals(pOther.UpdatedTime);
            }
        }

        public static OfficialCareerAppointmentResult Appoint(SQLiteConnection pDb,
            OfficialCareerAppointment pAppointment)
        {
            if (pDb == null || pAppointment == null || pAppointment.ActorId < 0 ||
                pAppointment.KingdomId < 0)
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.CleanFailure,
                    OfficialCareerMutation.Started);

            string table = CourtOfficerTableItem.GetTableName();
            SQLiteTransaction transaction = null;
            ActiveCareer original = null;
            ActiveCareer desired = null;
            bool originalCaptured = false;
            OfficialCareerMutation mutation = OfficialCareerMutation.Started;
            try
            {
                transaction = pDb.BeginTransaction();
                original = ReadActive(pDb, transaction, table, pAppointment.ActorId,
                    pAppointment.Layer);
                originalCaptured = true;
                bool insert = CourtOfficerRecordRules.ShouldInsertNewActiveRecord(
                    original != null,
                    original != null && original.KingdomId == pAppointment.KingdomId,
                    original != null && original.OfficeId == pAppointment.OfficeId,
                    original != null && original.Layer == pAppointment.Layer,
                    original != null && original.CityId == pAppointment.CityId);

                if (!insert)
                {
                    mutation = OfficialCareerMutation.Refreshed;
                    desired = Refreshed(original, pAppointment);
                    UpdateSnapshot(pDb, transaction, table, desired);
                }
                else
                {
                    mutation = original == null
                        ? OfficialCareerMutation.Started
                        : OfficialCareerMutation.Reassigned;
                    desired = NewActive(pAppointment);
                    if (original != null)
                        EndOriginal(pDb, transaction, table, original,
                            pAppointment.AppointedYear, pAppointment.AppointedTime);
                    desired.OfficerId = NextOfficerId(pDb, transaction, table);
                    InsertActive(pDb, transaction, table, desired);
                }

                transaction.Commit();
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.Committed, mutation);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Official career appointment transaction failed: " +
                                    error.Message);
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            if (!originalCaptured || desired == null)
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.Unknown, mutation);

            try
            {
                List<ActiveCareer> active = ReadActiveRows(pDb, null, table,
                    pAppointment.ActorId, pAppointment.Layer);
                ActiveCareer authoritative = active.Count == 1 ? active[0] : null;
                OfficialCareerPersistenceOutcome outcome = OfficialCareerReadbackRules.Resolve(
                    pQuerySucceeded: true, active.Count,
                    pDesiredExact: authoritative != null &&
                                   authoritative.Exact(desired,
                                       pRequireOfficerId: desired.OfficerId >= 0),
                    pOriginalExisted: original != null,
                    pOriginalExact: authoritative != null &&
                                    authoritative.Exact(original));
                return new OfficialCareerAppointmentResult(outcome, mutation);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Official career appointment readback failed: " +
                                    error.Message);
                return new OfficialCareerAppointmentResult(
                    OfficialCareerReadbackRules.Resolve(pQuerySucceeded: false, -1,
                        pDesiredExact: false, pOriginalExisted: original != null,
                        pOriginalExact: false), mutation);
            }
        }

        private static ActiveCareer ReadActive(SQLiteConnection pDb,
            SQLiteTransaction transaction, string pTable, long pActorId, string pLayer)
        {
            List<ActiveCareer> rows = ReadActiveRows(pDb, transaction, pTable, pActorId,
                pLayer);
            if (rows.Count > 1)
                throw new InvalidOperationException("multiple active careers for actor layer");
            return rows.Count == 0 ? null : rows[0];
        }

        private static List<ActiveCareer> ReadActiveRows(SQLiteConnection pDb,
            SQLiteTransaction transaction, string pTable, long pActorId, string pLayer)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = transaction };
            command.CommandText = "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,CITY_ID," +
                "LAYER,OFFICE_ID,SCHOOL_ID,INFLUENCE,APPOINTED_YEAR,APPOINTED_TIME," +
                "ENDED_YEAR,ENDED_TIME,ACTIVE,END_REASON,UPDATED_TIME FROM " + pTable +
                " WHERE ACTOR_ID=@actor AND LAYER=@layer AND ACTIVE=1";
            command.Parameters.AddWithValue("@actor", pActorId);
            command.Parameters.AddWithValue("@layer", pLayer ?? "");
            var rows = new List<ActiveCareer>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(ReadRow(reader));
            return rows;
        }

        private static ActiveCareer ReadRow(SQLiteDataReader pReader)
        {
            return new ActiveCareer
            {
                OfficerId = Long(pReader, 0, -1L),
                KingdomId = Long(pReader, 1, -1L),
                ActorId = Long(pReader, 2, -1L),
                ActorName = Text(pReader, 3),
                CityId = Long(pReader, 4, -1L),
                Layer = Text(pReader, 5),
                OfficeId = Text(pReader, 6),
                SchoolId = Text(pReader, 7),
                Influence = Double(pReader, 8, 0d),
                AppointedYear = Int(pReader, 9, -1),
                AppointedTime = Double(pReader, 10, -1d),
                EndedYear = Int(pReader, 11, -1),
                EndedTime = Double(pReader, 12, -1d),
                Active = Int(pReader, 13, 0),
                EndReason = Text(pReader, 14),
                UpdatedTime = Double(pReader, 15, -1d)
            };
        }

        private static ActiveCareer Refreshed(ActiveCareer pOriginal,
            OfficialCareerAppointment pAppointment)
        {
            ActiveCareer desired = pOriginal.Copy();
            desired.ActorName = pAppointment.ActorName;
            desired.SchoolId = pAppointment.SchoolId;
            desired.Influence = pAppointment.Influence;
            desired.UpdatedTime = pAppointment.AppointedTime;
            return desired;
        }

        private static ActiveCareer NewActive(OfficialCareerAppointment pAppointment)
        {
            return new ActiveCareer
            {
                OfficerId = -1L,
                KingdomId = pAppointment.KingdomId,
                ActorId = pAppointment.ActorId,
                ActorName = pAppointment.ActorName,
                CityId = pAppointment.CityId,
                Layer = pAppointment.Layer,
                OfficeId = pAppointment.OfficeId,
                SchoolId = pAppointment.SchoolId,
                Influence = pAppointment.Influence,
                AppointedYear = pAppointment.AppointedYear,
                AppointedTime = pAppointment.AppointedTime,
                EndedYear = -1,
                EndedTime = -1d,
                Active = CourtOfficerRecordRules.ActiveFlag(true),
                EndReason = "",
                UpdatedTime = pAppointment.AppointedTime
            };
        }

        private static void UpdateSnapshot(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, ActiveCareer pDesired)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + pTable +
                " SET ACTOR_NAME=@name,SCHOOL_ID=@school,INFLUENCE=@influence," +
                "UPDATED_TIME=@time WHERE OFFICER_ID=@id AND ACTOR_ID=@actor" +
                " AND LAYER=@layer AND ACTIVE=1";
            command.Parameters.AddWithValue("@name", pDesired.ActorName);
            command.Parameters.AddWithValue("@school", pDesired.SchoolId);
            command.Parameters.AddWithValue("@influence", pDesired.Influence);
            command.Parameters.AddWithValue("@time", pDesired.UpdatedTime);
            command.Parameters.AddWithValue("@id", pDesired.OfficerId);
            command.Parameters.AddWithValue("@actor", pDesired.ActorId);
            command.Parameters.AddWithValue("@layer", pDesired.Layer);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("active career refresh failed");
        }

        private static void EndOriginal(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, ActiveCareer pOriginal,
            int pYear, double pTime)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + pTable +
                " SET ACTIVE=0,ENDED_YEAR=@year,ENDED_TIME=@time,END_REASON='reassigned'," +
                "UPDATED_TIME=@time WHERE OFFICER_ID=@id AND ACTOR_ID=@actor" +
                " AND LAYER=@layer AND ACTIVE=1";
            command.Parameters.AddWithValue("@year", pYear);
            command.Parameters.AddWithValue("@time", pTime);
            command.Parameters.AddWithValue("@id", pOriginal.OfficerId);
            command.Parameters.AddWithValue("@actor", pOriginal.ActorId);
            command.Parameters.AddWithValue("@layer", pOriginal.Layer);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("active career close failed");
        }

        private static long NextOfficerId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(OFFICER_ID),0)+1 FROM " + pTable;
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 1L : Convert.ToInt64(value);
        }

        private static void InsertActive(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, ActiveCareer pCareer)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + pTable +
                " (OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,CITY_ID,LAYER,OFFICE_ID," +
                "SCHOOL_ID,INFLUENCE,APPOINTED_YEAR,APPOINTED_TIME,ENDED_YEAR,ENDED_TIME," +
                "ACTIVE,END_REASON,UPDATED_TIME) VALUES (@id,@kingdom,@actor,@name,@city," +
                "@layer,@office,@school,@influence,@year,@time,-1,-1,1,'',@time)";
            command.Parameters.AddWithValue("@id", pCareer.OfficerId);
            command.Parameters.AddWithValue("@kingdom", pCareer.KingdomId);
            command.Parameters.AddWithValue("@actor", pCareer.ActorId);
            command.Parameters.AddWithValue("@name", pCareer.ActorName);
            command.Parameters.AddWithValue("@city", pCareer.CityId);
            command.Parameters.AddWithValue("@layer", pCareer.Layer);
            command.Parameters.AddWithValue("@office", pCareer.OfficeId);
            command.Parameters.AddWithValue("@school", pCareer.SchoolId);
            command.Parameters.AddWithValue("@influence", pCareer.Influence);
            command.Parameters.AddWithValue("@year", pCareer.AppointedYear);
            command.Parameters.AddWithValue("@time", pCareer.AppointedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("active career insert failed");
        }

        private static long Long(SQLiteDataReader pReader, int pOrdinal, long pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToInt64(pReader.GetValue(pOrdinal));
        }

        private static int Int(SQLiteDataReader pReader, int pOrdinal, int pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToInt32(pReader.GetValue(pOrdinal));
        }

        private static double Double(SQLiteDataReader pReader, int pOrdinal, double pDefault)
        {
            return pReader.IsDBNull(pOrdinal)
                ? pDefault
                : Convert.ToDouble(pReader.GetValue(pOrdinal));
        }

        private static string Text(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal)
                ? ""
                : pReader.GetValue(pOrdinal)?.ToString() ?? "";
        }
    }
}
