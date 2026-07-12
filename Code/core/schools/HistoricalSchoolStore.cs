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
        private const double LedgerTraditionDecay = 0.995d;
        private const double LedgerPresenceDecay = 0.97d;
        private const double LedgerMomentumDecay = 0.85d;
        private const int TraditionDecayGraceYears = 3;

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
            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                using (var command = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    command.CommandText = "INSERT INTO " + EventTable +
                        " (EVENT_ID,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID,CITY_ID," +
                        "KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME) VALUES " +
                        " (@id,@type,@actor,@target,@school,@city,@kingdom,@year,@payload," +
                        "@importance,@time)";
                    command.Parameters.AddWithValue("@id", eventId);
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
        public static bool TryRecordDebateAndLedger(HistoricalSchoolDebateRecord pDebate,
            HistoricalSchoolLedgerDelta pFirstDelta, HistoricalSchoolLedgerDelta pSecondDelta,
            double pWorldTime)
        {
            if (DB == null || pDebate == null || pFirstDelta == null || pSecondDelta == null ||
                pDebate.CityId < 0 || pDebate.DebateYear < 0 || pDebate.FirstActorId < 0 ||
                pDebate.SecondActorId < 0 ||
                pDebate.FirstActorId == pDebate.SecondActorId ||
                string.IsNullOrWhiteSpace(pDebate.FirstSchoolId) ||
                string.IsNullOrWhiteSpace(pDebate.SecondSchoolId) ||
                string.Equals(pDebate.FirstSchoolId, pDebate.SecondSchoolId,
                    StringComparison.Ordinal) || string.IsNullOrWhiteSpace(pDebate.TopicId) ||
                !Enum.IsDefined(typeof(SchoolDebateOutcome), pDebate.Outcome)) return false;

            string firstSchool = ResolveLedgerSchool(pFirstDelta, pDebate.FirstSchoolId);
            string secondSchool = ResolveLedgerSchool(pSecondDelta, pDebate.SecondSchoolId);
            if (string.IsNullOrWhiteSpace(firstSchool) || string.IsNullOrWhiteSpace(secondSchool) ||
                !string.Equals(firstSchool, pDebate.FirstSchoolId, StringComparison.Ordinal) ||
                !string.Equals(secondSchool, pDebate.SecondSchoolId, StringComparison.Ordinal) ||
                string.Equals(firstSchool, secondSchool, StringComparison.Ordinal)) return false;

            // IDs are allocated before opening the transaction.  Ledger rows use
            // city+school keys and therefore do not consume an allocator id.
            long debateId = TableIdAllocator.Next(DB, DebateTable, "DEBATE_ID");
            long firstEventId = TableIdAllocator.Next(DB, EventTable, "EVENT_ID");
            if (debateId < 0 || firstEventId < 0 || firstEventId >= long.MaxValue - 1L)
                return false;
            long secondEventId = firstEventId + 1L;

            SQLiteTransaction transaction = null;
            try
            {
                transaction = DB.BeginTransaction();
                if (HasAnyDebateForYearCommand(transaction, pDebate.CityId,
                        pDebate.DebateYear) || HasDebateForYearCommand(transaction,
                        pDebate.CityId, pDebate.FirstActorId, pDebate.SecondActorId,
                        pDebate.DebateYear))
                {
                    transaction.Rollback();
                    return false;
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
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("HistoricalSchoolStore record debate failed: " +
                                    error.Message);
                return false;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
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
                                      "ACTIVE_PRESENCE,MOMENTUM,LAST_ACTIVE_YEAR FROM " + LedgerTable +
                                      " WHERE CITY_ID=@city AND SCHOOL_ID=@school LIMIT 1";
                command.Parameters.AddWithValue("@city", pCityId);
                command.Parameters.AddWithValue("@school", school);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                    return new HistoricalSchoolLedgerSnapshot(school, 0f, 0f, 0f, -1);
                return new HistoricalSchoolLedgerSnapshot(ValueString(reader, 0),
                    (float)ClampLedger01(ValueDouble(reader, 1)),
                    (float)ClampLedger01(ValueDouble(reader, 4)),
                    (float)ClampLedger01(ValueDouble(reader, 5)), ValueInt(reader, 6, -1),
                    (float)ClampLedger01(ValueDouble(reader, 2)),
                    (float)ClampLedgerInstitutions(ValueDouble(reader, 3)));
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
            var result = new Dictionary<string, HistoricalSchoolLedgerSnapshot>(
                StringComparer.Ordinal);
            if (DB == null || pCityId < 0) return result;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT SCHOOL_ID,TRADITION,MEMBERSHIP,INSTITUTIONS," +
                                      "ACTIVE_PRESENCE,MOMENTUM,LAST_ACTIVE_YEAR FROM " + LedgerTable +
                                      " WHERE CITY_ID=@city";
                command.Parameters.AddWithValue("@city", pCityId);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string school = ValueString(reader, 0);
                    if (string.IsNullOrWhiteSpace(school)) continue;
                    result[school] = new HistoricalSchoolLedgerSnapshot(school,
                        (float)ClampLedger01(ValueDouble(reader, 1)),
                        (float)ClampLedger01(ValueDouble(reader, 4)),
                        (float)ClampLedger01(ValueDouble(reader, 5)), ValueInt(reader, 6, -1),
                        (float)ClampLedger01(ValueDouble(reader, 2)),
                        (float)ClampLedgerInstitutions(ValueDouble(reader, 3)));
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore load city ledgers failed: " +
                                    error.Message);
            }
            return result;
        }

        public static int ApplyLedgerDecay(int pYear, double pWorldTime)
        {
            if (DB == null || pYear < 0) return 0;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "UPDATE " + LedgerTable +
                    " SET TRADITION=CASE WHEN COALESCE(LAST_ACTIVE_YEAR,-1)<=@traditionCutoff " +
                    "THEN MAX(0,MIN(1,TRADITION*@traditionDecay)) ELSE TRADITION END," +
                    "ACTIVE_PRESENCE=MAX(0,MIN(1,ACTIVE_PRESENCE*@presenceDecay))," +
                    "MOMENTUM=MAX(0,MIN(1,MOMENTUM*@momentumDecay))," +
                    "LAST_DECAY_YEAR=@year,UPDATED_TIME=@time" +
                    " WHERE COALESCE(LAST_DECAY_YEAR,-1)<@year AND " +
                    "(TRADITION>0 OR ACTIVE_PRESENCE>0 OR MOMENTUM>0)";
                command.Parameters.AddWithValue("@traditionDecay", LedgerTraditionDecay);
                command.Parameters.AddWithValue("@traditionCutoff",
                    pYear - TraditionDecayGraceYears);
                command.Parameters.AddWithValue("@presenceDecay", LedgerPresenceDecay);
                command.Parameters.AddWithValue("@momentumDecay", LedgerMomentumDecay);
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@time", FiniteNonNegative(pWorldTime));
                return command.ExecuteNonQuery();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("HistoricalSchoolStore decay ledgers failed: " +
                                    error.Message);
                return 0;
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

        private static bool HasDebateForYearCommand(SQLiteTransaction pTransaction, long pCityId,
            long pFirstActorId, long pSecondActorId, int pYear)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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

        private static bool HasAnyDebateForYearCommand(SQLiteTransaction pTransaction,
            long pCityId, int pYear)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "SELECT 1 FROM " + DebateTable +
                                  " WHERE CITY_ID=@city AND DEBATE_YEAR=@year LIMIT 1";
            command.Parameters.AddWithValue("@city", pCityId);
            command.Parameters.AddWithValue("@year", pYear);
            return command.ExecuteScalar() != null;
        }

        private static bool HasActiveInstitutionCommand(SQLiteTransaction pTransaction,
            HistoricalSchoolMasterDefinition pMaster, long pCityId)
        {
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + EventTable +
                                  " (EVENT_ID,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID," +
                                  "CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME) " +
                                  "VALUES (@id,'institution_founded',@actor,-1,@school,@city," +
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
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + EventTable +
                                  " (EVENT_ID,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID," +
                                  "CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME)" +
                                  " VALUES (@id,'debate',@actor,@target,@school,@city,-1,@year," +
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
            using (var read = new SQLiteCommand(DB) { Transaction = pTransaction })
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

            tradition = ClampLedger01(tradition + Finite(pDelta.Tradition));
            membership = ClampLedger01(membership + Finite(pMembershipDelta));
            activePresence = ClampLedger01(activePresence + Finite(pDelta.ActivePresence));
            momentum = ClampLedger01(momentum + Finite(pDelta.Momentum));
            institutions = ClampLedgerInstitutions(institutions +
                                                   Math.Max(0d, Math.Min(MaxLedgerInstitutions,
                                                       Finite(pDelta.Institutions))));
            if (pDelta.LastActiveYear >= 0)
                lastActiveYear = Math.Max(lastActiveYear, pDelta.LastActiveYear);
            double updatedTime = FiniteNonNegative(pWorldTime);

            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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
            using var command = new SQLiteCommand(DB) { Transaction = pTransaction };
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

        private static void InvalidateLedgerCaches(long pCityId)
        {
            if (pCityId < 0) return;
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
