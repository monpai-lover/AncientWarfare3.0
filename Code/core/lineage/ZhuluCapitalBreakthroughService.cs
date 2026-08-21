using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluCapitalBreakthroughService
    {
        internal static void TryApplyAfterCapture(City pCapturedCity,
            Kingdom pNewKingdom)
        {
            if (pCapturedCity?.data == null || pNewKingdom?.data == null ||
                pCapturedCity.kingdom != pNewKingdom) return;
            try
            {
                foreach (War war in World.world?.wars ??
                         Enumerable.Empty<War>())
                {
                    if (!ZhuluWarService.IsZhuluWar(war) ||
                        !IsAttackerSide(war, pNewKingdom)) continue;
                    TryApplyForWar(war, pCapturedCity, pNewKingdom);
                }
            }
            catch (Exception error)
            {
                ModClass.LogError("Zhulu capital breakthrough failed: " +
                                  error.Message);
            }
        }

        private static void TryApplyForWar(War pWar, City pCapturedCity,
            Kingdom pNewKingdom)
        {
            Kingdom defender = ZhuluWarService.ResolveLiveDeclaredDefender(pWar);
            if (defender?.data == null) return;
            bool isCapital = defender.capital == pCapturedCity;
            bool isSeat = DeJureRegionStore.TryGetBySeat(
                pCapturedCity.data.id, out DeJureRegion ignored);
            string key = BuildKey(pWar.data.id, pCapturedCity.data.id);
            if (!ZhuluCapitalBreakthroughRules.ShouldTrigger(true, isCapital,
                    isSeat, HasProcessed(pWar, key))) return;
            MarkProcessed(pWar, key);
            var regionIds = new List<long>();
            if (DeJureRegionStore.TryGetForCity(pCapturedCity.data.id,
                    out DeJureRegion region))
                regionIds.AddRange(region.MemberCityIds ?? new List<long>());
            IEnumerable<City> neighbors = pCapturedCity.neighbours_cities;
            var neighborIds = isCapital && neighbors != null
                ? neighbors.Where(p => p?.data != null)
                    .Select(p => p.data.id).ToArray()
                : Array.Empty<long>();
            IReadOnlyList<long> candidates = ZhuluCapitalBreakthroughRules.
                MergeCityIds(regionIds, neighborIds, pCapturedCity.data.id);
            HashSet<long> enemyIds = EnemyParticipantIds(pWar, pNewKingdom);
            foreach (long cityId in candidates)
            {
                City city = World.world?.cities?.get(cityId);
                if (!CanTransfer(city, pNewKingdom, enemyIds)) continue;
                try { city.joinAnotherKingdom(pNewKingdom); }
                catch (Exception error)
                {
                    ModClass.LogError("Zhulu breakthrough city transfer failed city=" +
                                      cityId + ": " + error.Message);
                }
            }
        }

        private static HashSet<long> EnemyParticipantIds(War pWar,
            Kingdom pNewKingdom)
        {
            var result = new HashSet<long>();
            bool attacker;
            try { attacker = pWar.isAttacker(pNewKingdom); }
            catch { return result; }
            IEnumerable<Kingdom> side = attacker ? pWar.getDefenders() :
                pWar.getAttackers();
            if (side == null) return result;
            foreach (Kingdom kingdom in side)
                if (kingdom?.data != null) result.Add(kingdom.data.id);
            return result;
        }

        private static bool CanTransfer(City pCity, Kingdom pNewKingdom,
            HashSet<long> pEnemyIds)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom?.data == null || pNewKingdom?.data == null ||
                PeasantRebelBanditStrongholdService.IsStrongholdCity(pCity))
                return false;
            long ownerId = pCity.kingdom.data.id;
            return ZhuluCapitalBreakthroughRules.ShouldTransferCity(
                pEnemyIds.Contains(ownerId), ownerId == pNewKingdom.data.id,
                false, pCity.kingdom.isNeutral(), true);
        }

        private static bool IsAttackerSide(War pWar, Kingdom pKingdom)
        {
            try { return pWar.isAttacker(pKingdom); }
            catch { return false; }
        }

        private static string BuildKey(long pWarId, long pCityId)
        {
            return pWarId.ToString() + ":" + pCityId.ToString();
        }

        private static bool HasProcessed(War pWar, string pKey)
        {
            string value;
            try { pWar.data.get(LineageKeys.ZHULU_CAPITAL_BREAKTHROUGH_KEYS,
                out value, ""); }
            catch { return false; }
            return new HashSet<string>((value ?? "").Split(
                new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal).Contains(pKey);
        }

        private static void MarkProcessed(War pWar, string pKey)
        {
            string value;
            try { pWar.data.get(LineageKeys.ZHULU_CAPITAL_BREAKTHROUGH_KEYS,
                out value, ""); }
            catch { value = ""; }
            var keys = new HashSet<string>((value ?? "").Split(
                new[] { '|' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            if (!keys.Add(pKey)) return;
            pWar.data.set(LineageKeys.ZHULU_CAPITAL_BREAKTHROUGH_KEYS,
                string.Join("|", keys.OrderBy(p => p)));
        }
    }
}
