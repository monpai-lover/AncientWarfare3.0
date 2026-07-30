using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class RestorationRebellionRedirectService
    {
        private const int MaxClaimsInspected = 8;

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
                List<RoyalClaimService.ClaimRow> claims =
                    RoyalClaimService.GetDormantClaimsForActor(
                        pActor.data.id, MaxClaimsInspected);
                foreach (RoyalClaimService.ClaimRow claim in claims)
                {
                    RestorationRebellionStartOutcome outcome =
                        AutonomousRestorationService
                            .TryStartSelfRestorationFromRebellion(
                                claim.claimId, pActor, pCity, out pError);
                    if (RestorationRebellionRedirectRules
                        .ShouldSuppressVanilla(outcome))
                        return outcome;
                }
            }
            catch (Exception e)
            {
                ModClass.LogWarning(
                    "Restoration rebellion redirect failed: " + e.Message);
                pError = "restoration_rebellion_redirect_error";
            }
            return RestorationRebellionStartOutcome.NotStarted;
        }
    }
}
