using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class BanditGreatUprisingService
    {
        private static int _indexYear = int.MinValue;
        private static readonly Dictionary<long, List<long>> BanditsByOrigin =
            new Dictionary<long, List<long>>();

        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!CanMutate() || !IsValidOrigin(pKingdom)) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (lastYear == year) return;

            RebuildIndexIfNeeded(year);
            if (!BanditsByOrigin.TryGetValue(pKingdom.id,
                    out List<long> candidates))
                candidates = new List<long>();

            int originPopulation = CountOriginPopulation(pKingdom);
            int banditPopulation = CountBanditPopulation(candidates);
            bool corruption = IsLongTermCorruption(pKingdom);
            bool famine = IsFamine(pKingdom, originPopulation);

            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_CORRUPTION_STREAK,
                out int corruptionStreak, 0);
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_FAMINE_STREAK,
                out int famineStreak, 0);
            corruptionStreak = BanditGreatUprisingRules.AdvanceStreak(
                corruptionStreak, corruption,
                BanditGreatUprisingRules.CorruptionStreakYears);
            famineStreak = BanditGreatUprisingRules.AdvanceStreak(
                famineStreak, famine,
                BanditGreatUprisingRules.FamineStreakYears);

            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_ACTIVE,
                out bool active, false);
            if (!active && originPopulation > 0 &&
                BanditGreatUprisingRules.ShouldActivate(
                    banditPopulation, originPopulation, corruptionStreak,
                    famineStreak))
            {
                active = true;
                pKingdom.data.set(
                    LineageKeys.MANDATE_REBEL_GREAT_UPRISING_STARTED_YEAR,
                    year);
                ModClass.LogInfo(
                    "[AW3 uprising] realm=" + pKingdom.id +
                    " entered great uprising era bandits=" +
                    banditPopulation + " population=" + originPopulation +
                    " ratio=" + (banditPopulation /
                        (double)Math.Max(1, originPopulation)));
            }

            pKingdom.data.set(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_ACTIVE, active);
            pKingdom.data.set(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_CORRUPTION_STREAK,
                corruptionStreak);
            pKingdom.data.set(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_FAMINE_STREAK,
                famineStreak);
            pKingdom.data.set(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_LAST_YEAR, year);

            if (!active || candidates.Count == 0) return;
            ConvertCandidates(pKingdom, candidates, year);
        }

        internal static void ClearRuntime()
        {
            _indexYear = int.MinValue;
            BanditsByOrigin.Clear();
        }

        internal static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.kingdoms == null) return;
            RebuildIndexIfNeeded(Date.getCurrentYear());
        }

        private static void ConvertCandidates(Kingdom pOrigin,
            List<long> pCandidates, int pYear)
        {
            int count = pCandidates.Count;
            if (count <= 0) return;

            pOrigin.data.get(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_CONVERSION_CURSOR,
                out int cursor, 0);
            cursor = NormalizeCursor(cursor, count);
            int budget = Math.Min(
                BanditGreatUprisingRules.ConversionBudgetPerYear, count);
            int converted = 0;
            for (int offset = 0; offset < budget; offset++)
            {
                int index = (cursor + offset) % count;
                Kingdom candidate = ResolveKingdom(pCandidates[index]);
                Kingdom origin = candidate == null
                    ? null
                    : PeasantRebelRouteService.ResolveOrigin(candidate);
                bool validOrigin = origin?.data != null &&
                                   !origin.isRekt() &&
                                   origin.isCiv() && !origin.isNeutral() &&
                                   origin == pOrigin;
                if (!BanditGreatUprisingRules.CanConvert(
                        true,
                        PeasantRebelRouteService.IsBandit(candidate),
                        validOrigin)) continue;
                try
                {
                    if (PeasantRebelRouteService.ConvertBanditToFounding(
                            candidate, pOrigin))
                    {
                        converted++;
                    }
                }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "[AW3 uprising] bandit conversion failed realm=" +
                        pOrigin.id + " bandit=" + candidate.id + " " +
                        error.Message);
                }
            }

            pOrigin.data.set(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_CONVERSION_CURSOR,
                BanditGreatUprisingRules.AdvanceCursor(cursor, budget,
                    count));
            if (converted > 0)
                pOrigin.data.set(
                    LineageKeys.MANDATE_REBEL_GREAT_UPRISING_LAST_CONVERSION_YEAR,
                    pYear);
            ModClass.LogInfo(
                "[AW3 uprising] realm=" + pOrigin.id + " candidates=" +
                count + " attempted=" + budget + " converted=" + converted);
        }

        private static void RebuildIndexIfNeeded(int pYear)
        {
            if (_indexYear == pYear) return;
            BanditsByOrigin.Clear();
            _indexYear = pYear;
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() ||
                    !PeasantRebelRouteService.IsBandit(kingdom)) continue;
                Kingdom origin = PeasantRebelRouteService.ResolveOrigin(
                    kingdom);
                if (!IsValidOrigin(origin)) continue;
                if (!BanditsByOrigin.TryGetValue(origin.id,
                        out List<long> list))
                {
                    list = new List<long>();
                    BanditsByOrigin[origin.id] = list;
                }
                list.Add(kingdom.id);
            }
            foreach (List<long> list in BanditsByOrigin.Values)
                list.Sort();
        }

        private static int CountOriginPopulation(Kingdom pOrigin)
        {
            int population = 0;
            try
            {
                foreach (City city in pOrigin.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    population = SaturatingAdd(population,
                        Math.Max(0, city.getPopulationPeople()));
                }
            }
            catch { }
            return population;
        }

        private static int CountBanditPopulation(List<long> pCandidates)
        {
            int population = 0;
            foreach (long id in pCandidates)
            {
                Kingdom kingdom = ResolveKingdom(id);
                if (kingdom?.data == null) continue;
                try
                {
                    foreach (Actor actor in kingdom.getUnits())
                    {
                        if (actor?.data == null || actor.isRekt() ||
                            !actor.isAlive() || actor.asset?.is_boat == true)
                            continue;
                        population = SaturatingAdd(population, 1);
                    }
                }
                catch { }
            }
            return population;
        }

        private static bool IsFamine(Kingdom pOrigin, int pPopulation)
        {
            if (pPopulation <= 0) return false;
            int hungry = 0;
            try
            {
                foreach (City city in pOrigin.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    hungry = SaturatingAdd(hungry,
                        Math.Max(0, city.status?.hungry ?? 0));
                }
            }
            catch { return false; }
            return hungry / (double)Math.Max(1, pPopulation) >= 0.30d;
        }

        private static bool IsLongTermCorruption(Kingdom pKingdom)
        {
            int mandate = 50;
            int authority = 50;
            try
            {
                pKingdom.data.get(LineageKeys.MANDATE_VALUE,
                    out mandate, mandate);
                pKingdom.data.get(LineageKeys.MANDATE_AUTHORITY,
                    out authority, authority);
            }
            catch { }

            MandateReport report = MandateService.ReadReportReadOnly();
            bool currentMandate = report?.active == true &&
                                  report.kingdom_id == pKingdom.id;
            if (currentMandate)
            {
                mandate = report.mandate_value;
                authority = report.imperial_authority;
            }
            return mandate <= 30 || authority <= 30 ||
                   (currentMandate &&
                    (MandatePhaseService.CurrentPhase == MandatePhase.Decline ||
                     MandatePhaseService.CurrentPhase == MandatePhase.Chaos));
        }

        private static bool IsValidOrigin(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId <= 0 || World.world?.kingdoms == null)
                return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(pKingdomId);
                return kingdom?.data != null && !kingdom.isRekt()
                    ? kingdom
                    : null;
            }
            catch { return null; }
        }

        private static int NormalizeCursor(int pCursor, int pCount)
        {
            if (pCount <= 0) return 0;
            int cursor = pCursor % pCount;
            return cursor < 0 ? cursor + pCount : cursor;
        }

        private static int SaturatingAdd(int pCurrent, int pDelta)
        {
            long result = (long)Math.Max(0, pCurrent) + Math.Max(0, pDelta);
            return result >= int.MaxValue ? int.MaxValue : (int)result;
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
