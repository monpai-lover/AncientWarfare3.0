using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class DiplomacyOpinionService
    {
        public static int Read(Kingdom pMain, Kingdom pTarget)
        {
            if (pMain?.data == null || pTarget?.data == null ||
                pMain == pTarget || World.world?.diplomacy == null) return 0;
            try
            {
                DiplomacyRelation relation = World.world.diplomacy.getRelation(
                    pMain, pTarget);
                KingdomOpinion opinion = relation.getOpinion(pMain, pTarget);
                int truceContribution = 0;
                foreach (KeyValuePair<OpinionAsset, int> entry in opinion.results)
                    if (entry.Key?.id == "opinion_truce")
                    {
                        truceContribution = entry.Value;
                        break;
                    }
                int normalized = DiplomacyOpinionRules.Normalize(opinion.total,
                    truceContribution,
                    relation.data?.timestamp_last_war_ended ?? 0d);
                return normalized +
                       DiplomaticRelationModifierService.ReadCached(
                           pMain.id, pTarget.id, Date.getCurrentYear());
            }
            catch { return 0; }
        }
    }
}
