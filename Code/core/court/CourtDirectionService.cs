using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal static class CourtDirectionService
    {
        public static void MarkDirty(Kingdom pKingdom)
        {
            pKingdom?.data?.set(LineageKeys.COURT_DIRECTION_DIRTY, true);
            HeirService.MarkSelectionDirty(pKingdom);
        }

        public static CourtDirectionSnapshot RecalculateIfDirty(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return new CourtDirectionSnapshot();
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_LAST_YEAR, out int lastYear, -1);
            pKingdom.data.get(LineageKeys.COURT_DIRECTION_DIRTY, out bool dirty, true);
            if (!dirty && lastYear == year) return ReadCached(pKingdom);

            CourtDirectionSnapshot target = CourtDirectionRules.Aggregate(BuildContributions(pKingdom));
            CourtDirectionSnapshot snapshot = lastYear < 0
                ? target
                : CourtDirectionRules.Smooth(ReadCached(pKingdom), target, alpha: 0.25f, deadband: 0.03f);
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

        internal static CourtDirectionSnapshot ReadCached(Kingdom pKingdom)
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
            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_ID,
                out long ministerialPremierId, -1L);
            pKingdom.data.get(LineageKeys.MINISTERIAL_PREMIER_POWER,
                out int ministerialPower, 0);
            float ministerialMultiplier =
                MinisterialPowerRules.DirectionMultiplier(ministerialPower);
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
                {
                    float weight = 4f * OfficialCareerRankRules.InfluenceMultiplier(
                        OfficialCareerStateService.ReadRankFast(actor));
                    weight = Math.Max(0f,
                        CustomCourtRuntimeEffectService.GetOfficeInfluenceModifier(
                            pKingdom, office, actor.data.id).Apply(weight));
                    if (actor.data.id == ministerialPremierId)
                        weight *= ministerialMultiplier;
                    result.Add(new CourtInfluenceContribution(actor.data.id, school,
                        weight, OfficeRank(office), isKing: false));
                }
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
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return false;
            pActor.data.get(LineageKeys.COURT_LAYER, out string layer, "");
            return CourtAffiliationResolver.CanServe(pActor, pKingdom, layer);
        }

        private static string ResolveSchool(Actor pActor)
        {
            return pActor?.data == null
                ? CourtSchoolId.None
                : SchoolMembershipService.GetSchool(pActor.data.id);
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
