using System;
using System.Data.SQLite;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.schools
{
    internal sealed class GuestOfficeStartRequest
    {
        internal GuestOfficeStartRequest(
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation,
            OfficialCareerAppointment pCareerAppointment, string pEventType,
            string pSchoolId, string pOfficeId, long pHostKingdomId, long pCityId,
            int pStartYear, int pEndYear, double pWorldTime)
            : this(pExpectedAffiliation, pCareerAppointment, pEventType, pSchoolId,
                pOfficeId, pHostKingdomId, pCityId, pStartYear, pEndYear, pWorldTime, null)
        {
        }

        internal GuestOfficeStartRequest(
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation,
            OfficialCareerAppointment pCareerAppointment, string pEventType,
            string pSchoolId, string pOfficeId, long pHostKingdomId, long pCityId,
            int pStartYear, int pEndYear, double pWorldTime,
            GuestOfficeDbStartRequest pDbRequest)
        {
            ExpectedAffiliation = pExpectedAffiliation;
            CareerAppointment = pCareerAppointment;
            EventType = pEventType ?? "";
            SchoolId = pSchoolId ?? "";
            OfficeId = pOfficeId ?? "";
            HostKingdomId = pHostKingdomId;
            CityId = pCityId;
            StartYear = pStartYear;
            EndYear = pEndYear;
            WorldTime = FiniteTime(pWorldTime);
            DbRequest = pDbRequest;
        }

        public HistoricalSchoolAffiliationSnapshot ExpectedAffiliation { get; }
        public OfficialCareerAppointment CareerAppointment { get; }
        public string EventType { get; }
        public string SchoolId { get; }
        public string OfficeId { get; }
        public long HostKingdomId { get; }
        public long CityId { get; }
        public int StartYear { get; }
        public int EndYear { get; }
        public double WorldTime { get; }
        public string OperationKey => DbRequest?.OperationKey ?? "";
        internal GuestOfficeDbStartRequest DbRequest { get; set; }

        private static double FiniteTime(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue) || pValue < 0d
                ? 0d
                : pValue;
        }
    }

    internal sealed class GuestOfficeStartResult
    {
        public GuestOfficeStartResult(GuestOfficePersistenceOutcome pOutcome,
            HistoricalSchoolAffiliationSnapshot pAffiliation,
            OfficialCareerAppointmentResult pCareer, string pOperationKey,
            bool pRecoveredExisting)
        {
            Persistence = new GuestOfficePersistenceResult(pOutcome);
            Affiliation = pAffiliation;
            Career = pCareer;
            OperationKey = pOperationKey ?? "";
            RecoveredExisting = pRecoveredExisting;
        }

        public GuestOfficePersistenceResult Persistence { get; }
        public HistoricalSchoolAffiliationSnapshot Affiliation { get; }
        public OfficialCareerAppointmentResult Career { get; }
        public string OperationKey { get; }
        public bool RecoveredExisting { get; }
    }

    internal sealed class GuestOfficeRecoveryResult
    {
        public GuestOfficeRecoveryResult(GuestOfficeRecoveryDecision pDecision,
            HistoricalSchoolAffiliationSnapshot pAffiliation, string pOfficeId,
            string pSchoolId)
        {
            Decision = pDecision;
            Affiliation = pAffiliation;
            OfficeId = pOfficeId ?? "";
            SchoolId = pSchoolId ?? "";
        }

        public GuestOfficeRecoveryDecision Decision { get; }
        public HistoricalSchoolAffiliationSnapshot Affiliation { get; }
        public string OfficeId { get; }
        public string SchoolId { get; }
    }

    internal static class GuestOfficePersistence
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        internal static GuestOfficeStartRequest PrepareStart(
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation,
            OfficialCareerAppointment pCareerAppointment, string pEventType,
            string pSchoolId, string pOfficeId, long pHostKingdomId, long pCityId,
            int pStartYear, int pEndYear, double pWorldTime)
        {
            return PrepareStart(DB, pExpectedAffiliation, pCareerAppointment, pEventType,
                pSchoolId, pOfficeId, pHostKingdomId, pCityId, pStartYear, pEndYear,
                pWorldTime);
        }

        internal static GuestOfficeStartRequest PrepareStart(SQLiteConnection pDb,
            HistoricalSchoolAffiliationSnapshot pExpectedAffiliation,
            OfficialCareerAppointment pCareerAppointment, string pEventType,
            string pSchoolId, string pOfficeId, long pHostKingdomId, long pCityId,
            int pStartYear, int pEndYear, double pWorldTime)
        {
            var request = new GuestOfficeStartRequest(pExpectedAffiliation,
                pCareerAppointment, pEventType, pSchoolId, pOfficeId, pHostKingdomId,
                pCityId, pStartYear, pEndYear, pWorldTime);
            if (pDb == null || !Valid(request)) return null;
            try
            {
                request.DbRequest = GuestOfficePersistenceDb.PrepareStart(pDb,
                    Seed(request));
                return request.DbRequest == null ? null : request;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Guest office start preparation failed: " +
                                    error.Message);
                return null;
            }
        }

        internal static GuestOfficeStartResult Start(GuestOfficeStartRequest pRequest)
        {
            return Start(DB, pRequest);
        }

        internal static GuestOfficeStartResult Start(SQLiteConnection pDb,
            GuestOfficeStartRequest pRequest)
        {
            if (pDb == null || !Valid(pRequest)) return Unknown(pRequest?.OperationKey);
            try
            {
                if (pRequest.DbRequest == null)
                    pRequest.DbRequest = GuestOfficePersistenceDb.PrepareStart(pDb,
                        Seed(pRequest));
                if (pRequest.DbRequest == null) return Unknown(pRequest.OperationKey);
                GuestOfficeDbStartResult result = GuestOfficePersistenceDb.Start(pDb,
                    pRequest.DbRequest);
                return Project(result, pRequest);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Guest office transaction failed: " + error.Message);
                return Unknown(pRequest.OperationKey);
            }
        }

        internal static GuestOfficeRecoveryResult ReadCommittedTuple(long pActorId,
            HistoricalSchoolAffiliationSnapshot pExpected)
        {
            return ReadCommittedTuple(DB, pActorId, pExpected);
        }

        internal static GuestOfficeRecoveryResult ReadCommittedTuple(SQLiteConnection pDb,
            long pActorId, HistoricalSchoolAffiliationSnapshot pExpected)
        {
            if (pDb == null || pActorId < 0 || pExpected == null ||
                pExpected.ActorId != pActorId || pExpected.LifecycleState !=
                HistoricalSchoolLifecycleState.Serving) return Retry();
            try
            {
                GuestOfficeDbRecoveryResult result =
                    GuestOfficePersistenceDb.ReadCommittedTuple(pDb, pActorId,
                        AffiliationRow(pExpected));
                if (result.Decision != GuestOfficeRecoveryDecision.Adopt ||
                    result.Affiliation == null || result.Career == null) return Retry();
                return new GuestOfficeRecoveryResult(result.Decision,
                    Snapshot(result.Affiliation), result.Career.OfficeId,
                    result.Career.SchoolId);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Guest office recovery query failed: " + error.Message);
                return Retry();
            }
        }

        private static GuestOfficeStartResult Project(GuestOfficeDbStartResult pResult,
            GuestOfficeStartRequest pRequest)
        {
            if (pResult == null) return Unknown(pRequest?.OperationKey);
            OfficialCareerPersistenceOutcome careerOutcome =
                pResult.Outcome == GuestOfficePersistenceOutcome.Committed
                    ? OfficialCareerPersistenceOutcome.Committed
                    : pResult.Outcome == GuestOfficePersistenceOutcome.CleanFailure
                        ? OfficialCareerPersistenceOutcome.CleanFailure
                        : OfficialCareerPersistenceOutcome.Unknown;
            OfficialCareerMutation mutation = pResult.RecoveredExisting
                ? OfficialCareerMutation.Refreshed
                : OfficialCareerMutation.Started;
            return new GuestOfficeStartResult(pResult.Outcome,
                pResult.Outcome == GuestOfficePersistenceOutcome.Committed
                    ? Snapshot(pResult.Request.DesiredAffiliation)
                    : null,
                new OfficialCareerAppointmentResult(careerOutcome, mutation),
                pResult.Request?.OperationKey ?? pRequest?.OperationKey,
                pResult.RecoveredExisting);
        }

        private static GuestOfficeStartSeed Seed(GuestOfficeStartRequest pRequest)
        {
            OfficialCareerAppointment appointment = pRequest.CareerAppointment;
            return new GuestOfficeStartSeed
            {
                ExpectedAffiliation = AffiliationRow(pRequest.ExpectedAffiliation),
                DesiredCareer = new GuestOfficeCareerRow
                {
                    OfficerId = -1L,
                    KingdomId = appointment.KingdomId,
                    ActorId = appointment.ActorId,
                    ActorName = appointment.ActorName ?? "",
                    CityId = appointment.CityId,
                    Layer = appointment.Layer ?? "",
                    OfficeId = appointment.OfficeId ?? "",
                    SchoolId = appointment.SchoolId ?? "",
                    Influence = appointment.Influence,
                    AppointedYear = appointment.AppointedYear,
                    AppointedTime = appointment.AppointedTime,
                    EndedYear = -1,
                    EndedTime = -1d,
                    Active = 1,
                    EndReason = "",
                    UpdatedTime = appointment.AppointedTime
                },
                EventType = pRequest.EventType,
                ServiceEndYear = pRequest.EndYear
            };
        }

        private static GuestOfficeAffiliationRow AffiliationRow(
            HistoricalSchoolAffiliationSnapshot pSnapshot)
        {
            if (pSnapshot == null) return null;
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

        private static bool Valid(GuestOfficeStartRequest pRequest)
        {
            OfficialCareerAppointment appointment = pRequest?.CareerAppointment;
            HistoricalSchoolAffiliationSnapshot affiliation =
                pRequest?.ExpectedAffiliation;
            return appointment != null && affiliation != null &&
                   appointment.ActorId >= 0 && affiliation.ActorId == appointment.ActorId &&
                   pRequest.HostKingdomId >= 0 && pRequest.CityId >= 0 &&
                   pRequest.EndYear > pRequest.StartYear &&
                   !string.IsNullOrWhiteSpace(pRequest.SchoolId) &&
                   !string.IsNullOrWhiteSpace(pRequest.OfficeId) &&
                   IsGuestEvent(pRequest.EventType) &&
                   appointment.KingdomId == pRequest.HostKingdomId &&
                   appointment.CityId == pRequest.CityId &&
                   appointment.Layer == CourtOfficeLayer.Central &&
                   appointment.OfficeId == pRequest.OfficeId &&
                   appointment.SchoolId == pRequest.SchoolId &&
                   appointment.AppointedYear == pRequest.StartYear &&
                   appointment.AppointedTime.Equals(pRequest.WorldTime);
        }

        private static bool IsGuestEvent(string pEventType)
        {
            return pEventType == "guest_service_started" ||
                   pEventType == "guest_service_renewed";
        }

        private static GuestOfficeStartResult Unknown(string pOperationKey)
        {
            return new GuestOfficeStartResult(GuestOfficePersistenceOutcome.Unknown, null,
                default, pOperationKey, pRecoveredExisting: false);
        }

        private static GuestOfficeRecoveryResult Retry()
        {
            return new GuestOfficeRecoveryResult(GuestOfficeRecoveryDecision.Retry,
                null, "", "");
        }
    }
}
