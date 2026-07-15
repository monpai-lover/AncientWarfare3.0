using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class AristocraticSuccessionService
    {
        private sealed class HouseAggregate
        {
            public Clan Clan;
            public int OfficeHolders;
            public int RealmMembers;
            public int EligibleAdultMales;
            public Actor BestRuler;
            public AristocraticRulerScore BestRulerScore;
        }

        public static Actor SelectRuler(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return null;

            var houses = new Dictionary<long, HouseAggregate>();
            foreach (Actor actor in pKingdom.getUnits())
            {
                if (!IsLivingDomesticMember(actor, pKingdom)) continue;
                Clan clan = actor.clan;
                if (clan?.data == null || clan.isRekt()) continue;

                long clanId = clan.data.id;
                if (!houses.TryGetValue(clanId, out HouseAggregate house))
                {
                    house = new HouseAggregate { Clan = clan };
                    houses.Add(clanId, house);
                }

                house.RealmMembers++;
                if (HoldsDomesticOffice(actor, pKingdom)) house.OfficeHolders++;

                bool inLineageSystem = LineageService.IsXia(actor) ||
                                       LineageService.UsesAwLineageSystem(actor);
                bool eligible = AristocraticSuccessionRules.IsEligibleRuler(
                    inLineageSystem,
                    hasVisibleClan: true,
                    isMale: actor.isSexMale(),
                    isAdult: actor.isAdult(),
                    isAlive: actor.isAlive() && !actor.isRekt(),
                    isSlave: SlaveService.IsSlave(actor),
                    isKing: actor.isKing());
                if (!eligible) continue;

                house.EligibleAdultMales++;
                AristocraticRulerScore score = BuildRulerScore(actor, clan);
                if (house.BestRuler == null ||
                    AristocraticSuccessionRules.CompareRulers(score, house.BestRulerScore) < 0)
                {
                    house.BestRuler = actor;
                    house.BestRulerScore = score;
                }
            }

            HouseAggregate bestHouse = null;
            AristocraticHouseScore bestScore = default;
            foreach (KeyValuePair<long, HouseAggregate> item in houses)
            {
                HouseAggregate house = item.Value;
                if (house.BestRuler?.data == null) continue;
                AristocraticHouseScore score = new AristocraticHouseScore(
                    item.Key,
                    SafeRenown(house.Clan),
                    house.OfficeHolders,
                    house.RealmMembers,
                    house.EligibleAdultMales,
                    house.BestRulerScore);
                if (bestHouse == null ||
                    AristocraticSuccessionRules.CompareHouses(score, bestScore) < 0)
                {
                    bestHouse = house;
                    bestScore = score;
                }
            }

            return bestHouse?.BestRuler;
        }

        private static bool IsLivingDomesticMember(Actor pActor, Kingdom pKingdom)
        {
            return pActor?.data != null &&
                   pActor.kingdom == pKingdom &&
                   pActor.isAlive() &&
                   !pActor.isRekt() &&
                   pActor.hasClan();
        }

        private static bool HoldsDomesticOffice(Actor pActor, Kingdom pKingdom)
        {
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID, out string officeId, "");
            if (courtKingdomId == pKingdom.id && !string.IsNullOrEmpty(officeId)) return true;
            if (pActor.isCityLeader() && pActor.city?.kingdom == pKingdom) return true;
            return GeneralService.IsActiveGeneralFast(pActor);
        }

        private static AristocraticRulerScore BuildRulerScore(Actor pActor, Clan pClan)
        {
            bool isChief = pClan?.data != null && pClan.data.chief_id == pActor.data.id;
            return new AristocraticRulerScore(
                pActor.data.id,
                isChief,
                pActor.diplomacy,
                pActor.warfare,
                pActor.stewardship,
                pActor.level,
                CombatScore(pActor),
                SafeAge(pActor));
        }

        private static float CombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return SafeStat(pActor, "damage") + SafeStat(pActor, "warfare") * 2f +
                   SafeStat(pActor, "health") * 0.1f + SafeStat(pActor, "armor") * 2f +
                   SafeStat(pActor, "speed") * 0.25f;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor.getAge(); }
            catch { return 0; }
        }

        private static int SafeRenown(Clan pClan)
        {
            try { return pClan?.getRenown() ?? 0; }
            catch { return 0; }
        }
    }
}
