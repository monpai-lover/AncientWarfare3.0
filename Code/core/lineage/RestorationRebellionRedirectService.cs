using System;

namespace AncientWarfare3.core.lineage
{
    internal static class RestorationRebellionRedirectService
    {
        public static RestorationRebellionStartOutcome TryRedirect(
            Actor pActor, City pCity, out string pError)
        {
            pError = "";
            if (!RestorationRebellionRedirectRules.ShouldInspect(
                    KingdomIdentityContinuityService.IsCreatingRestoration,
                    pActor?.data != null && pActor.isAlive() &&
                    !pActor.isRekt(),
                    pCity?.data != null && !pCity.isRekt()))
                return RestorationRebellionStartOutcome.NotStarted;
            try
            {
                long claimId = RoyalClaimService
                    .FindBestDormantClaimIdForActor(pActor.data.id);
                if (claimId < 0) return RestorationRebellionStartOutcome.NotStarted;
                RestorationRebellionStartOutcome outcome =
                    AutonomousRestorationService
                        .TryStartSelfRestorationFromRebellion(
                            claimId, pActor, pCity, out pError);
                if (RestorationRebellionRedirectRules
                    .ShouldSuppressVanilla(outcome))
                    return outcome;
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Restoration rebellion redirect failed: " + e.Message);
                pError = "restoration_rebellion_redirect_error";
            }
            return RestorationRebellionStartOutcome.NotStarted;
        }

        internal static RestorationRebellionStartOutcome
            TryRedirectBanditFounder(Actor pActor, City pCity,
                out Kingdom pRestored, out string pError)
        {
            pRestored = null;
            pError = "";
            if (!RestorationRebellionRedirectRules.ShouldInspectBanditFounder(
                    pAllowRedirect: !KingdomIdentityContinuityService
                        .IsCreatingRestoration,
                    pActorValid: pActor?.data != null && pActor.isAlive() &&
                        !pActor.isRekt(),
                    pCityValid: pCity?.data != null && !pCity.isRekt()))
                return RestorationRebellionStartOutcome.NotStarted;
            try
            {
                long claimId = RoyalClaimService
                    .FindBestDormantClaimIdForActor(pActor.data.id);
                if (claimId < 0)
                    return RestorationRebellionStartOutcome.NotStarted;
                return AutonomousRestorationService
                    .TryStartSelfRestorationFromExternalBandit(
                        claimId, pActor, pCity, out pRestored, out pError);
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Bandit claimant restoration redirect failed: " +
                    e.Message);
                pError = "restoration_bandit_redirect_error";
                return RestorationRebellionStartOutcome.NotStarted;
            }
        }
    }
}
