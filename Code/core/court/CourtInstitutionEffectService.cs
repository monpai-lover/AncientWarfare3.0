using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class CourtInstitutionEffectService
    {
        public static CourtInstitutionEffects Read(Kingdom pKingdom)
        {
            if (pKingdom?.data == null)
                return CourtInstitutionEffectRules.Resolve(
                    CourtInstitutionId.Zhou, eligibleXiaRealm: false);

            if (KingdomPolicyService.GetPolicyProfile(pKingdom) !=
                KingdomPolicyProfileId.Xia)
                return CourtInstitutionEffectRules.Resolve(
                    CourtInstitutionId.WesternPrimitive,
                    eligibleXiaRealm: false);

            bool eligible = pKingdom.data.original_actor_asset ==
                            LineageService.XIA_ASSET_ID;
            if (!eligible)
            {
                pKingdom.data.get(LineageKeys.XIAIZATION_LEVEL,
                    out int xiaizationLevel, 0);
                eligible = KingdomInstitutionalXiaizationRules
                    .ShouldUseXiaInstitutions(xiaizationLevel);
            }

            pKingdom.data.get(LineageKeys.COURT_INSTITUTION,
                out string institution, CourtInstitutionId.Zhou);
            return CourtInstitutionEffectRules.Resolve(institution,
                eligible);
        }
    }
}
