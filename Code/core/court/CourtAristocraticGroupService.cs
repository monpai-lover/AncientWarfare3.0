using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtAristocraticGroupService
    {
        private sealed class CachedGroups
        {
            public string Encoded = "";
            public IReadOnlyList<CourtAristocraticGroup> Groups =
                Array.Empty<CourtAristocraticGroup>();
        }

        private static readonly Dictionary<long, CachedGroups> RuntimeCache =
            new();

        public static void ClearRuntime()
        {
            RuntimeCache.Clear();
        }

        public static void Refresh(Kingdom pKingdom,
            IReadOnlyList<CourtOfficerView> pActiveOfficers)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            var facts = new List<CourtAristocraticMemberFact>(
                (pActiveOfficers?.Count ?? 0) +
                (pKingdom.cities?.Count ?? 0) + 8);

            foreach (CourtOfficerView officer in
                     pActiveOfficers ?? Array.Empty<CourtOfficerView>())
            {
                if (officer == null ||
                    officer.layer != CourtOfficeLayer.Central) continue;
                Actor actor = World.world?.units?.get(officer.actor_id);
                CourtAristocraticRole role = RoleForOffice(officer.office_id);
                AddFact(facts, actor, pKingdom, role,
                    (int)Math.Round(Math.Max(0f, officer.influence)),
                    ReadOfficerMerit(actor));
            }

            foreach (GeneralReadModelEntry entry in
                     GeneralService.GetActiveGeneralsForReadModel(
                         pKingdom, pAllowUnitFallback: false))
            {
                AddFact(facts, entry?.Actor, pKingdom,
                    CourtAristocraticRole.General, entry?.Merit ?? 0,
                    entry?.Merit ?? 0);
            }

            if (pKingdom.cities != null)
            {
                foreach (City city in pKingdom.cities)
                {
                    Actor leader = city?.leader;
                    AddFact(facts, leader, pKingdom,
                        CourtAristocraticRole.Governor,
                        (int)Math.Round(Math.Max(0f,
                            SafeStat(leader, "stewardship"))), 0);
                }
            }

            long rulingShiId = ReadShiId(pKingdom.king);
            IReadOnlyList<CourtAristocraticGroup> groups =
                CourtAristocraticGroupRules.Aggregate(facts, rulingShiId);
            string encoded = CourtAristocraticGroupRules.Encode(groups);
            pKingdom.data.set(LineageKeys.COURT_ARISTOCRATIC_GROUP_CACHE,
                encoded);
            RuntimeCache[pKingdom.id] = new CachedGroups
            {
                Encoded = encoded,
                Groups = groups
            };
        }

        public static IReadOnlyList<CourtAristocraticGroup> GetCachedGroups(
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null)
                return Array.Empty<CourtAristocraticGroup>();
            pKingdom.data.get(LineageKeys.COURT_ARISTOCRATIC_GROUP_CACHE,
                out string encoded, "");
            if (RuntimeCache.TryGetValue(pKingdom.id, out CachedGroups cached) &&
                string.Equals(cached.Encoded, encoded,
                    StringComparison.Ordinal)) return cached.Groups;
            IReadOnlyList<CourtAristocraticGroup> groups =
                CourtAristocraticGroupRules.Decode(encoded);
            RuntimeCache[pKingdom.id] = new CachedGroups
            {
                Encoded = encoded,
                Groups = groups
            };
            return groups;
        }

        public static int AppointmentPatronageBonus(Actor pActor,
            Kingdom pKingdom)
        {
            long shiId = ReadShiId(pActor);
            return CourtAristocraticGroupRules.PatronageBonus(shiId,
                GetCachedGroups(pKingdom));
        }

        private static void AddFact(
            ICollection<CourtAristocraticMemberFact> pFacts, Actor pActor,
            Kingdom pKingdom, CourtAristocraticRole pRole, int pInfluence,
            int pMerit)
        {
            if (pFacts == null || pActor?.data == null ||
                !pActor.isAlive() || pActor.isRekt() ||
                !CourtAffiliationResolver.IsDomestic(pActor, pKingdom)) return;
            long shiId = ReadShiId(pActor);
            pActor.data.get(LineageKeys.CLAN_NAME, out string shiName, "");
            if (shiId < 0 || string.IsNullOrWhiteSpace(shiName)) return;
            pFacts.Add(new CourtAristocraticMemberFact(pActor.data.id, shiId,
                shiName, pRole, pInfluence, pMerit));
        }

        private static CourtAristocraticRole RoleForOffice(string pOfficeId)
        {
            int rank = CourtPyramidRules.RankForOffice(pOfficeId);
            if (rank <= CourtPyramidRules.HighOfficeRank)
                return CourtAristocraticRole.CentralHigh;
            if (rank <= CourtPyramidRules.MinistryRank)
                return CourtAristocraticRole.CentralMiddle;
            return CourtAristocraticRole.CentralSpecialist;
        }

        private static long ReadShiId(Actor pActor)
        {
            if (pActor?.data == null) return -1L;
            pActor.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            return shiId;
        }

        private static int ReadOfficerMerit(Actor pActor)
        {
            if (pActor?.data == null) return 0;
            pActor.data.get(LineageKeys.OFFICER_MERIT,
                out float merit, 0f);
            return (int)Math.Round(Math.Max(0f, merit));
        }

        private static float SafeStat(Actor pActor, string pStat)
        {
            try { return pActor?.stats?[pStat] ?? 0f; }
            catch { return 0f; }
        }
    }
}
