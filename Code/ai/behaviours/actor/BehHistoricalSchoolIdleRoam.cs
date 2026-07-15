using AncientWarfare3.core.schools;
using ai.behaviours;

namespace AncientWarfare3.ai.behaviours.actor
{
    public sealed class BehHistoricalSchoolIdleRoam : BehaviourActionActor
    {
        public override BehResult execute(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                !HistoricalSchoolDescentService.IsCanonicalMaster(pActor))
                return BehResult.Stop;
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            bool waitingAfterPathFailure = affiliation != null &&
                affiliation.LifecycleState == HistoricalSchoolLifecycleState.Travelling &&
                !HistoricalSchoolTaskLeaseService.TryGet(pActor.data.id, out _);
            bool resident = affiliation != null &&
                (affiliation.LifecycleState == HistoricalSchoolLifecycleState.AtHome ||
                 affiliation.LifecycleState == HistoricalSchoolLifecycleState.Resident);
            if (affiliation == null || affiliation.ServiceKingdomId >= 0 ||
                !resident && !waitingAfterPathFailure)
                return BehResult.Stop;
            City residence = HistoricalAffiliationService.ResidenceCity(pActor);
            string schoolId = SchoolMembershipService.GetSchool(pActor.data.id);
            if (residence?.data == null || residence.isRekt() ||
                !HistoricalSchoolVenueProvider.TryFind(residence, pActor, schoolId,
                    HistoricalSchoolVenueKind.IdleRoam,
                    out WorldTile target, out _, out _)) return BehResult.Stop;
            pActor.beh_tile_target = target;
            return BehResult.Continue;
        }
    }
}
