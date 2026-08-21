using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolTeachingPersistenceDb
    {
        private const string EventTable = "SchoolEvent";
        private const string LedgerTable = "CitySchoolLedger";
        private const string TeachingPredicate =
            "EVENT_TYPE IN ('lecture','persuasion')";

        internal static HistoricalSchoolTeachingDbResult Record(SQLiteConnection pDb,
            HistoricalSchoolTeachingDbRequest pRequest)
        {
            if (pDb == null || !Valid(pRequest)) return Result(
                HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                HistoricalSchoolTeachingDbResult result = RecordInTransaction(pDb,
                    transaction, pRequest);
                if (result.Outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                {
                    transaction.Rollback();
                    return Readback(pDb, pRequest);
                }
                transaction.Commit();
                return result;
            }
            catch
            {
                try { transaction?.Rollback(); } catch { }
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            return Readback(pDb, pRequest);
        }

        internal static HistoricalSchoolTeachingDbResult RecordInTransaction(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            HistoricalSchoolTeachingDbRequest pRequest)
        {
            if (pDb == null || pTransaction == null || !Valid(pRequest)) return Result(
                HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            List<HistoricalSchoolTeachingEventRow> existing = ReadEvents(pDb,
                pTransaction, pRequest);
            if (existing.Count != 0) return ResolveExisting(pRequest, existing);

            CaptureLedger(pDb, pTransaction, pRequest);
            if (!pRequest.IdsFrozen)
            {
                long firstId = NextEventId(pDb, pTransaction);
                pRequest.FreezeIds(firstId,
                    pRequest.Plan.IncludePersuasion ? firstId + 1L : -1L);
            }
            InsertEvent(pDb, pTransaction, pRequest.Lecture);
            if (pRequest.Plan.IncludePersuasion)
                InsertEvent(pDb, pTransaction, pRequest.Persuasion);
            WriteLedger(pDb, pTransaction, pRequest.DesiredLedger,
                pRequest.OriginalLedger != null);
            return Result(HistoricalSchoolTeachingPersistenceOutcome.Committed);
        }

        internal static HistoricalSchoolTeachingHistory LoadHistory(SQLiteConnection pDb)
        {
            var history = new HistoricalSchoolTeachingHistory();
            if (pDb == null) return history;
            using var command = new SQLiteCommand(pDb);
            command.CommandText = "SELECT EVENT_TYPE,ACTOR_ID,SCHOOL_ID,CITY_ID," +
                                  "KINGDOM_ID,EVENT_YEAR FROM " + EventTable +
                                  " WHERE " + TeachingPredicate +
                                  " ORDER BY EVENT_YEAR,EVENT_ID";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string type = Text(reader, 0);
                var candidate = new HistoricalSchoolLectureCandidate(
                    Long(reader, 1, -1L), Text(reader, 2), Long(reader, 3, -1L),
                    Long(reader, 4, -1L), pCanonical: false, pStartYear: 0,
                    pReputation: 0f);
                int year = Int(reader, 5, -1);
                if (type == "lecture") history.RecordLecture(candidate, year);
                else if (type == "persuasion") history.RecordPersuasion(candidate, year);
            }
            return history;
        }

        private static HistoricalSchoolTeachingDbResult ResolveExisting(
            HistoricalSchoolTeachingDbRequest pRequest,
            IReadOnlyList<HistoricalSchoolTeachingEventRow> pExisting)
        {
            int expectedCount = pRequest.Plan.IncludePersuasion ? 2 : 1;
            if (pExisting.Count != expectedCount) return Result(
                HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            HistoricalSchoolTeachingEventRow lecture = null;
            HistoricalSchoolTeachingEventRow persuasion = null;
            foreach (HistoricalSchoolTeachingEventRow row in pExisting)
            {
                if (row.OperationKey == pRequest.LectureOperationKey) lecture = row;
                else if (row.OperationKey == pRequest.PersuasionOperationKey)
                    persuasion = row;
                else return Result(HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            }
            if (lecture == null || !lecture.MatchesStableReplay(
                    pRequest.Lecture,
                    pRequireEventId: pRequest.IdsFrozen)) return Result(
                HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            if (pRequest.Plan.IncludePersuasion && (persuasion == null ||
                !persuasion.MatchesStableReplay(pRequest.Persuasion,
                    pRequireEventId: pRequest.IdsFrozen))) return Result(
                HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            if (!pRequest.IdsFrozen)
                pRequest.FreezeIds(lecture.EventId,
                    pRequest.Plan.IncludePersuasion ? persuasion.EventId : -1L);
            return Result(HistoricalSchoolTeachingPersistenceOutcome.Replayed);
        }

        private static HistoricalSchoolTeachingDbResult Readback(SQLiteConnection pDb,
            HistoricalSchoolTeachingDbRequest pRequest)
        {
            if (pDb == null || pRequest == null) return Result(
                HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            try
            {
                List<HistoricalSchoolTeachingEventRow> events = ReadEvents(pDb, null,
                    pRequest);
                if (events.Count != 0)
                {
                    HistoricalSchoolTeachingDbResult existing = ResolveExisting(pRequest,
                        events);
                    return existing.IsCommitted
                        ? Result(HistoricalSchoolTeachingPersistenceOutcome.Committed)
                        : existing;
                }
                if (!pRequest.OriginalLedgerCaptured) return Result(
                    HistoricalSchoolTeachingPersistenceOutcome.Unknown);
                HistoricalSchoolTeachingLedgerRow current = ReadLedger(pDb, null,
                    LedgerKey(pRequest.Plan.Candidate.CityId,
                        pRequest.Plan.Candidate.SchoolId));
                bool original = pRequest.OriginalLedger == null
                    ? current == null
                    : pRequest.OriginalLedger.Exact(current);
                return Result(original
                    ? HistoricalSchoolTeachingPersistenceOutcome.CleanFailure
                    : HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            }
            catch
            {
                return Result(HistoricalSchoolTeachingPersistenceOutcome.Unknown);
            }
        }

        private static void CaptureLedger(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, HistoricalSchoolTeachingDbRequest pRequest)
        {
            HistoricalSchoolLectureCandidate candidate = pRequest.Plan.Candidate;
            string key = LedgerKey(candidate.CityId, candidate.SchoolId);
            HistoricalSchoolTeachingLedgerRow original = ReadLedger(pDb, pTransaction, key);
            HistoricalSchoolTeachingLedgerRow desired = original?.Copy() ??
                new HistoricalSchoolTeachingLedgerRow
                {
                    LedgerKey = key,
                    CityId = candidate.CityId,
                    SchoolId = candidate.SchoolId,
                    LastActiveYear = -1,
                    LastDecayYear = -1
                };
            HistoricalSchoolEffectiveLedger effective =
                HistoricalSchoolLedgerDecayRules.Effective(desired.Tradition,
                    desired.Membership, desired.Institutions, desired.ActivePresence,
                    desired.Momentum, desired.LastActiveYear, desired.LastDecayYear,
                    pRequest.Plan.Year);
            desired.Tradition = effective.Tradition;
            desired.Membership = effective.Membership;
            desired.Institutions = effective.Institutions;
            desired.ActivePresence = effective.ActivePresence;
            desired.Momentum = effective.Momentum;
            desired.LastDecayYear = effective.LastDecayYear;
            double persuasion = pRequest.Plan.IncludePersuasion ? 1d : 0d;
            desired.Tradition = Clamp01(desired.Tradition + 0.005d + 0.003d * persuasion);
            desired.Membership = Clamp01(desired.Membership + 0.01d + 0.01d * persuasion);
            desired.ActivePresence = Clamp01(desired.ActivePresence + 0.01d +
                                               0.015d * persuasion);
            desired.Momentum = Clamp01(desired.Momentum + 0.02d +
                                       0.03d * persuasion);
            desired.LastActiveYear = Math.Max(desired.LastActiveYear,
                pRequest.Plan.Year);
            desired.LastDecayYear = Math.Max(desired.LastDecayYear,
                pRequest.Plan.Year);
            desired.UpdatedTime = pRequest.WorldTime;
            pRequest.OriginalLedger = original;
            pRequest.DesiredLedger = desired;
            pRequest.OriginalLedgerCaptured = true;
        }

        private static void InsertEvent(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, HistoricalSchoolTeachingEventRow pEvent)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + EventTable +
                " (EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID," +
                "SCHOOL_ID,CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME)" +
                " VALUES (@id,@operation,@type,@actor,@target,@school,@city,@kingdom," +
                "@year,@payload,@importance,@time)";
            command.Parameters.AddWithValue("@id", pEvent.EventId);
            command.Parameters.AddWithValue("@operation", pEvent.OperationKey);
            command.Parameters.AddWithValue("@type", pEvent.EventType);
            command.Parameters.AddWithValue("@actor", pEvent.ActorId);
            command.Parameters.AddWithValue("@target", pEvent.TargetActorId);
            command.Parameters.AddWithValue("@school", pEvent.SchoolId);
            command.Parameters.AddWithValue("@city", pEvent.CityId);
            command.Parameters.AddWithValue("@kingdom", pEvent.KingdomId);
            command.Parameters.AddWithValue("@year", pEvent.EventYear);
            command.Parameters.AddWithValue("@payload", pEvent.Payload);
            command.Parameters.AddWithValue("@importance", pEvent.Importance);
            command.Parameters.AddWithValue("@time", pEvent.WorldTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("teaching event insert failed");
        }

        private static void WriteLedger(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, HistoricalSchoolTeachingLedgerRow pLedger,
            bool pExists)
        {
            if (pLedger == null) throw new ArgumentNullException(nameof(pLedger));
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            if (pExists)
            {
                command.CommandText = "UPDATE " + LedgerTable +
                    " SET CITY_ID=@city,SCHOOL_ID=@school,TRADITION=@tradition," +
                    "MEMBERSHIP=@membership,INSTITUTIONS=@institutions," +
                    "ACTIVE_PRESENCE=@active,MOMENTUM=@momentum," +
                    "LAST_ACTIVE_YEAR=@lastActive,LAST_DECAY_YEAR=@lastDecay," +
                    "UPDATED_TIME=@time WHERE LEDGER_KEY=@key";
            }
            else
            {
                command.CommandText = "INSERT INTO " + LedgerTable +
                    " (LEDGER_KEY,CITY_ID,SCHOOL_ID,TRADITION,MEMBERSHIP,INSTITUTIONS," +
                    "ACTIVE_PRESENCE,MOMENTUM,LAST_ACTIVE_YEAR,LAST_DECAY_YEAR," +
                    "UPDATED_TIME) VALUES (@key,@city,@school,@tradition,@membership," +
                    "@institutions,@active,@momentum,@lastActive,@lastDecay,@time)";
            }
            command.Parameters.AddWithValue("@key", pLedger.LedgerKey);
            command.Parameters.AddWithValue("@city", pLedger.CityId);
            command.Parameters.AddWithValue("@school", pLedger.SchoolId);
            command.Parameters.AddWithValue("@tradition", pLedger.Tradition);
            command.Parameters.AddWithValue("@membership", pLedger.Membership);
            command.Parameters.AddWithValue("@institutions", pLedger.Institutions);
            command.Parameters.AddWithValue("@active", pLedger.ActivePresence);
            command.Parameters.AddWithValue("@momentum", pLedger.Momentum);
            command.Parameters.AddWithValue("@lastActive", pLedger.LastActiveYear);
            command.Parameters.AddWithValue("@lastDecay", pLedger.LastDecayYear);
            command.Parameters.AddWithValue("@time", pLedger.UpdatedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("teaching ledger upsert failed");
        }

        private static HistoricalSchoolTeachingLedgerRow ReadLedger(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pKey)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT LEDGER_KEY,CITY_ID,SCHOOL_ID,TRADITION," +
                "MEMBERSHIP,INSTITUTIONS,ACTIVE_PRESENCE,MOMENTUM,LAST_ACTIVE_YEAR," +
                "LAST_DECAY_YEAR,UPDATED_TIME FROM " + LedgerTable +
                " WHERE LEDGER_KEY=@key LIMIT 2";
            command.Parameters.AddWithValue("@key", pKey ?? "");
            HistoricalSchoolTeachingLedgerRow row = null;
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (row != null)
                    throw new InvalidOperationException("duplicate teaching ledger rows");
                row = new HistoricalSchoolTeachingLedgerRow
                {
                    LedgerKey = Text(reader, 0),
                    CityId = Long(reader, 1, -1L),
                    SchoolId = Text(reader, 2),
                    Tradition = Double(reader, 3, 0d),
                    Membership = Double(reader, 4, 0d),
                    Institutions = Double(reader, 5, 0d),
                    ActivePresence = Double(reader, 6, 0d),
                    Momentum = Double(reader, 7, 0d),
                    LastActiveYear = Int(reader, 8, -1),
                    LastDecayYear = Int(reader, 9, -1),
                    UpdatedTime = Double(reader, 10, 0d)
                };
            }
            return row;
        }

        private static List<HistoricalSchoolTeachingEventRow> ReadEvents(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            HistoricalSchoolTeachingDbRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = EventSelect() +
                " WHERE OPERATION_KEY IN (@lecture,@persuasion)" +
                " AND OPERATION_KEY<>'' AND " + TeachingPredicate +
                " ORDER BY EVENT_ID";
            command.Parameters.AddWithValue("@lecture", pRequest.LectureOperationKey);
            command.Parameters.AddWithValue("@persuasion", pRequest.PersuasionOperationKey);
            var result = new List<HistoricalSchoolTeachingEventRow>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(ReadEvent(reader));
            return result;
        }

        private static string EventSelect()
        {
            return "SELECT EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID," +
                   "SCHOOL_ID,CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE," +
                   "WORLD_TIME FROM " + EventTable;
        }

        private static HistoricalSchoolTeachingEventRow ReadEvent(
            SQLiteDataReader pReader)
        {
            return new HistoricalSchoolTeachingEventRow
            {
                EventId = Long(pReader, 0, -1L),
                OperationKey = Text(pReader, 1),
                EventType = Text(pReader, 2),
                ActorId = Long(pReader, 3, -1L),
                TargetActorId = Long(pReader, 4, -1L),
                SchoolId = Text(pReader, 5),
                CityId = Long(pReader, 6, -1L),
                KingdomId = Long(pReader, 7, -1L),
                EventYear = Int(pReader, 8, -1),
                Payload = Text(pReader, 9),
                Importance = Int(pReader, 10, 0),
                WorldTime = Double(pReader, 11, -1d)
            };
        }

        private static long NextEventId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(EVENT_ID),0)+1 FROM " + EventTable;
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static bool Valid(HistoricalSchoolTeachingDbRequest pRequest)
        {
            return pRequest != null && pRequest.Plan.IsValid &&
                   pRequest.Lecture != null &&
                   (!pRequest.Plan.IncludePersuasion || pRequest.Persuasion != null) &&
                   !double.IsNaN(pRequest.WorldTime) &&
                   !double.IsInfinity(pRequest.WorldTime) && pRequest.WorldTime >= 0d;
        }

        private static string LedgerKey(long pCityId, string pSchoolId)
        {
            return pCityId.ToString(CultureInfo.InvariantCulture) + ":" +
                   (pSchoolId ?? "");
        }

        private static double Clamp01(double pValue)
        {
            if (double.IsNaN(pValue) || double.IsInfinity(pValue)) return 0d;
            return Math.Max(0d, Math.Min(1d, pValue));
        }

        private static HistoricalSchoolTeachingDbResult Result(
            HistoricalSchoolTeachingPersistenceOutcome pOutcome)
        {
            return new HistoricalSchoolTeachingDbResult(pOutcome);
        }

        private static long Long(SQLiteDataReader pReader, int pOrdinal, long pDefault)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault :
                Convert.ToInt64(pReader.GetValue(pOrdinal), CultureInfo.InvariantCulture);
        }

        private static int Int(SQLiteDataReader pReader, int pOrdinal, int pDefault)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault :
                Convert.ToInt32(pReader.GetValue(pOrdinal), CultureInfo.InvariantCulture);
        }

        private static double Double(SQLiteDataReader pReader, int pOrdinal,
            double pDefault)
        {
            return pReader.IsDBNull(pOrdinal) ? pDefault :
                Convert.ToDouble(pReader.GetValue(pOrdinal), CultureInfo.InvariantCulture);
        }

        private static string Text(SQLiteDataReader pReader, int pOrdinal)
        {
            return pReader.IsDBNull(pOrdinal) ? "" :
                pReader.GetValue(pOrdinal)?.ToString() ?? "";
        }
    }
}
