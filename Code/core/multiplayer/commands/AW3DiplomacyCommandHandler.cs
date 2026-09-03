using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.multiplayer.commands
{
    internal static class AW3DiplomacyCommandHandler
    {
        internal static AW3CommandResult Dispatch(AW3CommandRequest request)
        {
            switch (request.Kind)
            {
                case AW3CommandKind.CreateDiplomacyProposal:
                    return CreateProposal(request);
                case AW3CommandKind.RespondDiplomacyProposal:
                    return RespondToProposal(request);
                case AW3CommandKind.StartSpyNetwork:
                    return StartSpyNetwork(request);
                case AW3CommandKind.StartForgeDocuments:
                    return StartForgeDocuments(request);
                case AW3CommandKind.DeclareWar:
                    return DeclareWar(request);
                default:
                    return Invalid();
            }
        }

        private static AW3CommandResult CreateProposal(
            AW3CommandRequest request)
        {
            Kingdom requester = FindKingdom(request.CountryId);
            Kingdom responder = FindKingdom(request.TargetCountryId);
            if (requester == null || responder == null) return NotFound();
            if (!TryProposalType(request.Key,
                    out DiplomacyProposalType type) ||
                type == DiplomacyProposalType.None)
                return Invalid();
            if (DiplomacyProposalRules.IsPeaceProposal(type))
                return CreateWarPeaceProposal(request, requester,
                    responder, type);

            bool created;
            DiplomacyProposal proposal;
            string reason;
            if (type == DiplomacyProposalType.Coalition)
            {
                if (FindKingdom(request.SecondaryId) == null)
                    return NotFound();
                created = DiplomacyProposalService.TryCreateWithSelection(
                    requester, responder, type, pPlayerInitiated: true,
                    pWarId: -1L, new DiplomacyProposalSelection(
                        request.SecondaryId, -1L, -1L, -1L, ""),
                    out proposal, out reason);
            }
            else if (type == DiplomacyProposalType.RoyalMarriage)
            {
                if (FindActor(request.ActorId) == null ||
                    FindActor(request.TargetActorId) == null)
                    return NotFound();
                created = DiplomacyProposalService.TryCreateWithSelection(
                    requester, responder, type, pPlayerInitiated: true,
                    pWarId: -1L, new DiplomacyProposalSelection(-1L,
                        request.ActorId, request.TargetActorId, -1L, ""),
                    out proposal, out reason);
            }
            else if (type == DiplomacyProposalType.HouseholdOffering)
            {
                Actor offered = FindActor(request.ActorId);
                Actor ruler = FindActor(request.TargetActorId);
                if (offered?.data == null || ruler?.data == null)
                    return NotFound();
                if (ruler != responder.king ||
                    !RulerHouseholdRules.TryParseKind(request.SecondaryKey,
                        out _))
                    return Invalid();
                created = DiplomacyProposalService.TryCreateWithSelection(
                    requester, responder, type, pPlayerInitiated: true,
                    pWarId: -1L, new DiplomacyProposalSelection(-1L,
                        request.ActorId, request.TargetActorId, -1L,
                        request.SecondaryKey), out proposal, out reason);
            }
            else
            {
                created = DiplomacyProposalService.TryCreate(requester,
                    responder, type, pPlayerInitiated: true, pWarId: -1L,
                    out proposal, out reason);
            }

            return created
                ? AW3CommandResult.Success("aw3_diplomacy_proposal_created",
                    proposal?.ProposalId ?? -1L)
                : Rejected(reason);
        }

        private static AW3CommandResult CreateWarPeaceProposal(
            AW3CommandRequest request, Kingdom requester,
            Kingdom responder, DiplomacyProposalType type)
        {
            if (!WarPeaceDraftCodec.TryDeserialize(request.Payload,
                    out WarPeaceSettlementDraft draft,
                    out string reason)) return Rejected(reason);
            War war = FindWar(request.SecondaryId);
            if (war?.data == null || war.hasEnded())
                return Rejected("war_no_longer_active");
            if (draft.WarId != war.data.id ||
                draft.RequesterKingdomId != requester.id ||
                draft.ResponderKingdomId != responder.id)
                return Rejected("invalid_peace_draft");
            if (!WarScoreService.TryGetSnapshot(war, requester,
                    out WarScoreSnapshot score))
                return Rejected("war_score_unavailable");

            draft.SignedWarScore = score.Score;
            draft.PlayerInitiated = true;
            WarPeacePrepareResult prepared =
                WarPeaceSettlementService.Instance.Prepare(draft);
            if (!prepared.Success || prepared.Proposal == null)
            {
                ModClass.LogWarning(
                    "War peace proposal preparation failed: war=" +
                    war.data.id + " requester=" + requester.id +
                    " responder=" + responder.id + " reason=" +
                    (prepared.Reason ?? ""));
                return Rejected(prepared.Reason);
            }

            bool created = DiplomacyProposalService.TryCreateWithSelection(
                requester, responder, type, pPlayerInitiated: true,
                pWarId: war.data.id,
                new DiplomacyProposalSelection(-1L, -1L, -1L, -1L,
                    prepared.Proposal.DetailId),
                out DiplomacyProposal proposal, out reason);
            if (!created)
            {
                ModClass.LogWarning(
                    "War peace diplomacy proposal create failed: war=" +
                    war.data.id + " requester=" + requester.id +
                    " responder=" + responder.id + " reason=" +
                    (reason ?? ""));
                WarPeaceSettlementService.Instance.Cancel(
                    prepared.Proposal.DetailId,
                    string.IsNullOrWhiteSpace(reason)
                        ? "diplomacy_proposal_failed"
                        : reason);
                return Rejected(reason);
            }
            return AW3CommandResult.Success(
                "aw3_diplomacy_proposal_created",
                proposal?.ProposalId ?? -1L);
        }

        private static AW3CommandResult RespondToProposal(
            AW3CommandRequest request)
        {
            Kingdom responder = FindKingdom(request.CountryId);
            Kingdom requester = FindKingdom(request.TargetCountryId);
            if (responder == null || requester == null) return NotFound();
            DiplomacyProposal proposal = FindProposal(request.CountryId,
                request.TargetCountryId, request.SecondaryId);
            if (proposal == null) return NotFound();
            if (proposal.ResponderKingdomId != request.CountryId ||
                proposal.RequesterKingdomId != request.TargetCountryId)
                return AW3CommandResult.Rejected(
                    AW3CommandError.Unauthorized,
                    "aw3_command_unauthorized");

            if (request.BoolValue &&
                RulerHouseholdRules.IsConsortRequestDetail(
                    proposal.DetailId))
            {
                if (request.ActorId < 0L)
                    return Rejected(
                        "household_candidate_selection_required");
                if (!DiplomacyProposalService
                        .TryAttachConsortRequestCandidate(
                            proposal.ProposalId, request.ActorId,
                            out string selectionReason))
                    return Rejected(selectionReason);
            }

            bool accepted = DiplomacyProposalService.Respond(
                request.SecondaryId, request.BoolValue,
                pPlayerResponse: true, out string reason);
            return accepted
                ? AW3CommandResult.Success("aw3_diplomacy_response_recorded",
                    request.SecondaryId)
                : Rejected(reason);
        }

        private static AW3CommandResult StartSpyNetwork(
            AW3CommandRequest request)
        {
            Kingdom source = FindKingdom(request.CountryId);
            Kingdom target = FindKingdom(request.TargetCountryId);
            if (source == null || target == null) return NotFound();
            bool started = DiplomaticOperationService.TryStartSpyNetwork(
                source, target, pPlayerInitiated: true,
                out long operationId, out string reason);
            return started
                ? AW3CommandResult.Success("aw3_spy_network_started",
                    operationId)
                : Rejected(reason);
        }

        private static AW3CommandResult StartForgeDocuments(
            AW3CommandRequest request)
        {
            Kingdom source = FindKingdom(request.CountryId);
            Kingdom target = FindKingdom(request.TargetCountryId);
            City city = FindCity(request.CityId);
            if (source == null || target == null || city == null)
                return NotFound();
            if (city.kingdom != target) return StaleTarget();
            bool started =
                DiplomaticOperationService.TryStartForgeDocuments(
                    source, target, city, request.Key,
                    pPlayerInitiated: true, out long operationId,
                    out string reason);
            return started
                ? AW3CommandResult.Success("aw3_forgery_started",
                    operationId)
                : Rejected(reason);
        }

        private static AW3CommandResult DeclareWar(
            AW3CommandRequest request)
        {
            // 暂停状态下不允许发战书：暂停时模拟停转，战争相关的结算、
            // 军队行为、天命判定都不推进，此时开战只会让状态机进入一个
            // 永远无法自行收拾的中间态。
            try
            {
                if (World.world != null && World.world.isPaused())
                    return Rejected("world_paused");
            }
            catch { }
            Kingdom attacker = FindKingdom(request.CountryId);
            Kingdom defender = FindKingdom(request.TargetCountryId);
            if (attacker == null || defender == null) return NotFound();
            City targetCity = request.CityId >= 0
                ? FindCity(request.CityId)
                : null;
            if (request.CityId >= 0 &&
                (targetCity?.data == null || targetCity.isRekt() ||
                 targetCity.kingdom != defender))
                return StaleTarget();
            WarTerritoryService.WarTargetOption option = FindWarOption(
                attacker, defender, request.Key, request.CityId,
                request.SecondaryId);
            if (option == null)
            {
                string canonicalWarType = DiplomaticWarDeclarationService.
                    WarTypeForGoal(request.Key);
                bool goalAllowed = DiplomaticWarDeclarationService.
                    CanIssue(attacker, defender, request.Key,
                        canonicalWarType, out string validationFailure);
                DiplomaticWarSubmissionResolution resolution =
                    DiplomaticWarSubmissionRules.Resolve(
                        pTargetCityIdentityValid: true,
                        pCanonicalOptionMatched: false,
                        pAuthoritativeGoalAllowed: goalAllowed,
                        pAuthoritativeFailureReason: validationFailure);
                return Rejected(resolution.FailureReason);
            }
            bool issued = DiplomaticWarDeclarationService.TryIssue(attacker,
                option, out string failureReason);
            return issued
                ? AW3CommandResult.Success("aw3_war_declaration_issued",
                    defender.id)
                : Rejected(failureReason);
        }

        private static WarTerritoryService.WarTargetOption FindWarOption(
            Kingdom attacker, Kingdom defender, string goalType,
            long cityId, long sourceDeJureRegionId)
        {
            List<WarTerritoryService.WarTargetOption> options =
                WarTerritoryService.BuildTargetOptions(attacker, defender);
            for (var index = 0; index < options.Count; index++)
            {
                WarTerritoryService.WarTargetOption option = options[index];
                if (!string.Equals(option?.goal_type, goalType,
                        StringComparison.Ordinal))
                    continue;
                long optionCityId = option.target_city?.data?.id ?? -1L;
                if (optionCityId != cityId) continue;
                if (string.Equals(goalType,
                        WarTerritoryService.GOAL_TAKE_DE_JURE_REGION,
                        StringComparison.Ordinal) &&
                    option.source_de_jure_region_id !=
                    sourceDeJureRegionId) continue;
                return option;
            }
            return null;
        }

        private static DiplomacyProposal FindProposal(long countryA,
            long countryB, long proposalId)
        {
            IReadOnlyList<DiplomacyProposal> proposals =
                DiplomacyProposalService.ReadPair(countryA, countryB, 64);
            for (var index = 0; index < proposals.Count; index++)
                if (proposals[index]?.ProposalId == proposalId)
                    return proposals[index];
            return null;
        }

        private static bool TryProposalType(string value,
            out DiplomacyProposalType type)
        {
            switch (value)
            {
                case "alliance": type = DiplomacyProposalType.Alliance; return true;
                case "peace": type = DiplomacyProposalType.Peace; return true;
                case "non_aggression": type = DiplomacyProposalType.NonAggression; return true;
                case "join_war": type = DiplomacyProposalType.JoinWar; return true;
                case "vassalize": type = DiplomacyProposalType.Vassalize; return true;
                case "tributary": type = DiplomacyProposalType.Tributary; return true;
                case "end_alliance": type = DiplomacyProposalType.EndAlliance; return true;
                case "end_vassal": type = DiplomacyProposalType.EndVassal; return true;
                case "truce": type = DiplomacyProposalType.Truce; return true;
                case "break_non_aggression": type = DiplomacyProposalType.BreakNonAggression; return true;
                case "coalition": type = DiplomacyProposalType.Coalition; return true;
                case "royal_marriage": type = DiplomacyProposalType.RoyalMarriage; return true;
                case "household_offering": type = DiplomacyProposalType.HouseholdOffering; return true;
                case "surrender": type = DiplomacyProposalType.Surrender; return true;
                case "enforce_demands": type = DiplomacyProposalType.EnforceDemands; return true;
                default:
                    return Enum.TryParse(value, true, out type) &&
                           Enum.IsDefined(typeof(DiplomacyProposalType), type);
            }
        }

        private static AW3CommandResult Rejected(string reason)
        {
            string stableReason = DiplomacyFailureReasonRules.StableKey(
                reason);
            return AW3CommandResult.Rejected(MapError(stableReason),
                stableReason);
        }

        private static AW3CommandError MapError(string reason)
        {
            return AW3DiplomacyCommandErrorRules.Map(reason);
        }

        private static Kingdom FindKingdom(long id)
        {
            if (id <= 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(id);
                return kingdom?.data != null && !kingdom.isRekt()
                    ? kingdom
                    : null;
            }
            catch { return null; }
        }

        private static City FindCity(long id)
        {
            if (id <= 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(id);
                return city?.data != null && !city.isRekt() ? city : null;
            }
            catch { return null; }
        }

        private static War FindWar(long id)
        {
            if (id <= 0 || World.world?.wars == null) return null;
            try
            {
                War war = World.world.wars.get(id);
                return war?.data != null && !war.hasEnded() ? war : null;
            }
            catch { return null; }
        }

        private static Actor FindActor(long id)
        {
            if (id <= 0 || World.world?.units == null) return null;
            try
            {
                Actor actor = World.world.units.get(id);
                return actor?.data != null && !actor.isRekt() ? actor : null;
            }
            catch { return null; }
        }

        private static AW3CommandResult Invalid() =>
            AW3CommandResult.Rejected(AW3CommandError.InvalidRequest,
                "aw3_command_invalid_request");

        private static AW3CommandResult NotFound() =>
            AW3CommandResult.Rejected(AW3CommandError.NotFound,
                "not_found");

        private static AW3CommandResult StaleTarget() =>
            AW3CommandResult.Rejected(AW3CommandError.StaleState,
                "target_city_changed");
    }
}
