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

    internal sealed class OfficialCareerRecord
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

        public OfficialCareerRecord Copy()
        {
            return (OfficialCareerRecord)MemberwiseClone();
        }

        public OfficialCareerPrior ToPrior()
        {
            return new OfficialCareerPrior(KingdomId, CityId, Layer, OfficeId);
        }

        public bool Exact(OfficialCareerRecord pOther, bool pRequireOfficerId = true)
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

    internal sealed class OfficialCareerPersistenceToken
    {
        public OfficialCareerPersistenceToken(OfficialCareerAppointment pAppointment,
            OfficialCareerRecord pOriginal, OfficialCareerRecord pDesired,
            OfficialCareerMutation pMutation)
        {
            Appointment = pAppointment ?? throw new ArgumentNullException(nameof(pAppointment));
            Original = pOriginal;
            Desired = pDesired ?? throw new ArgumentNullException(nameof(pDesired));
            Mutation = pMutation;
        }

        public OfficialCareerAppointment Appointment { get; }
        public OfficialCareerRecord Original { get; }
        public OfficialCareerRecord Desired { get; }
        public OfficialCareerMutation Mutation { get; }
        public OfficialCareerPrior Prior => Original?.ToPrior();
    }

    internal sealed class OfficialCareerCloseRequest
    {
        public OfficialCareerCloseRequest(long pActorId, long pKingdomId, string pLayer,
            string pOfficeId, int pEndedYear, double pEndedTime, string pEndReason)
        {
            ActorId = pActorId;
            KingdomId = pKingdomId;
            Layer = pLayer ?? "";
            OfficeId = pOfficeId;
            EndedYear = pEndedYear;
            EndedTime = pEndedTime;
            EndReason = pEndReason ?? "";
        }

        public long ActorId { get; }
        public long KingdomId { get; }
        public string Layer { get; }
        public string OfficeId { get; }
        public int EndedYear { get; }
        public double EndedTime { get; }
        public string EndReason { get; }
    }

    internal sealed class OfficialCareerCloseToken
    {
        public OfficialCareerCloseToken(OfficialCareerCloseRequest pRequest,
            OfficialCareerRecord pOriginal, OfficialCareerRecord pDesired)
        {
            Request = pRequest ?? throw new ArgumentNullException(nameof(pRequest));
            Original = pOriginal ?? throw new ArgumentNullException(nameof(pOriginal));
            Desired = pDesired ?? throw new ArgumentNullException(nameof(pDesired));
        }

        public OfficialCareerCloseRequest Request { get; }
        public OfficialCareerRecord Original { get; }
        public OfficialCareerRecord Desired { get; }
        public OfficialCareerPrior Prior => Original.ToPrior();
    }

    internal static class OfficialCareerPersistence
    {
        internal static OfficialCareerCloseResult Close(SQLiteConnection pDb,
            OfficialCareerCloseRequest pRequest)
        {
            if (pDb == null || !ValidCloseRequest(pRequest))
                return new OfficialCareerCloseResult(
                    OfficialCareerPersistenceOutcome.CleanFailure, null);

            SQLiteTransaction transaction = null;
            OfficialCareerCloseToken token = null;
            try
            {
                transaction = pDb.BeginTransaction();
                token = CaptureClose(pDb, transaction, pRequest);
                StageClose(pDb, transaction, token);
                transaction.Commit();
                return ResultForClose(token, OfficialCareerPersistenceOutcome.Committed);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Official career close transaction failed: " +
                                    error.Message);
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            if (token == null)
                return new OfficialCareerCloseResult(
                    OfficialCareerPersistenceOutcome.CleanFailure, null);
            try
            {
                return ResultForClose(token, ReadbackClose(pDb, token));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Official career close readback failed: " +
                                    error.Message);
                return ResultForClose(token, OfficialCareerPersistenceOutcome.Unknown);
            }
        }

        internal static OfficialCareerCloseToken CaptureClose(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, OfficialCareerCloseRequest pRequest)
        {
            if (pDb == null || pTransaction == null || !ValidCloseRequest(pRequest))
                throw new ArgumentException("invalid official career close capture");
            string table = CourtOfficerTableItem.GetTableName();
            List<OfficialCareerRecord> active = ReadActiveRows(pDb, pTransaction, table,
                pRequest.ActorId, pRequest.Layer);
            if (active.Count != 1)
                throw new InvalidOperationException(
                    "official career close requires one active actor-layer row");
            OfficialCareerRecord original = active[0];
            if (original.KingdomId != pRequest.KingdomId ||
                pRequest.OfficeId != null && original.OfficeId != pRequest.OfficeId)
                throw new InvalidOperationException(
                    "active career does not match the frozen close target");
            OfficialCareerRecord desired = original.Copy();
            desired.Active = CourtOfficerRecordRules.ActiveFlag(false);
            desired.EndedYear = pRequest.EndedYear;
            desired.EndedTime = pRequest.EndedTime;
            desired.EndReason = pRequest.EndReason;
            desired.UpdatedTime = pRequest.EndedTime;
            return new OfficialCareerCloseToken(pRequest, original, desired);
        }

        internal static void StageClose(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, OfficialCareerCloseToken pToken)
        {
            if (pDb == null || pTransaction == null || pToken == null)
                throw new ArgumentException("invalid official career close stage");
            OfficialCareerRecord original = pToken.Original;
            OfficialCareerRecord desired = pToken.Desired;
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + CourtOfficerTableItem.GetTableName() +
                " SET ACTIVE=@dActive,ENDED_YEAR=@dEndedYear,ENDED_TIME=@dEndedTime," +
                "END_REASON=@dReason,UPDATED_TIME=@dUpdated" +
                " WHERE OFFICER_ID=@oId AND KINGDOM_ID=@oKingdom AND ACTOR_ID=@oActor" +
                " AND ACTOR_NAME=@oName AND CITY_ID=@oCity AND LAYER=@oLayer" +
                " AND OFFICE_ID=@oOffice AND SCHOOL_ID=@oSchool" +
                " AND INFLUENCE=@oInfluence AND APPOINTED_YEAR=@oAppointedYear" +
                " AND APPOINTED_TIME=@oAppointedTime AND ENDED_YEAR=@oEndedYear" +
                " AND ENDED_TIME=@oEndedTime AND ACTIVE=@oActive" +
                " AND END_REASON=@oReason AND UPDATED_TIME=@oUpdated";
            command.Parameters.AddWithValue("@dActive", desired.Active);
            command.Parameters.AddWithValue("@dEndedYear", desired.EndedYear);
            command.Parameters.AddWithValue("@dEndedTime", desired.EndedTime);
            command.Parameters.AddWithValue("@dReason", desired.EndReason);
            command.Parameters.AddWithValue("@dUpdated", desired.UpdatedTime);
            BindCareerRecord(command, "o", original);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("exact active career close failed");
        }

        internal static OfficialCareerPersistenceOutcome ReadbackClose(
            SQLiteConnection pDb, OfficialCareerCloseToken pToken)
        {
            return ReadbackClose(pDb, null, pToken);
        }

        internal static OfficialCareerPersistenceOutcome ReadbackClose(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            OfficialCareerCloseToken pToken)
        {
            if (pDb == null || pToken == null)
                return OfficialCareerPersistenceOutcome.Unknown;
            string table = CourtOfficerTableItem.GetTableName();
            List<OfficialCareerRecord> rows = ReadRowsByOfficerId(pDb, pTransaction,
                table, pToken.Original.OfficerId);
            List<OfficialCareerRecord> active = ReadActiveRows(pDb, pTransaction, table,
                pToken.Original.ActorId, pToken.Original.Layer);
            OfficialCareerRecord actual = rows.Count == 1 ? rows[0] : null;
            return OfficialCareerReadbackRules.ResolveClose(pQuerySucceeded: true,
                rows.Count, active.Count,
                pDesiredClosedExact: actual != null && actual.Exact(pToken.Desired),
                pOriginalActiveExact: actual != null && actual.Exact(pToken.Original) &&
                    active.Count == 1 && active[0].Exact(pToken.Original));
        }

        internal static OfficialCareerCloseResult ResultForClose(
            OfficialCareerCloseToken pToken, OfficialCareerPersistenceOutcome pOutcome)
        {
            return new OfficialCareerCloseResult(pOutcome, pToken?.Prior);
        }

        public static OfficialCareerAppointmentResult Appoint(SQLiteConnection pDb,
            OfficialCareerAppointment pAppointment)
        {
            if (pDb == null || pAppointment == null || pAppointment.ActorId < 0 ||
                pAppointment.KingdomId < 0)
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.CleanFailure,
                    OfficialCareerMutation.Started);

            SQLiteTransaction transaction = null;
            OfficialCareerPersistenceToken token = null;
            try
            {
                transaction = pDb.BeginTransaction();
                token = Capture(pDb, transaction, pAppointment);
                Stage(pDb, transaction, token);
                transaction.Commit();
                return ResultFor(token, OfficialCareerPersistenceOutcome.Committed);
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

            if (token == null)
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.Unknown,
                    OfficialCareerMutation.Noop);

            try
            {
                return ResultFor(token, Readback(pDb, token));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Official career appointment readback failed: " +
                                    error.Message);
                return ResultFor(token, OfficialCareerPersistenceOutcome.Unknown);
            }
        }

        internal static OfficialCareerPersistenceToken Capture(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, OfficialCareerAppointment pAppointment)
        {
            if (pDb == null || pTransaction == null || pAppointment == null ||
                pAppointment.ActorId < 0 || pAppointment.KingdomId < 0)
                throw new ArgumentException("invalid official career capture");

            string table = CourtOfficerTableItem.GetTableName();
            OfficialCareerRecord original = ReadActive(pDb, pTransaction, table,
                pAppointment.ActorId, pAppointment.Layer);
            bool insert = CourtOfficerRecordRules.ShouldInsertNewActiveRecord(
                original != null,
                original != null && original.KingdomId == pAppointment.KingdomId,
                original != null && original.OfficeId == pAppointment.OfficeId,
                original != null && original.Layer == pAppointment.Layer,
                original != null && original.CityId == pAppointment.CityId);
            if (!insert)
                return new OfficialCareerPersistenceToken(pAppointment, original,
                    Refreshed(original, pAppointment), OfficialCareerMutation.Refreshed);

            OfficialCareerRecord desired = NewActive(pAppointment);
            desired.OfficerId = NextOfficerId(pDb, pTransaction, table);
            return new OfficialCareerPersistenceToken(pAppointment, original, desired,
                original == null
                    ? OfficialCareerMutation.Started
                    : OfficialCareerMutation.Reassigned);
        }

        internal static void Stage(SQLiteConnection pDb, SQLiteTransaction pTransaction,
            OfficialCareerPersistenceToken pToken)
        {
            if (pDb == null || pTransaction == null || pToken == null)
                throw new ArgumentException("invalid official career stage");
            string table = CourtOfficerTableItem.GetTableName();
            if (pToken.Mutation == OfficialCareerMutation.Refreshed)
            {
                UpdateSnapshot(pDb, pTransaction, table, pToken.Desired);
                return;
            }
            if (pToken.Mutation == OfficialCareerMutation.Reassigned)
                EndOriginal(pDb, pTransaction, table, pToken.Original,
                    pToken.Appointment.AppointedYear, pToken.Appointment.AppointedTime);
            if (pToken.Mutation == OfficialCareerMutation.Started ||
                pToken.Mutation == OfficialCareerMutation.Reassigned)
            {
                InsertActive(pDb, pTransaction, table, pToken.Desired);
                return;
            }
            throw new InvalidOperationException("unsupported official career mutation");
        }

        internal static OfficialCareerPersistenceOutcome Readback(SQLiteConnection pDb,
            OfficialCareerPersistenceToken pToken)
        {
            if (pDb == null || pToken == null)
                return OfficialCareerPersistenceOutcome.Unknown;
            string table = CourtOfficerTableItem.GetTableName();
            List<OfficialCareerRecord> active = ReadActiveRows(pDb, null, table,
                pToken.Appointment.ActorId, pToken.Appointment.Layer);
            OfficialCareerRecord authoritative = active.Count == 1 ? active[0] : null;

            if (pToken.Mutation == OfficialCareerMutation.Reassigned)
            {
                OfficialCareerRecord closed = ReadClosedOriginal(pDb, pToken,
                    out int closedCount);
                List<OfficialCareerRecord> desiredRows = ReadRowsByOfficerId(pDb, null,
                    table, pToken.Desired.OfficerId);
                return OfficialCareerReadbackRules.ResolveReassignment(
                    pQuerySucceeded: closedCount <= 1 && desiredRows.Count <= 1,
                    active.Count,
                    pDesiredActiveExact: authoritative != null &&
                        authoritative.Exact(pToken.Desired),
                    pClosedOriginalExact: closedCount == 1 &&
                        ClosedOriginalExact(pToken, closed),
                    pOriginalActiveExact: authoritative != null &&
                        authoritative.Exact(pToken.Original),
                    pDesiredRowAbsent: desiredRows.Count == 0);
            }

            bool desiredExact = authoritative != null &&
                                authoritative.Exact(pToken.Desired);
            bool originalExact = authoritative != null &&
                                 authoritative.Exact(pToken.Original);
            OfficialCareerPersistenceOutcome outcome = OfficialCareerReadbackRules.Resolve(
                pQuerySucceeded: true, active.Count, desiredExact,
                pOriginalExisted: pToken.Original != null, originalExact);
            if (outcome == OfficialCareerPersistenceOutcome.CleanFailure &&
                pToken.Original == null && ReadRowsByOfficerId(pDb, null, table,
                    pToken.Desired.OfficerId).Count != 0)
                return OfficialCareerPersistenceOutcome.Unknown;
            return outcome;
        }

        internal static OfficialCareerAppointmentResult ResultFor(
            OfficialCareerPersistenceToken pToken, OfficialCareerPersistenceOutcome pOutcome)
        {
            if (pToken == null)
                return new OfficialCareerAppointmentResult(
                    OfficialCareerPersistenceOutcome.Unknown,
                    OfficialCareerMutation.Noop);
            return new OfficialCareerAppointmentResult(pOutcome, pToken.Mutation,
                pToken.Prior);
        }

        internal static List<OfficialCareerRecord> ReadActiveRecords(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pActorId, string pLayer)
        {
            return ReadActiveRows(pDb, pTransaction, CourtOfficerTableItem.GetTableName(),
                pActorId, pLayer);
        }

        private static OfficialCareerRecord ReadActive(SQLiteConnection pDb,
            SQLiteTransaction transaction, string pTable, long pActorId, string pLayer)
        {
            List<OfficialCareerRecord> rows = ReadActiveRows(pDb, transaction, pTable, pActorId,
                pLayer);
            if (rows.Count > 1)
                throw new InvalidOperationException("multiple active careers for actor layer");
            return rows.Count == 0 ? null : rows[0];
        }

        private static List<OfficialCareerRecord> ReadActiveRows(SQLiteConnection pDb,
            SQLiteTransaction transaction, string pTable, long pActorId, string pLayer)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = transaction };
            command.CommandText = "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,CITY_ID," +
                "LAYER,OFFICE_ID,SCHOOL_ID,INFLUENCE,APPOINTED_YEAR,APPOINTED_TIME," +
                "ENDED_YEAR,ENDED_TIME,ACTIVE,END_REASON,UPDATED_TIME FROM " + pTable +
                " WHERE ACTOR_ID=@actor AND LAYER=@layer AND ACTIVE=1";
            command.Parameters.AddWithValue("@actor", pActorId);
            command.Parameters.AddWithValue("@layer", pLayer ?? "");
            var rows = new List<OfficialCareerRecord>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(ReadRow(reader));
            return rows;
        }

        private static OfficialCareerRecord ReadClosedOriginal(SQLiteConnection pDb,
            OfficialCareerPersistenceToken pToken, out int pCount)
        {
            List<OfficialCareerRecord> rows = ReadRowsByOfficerId(pDb, null,
                CourtOfficerTableItem.GetTableName(), pToken.Original.OfficerId);
            pCount = rows.Count;
            return rows.Count == 1 ? rows[0] : null;
        }

        private static bool ClosedOriginalExact(OfficialCareerPersistenceToken pToken,
            OfficialCareerRecord pActual)
        {
            if (pToken?.Original == null || pActual == null) return false;
            OfficialCareerRecord expected = pToken.Original.Copy();
            expected.Active = CourtOfficerRecordRules.ActiveFlag(false);
            expected.EndedYear = pToken.Appointment.AppointedYear;
            expected.EndedTime = pToken.Appointment.AppointedTime;
            expected.EndReason = "reassigned";
            expected.UpdatedTime = pToken.Appointment.AppointedTime;
            return pActual.Exact(expected);
        }

        private static List<OfficialCareerRecord> ReadRowsByOfficerId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, long pOfficerId)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,CITY_ID," +
                "LAYER,OFFICE_ID,SCHOOL_ID,INFLUENCE,APPOINTED_YEAR,APPOINTED_TIME," +
                "ENDED_YEAR,ENDED_TIME,ACTIVE,END_REASON,UPDATED_TIME FROM " + pTable +
                " WHERE OFFICER_ID=@officer";
            command.Parameters.AddWithValue("@officer", pOfficerId);
            var rows = new List<OfficialCareerRecord>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(ReadRow(reader));
            return rows;
        }

        private static OfficialCareerRecord ReadRow(SQLiteDataReader pReader)
        {
            return new OfficialCareerRecord
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

        private static OfficialCareerRecord Refreshed(OfficialCareerRecord pOriginal,
            OfficialCareerAppointment pAppointment)
        {
            OfficialCareerRecord desired = pOriginal.Copy();
            desired.ActorName = pAppointment.ActorName;
            desired.SchoolId = pAppointment.SchoolId;
            desired.Influence = pAppointment.Influence;
            desired.UpdatedTime = pAppointment.AppointedTime;
            return desired;
        }

        private static OfficialCareerRecord NewActive(OfficialCareerAppointment pAppointment)
        {
            return new OfficialCareerRecord
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
            SQLiteTransaction pTransaction, string pTable, OfficialCareerRecord pDesired)
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
            SQLiteTransaction pTransaction, string pTable, OfficialCareerRecord pOriginal,
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
            SQLiteTransaction pTransaction, string pTable, OfficialCareerRecord pCareer)
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

        private static bool ValidCloseRequest(OfficialCareerCloseRequest pRequest)
        {
            return pRequest != null && pRequest.ActorId >= 0 &&
                   pRequest.KingdomId >= 0 && !string.IsNullOrEmpty(pRequest.Layer) &&
                   pRequest.EndedYear >= 0 && !double.IsNaN(pRequest.EndedTime) &&
                   !double.IsInfinity(pRequest.EndedTime) && pRequest.EndedTime >= 0d;
        }

        private static void BindCareerRecord(SQLiteCommand pCommand, string pPrefix,
            OfficialCareerRecord pRecord)
        {
            string prefix = "@" + pPrefix;
            pCommand.Parameters.AddWithValue(prefix + "Id", pRecord.OfficerId);
            pCommand.Parameters.AddWithValue(prefix + "Kingdom", pRecord.KingdomId);
            pCommand.Parameters.AddWithValue(prefix + "Actor", pRecord.ActorId);
            pCommand.Parameters.AddWithValue(prefix + "Name", pRecord.ActorName);
            pCommand.Parameters.AddWithValue(prefix + "City", pRecord.CityId);
            pCommand.Parameters.AddWithValue(prefix + "Layer", pRecord.Layer);
            pCommand.Parameters.AddWithValue(prefix + "Office", pRecord.OfficeId);
            pCommand.Parameters.AddWithValue(prefix + "School", pRecord.SchoolId);
            pCommand.Parameters.AddWithValue(prefix + "Influence", pRecord.Influence);
            pCommand.Parameters.AddWithValue(prefix + "AppointedYear",
                pRecord.AppointedYear);
            pCommand.Parameters.AddWithValue(prefix + "AppointedTime",
                pRecord.AppointedTime);
            pCommand.Parameters.AddWithValue(prefix + "EndedYear", pRecord.EndedYear);
            pCommand.Parameters.AddWithValue(prefix + "EndedTime", pRecord.EndedTime);
            pCommand.Parameters.AddWithValue(prefix + "Active", pRecord.Active);
            pCommand.Parameters.AddWithValue(prefix + "Reason", pRecord.EndReason);
            pCommand.Parameters.AddWithValue(prefix + "Updated", pRecord.UpdatedTime);
        }
    }
}
