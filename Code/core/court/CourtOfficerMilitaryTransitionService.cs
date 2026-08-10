using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtOfficerMilitaryTransitionService
    {
        public static void ReleaseAfterCommittedAppointment(Actor pActor,
            string pLayer, string pOfficeId)
        {
            if (pActor?.data == null || pActor.isRekt() ||
                !CourtManualAppointmentRules.ShouldReleaseMilitaryIdentity(
                    pLayer, pOfficeId)) return;

            Army previousArmy = pActor.army;
            try { GeneralService.RetireForCivilOffice(pActor); }
            catch { }
            try
            {
                if (RoyalGuardService.IsRoyalGuard(pActor))
                    RoyalGuardService.DismissGuard(pActor, "civil_office");
            }
            catch { }
            try { MandateBorderDefenseService.ReleaseBorderGuard(pActor); }
            catch { }
            try { pActor.stopBeingWarrior(); }
            catch
            {
                try { pActor.setArmy(null); }
                catch { }
            }
            try { AWArmyService.TryRemoveEmptyArmy(previousArmy); }
            catch { }
        }
    }
}
