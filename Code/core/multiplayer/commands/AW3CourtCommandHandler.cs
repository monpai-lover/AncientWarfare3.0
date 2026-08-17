using System;
using System.Collections.Generic;
using System.IO;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.multiplayer.commands
{
    internal static class AW3CourtCommandHandler
    {
        internal static AW3CommandResult Dispatch(AW3CommandRequest request)
        {
            switch (request.Kind)
            {
                case AW3CommandKind.AppointCourtOfficer:
                    return Appoint(request);
                case AW3CommandKind.SetCourtDisposition:
                    return SetDisposition(request);
                case AW3CommandKind.ChangeCourtAuxiliaryLaw:
                    return ChangeAuxiliaryLaw(request);
                case AW3CommandKind.ChangeInheritanceLaw:
                    return ChangeInheritanceLaw(request);
                case AW3CommandKind.SubmitCivilServiceRanking:
                    return SubmitCivilServiceRanking(request);
                case AW3CommandKind.ApplyCustomCourtTemplate:
                    return ApplyCustomCourtTemplate(request);
                default:
                    return Invalid();
            }
        }

        private static AW3CommandResult Appoint(AW3CommandRequest request)
        {
            CourtManualAppointmentResult result =
                CourtService.TryManualAppointment(request.CountryId,
                    request.Key, request.ActorId, request.TargetActorId);
            if (result == CourtManualAppointmentResult.Success)
                return AW3CommandResult.Success("aw3_court_appointment_ok",
                    request.ActorId, (int)result);
            return AW3CommandResult.Rejected(AppointmentError(result),
                "aw3_court_appointment_rejected", request.ActorId,
                (int)result);
        }

        private static AW3CommandResult SetDisposition(
            AW3CommandRequest request)
        {
            Kingdom kingdom = FindKingdom(request.CountryId);
            Actor ruler = kingdom?.king;
            if (ruler?.data == null || ruler.isRekt())
                return NotFound("aw3_court_disposition_invalid_ruler");
            if (!TryEnum(request.Key, out CourtDispositionAction action))
                return Invalid();

            string operationKey = "aw3_court_" + request.CountryId + "_" +
                                  request.ActorId + "_" +
                                  request.SecondaryKey;
            var command = new CourtDispositionCommand(request.CountryId,
                ruler.data.id, request.ActorId, action, request.IntValue,
                request.CityId, operationKey);
            CourtDispositionResult result =
                CourtDispositionService.Execute(command);
            if (result.Outcome == CourtDispositionOutcome.Committed ||
                result.Outcome == CourtDispositionOutcome.Rebelled)
                return AW3CommandResult.Success(
                    "aw3_court_disposition_accepted", request.ActorId,
                    (int)result.Outcome);
            return AW3CommandResult.Rejected(DispositionError(result.Reason),
                "aw3_court_disposition_rejected", request.ActorId,
                (int)result.Outcome);
        }

        private static AW3CommandResult ChangeAuxiliaryLaw(
            AW3CommandRequest request)
        {
            Kingdom kingdom = FindKingdom(request.CountryId);
            if (!TryEnum(request.Key, out CourtAuxiliaryLawKind kind))
                return Invalid();
            CourtAuxiliaryLawChangeResult result =
                CourtAuxiliaryLawService.TryChangeLaw(kingdom, kind,
                    request.IntValue);
            if (result == CourtAuxiliaryLawChangeResult.Success)
                return AW3CommandResult.Success("aw3_court_auxiliary_law_ok",
                    request.CountryId, (int)result);
            return AW3CommandResult.Rejected(AuxiliaryLawError(result),
                "aw3_court_auxiliary_law_rejected", request.CountryId,
                (int)result);
        }

        private static AW3CommandResult ChangeInheritanceLaw(
            AW3CommandRequest request)
        {
            Kingdom kingdom = FindKingdom(request.CountryId);
            InheritanceLaw? law = null;
            if (!string.Equals(request.Key, "automatic",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!TryEnum(request.Key, out InheritanceLaw parsed))
                    return Invalid();
                law = parsed;
            }
            InheritanceLawChangeResult result =
                InheritanceLawService.TryChangeLock(kingdom, law);
            if (result == InheritanceLawChangeResult.Success)
                return AW3CommandResult.Success("aw3_inheritance_law_ok",
                    request.CountryId, (int)result);
            return AW3CommandResult.Rejected(InheritanceLawError(result),
                "aw3_inheritance_law_rejected", request.CountryId,
                (int)result);
        }

        private static AW3CommandResult SubmitCivilServiceRanking(
            AW3CommandRequest request)
        {
            var preferred = new List<long>(3) { request.ActorId };
            if (request.TargetActorId > 0)
                preferred.Add(request.TargetActorId);
            if (request.CityId > 0)
                preferred.Add(request.CityId);
            bool submitted = CivilServiceExamService.TrySubmitPlayerRanking(
                request.CountryId, request.SecondaryId, preferred,
                out string reasonKey);
            if (submitted)
                return AW3CommandResult.Success(reasonKey,
                    request.SecondaryId);
            return AW3CommandResult.Rejected(
                CivilServiceRankingError(reasonKey), reasonKey,
                request.SecondaryId);
        }

        private static AW3CommandResult ApplyCustomCourtTemplate(
            AW3CommandRequest request)
        {
            Kingdom kingdom = FindKingdom(request.CountryId);
            if (kingdom?.data == null || kingdom.isRekt())
                return NotFound("aw_custom_court_invalid_kingdom");
            if (request.IntValue < 1 || request.SecondaryId < 1 ||
                string.IsNullOrEmpty(request.Key) ||
                string.IsNullOrEmpty(request.SecondaryKey))
                return Invalid();

            string root = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "WorldBox", "AncientWarfare3.0", "court-templates");
            var store = new CustomCourtTemplateStore(root);
            CustomCourtTemplate template;
            CustomCourtTemplateValidationError loadError;
            if (!store.TryLoad(request.Key, out template, out loadError))
                return NotFound("aw_custom_court_template_not_found");
            if (template.Revision != request.IntValue ||
                !string.Equals(CustomCourtTemplateJsonCodec.Hash(template),
                    request.SecondaryKey, StringComparison.Ordinal))
                return AW3CommandResult.Rejected(AW3CommandError.StaleState,
                    "aw_custom_court_template_stale", request.CountryId);

            string kingdomKey = CustomCourtRuntime.KingdomKey(kingdom);
            CustomCourtInstance current;
            bool hasCurrent = CustomCourtRuntime.TryGetInstance(kingdom,
                out current);
            bool revisionMatches = hasCurrent
                ? current.InstanceRevision == request.SecondaryId
                : request.SecondaryId == 1;
            if (!CustomCourtMultiplayerRules.CanApply(true, true,
                    revisionMatches))
                return AW3CommandResult.Rejected(AW3CommandError.StaleState,
                    "aw_custom_court_instance_stale", request.CountryId);

            if (!CustomCourtRuntime.TryApply(kingdom, template,
                    new Dictionary<string, long>()))
                return AW3CommandResult.Rejected(AW3CommandError.ExecutionFailed,
                    "aw_custom_court_apply_failed", request.CountryId);
            return AW3CommandResult.Success("aw_custom_court_apply_ok",
                request.CountryId);
        }

        private static AW3CommandError AppointmentError(
            CourtManualAppointmentResult result)
        {
            switch (result)
            {
                case CourtManualAppointmentResult.InvalidKingdom:
                case CourtManualAppointmentResult.InvalidOffice:
                case CourtManualAppointmentResult.InvalidActor:
                    return AW3CommandError.NotFound;
                case CourtManualAppointmentResult.OfficeOccupied:
                case CourtManualAppointmentResult.OfficeChanged:
                    return AW3CommandError.StaleState;
                case CourtManualAppointmentResult.CandidateIneligible:
                    return AW3CommandError.IllegalTarget;
                default:
                    return AW3CommandError.ExecutionFailed;
            }
        }

        private static AW3CommandError AuxiliaryLawError(
            CourtAuxiliaryLawChangeResult result)
        {
            switch (result)
            {
                case CourtAuxiliaryLawChangeResult.InvalidKingdom:
                    return AW3CommandError.NotFound;
                case CourtAuxiliaryLawChangeResult.InvalidChoice:
                    return AW3CommandError.IllegalTarget;
                case CourtAuxiliaryLawChangeResult.Unchanged:
                    return AW3CommandError.Conflict;
                case CourtAuxiliaryLawChangeResult.InsufficientPoints:
                    return AW3CommandError.InsufficientResources;
                case CourtAuxiliaryLawChangeResult.Cooldown:
                    return AW3CommandError.Cooldown;
                default:
                    return AW3CommandError.ExecutionFailed;
            }
        }

        private static AW3CommandError InheritanceLawError(
            InheritanceLawChangeResult result)
        {
            switch (result)
            {
                case InheritanceLawChangeResult.InvalidKingdom:
                    return AW3CommandError.NotFound;
                case InheritanceLawChangeResult.NoChange:
                    return AW3CommandError.Conflict;
                case InheritanceLawChangeResult.Cooldown:
                    return AW3CommandError.Cooldown;
                case InheritanceLawChangeResult.InsufficientPoliticalPoints:
                    return AW3CommandError.InsufficientResources;
                case InheritanceLawChangeResult.Unavailable:
                    return AW3CommandError.IllegalTarget;
                default:
                    return AW3CommandError.ExecutionFailed;
            }
        }

        private static AW3CommandError CivilServiceRankingError(
            string reasonKey)
        {
            switch (reasonKey)
            {
                case "aw_civil_service_exam_read_only":
                    return AW3CommandError.Unauthorized;
                case "aw_civil_service_exam_ranking_stale":
                    return AW3CommandError.StaleState;
                case "aw_civil_service_exam_ranking_invalid":
                    return AW3CommandError.IllegalTarget;
                default:
                    return AW3CommandError.ExecutionFailed;
            }
        }

        private static AW3CommandError DispositionError(string reason)
        {
            switch (reason)
            {
                case CourtDispositionService.ReasonInvalidRuler:
                case CourtDispositionService.ReasonInvalidTarget:
                    return AW3CommandError.NotFound;
                case CourtDispositionService.ReasonInsufficientPoints:
                    return AW3CommandError.InsufficientResources;
                case CourtDispositionService.ReasonIneligible:
                case CourtDispositionService.ReasonInvalidParameter:
                    return AW3CommandError.IllegalTarget;
                default:
                    return AW3CommandError.ExecutionFailed;
            }
        }

        private static bool TryEnum<T>(string value, out T result)
            where T : struct
        {
            return Enum.TryParse(value, true, out result) &&
                   Enum.IsDefined(typeof(T), result);
        }

        private static Kingdom FindKingdom(long id)
        {
            try { return World.world?.kingdoms?.get(id); }
            catch { return null; }
        }

        private static AW3CommandResult Invalid() =>
            AW3CommandResult.Rejected(AW3CommandError.InvalidRequest,
                "aw3_command_invalid_request");

        private static AW3CommandResult NotFound(string message) =>
            AW3CommandResult.Rejected(AW3CommandError.NotFound, message);
    }
}
