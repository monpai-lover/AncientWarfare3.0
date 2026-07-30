using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtDispositionResistanceService
    {
        public static CourtDispositionResistanceResolution Resolve(
            Kingdom pKingdom, Actor pTarget, CourtDispositionAction pAction,
            long pLongParameter)
        {
            if (!CourtDispositionRules.IsPunishment(pAction))
                return Accepted(CourtDispositionResistanceRoute.None);

            FeudatorySnapshot feudatory = null;
            bool isPrince = pTarget?.data != null &&
                            FeudatoryService.TryGetByPrince(pTarget.data.id,
                                out feudatory);
            bool isLandedGeneral = pTarget?.data != null &&
                                   GeneralService.IsGeneral(pTarget) &&
                                   (FiefService.GetFiefCityId(pTarget) >= 0 ||
                                    pTarget.isCityLeader());
            bool isChiefMinister = IsCurrentPremier(pKingdom, pTarget);
            CourtDispositionResistanceRoute route =
                CourtDispositionRules.ResistanceRoute(isPrince,
                    isLandedGeneral, isChiefMinister);
            int intensity = CourtDispositionRules.Cost(pAction);

            if (route == CourtDispositionResistanceRoute.FeudatoryJingnan)
            {
                if (pAction == CourtDispositionAction.RelocateFeudatory)
                    return Feudatory(route,
                        FeudatoryService.TryRelocateFeudatoryDisposition(
                            pKingdom, feudatory.FeudatoryId, out _));
                if (pAction == CourtDispositionAction.ReclaimFeudatoryCity)
                    return Feudatory(route,
                        FeudatoryService.TryReclaimFeudatoryCityDisposition(
                            pKingdom, feudatory.FeudatoryId, pLongParameter,
                            out _));
                return new CourtDispositionResistanceResolution(route,
                    FeudatoryService.TryStartDispositionResistance(pKingdom,
                        feudatory.FeudatoryId, intensity,
                        "court_disposition_" +
                        pAction.ToString().ToLowerInvariant()));
            }

            CourtDispositionResistanceResult result = route switch
            {
                CourtDispositionResistanceRoute.GeneralRebellion =>
                    GeneralRebellionService.TryStartDispositionRebellion(
                        pTarget, pKingdom, intensity),
                CourtDispositionResistanceRoute.MinisterialCoup =>
                    MinisterialPowerService.TryStartDispositionCoup(
                        pTarget, pKingdom, intensity),
                _ => CourtDispositionResistanceResult.Accepted
            };
            return new CourtDispositionResistanceResolution(route, result);
        }

        private static CourtDispositionResistanceResolution Feudatory(
            CourtDispositionResistanceRoute pRoute,
            CourtDispositionResistanceResult pResult)
        {
            return new CourtDispositionResistanceResolution(pRoute, pResult,
                pResult == CourtDispositionResistanceResult.Accepted);
        }

        private static CourtDispositionResistanceResolution Accepted(
            CourtDispositionResistanceRoute pRoute)
        {
            return new CourtDispositionResistanceResolution(pRoute,
                CourtDispositionResistanceResult.Accepted);
        }

        private static bool IsCurrentPremier(Kingdom pKingdom, Actor pActor)
        {
            if (pKingdom?.data == null || pActor?.data == null) return false;
            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_ID,
                out long premierId, -1L);
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string officeId, "");
            pActor.data.get(LineageKeys.COURT_LAYER,
                out string layer, "");
            bool currentOfficer = courtKingdomId == pKingdom.id &&
                                  MinisterialPowerRules.OfficePriority(
                                      officeId) != int.MaxValue &&
                                  CourtAffiliationResolver.CanServe(pActor,
                                      pKingdom, layer);
            return CourtDispositionRules.IsChiefMinisterCandidate(
                premierId == pActor.data.id, currentOfficer);
        }
    }
}
