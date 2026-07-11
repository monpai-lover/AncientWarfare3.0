using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtDirectionService
    {
        public static void MarkDirty(Kingdom pKingdom)
        {
            pKingdom?.data?.set(LineageKeys.COURT_DIRECTION_DIRTY, true);
        }

        public static CourtDirectionSnapshot RecalculateIfDirty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return new CourtDirectionSnapshot();
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_LAST_YEAR, out int lastYear, -1);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_DIRTY, out bool dirty, true);
            if (!dirty && lastYear == year) return Read(pKingdom);

            CourtDirectionSnapshot snapshot = CourtDirectionRules.Aggregate(BuildContributions(pKingdom));
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_LIVELIHOOD, snapshot.Livelihood);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_AGGRESSION, snapshot.Aggression);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_PEACE, snapshot.Peace);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_LAST_YEAR, year);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_DIRTY, false);
            return snapshot;
        }

        private static CourtDirectionSnapshot Read(Kingdom pKingdom)
        {
            var result = new CourtDirectionSnapshot();
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_LIVELIHOOD, out result.Livelihood, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_AGGRESSION, out result.Aggression, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_PEACE, out result.Peace, 0.5f);
            return result;
        }

        private static IEnumerable<CourtInfluenceContribution> BuildContributions(Kingdom pKingdom)
        {
            var result = new List<CourtInfluenceContribution>();
            Actor king = pKingdom.king;
            if (IsValid(king, pKingdom))
            {
                string school = ResolveSchool(king, "king_council");
                result.Add(new CourtInfluenceContribution(king.data.id, school, 8f, 0, isKing: true));
            }

            try
            {
                foreach (Actor actor in pKingdom.getUnits())
                {
                    if (!IsValid(actor, pKingdom) || actor == king) continue;
                    actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                    actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                    if (courtKingdomId != pKingdom.id || string.IsNullOrEmpty(office)) continue;
                    result.Add(new CourtInfluenceContribution(actor.data.id, ResolveSchool(actor, office),
                        OfficeWeight(office), OfficeRank(office), isKing: false));
                }
            }
            catch { }

            foreach (Actor general in GeneralService.GetActiveGenerals(pKingdom))
            {
                if (!IsValid(general, pKingdom)) continue;
                float weight = 3.5f + Math.Min(2f, GeneralService.GetMerit(general) / 25f);
                result.Add(new CourtInfluenceContribution(general.data.id, CourtSchoolId.Military,
                    weight, 40, isKing: false));
            }

            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    Actor leader = city?.leader;
                    if (!IsValid(leader, pKingdom)) continue;
                    result.Add(new CourtInfluenceContribution(leader.data.id,
                        ResolveSchool(leader, CourtOfficeId.Governor), 2f, 50, isKing: false));
                }
            }
            catch { }
            return result;
        }

        private static bool IsValid(Actor pActor, Kingdom pKingdom)
        {
            return pActor?.data != null && pActor.kingdom == pKingdom &&
                   pActor.isAlive() && !pActor.isRekt();
        }

        private static string ResolveSchool(Actor pActor, string pOffice)
        {
            pActor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
            if (!string.IsNullOrEmpty(CourtTraitRules.TraitForSchool(school))) return school;
            return CourtSchoolAssignmentRules.ResolveSchool(pOffice, new CourtCandidateProfile(
                pActor.data.id, Stat(pActor, "stewardship"), Stat(pActor, "diplomacy"),
                Stat(pActor, "warfare"), Stat(pActor, "intelligence"), school));
        }

        private static int OfficeRank(string pOffice)
        {
            if (pOffice == CourtOfficeId.ImperialPhysician || pOffice == CourtOfficeId.ImperialAstrologer) return 30;
            if (pOffice == CourtOfficeId.Justice || pOffice == CourtOfficeId.Steward ||
                pOffice == CourtOfficeId.Erudite || pOffice == CourtOfficeId.Libu ||
                pOffice == CourtOfficeId.Hubu || pOffice == CourtOfficeId.Ribu ||
                pOffice == CourtOfficeId.Bingbu || pOffice == CourtOfficeId.Xingbu ||
                pOffice == CourtOfficeId.Gongbu) return 20;
            return 10;
        }

        private static float OfficeWeight(string pOffice)
        {
            int rank = OfficeRank(pOffice);
            return rank == 10 ? 6f : rank == 20 ? 4.5f : 3.5f;
        }

        private static float Stat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}
