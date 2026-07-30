using System;
using System.Data.SQLite;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtOfficerReplacementResult
    {
        public CourtOfficerReplacementResult(
            CourtReplacementPersistenceOutcome pOutcome,
            OfficialCareerAppointmentResult pAppointment,
            OfficialCareerCloseResult pIncumbentCareer,
            HistoricalSchoolAffiliationSnapshot pGuestAffiliation)
        {
            Outcome = pOutcome;
            Appointment = pAppointment;
            IncumbentCareer = pIncumbentCareer;
            GuestAffiliation = pGuestAffiliation;
        }

        public CourtReplacementPersistenceOutcome Outcome { get; }
        public OfficialCareerAppointmentResult Appointment { get; }
        public OfficialCareerCloseResult IncumbentCareer { get; }
        public HistoricalSchoolAffiliationSnapshot GuestAffiliation { get; }
        public bool IsCommitted =>
            Outcome == CourtReplacementPersistenceOutcome.Committed &&
            Appointment.IsCommitted && IncumbentCareer.IsCommitted;
    }

    internal static class CourtOfficerReplacementPersistence
    {
        internal static CourtOfficerReplacementResult Replace(
            SQLiteConnection pDb, OfficialCareerAppointment pAppointment,
            OfficialCareerCloseRequest pLocalClose,
            GuestOfficeEndRequest pGuestEnd,
            Action<SQLiteConnection, SQLiteTransaction> pStageAdditional = null)
        {
            if (pDb == null || pAppointment == null ||
                (pLocalClose == null) == (pGuestEnd == null))
                return Empty(CourtReplacementPersistenceOutcome.CleanFailure);

            SQLiteTransaction transaction = null;
            OfficialCareerCloseToken closeToken = pGuestEnd?.CareerToken;
            OfficialCareerPersistenceToken appointmentToken = null;
            GuestOfficeEndResult guestResult = null;
            try
            {
                transaction = pDb.BeginTransaction();
                if (pGuestEnd != null)
                {
                    guestResult = GuestOfficeEndPersistence.EndInTransaction(
                        pDb, transaction, pGuestEnd);
                    if (guestResult.Persistence.Outcome !=
                        GuestOfficePersistenceOutcome.Committed)
                        throw new InvalidOperationException(
                            "guest incumbent close was not committed");
                }
                else
                {
                    closeToken = OfficialCareerPersistence.CaptureClose(
                        pDb, transaction, pLocalClose);
                    OfficialCareerPersistence.StageClose(
                        pDb, transaction, closeToken);
                }

                appointmentToken = OfficialCareerPersistence.Capture(
                    pDb, transaction, pAppointment);
                OfficialCareerPersistence.Stage(pDb, transaction, appointmentToken);
                pStageAdditional?.Invoke(pDb, transaction);
                transaction.Commit();
                return Build(CourtReplacementPersistenceOutcome.Committed,
                    closeToken, appointmentToken, guestResult);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); } catch { }
                ModClass.LogWarning("Court officer replacement transaction failed: " +
                                    error.Message);
            }
            finally
            {
                try { transaction?.Dispose(); } catch { }
            }

            if (closeToken == null || appointmentToken == null)
                return Empty(CourtReplacementPersistenceOutcome.CleanFailure);
            try
            {
                OfficialCareerPersistenceOutcome closeOutcome;
                if (pGuestEnd != null)
                {
                    GuestOfficePersistenceOutcome guestOutcome =
                        GuestOfficeEndPersistence.Readback(pDb, pGuestEnd);
                    closeOutcome = ToCareerOutcome(guestOutcome);
                }
                else
                {
                    closeOutcome = OfficialCareerPersistence.ReadbackClose(
                        pDb, closeToken);
                }
                OfficialCareerPersistenceOutcome appointmentOutcome =
                    OfficialCareerPersistence.Readback(pDb, appointmentToken);
                CourtReplacementPersistenceOutcome combined =
                    CourtManualAppointmentRules.ResolveReplacementOutcome(
                        closeOutcome == OfficialCareerPersistenceOutcome.Committed,
                        closeOutcome == OfficialCareerPersistenceOutcome.CleanFailure,
                        appointmentOutcome == OfficialCareerPersistenceOutcome.Committed,
                        appointmentOutcome == OfficialCareerPersistenceOutcome.CleanFailure);
                return Build(combined, closeToken, appointmentToken, guestResult);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Court officer replacement readback failed: " +
                                    error.Message);
                return Build(CourtReplacementPersistenceOutcome.Unknown,
                    closeToken, appointmentToken, guestResult);
            }
        }

        private static CourtOfficerReplacementResult Build(
            CourtReplacementPersistenceOutcome pOutcome,
            OfficialCareerCloseToken pCloseToken,
            OfficialCareerPersistenceToken pAppointmentToken,
            GuestOfficeEndResult pGuestResult)
        {
            OfficialCareerPersistenceOutcome careerOutcome = ToCareerOutcome(pOutcome);
            OfficialCareerAppointmentResult appointment =
                OfficialCareerPersistence.ResultFor(pAppointmentToken, careerOutcome);
            OfficialCareerCloseResult close = OfficialCareerPersistence.ResultForClose(
                pCloseToken, careerOutcome);
            return new CourtOfficerReplacementResult(pOutcome, appointment, close,
                pOutcome == CourtReplacementPersistenceOutcome.Committed
                    ? pGuestResult?.Affiliation
                    : null);
        }

        private static CourtOfficerReplacementResult Empty(
            CourtReplacementPersistenceOutcome pOutcome)
        {
            OfficialCareerPersistenceOutcome careerOutcome = ToCareerOutcome(pOutcome);
            return new CourtOfficerReplacementResult(pOutcome,
                new OfficialCareerAppointmentResult(careerOutcome,
                    OfficialCareerMutation.Noop),
                new OfficialCareerCloseResult(careerOutcome, null), null);
        }

        private static OfficialCareerPersistenceOutcome ToCareerOutcome(
            CourtReplacementPersistenceOutcome pOutcome)
        {
            switch (pOutcome)
            {
                case CourtReplacementPersistenceOutcome.Committed:
                    return OfficialCareerPersistenceOutcome.Committed;
                case CourtReplacementPersistenceOutcome.CleanFailure:
                    return OfficialCareerPersistenceOutcome.CleanFailure;
                default:
                    return OfficialCareerPersistenceOutcome.Unknown;
            }
        }

        private static OfficialCareerPersistenceOutcome ToCareerOutcome(
            GuestOfficePersistenceOutcome pOutcome)
        {
            switch (pOutcome)
            {
                case GuestOfficePersistenceOutcome.Committed:
                    return OfficialCareerPersistenceOutcome.Committed;
                case GuestOfficePersistenceOutcome.CleanFailure:
                    return OfficialCareerPersistenceOutcome.CleanFailure;
                default:
                    return OfficialCareerPersistenceOutcome.Unknown;
            }
        }
    }
}
