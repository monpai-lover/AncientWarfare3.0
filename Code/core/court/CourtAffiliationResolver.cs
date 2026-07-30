using AncientWarfare3.core.schools;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtAffiliationResolver
    {
        public static bool IsDomestic(Actor pActor, Kingdom pHost)
        {
            if (pActor?.data == null || pHost?.data == null || pHost.isRekt())
                return false;
            HistoricalSchoolAffiliationSnapshot state = HistoricalAffiliationService.Get(
                pActor.data.id);
            return state != null
                ? state.HomeKingdomId == pHost.id
                : pActor.kingdom == pHost;
        }

        public static bool IsValidGuestService(Actor pActor, Kingdom pHost)
        {
            if (pActor?.data == null || pHost?.data == null || pHost.isRekt() ||
                !pActor.isAlive() || pActor.isRekt() || pActor.isKing() ||
                pActor.isCityLeader() || GeneralService.IsGeneral(pActor)) return false;
            HistoricalSchoolAffiliationSnapshot state = HistoricalAffiliationService.Get(
                pActor.data.id);
            return state?.LifecycleState == HistoricalSchoolLifecycleState.Serving &&
                   state.ServiceKingdomId == pHost.id &&
                   HistoricalAffiliationService.ServiceKingdom(pActor)?.id == pHost.id;
        }

        public static bool CanServe(Actor pActor, Kingdom pHost, string pLayer)
        {
            if (pActor?.data == null || pHost?.data == null || pHost.isRekt() ||
                !pActor.isAlive() || pActor.isRekt()) return false;
            HistoricalSchoolAffiliationSnapshot state = HistoricalAffiliationService.Get(
                pActor.data.id);
            // The persisted home kingdom is the nationality authority.  Engine pointers
            // may be repaired after the original kingdom is destroyed and must not turn a
            // foreign scholar into a domestic officer by accident.
            if (!IsDomestic(pActor, pHost))
            {
                if (!IsValidGuestService(pActor, pHost)) return false;
                bool maleCivilOffice = pLayer == CourtOfficeLayer.Central ||
                                       pLayer == CourtOfficeLayer.Feudatory;
                return !maleCivilOffice || pActor.isSexMale();
            }
            return true;
        }
    }
}
