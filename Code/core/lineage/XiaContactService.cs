using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class XiaContactService
    {
        private static readonly Dictionary<long, bool> XiaVassalContactBySuzerain = new Dictionary<long, bool>();
        private static int _vassalContactCacheYear = int.MinValue;

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral() || !pKingdom.isCiv()) return;
            if (LineageService.IsXiaKingdom(pKingdom)) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.XIA_CONTACT_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.XIA_CONTACT_LAST_YEAR, year);

            pKingdom.data.get(LineageKeys.XIA_CONTACT_MIXED_CHILD_EVENTS, out int mixedChildren, 0);
            if (mixedChildren > 0)
                pKingdom.data.set(LineageKeys.XIA_CONTACT_MIXED_CHILD_EVENTS, 0);

            bool borders = BordersXiaContactKingdom(pKingdom);
            bool nearby = !borders && NearbyXiaContactKingdom(pKingdom);
            bool diplomacy = HasAllianceWithXiaContactKingdom(pKingdom);
            bool vassal = HasVassalContactWithXia(pKingdom);
            int occupied = CountOccupiedXiaCities(pKingdom);
            bool official = HasOfficialXiaContact(pKingdom);

            float gain = XiaContactRules.CalculateYearlyGain(borders, diplomacy, vassal, occupied, mixedChildren,
                official, nearby);
            if (gain <= 0f) return;

            string sources = XiaContactRules.BuildSourceMask(borders, diplomacy, vassal, occupied, mixedChildren,
                official, nearby);
            pKingdom.data.set(LineageKeys.XIA_CONTACT_LAST_SOURCE_MASK, sources);
            pKingdom.data.set(LineageKeys.XIA_CONTACT_LAST_GAIN, gain);

            string reason = XiaContactRules.PrimaryReason(sources);
            XiaizationService.RegisterContactProgress(pKingdom, gain, reason, pRecord: true);
        }

        public static void OnMixedChildBorn(Actor pBaby, Actor pParent1, Actor pParent2)
        {
            if (pParent1?.data == null || pParent2?.data == null) return;
            bool p1Xia = IsXiaContactActor(pParent1);
            bool p2Xia = IsXiaContactActor(pParent2);
            if (p1Xia == p2Xia) return;

            var seen = new HashSet<long>();
            RegisterMixedChildKingdom(p1Xia ? pParent2.kingdom : pParent1.kingdom, seen);
            RegisterMixedChildKingdom(pBaby?.kingdom, seen);
        }

        private static void RegisterMixedChildKingdom(Kingdom pKingdom, HashSet<long> pSeen)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || LineageService.IsXiaKingdom(pKingdom)) return;
            if (!pSeen.Add(pKingdom.id)) return;
            pKingdom.data.get(LineageKeys.XIA_CONTACT_MIXED_CHILD_EVENTS, out int value, 0);
            pKingdom.data.set(LineageKeys.XIA_CONTACT_MIXED_CHILD_EVENTS, Math.Min(99, value + 1));
            pKingdom.data.get(LineageKeys.XIA_CONTACT_TOTAL_MIXED_CHILDREN, out int total, 0);
            pKingdom.data.set(LineageKeys.XIA_CONTACT_TOTAL_MIXED_CHILDREN, Math.Min(999999, total + 1));
        }

        private static bool BordersXiaContactKingdom(Kingdom pKingdom)
        {
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                try
                {
                    foreach (Kingdom other in city.neighbours_kingdoms)
                    {
                        if (IsXiaContactKingdom(other)) return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool NearbyXiaContactKingdom(Kingdom pKingdom)
        {
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                try
                {
                    foreach (TileZone nearZone in city.neighbour_zones)
                    {
                        if (IsXiaZone(nearZone, pKingdom)) return true;
                        TileZone[] around = nearZone?.neighbours_all;
                        if (around == null) continue;
                        for (int i = 0; i < around.Length; i++)
                            if (IsXiaZone(around[i], pKingdom))
                                return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static bool IsXiaZone(TileZone pZone, Kingdom pOwnKingdom)
        {
            Kingdom other = pZone?.city?.kingdom;
            return other != null && other != pOwnKingdom && IsXiaContactKingdom(other);
        }

        private static bool HasAllianceWithXiaContactKingdom(Kingdom pKingdom)
        {
            try
            {
                Alliance alliance = pKingdom.getAlliance();
                if (alliance?.kingdoms_list == null) return false;
                foreach (Kingdom other in alliance.kingdoms_list)
                    if (other != pKingdom && IsXiaContactKingdom(other))
                        return true;
            }
            catch { }
            return false;
        }

        private static bool HasVassalContactWithXia(Kingdom pKingdom)
        {
            Kingdom suzerain = VassalService.GetSuzerain(pKingdom);
            if (IsXiaContactKingdom(suzerain)) return true;
            EnsureVassalContactCache();
            return XiaVassalContactBySuzerain.ContainsKey(pKingdom.id);
        }

        private static void EnsureVassalContactCache()
        {
            int year = Date.getCurrentYear();
            if (_vassalContactCacheYear == year) return;
            _vassalContactCacheYear = year;
            XiaVassalContactBySuzerain.Clear();

            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsXiaContactKingdom(kingdom)) continue;
                long suzerainId = VassalService.GetSuzerainId(kingdom);
                if (suzerainId >= 0 && !XiaVassalContactBySuzerain.ContainsKey(suzerainId))
                    XiaVassalContactBySuzerain.Add(suzerainId, true);
            }
        }

        private static bool HasOfficialXiaContact(Kingdom pKingdom)
        {
            if (IsXiaContactOfficial(pKingdom?.king)) return true;
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                if (IsXiaContactOfficial(city.leader)) return true;
                try
                {
                    if (IsXiaContactOfficial(city.army?.getCaptain())) return true;
                }
                catch { }
            }

            return false;
        }

        private static bool IsXiaContactOfficial(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            return LineageService.IsXia(pActor) || LineageService.UsesAwLineageSystem(pActor);
        }

        private static int CountOccupiedXiaCities(Kingdom pKingdom)
        {
            int count = 0;
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                if (IsXiaOriginCity(city)) count++;
            }
            return count;
        }

        private static bool IsXiaOriginCity(City pCity)
        {
            if (pCity?.data == null) return false;
            try
            {
                if (MandateService.IsLegalCoreCity(pCity)) return true;
            }
            catch { }
            return IsXiaCulture(pCity.culture) || IsXiaLanguage(pCity.language);
        }

        private static bool IsXiaContactActor(Actor pActor)
        {
            return LineageService.IsXia(pActor) || IsXiaContactKingdom(pActor?.kingdom);
        }

        private static bool IsXiaContactKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            return LineageService.IsXiaKingdom(pKingdom) ||
                   XiaizationService.UsesXiaizedInstitutionSystem(pKingdom);
        }

        private static bool IsXiaCulture(Culture pCulture)
        {
            try
            {
                return pCulture?.data?.creator_species_id == LineageService.XIA_ASSET_ID ||
                       pCulture?.data?.original_actor_asset == LineageService.XIA_ASSET_ID;
            }
            catch { return false; }
        }

        private static bool IsXiaLanguage(Language pLanguage)
        {
            try { return pLanguage?.data?.creator_species_id == LineageService.XIA_ASSET_ID; }
            catch { return false; }
        }
    }
}
