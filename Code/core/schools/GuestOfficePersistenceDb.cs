using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace AncientWarfare3.core.schools
{
    internal static class GuestOfficePersistenceDb
    {
        private const string AffiliationTable = "SchoolAffiliation";
        private const string CareerTable = "CourtOfficer";
        private const string EventTable = "SchoolEvent";
        private const string CentralLayer = "central";
        private const string GuestEventPredicate =
            "EVENT_TYPE IN ('guest_service_started','guest_service_renewed')";

        internal static GuestOfficeDbStartRequest PrepareStart(SQLiteConnection pDb,
            GuestOfficeStartSeed pSeed)
        {
            if (pDb == null || !ValidSeed(pSeed)) return null;
            List<GuestOfficeAffiliationRow> rows = ReadAffiliations(pDb, null,
                pSeed.ExpectedAffiliation.ActorId);
            if (rows.Count != 1 ||
                !rows[0].ExactExceptUpdatedTime(pSeed.ExpectedAffiliation)) return null;
            GuestOfficeAffiliationRow original = rows[0];
            if (original.ServiceKingdomId >= 0 ||
                original.ResidenceCityId != pSeed.DesiredCareer.CityId ||
                (original.LifecycleState != "AtHome" &&
                 original.LifecycleState != "Resident")) return null;

            GuestOfficeAffiliationRow desired = original.Copy();
            desired.DestinationCityId = -1L;
            desired.ServiceKingdomId = pSeed.DesiredCareer.KingdomId;
            desired.LifecycleState = "Serving";
            desired.ServiceStartYear = pSeed.DesiredCareer.AppointedYear;
            desired.ServiceEndYear = pSeed.ServiceEndYear;
            desired.UpdatedTime = pSeed.DesiredCareer.AppointedTime;

            GuestOfficeCareerRow career = pSeed.DesiredCareer.Copy();
            career.OfficerId = -1L;
            career.EndedYear = -1;
            career.EndedTime = -1d;
            career.Active = 1;
            career.EndReason = "";
            career.UpdatedTime = career.AppointedTime;
            GuestOfficeEventRow eventTemplate = EventTemplate(desired, career,
                pSeed.EventType);
            return new GuestOfficeDbStartRequest(original, desired, career, eventTemplate,
                PayloadBase(career.OfficeId, desired.ServiceStartYear,
                    desired.ServiceEndYear));
        }

        internal static GuestOfficeDbStartResult Start(SQLiteConnection pDb,
            GuestOfficeDbStartRequest pRequest)
        {
            if (pDb == null || !ValidRequest(pRequest)) return Unknown(pRequest);
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                GuestOfficeDbStartResult result = StartInTransaction(pDb, transaction,
                    pRequest);
                if (result.Outcome == GuestOfficePersistenceOutcome.Unknown)
                {
                    transaction.Rollback();
                    throw new InvalidOperationException(
                        "guest start transaction outcome is unknown");
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

            if (!pRequest.IdsFrozen) return Unknown(pRequest);
            try
            {
                GuestOfficePersistenceOutcome outcome = Readback(pDb, pRequest);
                return new GuestOfficeDbStartResult(outcome, pRequest,
                    pRecoveredExisting: false);
            }
            catch
            {
                return Unknown(pRequest);
            }
        }

        internal static GuestOfficeDbStartResult StartInTransaction(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeDbStartRequest pRequest)
        {
            if (pDb == null || pTransaction == null || !ValidRequest(pRequest))
                return Unknown(pRequest);
            List<GuestOfficeEventRow> existing = ReadEventsByOperationKey(pDb,
                pTransaction, pRequest.OperationKey);
            if (existing.Count != 0)
                return ResolveExisting(pDb, pTransaction, pRequest, existing);

            RequireFrozenOriginal(pDb, pTransaction, pRequest);
            if (!pRequest.IdsFrozen)
                pRequest.FreezeIds(NextId(pDb, pTransaction, CareerTable, "OFFICER_ID"),
                    NextId(pDb, pTransaction, EventTable, "EVENT_ID"));
            StageAffiliation(pDb, pTransaction, pRequest);
            InsertCareer(pDb, pTransaction, pRequest.DesiredCareer);
            InsertEvent(pDb, pTransaction, pRequest.DesiredEvent);
            return new GuestOfficeDbStartResult(GuestOfficePersistenceOutcome.Committed,
                pRequest, pRecoveredExisting: false);
        }

        internal static GuestOfficeDbRecoveryResult ReadCommittedTuple(SQLiteConnection pDb,
            long pActorId, GuestOfficeAffiliationRow pExpectedAffiliation)
        {
            if (pDb == null || pActorId < 0 || pExpectedAffiliation == null ||
                pExpectedAffiliation.ActorId != pActorId ||
                pExpectedAffiliation.LifecycleState != "Serving") return Retry();
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                List<GuestOfficeAffiliationRow> affiliations = ReadAffiliations(pDb,
                    transaction, pActorId);
                if (affiliations.Count != 1 ||
                    !affiliations[0].ExactExceptUpdatedTime(pExpectedAffiliation))
                {
                    transaction.Commit();
                    return Retry();
                }

                List<GuestOfficeCareerRow> careers = ReadActiveCareers(pDb, transaction,
                    pActorId, CentralLayer);
                if (careers.Count != 1 ||
                    !CompleteCareerShape(affiliations[0], careers[0]))
                {
                    transaction.Commit();
                    return Retry();
                }

                List<GuestOfficeEventRow> events = ReadGuestEventsForCareer(pDb,
                    transaction, affiliations[0], careers[0]);
                if (events.Count != 1 ||
                    !CompleteRecoveredTuple(affiliations[0], careers[0], events[0]))
                {
                    transaction.Commit();
                    return Retry();
                }

                transaction.Commit();
                return new GuestOfficeDbRecoveryResult(GuestOfficeRecoveryDecision.Adopt,
                    affiliations[0], careers[0], events[0]);
            }
            catch
            {
                try { transaction?.Rollback(); } catch { }
                return Retry();
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        private static bool ValidSeed(GuestOfficeStartSeed pSeed)
        {
            GuestOfficeAffiliationRow expected = pSeed?.ExpectedAffiliation;
            GuestOfficeCareerRow career = pSeed?.DesiredCareer;
            return expected != null && career != null && expected.ActorId >= 0 &&
                   career.ActorId == expected.ActorId && career.KingdomId >= 0 &&
                   career.CityId >= 0 && career.Layer == CentralLayer &&
                   !string.IsNullOrWhiteSpace(career.OfficeId) &&
                   !string.IsNullOrWhiteSpace(career.SchoolId) &&
                   pSeed.ServiceEndYear > career.AppointedYear &&
                   IsGuestEvent(pSeed.EventType) && Finite(career.Influence) &&
                   FiniteNonNegative(career.AppointedTime);
        }

        private static bool ValidRequest(GuestOfficeDbStartRequest pRequest)
        {
            return pRequest != null &&
                   pRequest.OriginalAffiliation?.ActorId >= 0 &&
                   pRequest.DesiredAffiliation?.ActorId ==
                   pRequest.OriginalAffiliation.ActorId &&
                   pRequest.DesiredCareer?.ActorId ==
                   pRequest.OriginalAffiliation.ActorId &&
                   pRequest.DesiredEvent?.ActorId ==
                   pRequest.OriginalAffiliation.ActorId &&
                   pRequest.DesiredCareer.Layer == CentralLayer &&
                   IsGuestEvent(pRequest.DesiredEvent.EventType) &&
                   !string.IsNullOrEmpty(pRequest.OperationKey) &&
                   pRequest.TupleFingerprint?.Length == 64;
        }

        private static void RequireFrozenOriginal(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeDbStartRequest pRequest)
        {
            List<GuestOfficeAffiliationRow> affiliations = ReadAffiliations(pDb,
                pTransaction, pRequest.OriginalAffiliation.ActorId);
            if (affiliations.Count != 1 ||
                !affiliations[0].Exact(pRequest.OriginalAffiliation))
                throw new InvalidOperationException("authoritative affiliation changed");
            List<GuestOfficeCareerRow> careers = ReadActiveCareers(pDb, pTransaction,
                pRequest.DesiredCareer.ActorId, CentralLayer);
            if (careers.Count != 0)
                throw new InvalidOperationException("guest actor already has a central career");
        }

        private static void StageAffiliation(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeDbStartRequest pRequest)
        {
            GuestOfficeAffiliationRow desired = pRequest.DesiredAffiliation;
            GuestOfficeAffiliationRow original = pRequest.OriginalAffiliation;
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + AffiliationTable +
                " SET HOME_KINGDOM_ID=@dHome,HOME_KINGDOM_NAME=@dHomeName," +
                "HOMETOWN_CITY_ID=@dHometown,RESIDENCE_CITY_ID=@dResidence," +
                "PREVIOUS_RESIDENCE_CITY_ID=@dPrevious,DESTINATION_CITY_ID=@dDestination," +
                "SERVICE_KINGDOM_ID=@dService,LIFECYCLE_STATE=@dState," +
                "SERVICE_START_YEAR=@dStart,SERVICE_END_YEAR=@dEnd," +
                "LAST_TRAVEL_YEAR=@dLast,TRAVEL_WAIT_START_YEAR=@dWait," +
                "VOYAGE_START_YEAR=@dVoyageStart,VOYAGE_ARRIVAL_YEAR=@dVoyageArrival," +
                "TRANSPORT_FAILURES=@dFailures,UPDATED_TIME=@dTime" +
                " WHERE ACTOR_ID=@oActor AND HOME_KINGDOM_ID=@oHome" +
                " AND HOME_KINGDOM_NAME=@oHomeName AND HOMETOWN_CITY_ID=@oHometown" +
                " AND RESIDENCE_CITY_ID=@oResidence AND PREVIOUS_RESIDENCE_CITY_ID=@oPrevious" +
                " AND DESTINATION_CITY_ID=@oDestination AND SERVICE_KINGDOM_ID=@oService" +
                " AND LIFECYCLE_STATE=@oState AND SERVICE_START_YEAR=@oStart" +
                " AND SERVICE_END_YEAR=@oEnd AND LAST_TRAVEL_YEAR=@oLast" +
                " AND TRAVEL_WAIT_START_YEAR=@oWait AND VOYAGE_START_YEAR=@oVoyageStart" +
                " AND VOYAGE_ARRIVAL_YEAR=@oVoyageArrival" +
                " AND TRANSPORT_FAILURES=@oFailures AND UPDATED_TIME=@oTime";
            BindAffiliation(command, "d", desired, includeActor: false);
            BindAffiliation(command, "o", original, includeActor: true);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("guest affiliation update failed");
        }

        private static void InsertCareer(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeCareerRow pCareer)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + CareerTable +
                " (OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,CITY_ID,LAYER,OFFICE_ID," +
                "SCHOOL_ID,INFLUENCE,APPOINTED_YEAR,APPOINTED_TIME,ENDED_YEAR,ENDED_TIME," +
                "ACTIVE,END_REASON,UPDATED_TIME) VALUES (@id,@kingdom,@actor,@name,@city," +
                "@layer,@office,@school,@influence,@year,@time,@endedYear,@endedTime," +
                "@active,@reason,@updated)";
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
            command.Parameters.AddWithValue("@endedYear", pCareer.EndedYear);
            command.Parameters.AddWithValue("@endedTime", pCareer.EndedTime);
            command.Parameters.AddWithValue("@active", pCareer.Active);
            command.Parameters.AddWithValue("@reason", pCareer.EndReason);
            command.Parameters.AddWithValue("@updated", pCareer.UpdatedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("guest career insert failed");
        }

        private static void InsertEvent(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeEventRow pEvent)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + EventTable +
                " (EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID,SCHOOL_ID," +
                "CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME) VALUES " +
                "(@id,@operation,@type,@actor,@target,@school,@city,@kingdom,@year," +
                "@payload,@importance,@time)";
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
                throw new InvalidOperationException("guest event insert failed");
        }

        private static GuestOfficePersistenceOutcome Readback(SQLiteConnection pDb,
            GuestOfficeDbStartRequest pRequest)
        {
            GuestOfficeProjectionState affiliation = AffiliationState(pDb, pRequest);
            GuestOfficeProjectionState career = CareerState(pDb, pRequest);
            GuestOfficeProjectionState schoolEvent = EventState(pDb, pRequest);
            return GuestOfficeReadbackRules.Resolve(pQuerySucceeded: true, affiliation,
                career, schoolEvent);
        }

        private static GuestOfficeProjectionState AffiliationState(SQLiteConnection pDb,
            GuestOfficeDbStartRequest pRequest)
        {
            List<GuestOfficeAffiliationRow> rows = ReadAffiliations(pDb, null,
                pRequest.OriginalAffiliation.ActorId);
            if (rows.Count != 1) return GuestOfficeProjectionState.Conflict;
            bool original = rows[0].Exact(pRequest.OriginalAffiliation);
            bool desired = rows[0].Exact(pRequest.DesiredAffiliation);
            if (original && desired) return GuestOfficeProjectionState.Both;
            if (desired) return GuestOfficeProjectionState.Desired;
            return original ? GuestOfficeProjectionState.Original :
                GuestOfficeProjectionState.Conflict;
        }

        private static GuestOfficeProjectionState CareerState(SQLiteConnection pDb,
            GuestOfficeDbStartRequest pRequest)
        {
            List<GuestOfficeCareerRow> active = ReadActiveCareers(pDb, null,
                pRequest.DesiredCareer.ActorId, CentralLayer);
            if (active.Count == 1 &&
                active[0].Exact(pRequest.DesiredCareer, pRequireOfficerId: true))
                return GuestOfficeProjectionState.Desired;
            if (active.Count != 0) return GuestOfficeProjectionState.Conflict;
            List<GuestOfficeCareerRow> allocated = ReadCareersByOfficerId(pDb, null,
                pRequest.DesiredCareer.OfficerId);
            return allocated.Count == 0
                ? GuestOfficeProjectionState.Original
                : GuestOfficeProjectionState.Conflict;
        }

        private static GuestOfficeProjectionState EventState(SQLiteConnection pDb,
            GuestOfficeDbStartRequest pRequest)
        {
            List<GuestOfficeEventRow> rows = ReadEventsByOperationKey(pDb, null,
                pRequest.OperationKey);
            if (rows.Count == 0) return GuestOfficeProjectionState.Original;
            return rows.Count == 1 && rows[0].Exact(pRequest.DesiredEvent,
                pRequireEventId: true)
                ? GuestOfficeProjectionState.Desired
                : GuestOfficeProjectionState.Conflict;
        }

        private static GuestOfficeDbStartResult ResolveExisting(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeDbStartRequest pRequest,
            List<GuestOfficeEventRow> pEvents)
        {
            if (pEvents.Count != 1) return Unknown(pRequest);
            List<GuestOfficeAffiliationRow> affiliations = ReadAffiliations(pDb,
                pTransaction, pRequest.DesiredAffiliation.ActorId);
            List<GuestOfficeCareerRow> careers = ReadActiveCareers(pDb, pTransaction,
                pRequest.DesiredCareer.ActorId, CentralLayer);
            bool requireIds = pRequest.IdsFrozen;
            if (affiliations.Count != 1 || careers.Count != 1 ||
                !affiliations[0].Exact(pRequest.DesiredAffiliation) ||
                !careers[0].Exact(pRequest.DesiredCareer, requireIds) ||
                !pEvents[0].Exact(pRequest.DesiredEvent, requireIds))
                return Unknown(pRequest);
            if (!requireIds)
                pRequest.FreezeIds(careers[0].OfficerId, pEvents[0].EventId);
            return new GuestOfficeDbStartResult(GuestOfficePersistenceOutcome.Committed,
                pRequest, pRecoveredExisting: true);
        }

        private static bool CompleteCareerShape(GuestOfficeAffiliationRow pAffiliation,
            GuestOfficeCareerRow pCareer)
        {
            return pAffiliation != null && pCareer != null &&
                   pAffiliation.LifecycleState == "Serving" &&
                   pAffiliation.ServiceKingdomId == pCareer.KingdomId &&
                   pAffiliation.ResidenceCityId == pCareer.CityId &&
                   pAffiliation.ServiceStartYear == pCareer.AppointedYear &&
                   pAffiliation.ServiceEndYear > pAffiliation.ServiceStartYear &&
                   pAffiliation.ActorId == pCareer.ActorId &&
                   pCareer.Layer == CentralLayer && pCareer.Active == 1 &&
                   pCareer.EndedYear == -1 && pCareer.EndedTime.Equals(-1d) &&
                   pCareer.EndReason == "" &&
                   pAffiliation.UpdatedTime.Equals(pCareer.AppointedTime) &&
                   pCareer.UpdatedTime.Equals(pCareer.AppointedTime);
        }

        private static bool CompleteRecoveredTuple(GuestOfficeAffiliationRow pAffiliation,
            GuestOfficeCareerRow pCareer, GuestOfficeEventRow pEvent)
        {
            if (!CompleteCareerShape(pAffiliation, pCareer) || pEvent == null ||
                !IsGuestEvent(pEvent.EventType)) return false;
            GuestOfficeEventRow expected = ExpectedEvent(pAffiliation, pCareer,
                pEvent.EventType);
            expected.EventId = pEvent.EventId;
            return pEvent.Exact(expected, pRequireEventId: true);
        }

        private static GuestOfficeEventRow EventTemplate(
            GuestOfficeAffiliationRow pAffiliation, GuestOfficeCareerRow pCareer,
            string pEventType)
        {
            return new GuestOfficeEventRow
            {
                EventType = pEventType ?? "",
                ActorId = pCareer.ActorId,
                TargetActorId = -1L,
                SchoolId = pCareer.SchoolId,
                CityId = pCareer.CityId,
                KingdomId = pCareer.KingdomId,
                EventYear = pAffiliation.ServiceStartYear,
                Importance = 3,
                WorldTime = pCareer.AppointedTime
            };
        }

        private static GuestOfficeEventRow ExpectedEvent(
            GuestOfficeAffiliationRow pAffiliation, GuestOfficeCareerRow pCareer,
            string pEventType)
        {
            GuestOfficeEventRow template = EventTemplate(pAffiliation, pCareer, pEventType);
            string payloadBase = PayloadBase(pCareer.OfficeId,
                pAffiliation.ServiceStartYear, pAffiliation.ServiceEndYear);
            string fingerprint = GuestOfficeTupleFingerprint.Compute(pAffiliation, pCareer,
                template, payloadBase);
            template.OperationKey = GuestOfficeOperationKeyRules.Build(pEventType,
                pCareer.ActorId, pCareer.KingdomId, pCareer.CityId, pCareer.SchoolId,
                pCareer.OfficeId, pAffiliation.ServiceStartYear,
                pAffiliation.ServiceEndYear, fingerprint);
            template.Payload = payloadBase + "|tuple=" + fingerprint;
            return template;
        }

        private static List<GuestOfficeAffiliationRow> ReadAffiliations(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pActorId)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE," +
                "SERVICE_START_YEAR,SERVICE_END_YEAR,LAST_TRAVEL_YEAR," +
                "TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR," +
                "TRANSPORT_FAILURES,UPDATED_TIME FROM " + AffiliationTable +
                " WHERE ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@actor", pActorId);
            var rows = new List<GuestOfficeAffiliationRow>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(ReadAffiliation(reader));
            return rows;
        }

        private static List<GuestOfficeCareerRow> ReadActiveCareers(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pActorId, string pLayer)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = CareerSelect() +
                " WHERE ACTOR_ID=@actor AND LAYER=@layer AND ACTIVE=1";
            command.Parameters.AddWithValue("@actor", pActorId);
            command.Parameters.AddWithValue("@layer", pLayer ?? "");
            return ReadCareers(command);
        }

        private static List<GuestOfficeCareerRow> ReadCareersByOfficerId(
            SQLiteConnection pDb, SQLiteTransaction pTransaction, long pOfficerId)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = CareerSelect() + " WHERE OFFICER_ID=@officer";
            command.Parameters.AddWithValue("@officer", pOfficerId);
            return ReadCareers(command);
        }

        private static string CareerSelect()
        {
            return "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME,CITY_ID,LAYER," +
                   "OFFICE_ID,SCHOOL_ID,INFLUENCE,APPOINTED_YEAR,APPOINTED_TIME," +
                   "ENDED_YEAR,ENDED_TIME,ACTIVE,END_REASON,UPDATED_TIME FROM " +
                   CareerTable;
        }

        private static List<GuestOfficeCareerRow> ReadCareers(SQLiteCommand pCommand)
        {
            var rows = new List<GuestOfficeCareerRow>();
            using SQLiteDataReader reader = pCommand.ExecuteReader();
            while (reader.Read()) rows.Add(ReadCareer(reader));
            return rows;
        }

        private static List<GuestOfficeEventRow> ReadEventsByOperationKey(
            SQLiteConnection pDb, SQLiteTransaction pTransaction, string pOperationKey)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = EventSelect() + " WHERE OPERATION_KEY=@operation" +
                                  " AND OPERATION_KEY<>'' AND " + GuestEventPredicate;
            command.Parameters.AddWithValue("@operation", pOperationKey ?? "");
            return ReadEvents(command);
        }

        private static List<GuestOfficeEventRow> ReadGuestEventsForCareer(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            GuestOfficeAffiliationRow pAffiliation, GuestOfficeCareerRow pCareer)
        {
            string started = ExpectedEvent(pAffiliation, pCareer,
                "guest_service_started").OperationKey;
            string renewed = ExpectedEvent(pAffiliation, pCareer,
                "guest_service_renewed").OperationKey;
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = EventSelect() +
                " WHERE OPERATION_KEY IN (@started,@renewed) AND OPERATION_KEY<>'' AND " +
                GuestEventPredicate + " ORDER BY EVENT_ID";
            command.Parameters.AddWithValue("@started", started);
            command.Parameters.AddWithValue("@renewed", renewed);
            return ReadEvents(command);
        }

        private static string EventSelect()
        {
            return "SELECT EVENT_ID,OPERATION_KEY,EVENT_TYPE,ACTOR_ID,TARGET_ACTOR_ID," +
                   "SCHOOL_ID,CITY_ID,KINGDOM_ID,EVENT_YEAR,PAYLOAD,IMPORTANCE,WORLD_TIME" +
                   " FROM " + EventTable;
        }

        private static List<GuestOfficeEventRow> ReadEvents(SQLiteCommand pCommand)
        {
            var rows = new List<GuestOfficeEventRow>();
            using SQLiteDataReader reader = pCommand.ExecuteReader();
            while (reader.Read()) rows.Add(ReadEvent(reader));
            return rows;
        }

        private static GuestOfficeAffiliationRow ReadAffiliation(SQLiteDataReader pReader)
        {
            return new GuestOfficeAffiliationRow
            {
                ActorId = Long(pReader, 0, -1L),
                HomeKingdomId = Long(pReader, 1, -1L),
                HomeKingdomName = Text(pReader, 2),
                HometownCityId = Long(pReader, 3, -1L),
                ResidenceCityId = Long(pReader, 4, -1L),
                PreviousResidenceCityId = Long(pReader, 5, -1L),
                DestinationCityId = Long(pReader, 6, -1L),
                ServiceKingdomId = Long(pReader, 7, -1L),
                LifecycleState = Text(pReader, 8),
                ServiceStartYear = Int(pReader, 9, -1),
                ServiceEndYear = Int(pReader, 10, -1),
                LastTravelYear = Int(pReader, 11, -1),
                TravelWaitStartYear = Int(pReader, 12, -1),
                VoyageStartYear = Int(pReader, 13, -1),
                VoyageArrivalYear = Int(pReader, 14, -1),
                TransportFailures = Int(pReader, 15, 0),
                UpdatedTime = Double(pReader, 16, -1d)
            };
        }

        private static GuestOfficeCareerRow ReadCareer(SQLiteDataReader pReader)
        {
            return new GuestOfficeCareerRow
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

        private static GuestOfficeEventRow ReadEvent(SQLiteDataReader pReader)
        {
            return new GuestOfficeEventRow
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

        private static void BindAffiliation(SQLiteCommand pCommand, string pPrefix,
            GuestOfficeAffiliationRow pRecord, bool includeActor)
        {
            string prefix = "@" + pPrefix;
            if (includeActor)
                pCommand.Parameters.AddWithValue(prefix + "Actor", pRecord.ActorId);
            pCommand.Parameters.AddWithValue(prefix + "Home", pRecord.HomeKingdomId);
            pCommand.Parameters.AddWithValue(prefix + "HomeName", pRecord.HomeKingdomName);
            pCommand.Parameters.AddWithValue(prefix + "Hometown", pRecord.HometownCityId);
            pCommand.Parameters.AddWithValue(prefix + "Residence", pRecord.ResidenceCityId);
            pCommand.Parameters.AddWithValue(prefix + "Previous",
                pRecord.PreviousResidenceCityId);
            pCommand.Parameters.AddWithValue(prefix + "Destination",
                pRecord.DestinationCityId);
            pCommand.Parameters.AddWithValue(prefix + "Service", pRecord.ServiceKingdomId);
            pCommand.Parameters.AddWithValue(prefix + "State", pRecord.LifecycleState);
            pCommand.Parameters.AddWithValue(prefix + "Start", pRecord.ServiceStartYear);
            pCommand.Parameters.AddWithValue(prefix + "End", pRecord.ServiceEndYear);
            pCommand.Parameters.AddWithValue(prefix + "Last", pRecord.LastTravelYear);
            pCommand.Parameters.AddWithValue(prefix + "Wait", pRecord.TravelWaitStartYear);
            pCommand.Parameters.AddWithValue(prefix + "VoyageStart", pRecord.VoyageStartYear);
            pCommand.Parameters.AddWithValue(prefix + "VoyageArrival",
                pRecord.VoyageArrivalYear);
            pCommand.Parameters.AddWithValue(prefix + "Failures", pRecord.TransportFailures);
            pCommand.Parameters.AddWithValue(prefix + "Time", pRecord.UpdatedTime);
        }

        private static long NextId(SQLiteConnection pDb, SQLiteTransaction pTransaction,
            string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn + "),0)+1 FROM " + pTable;
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 1L : Convert.ToInt64(value);
        }

        private static string PayloadBase(string pOfficeId, int pStartYear, int pEndYear)
        {
            return (pOfficeId ?? "") + "|" +
                   pStartYear.ToString(CultureInfo.InvariantCulture) + "|" +
                   pEndYear.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsGuestEvent(string pEventType)
        {
            return pEventType == "guest_service_started" ||
                   pEventType == "guest_service_renewed";
        }

        private static bool Finite(double pValue)
        {
            return !double.IsNaN(pValue) && !double.IsInfinity(pValue);
        }

        private static bool FiniteNonNegative(double pValue)
        {
            return Finite(pValue) && pValue >= 0d;
        }

        private static GuestOfficeDbStartResult Unknown(
            GuestOfficeDbStartRequest pRequest)
        {
            return new GuestOfficeDbStartResult(GuestOfficePersistenceOutcome.Unknown,
                pRequest, pRecoveredExisting: false);
        }

        private static GuestOfficeDbRecoveryResult Retry()
        {
            return new GuestOfficeDbRecoveryResult(GuestOfficeRecoveryDecision.Retry,
                null, null, null);
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

        private static double Double(SQLiteDataReader pReader, int pOrdinal,
            double pDefault)
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
