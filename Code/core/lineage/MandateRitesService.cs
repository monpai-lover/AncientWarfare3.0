using System;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MandateRitesSnapshot
    {
        public int policy_points;
        public int temple_points;
        public int permanent_points;
        public int total_points;
        public int ordinary_required = MandateRitesRules.OrdinaryRequirement;
        public bool ordinary_ready;
    }

    internal static class MandateRitesService
    {
        public static MandateRitesSnapshot ReadSnapshot(Kingdom pKingdom)
        {
            var snapshot = new MandateRitesSnapshot();
            if (pKingdom?.data == null) return snapshot;

            snapshot.policy_points = KingdomPolicyService.IsCompleted(pKingdom,
                PolicyNodeKind.Social, "aw_policy_mandate_rites") ? 1 : 0;
            snapshot.temple_points = HasUsableCapitalTemple(pKingdom) ? 1 : 0;
            pKingdom.data.get(LineageKeys.MANDATE_RITUAL_COMPLETENESS,
                out int storedPermanentPoints, 0);
            snapshot.permanent_points = MandateRitesRules.NormalizePermanentPoints(
                storedPermanentPoints);
            snapshot.total_points = MandateRitesRules.TotalCompleteness(
                snapshot.policy_points > 0, snapshot.temple_points > 0,
                snapshot.permanent_points);
            snapshot.ordinary_ready = snapshot.total_points >=
                                      snapshot.ordinary_required;
            return snapshot;
        }

        public static bool HasUsableCapitalTemple(Kingdom pKingdom)
        {
            City capital = pKingdom?.capital;
            if (capital?.data == null || capital.isRekt() || capital.buildings == null)
                return false;
            try
            {
                foreach (Building building in capital.buildings)
                {
                    string assetId = building?.asset?.id;
                    if (string.IsNullOrEmpty(assetId) ||
                        !assetId.StartsWith("temple_", StringComparison.Ordinal))
                        continue;
                    if (building.isUsable() && !building.isAbandoned() &&
                        !building.isUnderConstruction()) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool CanDeclare(Kingdom pKingdom,
            MandateDeclarationSource pSource, out string pReason)
        {
            MandateRitesSnapshot snapshot = ReadSnapshot(pKingdom);
            return MandateRitesRules.CanDeclare(snapshot.total_points, pSource,
                out pReason);
        }

        public static bool CanPromoteToEmperor(Kingdom pKingdom,
            out string pReason)
        {
            bool ancestralRites = KingdomPolicyService.IsCompleted(pKingdom,
                PolicyNodeKind.Social, "aw_policy_ancestral_rites");
            bool ritesMusic = KingdomPolicyService.IsCompleted(pKingdom,
                PolicyNodeKind.Tech, "aw_tech_rites_music");
            return MandateRitesRules.CanPromoteToEmperor(true, ancestralRites,
                ritesMusic, out pReason);
        }
    }
}
