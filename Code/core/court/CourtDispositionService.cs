using System;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class CourtDispositionService
    {
        public const string ReasonInvalidCommand = "invalid_command";
        public const string ReasonInvalidRuler = "invalid_ruler";
        public const string ReasonInvalidTarget = "invalid_target";
        public const string ReasonInvalidParameter = "invalid_parameter";
        public const string ReasonIneligible = "ineligible_action";
        public const string ReasonInsufficientPoints =
            "insufficient_political_points";
        public const string ReasonPersistenceFailed = "persistence_failed";
        public const string ReasonPoliticalSpendFailed =
            "political_spend_failed";
        public const string ReasonResistanceFailed = "resistance_failed";

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public static CourtDispositionPreview Preview(
            CourtDispositionCommand pCommand)
        {
            int cost = pCommand == null
                ? 0
                : CourtDispositionRules.Cost(pCommand.Action,
                    pCommand.IntParameter);
            if (pCommand == null || pCommand.KingdomId < 0 ||
                pCommand.RulerActorId < 0 || pCommand.TargetActorId < 0 ||
                string.IsNullOrWhiteSpace(pCommand.OperationKey))
                return Blocked(ReasonInvalidCommand, cost);

            Kingdom kingdom = FindKingdom(pCommand.KingdomId);
            Actor ruler = FindActor(pCommand.RulerActorId);
            Actor target = FindActor(pCommand.TargetActorId);
            if (kingdom?.data == null || kingdom.isRekt() ||
                ruler?.data == null || ruler.isRekt() || !ruler.isAlive() ||
                kingdom.king != ruler || ruler.kingdom != kingdom)
                return Blocked(ReasonInvalidRuler, cost);
            if (target?.data == null || target.isRekt() ||
                !target.isAlive() || target == ruler || target.isKing() ||
                !BelongsToCourt(target, kingdom))
                return Blocked(ReasonInvalidTarget, cost);
            if (!ValidParameter(pCommand, kingdom, target))
                return Blocked(ReasonInvalidParameter, cost);
            if (!EligibleForAction(pCommand, kingdom, target))
                return Blocked(ReasonIneligible, cost);
            if (!CourtDispositionRules.CanAfford(
                    KingdomPolicyService.GetPoliticalPoints(kingdom),
                    pCommand.Action, pCommand.IntParameter))
                return Blocked(ReasonInsufficientPoints, cost);
            return new CourtDispositionPreview(true, "", cost);
        }

        public static CourtDispositionResult Execute(
            CourtDispositionCommand pCommand)
        {
            CourtDispositionLedgerEntry existing = pCommand == null
                ? null
                : CourtDispositionPersistence.ReadByOperationKey(DB,
                    pCommand.OperationKey);
            if (existing != null)
                return ExistingResult(existing, pCommand.Action);

            CourtDispositionPreview preview = Preview(pCommand);
            if (!preview.Allowed)
                return Result(CourtDispositionOutcome.Rejected,
                    preview.Reason, -1L, preview.Cost, pCommand?.Action);

            long actionId = CourtDispositionPersistence.Begin(DB, pCommand,
                preview.Cost, Date.getCurrentYear(), LineageService.CurTime());
            if (actionId < 0)
            {
                existing = CourtDispositionPersistence.ReadByOperationKey(DB,
                    pCommand.OperationKey);
                return existing != null
                    ? ExistingResult(existing, pCommand.Action)
                    : Result(CourtDispositionOutcome.CleanFailure,
                        ReasonPersistenceFailed, -1L, preview.Cost,
                        pCommand.Action);
            }

            Kingdom kingdom = FindKingdom(pCommand.KingdomId);
            Actor ruler = FindActor(pCommand.RulerActorId);
            Actor target = FindActor(pCommand.TargetActorId);
            CourtDispositionResistanceResolution resistance =
                CourtDispositionResistanceService.Resolve(kingdom, target,
                    pCommand.Action, pCommand.LongParameter);

            CourtDispositionOutcome outcome;
            string reason = "";
            if (resistance.Result ==
                CourtDispositionResistanceResult.Rebelled)
            {
                outcome = CourtDispositionOutcome.Rebelled;
            }
            else if (resistance.Result ==
                     CourtDispositionResistanceResult.FailedToStart)
            {
                outcome = CourtDispositionOutcome.CleanFailure;
                reason = ReasonResistanceFailed;
            }
            else
            {
                bool committed = resistance.DomainCommitted ||
                                 ApplyDomain(pCommand, kingdom, ruler,
                                     target);
                outcome = committed
                    ? CourtDispositionOutcome.Committed
                    : CourtDispositionOutcome.CleanFailure;
                if (!committed) reason = ReasonIneligible;
            }

            if (CourtDispositionRules.ShouldSpend(outcome) &&
                !KingdomPolicyService.TrySpendPoliticalPoints(kingdom,
                    preview.Cost))
            {
                outcome = CourtDispositionOutcome.Unknown;
                reason = ReasonPoliticalSpendFailed;
                ModClass.LogWarning("Court disposition committed without " +
                                    "political-point settlement: " +
                                    pCommand.OperationKey);
            }

            if (!CourtDispositionPersistence.Finalize(DB, actionId, outcome,
                    reason, LineageService.CurTime()))
            {
                ModClass.LogWarning("Court disposition ledger finalization " +
                                    "failed: " + pCommand.OperationKey);
                return Result(CourtDispositionOutcome.Unknown,
                    ReasonPersistenceFailed, actionId, preview.Cost,
                    pCommand.Action);
            }
            return Result(outcome, reason, actionId, preview.Cost,
                pCommand.Action);
        }

        private static bool ApplyDomain(CourtDispositionCommand pCommand,
            Kingdom pKingdom, Actor pRuler, Actor pTarget)
        {
            switch (pCommand.Action)
            {
                case CourtDispositionAction.PromoteRank:
                    return OfficialCareerStateService.TryApplyManualRankChange(
                        pTarget, pKingdom, 1,
                        pCommand.IntParameter > 0, out _, out _);
                case CourtDispositionAction.DemoteRank:
                    return OfficialCareerStateService.TryApplyManualRankChange(
                        pTarget, pKingdom, -1,
                        pCommand.IntParameter > 0, out _, out _);
                case CourtDispositionAction.DismissOffice:
                    return CourtService.TryDismissOfficer(pTarget, pKingdom,
                        "court_disposition");
                case CourtDispositionAction.GrantNobleRank:
                    return NobleRankService.TryGrant(pKingdom, pRuler,
                        pTarget, NobleRankRules.ManualGrantRank(
                            pTarget.isSexMale(), pCommand.IntParameter),
                        pTarget.isSexMale()
                            ? NobleTitleStyle.Male
                            : NobleTitleStyle.Princess,
                        "court_disposition", -1L, out _);
                case CourtDispositionAction.GrantFief:
                    return FiefService.GrantFief(pKingdom, pTarget,
                        FindCity(pCommand.LongParameter),
                        "court_disposition");
                case CourtDispositionAction.RevokeFief:
                    return FiefService.TryRevokeActorFief(pTarget,
                        "court_disposition");
                case CourtDispositionAction.GrantSurname:
                    return LineageDispositionService.TryGrantSurname(
                        pKingdom, pRuler, pTarget, out _);
                case CourtDispositionAction.ExpelLineage:
                    return LineageDispositionService.TryExpel(pKingdom,
                        pRuler, pTarget, out _, out _);
                default:
                    return false;
            }
        }

        private static bool EligibleForAction(
            CourtDispositionCommand pCommand, Kingdom pKingdom,
            Actor pTarget)
        {
            pTarget.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long courtKingdomId, -1L);
            pTarget.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, "");
            switch (pCommand.Action)
            {
                case CourtDispositionAction.PromoteRank:
                case CourtDispositionAction.DemoteRank:
                case CourtDispositionAction.DismissOffice:
                    return courtKingdomId == pKingdom.id &&
                           !string.IsNullOrWhiteSpace(officeId);
                case CourtDispositionAction.GrantFief:
                    return GeneralService.IsGeneral(pTarget);
                case CourtDispositionAction.GrantNobleRank:
                    NobleTitleSnapshot title =
                        NobleRankService.ReadHot(pTarget);
                    if (!title.IsActive) return true;
                    return pTarget.isSexMale() &&
                           title.Style == NobleTitleStyle.Male &&
                           pCommand.IntParameter > title.Rank;
                case CourtDispositionAction.RevokeFief:
                    return FiefService.GetFiefCityId(pTarget) >= 0;
                case CourtDispositionAction.GrantSurname:
                    return !SharesRulerLineage(pKingdom.king, pTarget);
                case CourtDispositionAction.ExpelLineage:
                    pTarget.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
                    return shiId >= 0 &&
                           SharesRulerLineage(pKingdom.king, pTarget);
                case CourtDispositionAction.RelocateFeudatory:
                    return FeudatoryService.TryGetByPrince(pTarget.data.id,
                               out FeudatorySnapshot relocate) &&
                           relocate.EmpireKingdomId == pKingdom.id &&
                           FeudatoryService.CanRelocateFeudatory(pKingdom,
                               relocate.FeudatoryId);
                case CourtDispositionAction.ReclaimFeudatoryCity:
                    return FeudatoryService.TryGetByPrince(pTarget.data.id,
                               out FeudatorySnapshot reclaim) &&
                           reclaim.EmpireKingdomId == pKingdom.id &&
                           FeudatoryService.CanReclaimFeudatoryCity(pKingdom,
                               reclaim.FeudatoryId, pCommand.LongParameter);
                default:
                    return true;
            }
        }

        private static bool ValidParameter(CourtDispositionCommand pCommand,
            Kingdom pKingdom, Actor pTarget)
        {
            if (CourtDispositionRules.RequiresIntParameter(pCommand.Action) &&
                (pCommand.IntParameter < 1 ||
                 pCommand.IntParameter > NobleRankRules.MaximumRank))
                return false;
            if (pCommand.Action == CourtDispositionAction.GrantNobleRank &&
                !NobleRankRules.CanGrantRank(
                    (int)KingdomTitleService.GetTitle(pKingdom),
                    pCommand.IntParameter))
                return false;
            if (!CourtDispositionRules.RequiresCityParameter(pCommand.Action))
                return true;
            City city = FindCity(pCommand.LongParameter);
            bool validCity = city?.data != null && !city.isRekt() &&
                   city.kingdom == pKingdom &&
                   (pCommand.Action != CourtDispositionAction.GrantFief ||
                    city != pKingdom.capital);
            return validCity &&
                   (pCommand.Action != CourtDispositionAction.GrantFief ||
                    FiefService.CanGrantFief(pKingdom, pTarget, city));
        }

        private static bool SharesRulerLineage(Actor pRuler, Actor pTarget)
        {
            if (pRuler?.data == null || pTarget?.data == null) return false;
            pRuler.data.get(LineageKeys.LINEAGE_ID,
                out long rulerLineageId, -1L);
            pTarget.data.get(LineageKeys.LINEAGE_ID,
                out long targetLineageId, -1L);
            return rulerLineageId >= 0 &&
                   targetLineageId == rulerLineageId;
        }

        private static bool BelongsToCourt(Actor pActor, Kingdom pKingdom)
        {
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long courtKingdomId, -1L);
            bool courtOfficer = courtKingdomId == pKingdom.id &&
                                (CourtAffiliationResolver.IsDomestic(pActor,
                                     pKingdom) ||
                                 CourtAffiliationResolver.IsValidGuestService(
                                     pActor, pKingdom));
            bool general = GeneralService.IsGeneral(pActor) &&
                           CourtAffiliationResolver.IsDomestic(pActor,
                               pKingdom);
            bool cityLeader = pActor.isCityLeader() &&
                              pActor.city?.kingdom == pKingdom;
            bool prince = FeudatoryService.TryGetByPrince(pActor.data.id,
                              out FeudatorySnapshot feudatory) &&
                          feudatory.EmpireKingdomId == pKingdom.id;
            return courtOfficer || general || cityLeader || prince;
        }

        private static CourtDispositionPreview Blocked(string pReason,
            int pCost)
        {
            return new CourtDispositionPreview(false, pReason, pCost);
        }

        private static CourtDispositionResult ExistingResult(
            CourtDispositionLedgerEntry pEntry,
            CourtDispositionAction pAction)
        {
            return Result(pEntry.Outcome ?? CourtDispositionOutcome.Unknown,
                pEntry.Outcome.HasValue ? pEntry.Reason :
                ReasonPersistenceFailed, pEntry.ActionId, pEntry.Cost,
                pAction);
        }

        private static CourtDispositionResult Result(
            CourtDispositionOutcome pOutcome, string pReason,
            long pActionId, int pCost, CourtDispositionAction? pAction)
        {
            return new CourtDispositionResult(pOutcome, pReason, pActionId,
                pCost, pAction.HasValue &&
                       CourtDispositionRules.ShouldRefreshCourt(
                           pAction.Value) &&
                       (pOutcome == CourtDispositionOutcome.Committed ||
                        pOutcome == CourtDispositionOutcome.Rebelled));
        }

        private static Actor FindActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
