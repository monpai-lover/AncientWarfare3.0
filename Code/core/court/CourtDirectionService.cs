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

            CourtDirectionSnapshot target = CourtDirectionRules.Aggregate(BuildContributions(pKingdom));
            CourtDirectionSnapshot snapshot = lastYear < 0
                ? target
                : CourtDirectionRules.Smooth(Read(pKingdom), target, alpha: 0.25f, deadband: 0.03f);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_LIVELIHOOD, snapshot.Livelihood);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_WAR, snapshot.War);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_AGGRESSION, snapshot.Aggression);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_PEACE, snapshot.Peace);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_ORDER, snapshot.Order);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_COMMERCE, snapshot.Commerce);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_TECHNOLOGY, snapshot.Technology);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_LAST_YEAR, year);
            pKingdom.data.set(LineageKeys.COURT_DIRECTION_DIRTY, false);
            return snapshot;
        }

        private static CourtDirectionSnapshot Read(Kingdom pKingdom)
        {
            var result = new CourtDirectionSnapshot();
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_LIVELIHOOD, out result.Livelihood, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_WAR, out result.War, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_AGGRESSION, out result.Aggression, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_PEACE, out result.Peace, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_ORDER, out result.Order, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_COMMERCE, out result.Commerce, 0.5f);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_TECHNOLOGY, out result.Technology, 0.5f);
            return result;
        }

        private static IEnumerable<CourtInfluenceContribution> BuildContributions(Kingdom pKingdom)
        {
            var result = new List<CourtInfluenceContribution>();
            Actor king = pKingdom.king;
            if (IsValid(king, pKingdom))
            {
                string school = ResolveSchool(king);
                if (!string.IsNullOrEmpty(school))
                    result.Add(new CourtInfluenceContribution(king.data.id, school, 8f, 0, isKing: true));
            }

            Actor heir = HeirService.PeekRegisteredHeir(pKingdom);
            if (IsValid(heir, pKingdom) && heir != king)
            {
                string school = ResolveSchool(heir);
                if (!string.IsNullOrEmpty(school))
                    result.Add(new CourtInfluenceContribution(heir.data.id, school, 5f, 5, isKing: false));
            }

            foreach (CourtOfficerView officer in CourtService.GetActiveOfficers(pKingdom, 96))
            {
                Actor actor = World.world?.units?.get(officer.actor_id);
                if (!IsValid(actor, pKingdom) || actor == king) continue;
                actor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
                actor.data.get(LineageKeys.COURT_OFFICE_ID, out string office, "");
                if (courtKingdomId != pKingdom.id || string.IsNullOrEmpty(office) ||
                    office != officer.office_id) continue;
                string school = ResolveSchool(actor);
                if (!string.IsNullOrEmpty(school))
                    result.Add(new CourtInfluenceContribution(actor.data.id, school,
                        4f, OfficeRank(office), isKing: false));
            }

            foreach (GeneralReadModelEntry entry in GeneralService.GetActiveGeneralsForReadModel(
                         pKingdom, pAllowUnitFallback: false))
            {
                Actor general = entry.Actor;
                if (!IsValid(general, pKingdom)) continue;
                string school = ResolveSchool(general);
                if (!string.IsNullOrEmpty(school))
                    result.Add(new CourtInfluenceContribution(general.data.id, school,
                        3f, 40, isKing: false));
            }

            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    Actor leader = city?.leader;
                    if (!IsValid(leader, pKingdom)) continue;
                    string school = ResolveSchool(leader);
                    if (!string.IsNullOrEmpty(school))
                        result.Add(new CourtInfluenceContribution(leader.data.id,
                            school, 5f, 50, isKing: false));
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

        private static string ResolveSchool(Actor pActor)
        {
            pActor.data.get(LineageKeys.COURT_SCHOOL, out string school, "");
            return CourtSchoolRegistry.Find(school) == null ? CourtSchoolId.None : school;
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

    }
}
