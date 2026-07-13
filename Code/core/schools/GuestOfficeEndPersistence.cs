using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.schools
{
    internal sealed class GuestOfficeEndRequest
    {
        public GuestOfficeEndRequest(GuestOfficeAffiliationRow pOriginalAffiliation,
            GuestOfficeAffiliationRow pDesiredAffiliation,
            OfficialCareerCloseToken pCareerToken, int pEndedYear,
            double pEndedTime, string pEndReason)
        {
            OriginalAffiliation = pOriginalAffiliation ??
                throw new ArgumentNullException(nameof(pOriginalAffiliation));
            DesiredAffiliation = pDesiredAffiliation ??
                throw new ArgumentNullException(nameof(pDesiredAffiliation));
            CareerToken = pCareerToken ?? throw new ArgumentNullException(nameof(pCareerToken));
            EndedYear = pEndedYear;
            EndedTime = pEndedTime;
            EndReason = pEndReason ?? "";
        }

        public GuestOfficeAffiliationRow OriginalAffiliation { get; }
        public GuestOfficeAffiliationRow DesiredAffiliation { get; }
        public OfficialCareerCloseToken CareerToken { get; }
        public long ActorId => OriginalAffiliation.ActorId;
        public long HostKingdomId => OriginalAffiliation.ServiceKingdomId;
        public long CityId => OriginalAffiliation.ResidenceCityId;
        public string OfficeId => CareerToken.Original.OfficeId;
        public string SchoolId => CareerToken.Original.SchoolId;
        public int EndedYear { get; }
        public double EndedTime { get; }
        public string EndReason { get; }
    }

    internal sealed class GuestOfficeEndResult
    {
        public GuestOfficeEndResult(GuestOfficePersistenceOutcome pOutcome,
            HistoricalSchoolAffiliationSnapshot pAffiliation,
            OfficialCareerCloseResult pCareer, bool pRecoveredExisting)
        {
            Persistence = new GuestOfficePersistenceResult(pOutcome);
            Affiliation = pAffiliation;
            Career = pCareer;
            RecoveredExisting = pRecoveredExisting;
        }

        public GuestOfficePersistenceResult Persistence { get; }
        public HistoricalSchoolAffiliationSnapshot Affiliation { get; }
        public OfficialCareerCloseResult Career { get; }
        public bool RecoveredExisting { get; }
    }

    internal sealed class GuestOfficeEndRecoveryResult
    {
        public GuestOfficeEndRecoveryResult(GuestOfficePersistenceOutcome pOutcome,
            HistoricalSchoolAffiliationSnapshot pAffiliation, long pHostKingdomId,
            string pOfficeId, string pEndReason)
        {
            Persistence = new GuestOfficePersistenceResult(pOutcome);
            Affiliation = pAffiliation;
            HostKingdomId = pHostKingdomId;
            OfficeId = pOfficeId ?? "";
            EndReason = pEndReason ?? "";
        }

        public GuestOfficePersistenceResult Persistence { get; }
        public HistoricalSchoolAffiliationSnapshot Affiliation { get; }
        public long HostKingdomId { get; }
        public string OfficeId { get; }
        public string EndReason { get; }
    }

    internal static class GuestOfficeEndPersistence
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        internal static GuestOfficeEndRequest PrepareEnd(
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation, string pEndReason,
            int pEndedYear, double pEndedTime)
        {
            return PrepareEnd(DB, pExpectedAffiliation, pEndReason, pEndedYear, pEndedTime);
        }

        internal static GuestOfficeEndRequest PrepareEnd(SQLiteConnection pDb,
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation, string pEndReason,
            int pEndedYear, double pEndedTime)
        {
            if (pDb == null || !ValidExpected(pExpectedAffiliation, pEndedYear,
                    pEndedTime)) return null;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                List<GuestOfficeAffiliationRow> rows = ReadAffiliations(pDb, transaction,
                    pExpectedAffiliation.ActorId);
                GuestOfficeAffiliationRow expected = AffiliationRow(pExpectedAffiliation);
                if (rows.Count != 1 || !rows[0].ExactExceptUpdatedTime(expected))
                {
                    transaction.Rollback();
                    return null;
                }

                GuestOfficeAffiliationRow original = rows[0];
                if (original.LifecycleState != "Serving" ||
                    original.ServiceKingdomId < 0 || original.ServiceStartYear < 0)
                {
                    transaction.Rollback();
                    return null;
                }
                GuestOfficeAffiliationRow desired = ClosedAffiliation(original,
                    pEndedYear, pEndedTime);
                var close = new OfficialCareerCloseRequest(original.ActorId,
                    original.ServiceKingdomId, CourtOfficeLayer.Central, null,
                    pEndedYear, pEndedTime, pEndReason ?? "");
                OfficialCareerCloseToken career = OfficialCareerPersistence.CaptureClose(
                    pDb, transaction, close);
                if (!GuestCareerMatches(original, career.Original))
                {
                    transaction.Rollback();
                    return null;
                }
                transaction.Commit();
                return new GuestOfficeEndRequest(original, desired, career, pEndedYear,
                    pEndedTime, pEndReason);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Guest office end preparation failed: " + error.Message);
                return null;
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static GuestOfficeEndResult End(GuestOfficeEndRequest pRequest)
        {
            return End(DB, pRequest);
        }

        internal static GuestOfficeEndResult End(SQLiteConnection pDb,
            GuestOfficeEndRequest pRequest)
        {
            if (pDb == null || !Valid(pRequest)) return Unknown(pRequest);
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                GuestOfficePersistenceOutcome before = Readback(pDb, transaction,
                    pRequest);
                if (before == GuestOfficePersistenceOutcome.Committed)
                {
                    transaction.Commit();
                    return Project(pRequest, before, pRecoveredExisting: true);
                }
                if (before != GuestOfficePersistenceOutcome.CleanFailure)
                {
                    transaction.Commit();
                    return Project(pRequest, before, pRecoveredExisting: false);
                }

                OfficialCareerPersistence.StageClose(pDb, transaction,
                    pRequest.CareerToken);
                StageAffiliation(pDb, transaction, pRequest);
                transaction.Commit();
                return Project(pRequest, GuestOfficePersistenceOutcome.Committed,
                    pRecoveredExisting: false);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Guest office end transaction failed: " + error.Message);
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            GuestOfficePersistenceOutcome outcome = Readback(pDb, pRequest);
            return Project(pRequest, outcome,
                pRecoveredExisting: outcome == GuestOfficePersistenceOutcome.Committed);
        }

        internal static GuestOfficeEndRecoveryResult ReadCommittedEnd(
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation,
            long pHostKingdomId, string pLayer, string pOfficeId)
        {
            return ReadCommittedEnd(DB, pExpectedAffiliation, pHostKingdomId, pLayer,
                pOfficeId);
        }

        internal static GuestOfficeEndRecoveryResult ReadCommittedEnd(SQLiteConnection pDb,
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation,
            long pHostKingdomId, string pLayer, string pOfficeId)
        {
            if (pDb == null || !ValidClosedExpected(pExpectedAffiliation) ||
                pHostKingdomId < -1L || pLayer != CourtOfficeLayer.Central)
                return UnknownRecovery();
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                List<GuestOfficeAffiliationRow> affiliations = ReadAffiliations(pDb,
                    transaction, pExpectedAffiliation.ActorId);
                GuestOfficeAffiliationRow expected = AffiliationRow(pExpectedAffiliation);
                if (affiliations.Count != 1 ||
                    !affiliations[0].ExactExceptUpdatedTime(expected) ||
                    !ClosedAffiliationShape(affiliations[0]))
                {
                    transaction.Commit();
                    return UnknownRecovery();
                }

                GuestOfficeAffiliationRow affiliation = affiliations[0];
                List<GuestOfficeCareerRow> active = ReadCareerRows(pDb, transaction,
                    "ACTOR_ID=@actor AND LAYER=@layer AND ACTIVE=1",
                    command =>
                    {
                        command.Parameters.AddWithValue("@actor",
                            pExpectedAffiliation.ActorId);
                        command.Parameters.AddWithValue("@layer", pLayer);
                    });
                if (active.Count == 1)
                {
                    transaction.Commit();
                    long activeHost = pHostKingdomId >= 0
                        ? pHostKingdomId
                        : active[0].KingdomId;
                    string activeOffice = string.IsNullOrEmpty(pOfficeId)
                        ? active[0].OfficeId
                        : pOfficeId;
                    if (LooksLikeUnclosedGuestCareer(affiliation, active[0],
                            activeHost, pLayer, activeOffice))
                        return UnknownRecovery();
                    return new GuestOfficeEndRecoveryResult(
                        GuestOfficePersistenceOutcome.CleanFailure, null,
                        pHostKingdomId, pOfficeId, "");
                }
                if (active.Count != 0)
                {
                    transaction.Commit();
                    return UnknownRecovery();
                }

                string closedPredicate = "ACTOR_ID=@actor AND LAYER=@layer " +
                    "AND ACTIVE=0 AND ENDED_YEAR=@year AND ENDED_TIME=@time " +
                    "AND UPDATED_TIME=@time";
                if (pHostKingdomId >= 0) closedPredicate += " AND KINGDOM_ID=@kingdom";
                if (!string.IsNullOrEmpty(pOfficeId))
                    closedPredicate += " AND OFFICE_ID=@office";
                List<GuestOfficeCareerRow> closed = ReadCareerRows(pDb, transaction,
                    closedPredicate,
                    command =>
                    {
                        command.Parameters.AddWithValue("@actor", affiliation.ActorId);
                        command.Parameters.AddWithValue("@layer", pLayer);
                        command.Parameters.AddWithValue("@year",
                            affiliation.ServiceEndYear);
                        command.Parameters.AddWithValue("@time", affiliation.UpdatedTime);
                        if (pHostKingdomId >= 0)
                            command.Parameters.AddWithValue("@kingdom", pHostKingdomId);
                        if (!string.IsNullOrEmpty(pOfficeId))
                            command.Parameters.AddWithValue("@office", pOfficeId);
                    });
                long closedHost = closed.Count == 1 && pHostKingdomId < 0
                    ? closed[0].KingdomId
                    : pHostKingdomId;
                string closedOffice = closed.Count == 1 && string.IsNullOrEmpty(pOfficeId)
                    ? closed[0].OfficeId
                    : pOfficeId;
                if (closed.Count != 1 ||
                    !CompleteClosedCareer(affiliation, closed[0], closedHost,
                        pLayer, closedOffice))
                {
                    transaction.Commit();
                    return UnknownRecovery();
                }

                transaction.Commit();
                return new GuestOfficeEndRecoveryResult(
                    GuestOfficePersistenceOutcome.Committed, Snapshot(affiliation),
                    closedHost, closedOffice, closed[0].EndReason);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Guest office committed end recovery failed: " +
                                    error.Message);
                return UnknownRecovery();
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }
        }

        internal static GuestOfficePersistenceOutcome Readback(SQLiteConnection pDb,
            GuestOfficeEndRequest pRequest)
        {
            if (pDb == null || !Valid(pRequest))
                return GuestOfficePersistenceOutcome.Unknown;
            try
            {
                return Readback(pDb, null, pRequest);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Guest office end readback failed: " + error.Message);
                return GuestOfficePersistenceOutcome.Unknown;
            }
        }

        internal static void StageAffiliation(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeEndRequest pRequest)
        {
            if (pDb == null || pTransaction == null || !Valid(pRequest))
                throw new ArgumentException("invalid guest affiliation close stage");
            GuestOfficeAffiliationRow desired = pRequest.DesiredAffiliation;
            GuestOfficeAffiliationRow original = pRequest.OriginalAffiliation;
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "UPDATE " + SchoolAffiliationTableItem.GetTableName() +
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
                " AND RESIDENCE_CITY_ID=@oResidence" +
                " AND PREVIOUS_RESIDENCE_CITY_ID=@oPrevious" +
                " AND DESTINATION_CITY_ID=@oDestination" +
                " AND SERVICE_KINGDOM_ID=@oService AND LIFECYCLE_STATE=@oState" +
                " AND SERVICE_START_YEAR=@oStart AND SERVICE_END_YEAR=@oEnd" +
                " AND LAST_TRAVEL_YEAR=@oLast AND TRAVEL_WAIT_START_YEAR=@oWait" +
                " AND VOYAGE_START_YEAR=@oVoyageStart" +
                " AND VOYAGE_ARRIVAL_YEAR=@oVoyageArrival" +
                " AND TRANSPORT_FAILURES=@oFailures AND UPDATED_TIME=@oTime";
            BindAffiliation(command, "d", desired, includeActor: false);
            BindAffiliation(command, "o", original, includeActor: true);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("exact guest affiliation close failed");
        }

        private static GuestOfficePersistenceOutcome Readback(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeEndRequest pRequest)
        {
            GuestOfficeProjectionState affiliation = AffiliationState(pDb, pTransaction,
                pRequest);
            OfficialCareerPersistenceOutcome career =
                OfficialCareerPersistence.ReadbackClose(pDb, pTransaction,
                    pRequest.CareerToken);
            return GuestOfficeEndReadbackRules.Resolve(pQuerySucceeded: true,
                affiliation, career);
        }

        private static GuestOfficeProjectionState AffiliationState(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, GuestOfficeEndRequest pRequest)
        {
            List<GuestOfficeAffiliationRow> rows = ReadAffiliations(pDb, pTransaction,
                pRequest.ActorId);
            if (rows.Count != 1) return GuestOfficeProjectionState.Conflict;
            bool original = rows[0].Exact(pRequest.OriginalAffiliation);
            bool desired = rows[0].Exact(pRequest.DesiredAffiliation);
            if (original && desired) return GuestOfficeProjectionState.Both;
            if (desired) return GuestOfficeProjectionState.Desired;
            return original ? GuestOfficeProjectionState.Original :
                GuestOfficeProjectionState.Conflict;
        }

        private static GuestOfficeEndResult Project(GuestOfficeEndRequest pRequest,
            GuestOfficePersistenceOutcome pOutcome, bool pRecoveredExisting)
        {
            OfficialCareerPersistenceOutcome careerOutcome =
                pOutcome == GuestOfficePersistenceOutcome.Committed
                    ? OfficialCareerPersistenceOutcome.Committed
                    : pOutcome == GuestOfficePersistenceOutcome.CleanFailure
                        ? OfficialCareerPersistenceOutcome.CleanFailure
                        : OfficialCareerPersistenceOutcome.Unknown;
            return new GuestOfficeEndResult(pOutcome,
                pOutcome == GuestOfficePersistenceOutcome.Committed
                    ? Snapshot(pRequest.DesiredAffiliation)
                    : null,
                OfficialCareerPersistence.ResultForClose(pRequest.CareerToken,
                    careerOutcome), pRecoveredExisting);
        }

        private static GuestOfficeEndResult Unknown(GuestOfficeEndRequest pRequest)
        {
            return new GuestOfficeEndResult(GuestOfficePersistenceOutcome.Unknown, null,
                OfficialCareerPersistence.ResultForClose(pRequest?.CareerToken,
                    OfficialCareerPersistenceOutcome.Unknown),
                pRecoveredExisting: false);
        }

        private static GuestOfficeAffiliationRow ClosedAffiliation(
            GuestOfficeAffiliationRow pOriginal, int pEndedYear, double pEndedTime)
        {
            GuestOfficeAffiliationRow desired = pOriginal.Copy();
            desired.DestinationCityId = -1L;
            desired.ServiceKingdomId = -1L;
            desired.LifecycleState = desired.ResidenceCityId == desired.HometownCityId
                ? "AtHome"
                : "Resident";
            desired.ServiceStartYear = -1;
            desired.ServiceEndYear = Math.Max(pOriginal.ServiceStartYear, pEndedYear);
            desired.UpdatedTime = pEndedTime;
            return desired;
        }

        private static bool GuestCareerMatches(GuestOfficeAffiliationRow pAffiliation,
            OfficialCareerRecord pCareer)
        {
            return pAffiliation != null && pCareer != null && pCareer.Active == 1 &&
                   pCareer.ActorId == pAffiliation.ActorId &&
                   pCareer.KingdomId == pAffiliation.ServiceKingdomId &&
                   pCareer.CityId == pAffiliation.ResidenceCityId &&
                   pCareer.Layer == CourtOfficeLayer.Central &&
                   pCareer.AppointedYear == pAffiliation.ServiceStartYear &&
                   pCareer.AppointedTime.Equals(pAffiliation.UpdatedTime) &&
                   pCareer.UpdatedTime.Equals(pCareer.AppointedTime) &&
                   pCareer.EndedYear == -1 && pCareer.EndedTime.Equals(-1d) &&
                   pCareer.EndReason == "";
        }

        private static bool CompleteClosedCareer(
            GuestOfficeAffiliationRow pAffiliation, GuestOfficeCareerRow pCareer,
            long pHostKingdomId, string pLayer, string pOfficeId)
        {
            return pAffiliation != null && pCareer != null && pCareer.Active == 0 &&
                   pCareer.ActorId == pAffiliation.ActorId &&
                   pCareer.KingdomId == pHostKingdomId &&
                   pCareer.CityId == pAffiliation.ResidenceCityId &&
                   pCareer.Layer == pLayer && pCareer.OfficeId == pOfficeId &&
                   pCareer.AppointedYear <= pCareer.EndedYear &&
                   pCareer.EndedYear == pAffiliation.ServiceEndYear &&
                   pCareer.EndedTime.Equals(pAffiliation.UpdatedTime) &&
                   pCareer.UpdatedTime.Equals(pAffiliation.UpdatedTime);
        }

        private static bool LooksLikeUnclosedGuestCareer(
            GuestOfficeAffiliationRow pAffiliation, GuestOfficeCareerRow pCareer,
            long pHostKingdomId, string pLayer, string pOfficeId)
        {
            return pAffiliation != null && pCareer != null && pCareer.Active == 1 &&
                   pCareer.ActorId == pAffiliation.ActorId &&
                   pCareer.KingdomId == pHostKingdomId &&
                   pCareer.CityId == pAffiliation.ResidenceCityId &&
                   pCareer.Layer == pLayer && pCareer.OfficeId == pOfficeId &&
                   (pCareer.AppointedYear < pAffiliation.ServiceEndYear ||
                    pCareer.AppointedTime < pAffiliation.UpdatedTime);
        }

        private static bool ClosedAffiliationShape(GuestOfficeAffiliationRow pAffiliation)
        {
            return pAffiliation != null && pAffiliation.ServiceKingdomId == -1L &&
                   pAffiliation.ServiceStartYear == -1 &&
                   pAffiliation.ServiceEndYear >= 0 &&
                   (pAffiliation.LifecycleState == "AtHome" ||
                    pAffiliation.LifecycleState == "Resident") &&
                   FiniteNonNegative(pAffiliation.UpdatedTime);
        }

        private static bool ValidExpected(HistoricalSchoolAffiliationSnapshot pExpected,
            int pEndedYear, double pEndedTime)
        {
            return pExpected != null && pExpected.ActorId >= 0 &&
                   pExpected.ServiceKingdomId >= 0 &&
                   pExpected.LifecycleState == HistoricalSchoolLifecycleState.Serving &&
                   pExpected.ServiceStartYear >= 0 &&
                   pEndedYear >= pExpected.ServiceStartYear &&
                   FiniteNonNegative(pEndedTime);
        }

        private static bool ValidClosedExpected(
            HistoricalSchoolAffiliationSnapshot pExpected)
        {
            return pExpected != null && pExpected.ActorId >= 0 &&
                   pExpected.ServiceKingdomId == -1L &&
                   pExpected.ServiceStartYear == -1 && pExpected.ServiceEndYear >= 0 &&
                   (pExpected.LifecycleState == HistoricalSchoolLifecycleState.AtHome ||
                    pExpected.LifecycleState == HistoricalSchoolLifecycleState.Resident);
        }

        private static bool Valid(GuestOfficeEndRequest pRequest)
        {
            if (pRequest?.OriginalAffiliation == null ||
                pRequest.DesiredAffiliation == null || pRequest.CareerToken == null ||
                pRequest.ActorId < 0 || pRequest.HostKingdomId < 0 ||
                pRequest.EndedYear < 0 || !FiniteNonNegative(pRequest.EndedTime))
                return false;
            GuestOfficeAffiliationRow original = pRequest.OriginalAffiliation;
            GuestOfficeAffiliationRow expectedDesired = ClosedAffiliation(original,
                pRequest.EndedYear, pRequest.EndedTime);
            return original.LifecycleState == "Serving" &&
                   original.ServiceStartYear >= 0 &&
                   pRequest.EndedYear >= original.ServiceStartYear &&
                   pRequest.DesiredAffiliation.Exact(expectedDesired) &&
                   GuestCareerMatches(original, pRequest.CareerToken.Original) &&
                   pRequest.CareerToken.Desired.Active == 0 &&
                   pRequest.CareerToken.Desired.EndedYear == pRequest.EndedYear &&
                   pRequest.CareerToken.Desired.EndedTime.Equals(pRequest.EndedTime) &&
                   pRequest.CareerToken.Desired.EndReason == pRequest.EndReason &&
                   pRequest.CareerToken.Desired.UpdatedTime.Equals(pRequest.EndedTime);
        }

        private static List<GuestOfficeAffiliationRow> ReadAffiliations(
            SQLiteConnection pDb, SQLiteTransaction pTransaction, long pActorId)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT ACTOR_ID,HOME_KINGDOM_ID,HOME_KINGDOM_NAME," +
                "HOMETOWN_CITY_ID,RESIDENCE_CITY_ID,PREVIOUS_RESIDENCE_CITY_ID," +
                "DESTINATION_CITY_ID,SERVICE_KINGDOM_ID,LIFECYCLE_STATE," +
                "SERVICE_START_YEAR,SERVICE_END_YEAR,LAST_TRAVEL_YEAR," +
                "TRAVEL_WAIT_START_YEAR,VOYAGE_START_YEAR,VOYAGE_ARRIVAL_YEAR," +
                "TRANSPORT_FAILURES,UPDATED_TIME FROM " +
                SchoolAffiliationTableItem.GetTableName() + " WHERE ACTOR_ID=@actor";
            command.Parameters.AddWithValue("@actor", pActorId);
            var rows = new List<GuestOfficeAffiliationRow>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(ReadAffiliation(reader));
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

        private static List<GuestOfficeCareerRow> ReadCareerRows(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pPredicate,
            Action<SQLiteCommand> pBind)
        {
            using var command = new SQLiteCommand(pDb) { Transaction = pTransaction };
            command.CommandText = "SELECT OFFICER_ID,KINGDOM_ID,ACTOR_ID,ACTOR_NAME," +
                "CITY_ID,LAYER,OFFICE_ID,SCHOOL_ID,INFLUENCE,APPOINTED_YEAR," +
                "APPOINTED_TIME,ENDED_YEAR,ENDED_TIME,ACTIVE,END_REASON,UPDATED_TIME " +
                "FROM " + CourtOfficerTableItem.GetTableName() + " WHERE " + pPredicate;
            pBind?.Invoke(command);
            var rows = new List<GuestOfficeCareerRow>();
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new GuestOfficeCareerRow
                {
                    OfficerId = Long(reader, 0, -1L),
                    KingdomId = Long(reader, 1, -1L),
                    ActorId = Long(reader, 2, -1L),
                    ActorName = Text(reader, 3),
                    CityId = Long(reader, 4, -1L),
                    Layer = Text(reader, 5),
                    OfficeId = Text(reader, 6),
                    SchoolId = Text(reader, 7),
                    Influence = Double(reader, 8, 0d),
                    AppointedYear = Int(reader, 9, -1),
                    AppointedTime = Double(reader, 10, -1d),
                    EndedYear = Int(reader, 11, -1),
                    EndedTime = Double(reader, 12, -1d),
                    Active = Int(reader, 13, 0),
                    EndReason = Text(reader, 14),
                    UpdatedTime = Double(reader, 15, -1d)
                });
            }
            return rows;
        }

        private static GuestOfficeAffiliationRow AffiliationRow(
            HistoricalSchoolAffiliationSnapshot pSnapshot)
        {
            return new GuestOfficeAffiliationRow
            {
                ActorId = pSnapshot.ActorId,
                HomeKingdomId = pSnapshot.HomeKingdomId,
                HomeKingdomName = pSnapshot.HomeKingdomName,
                HometownCityId = pSnapshot.HometownCityId,
                ResidenceCityId = pSnapshot.ResidenceCityId,
                PreviousResidenceCityId = pSnapshot.PreviousResidenceCityId,
                DestinationCityId = pSnapshot.DestinationCityId,
                ServiceKingdomId = pSnapshot.ServiceKingdomId,
                LifecycleState = pSnapshot.LifecycleState.ToString(),
                ServiceStartYear = pSnapshot.ServiceStartYear,
                ServiceEndYear = pSnapshot.ServiceEndYear,
                LastTravelYear = pSnapshot.LastTravelYear,
                TravelWaitStartYear = pSnapshot.TravelWaitStartYear,
                VoyageStartYear = pSnapshot.VoyageStartYear,
                VoyageArrivalYear = pSnapshot.VoyageArrivalYear,
                TransportFailures = pSnapshot.TransportFailures,
                UpdatedTime = 0d
            };
        }

        private static HistoricalSchoolAffiliationSnapshot Snapshot(
            GuestOfficeAffiliationRow pRow)
        {
            if (pRow == null || !Enum.TryParse(pRow.LifecycleState,
                    out HistoricalSchoolLifecycleState state)) return null;
            return new HistoricalSchoolAffiliationSnapshot(pRow.ActorId,
                pRow.HomeKingdomId, pRow.HomeKingdomName, pRow.HometownCityId,
                pRow.ResidenceCityId, pRow.PreviousResidenceCityId,
                pRow.DestinationCityId, pRow.ServiceKingdomId, state,
                pRow.ServiceStartYear, pRow.ServiceEndYear, pRow.LastTravelYear,
                pRow.TravelWaitStartYear, pRow.VoyageStartYear,
                pRow.VoyageArrivalYear, pRow.TransportFailures);
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
            pCommand.Parameters.AddWithValue(prefix + "VoyageStart",
                pRecord.VoyageStartYear);
            pCommand.Parameters.AddWithValue(prefix + "VoyageArrival",
                pRecord.VoyageArrivalYear);
            pCommand.Parameters.AddWithValue(prefix + "Failures",
                pRecord.TransportFailures);
            pCommand.Parameters.AddWithValue(prefix + "Time", pRecord.UpdatedTime);
        }

        private static bool FiniteNonNegative(double pValue)
        {
            return !double.IsNaN(pValue) && !double.IsInfinity(pValue) && pValue >= 0d;
        }

        private static GuestOfficeEndRecoveryResult UnknownRecovery()
        {
            return new GuestOfficeEndRecoveryResult(GuestOfficePersistenceOutcome.Unknown,
                null, -1L, "", "");
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
