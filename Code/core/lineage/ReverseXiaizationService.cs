using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ReverseXiaizationService
    {
        private sealed class CultureBucket
        {
            internal Culture Culture;
            internal int Population;
            internal bool XiaAssociated;
        }

        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            int year = Date.getCurrentYear();
            string sourceMask = ReadString(pKingdom,
                LineageKeys.XIA_CONTACT_LAST_SOURCE_MASK);
            float contactFactor = ReverseXiaizationRules.ContactFactor(
                LineageService.IsXiaKingdom(pKingdom), sourceMask);

            List<City> cities = pKingdom.cities;
            if (cities == null) return;
            for (int i = 0; i < cities.Count; i++)
            {
                City city = cities[i];
                if (!CanProcess(city, pKingdom)) continue;
                city.data.get(LineageKeys.REVERSE_XIA_LAST_YEAR,
                    out int lastYear, int.MinValue);
                if (lastYear == year) continue;
                city.data.set(LineageKeys.REVERSE_XIA_LAST_YEAR, year);
                try
                {
                    ProcessCity(city, pKingdom, contactFactor);
                }
                catch (Exception e)
                {
                    ModClass.LogError("Reverse Xiaization city update " +
                                      "failed for city " + city.getID() +
                                      ": " + e);
                }
            }
        }

        private static void ProcessCity(City pCity, Kingdom pKingdom,
            float pContactFactor)
        {
            List<Actor> residents = pCity.units;
            if (residents == null || residents.Count == 0)
            {
                SaveInactive(pCity);
                return;
            }

            var buckets = new Dictionary<long, CultureBucket>();
            var xiaResidents = new List<Actor>();
            int totalPopulation = 0;
            for (int i = 0; i < residents.Count; i++)
            {
                Actor actor = residents[i];
                if (actor?.data == null || actor.isRekt()) continue;
                totalPopulation++;
                Culture culture = actor.culture;
                if (culture?.data == null) continue;
                long cultureId = culture.getID();
                if (!buckets.TryGetValue(cultureId,
                        out CultureBucket bucket))
                {
                    bucket = new CultureBucket
                    {
                        Culture = culture,
                        XiaAssociated = IsXiaAssociated(culture)
                    };
                    buckets[cultureId] = bucket;
                }
                bucket.Population++;
                if (bucket.XiaAssociated) xiaResidents.Add(actor);
            }

            if (totalPopulation <= 0)
            {
                SaveInactive(pCity);
                return;
            }

            var facts = new List<ReverseXiaizationCultureFact>(
                buckets.Count);
            foreach (KeyValuePair<long, CultureBucket> pair in buckets)
            {
                CultureBucket bucket = pair.Value;
                facts.Add(new ReverseXiaizationCultureFact(pair.Key,
                    bucket.Population, pCity.culture == bucket.Culture,
                    bucket.XiaAssociated));
            }
            long targetId = ReverseXiaizationRules.SelectTargetCultureId(
                facts);
            CultureBucket targetBucket = null;
            Culture target = targetId >= 0 &&
                             buckets.TryGetValue(targetId,
                                 out targetBucket)
                ? targetBucket.Culture
                : null;
            if (target != null && pCity.culture != target &&
                ReverseXiaizationRules.ShouldSwitchCityCulture(
                    targetBucket.Population, totalPopulation))
            {
                pCity.setCulture(target);
                RecordCultureShift(pCity, pKingdom, target);
            }
            if (xiaResidents.Count == 0)
            {
                SaveInactive(pCity);
                return;
            }
            float xiaRatio = xiaResidents.Count / (float)totalPopulation;
            pCity.data.get(LineageKeys.REVERSE_XIA_ACTIVE,
                out bool wasActive, false);
            bool active = ReverseXiaizationRules.ShouldRemainActive(
                wasActive, xiaRatio, target != null);
            pCity.data.set(LineageKeys.REVERSE_XIA_ACTIVE, active);
            if (!active)
            {
                pCity.data.set(LineageKeys.REVERSE_XIA_TARGET_CULTURE_ID,
                    -1L);
                pCity.data.set(LineageKeys.REVERSE_XIA_PROGRESS, 0f);
                return;
            }

            pCity.data.set(LineageKeys.REVERSE_XIA_TARGET_CULTURE_ID,
                targetId);
            if (!wasActive) RecordStarted(pCity, pKingdom, target);
            if (pContactFactor <= 0f || xiaResidents.Count == 0) return;

            pCity.data.get(LineageKeys.REVERSE_XIA_PROGRESS,
                out float savedProgress, 0f);
            ReverseXiaizationBudget budget =
                ReverseXiaizationRules.CalculateBudget(xiaResidents.Count,
                    ReverseXiaizationRules.YearlyRate(xiaRatio),
                    pContactFactor, savedProgress);
            int converted = ConvertResidents(xiaResidents, target,
                budget.WholeConversions);
            float remaining = budget.Remainder +
                              budget.WholeConversions - converted;
            pCity.data.set(LineageKeys.REVERSE_XIA_PROGRESS,
                Math.Min(xiaResidents.Count, Math.Max(0f, remaining)));

            int targetPopulation = targetBucket.Population + converted;
            if (pCity.culture != target &&
                ReverseXiaizationRules.ShouldSwitchCityCulture(
                    targetPopulation, totalPopulation))
            {
                pCity.setCulture(target);
                RecordCultureShift(pCity, pKingdom, target);
            }

            int remainingXia = Math.Max(0, xiaResidents.Count - converted);
            float remainingRatio = remainingXia / (float)totalPopulation;
            if (!ReverseXiaizationRules.ShouldRemainActive(true,
                    remainingRatio, target != null))
                SaveInactive(pCity);
        }

        private static int ConvertResidents(List<Actor> pResidents,
            Culture pTarget, int pLimit)
        {
            if (pTarget?.data == null || pLimit <= 0) return 0;
            pResidents.Sort((pLeft, pRight) =>
                (pLeft?.data?.id ?? long.MaxValue).CompareTo(
                    pRight?.data?.id ?? long.MaxValue));
            int converted = 0;
            for (int i = 0; i < pResidents.Count && converted < pLimit;
                 i++)
            {
                Actor actor = pResidents[i];
                if (actor?.data == null || actor.isRekt() ||
                    actor.culture == pTarget) continue;
                try
                {
                    if (actor.tryToConvertToCulture(pTarget)) converted++;
                }
                catch (Exception e)
                {
                    ModClass.LogError("Reverse Xiaization actor conversion " +
                                      "failed: " + e);
                }
            }
            return converted;
        }

        private static bool CanProcess(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.kingdom == pKingdom &&
                   !PeasantRebelBanditStrongholdService.IsStronghold(pCity);
        }

        private static bool IsXiaAssociated(Culture pCulture)
        {
            return ReverseXiaizationRules.IsXiaAssociatedCulture(
                XiaCultureIntegrationService.IsNativeXiaCulture(pCulture),
                XiaCultureIntegrationService.IsIntegrated(pCulture),
                XiaCultureIntegrationService.IsFullyIntegrated(pCulture));
        }

        private static string ReadString(Kingdom pKingdom, string pKey)
        {
            pKingdom.data.get(pKey, out string value, "");
            return value ?? "";
        }

        private static void SaveInactive(City pCity)
        {
            if (pCity?.data == null) return;
            pCity.data.set(LineageKeys.REVERSE_XIA_ACTIVE, false);
            pCity.data.set(LineageKeys.REVERSE_XIA_TARGET_CULTURE_ID, -1L);
            pCity.data.set(LineageKeys.REVERSE_XIA_PROGRESS, 0f);
        }

        private static void RecordStarted(City pCity, Kingdom pKingdom,
            Culture pTarget)
        {
            pCity.data.get(LineageKeys.REVERSE_XIA_START_RECORDED,
                out bool recorded, false);
            if (recorded) return;
            pCity.data.set(LineageKeys.REVERSE_XIA_START_RECORDED, true);
            HistoryWriter.RecordCity(pCity, pKingdom,
                CityEvent.CULTURE_ASSIMILATED,
                HistoryText.City(pCity, pKingdom) +
                HistoryLocalizationRules.H(
                    "aw_hist_reverse_xiaization_started_mid") +
                HistoryText.PlainText(CultureName(pTarget)) +
                HistoryLocalizationRules.H(
                    "aw_hist_reverse_xiaization_suffix"),
                HistoryTarget.City(pCity));
        }

        private static void RecordCultureShift(City pCity,
            Kingdom pKingdom, Culture pTarget)
        {
            HistoryWriter.RecordCity(pCity, pKingdom,
                CityEvent.CULTURE_ASSIMILATED,
                HistoryText.City(pCity, pKingdom) +
                HistoryLocalizationRules.H(
                    "aw_hist_reverse_xiaization_shift_mid") +
                HistoryText.PlainText(CultureName(pTarget)) +
                HistoryLocalizationRules.H(
                    "aw_hist_reverse_xiaization_suffix"),
                HistoryTarget.City(pCity));
        }

        private static string CultureName(Culture pCulture)
        {
            try { return pCulture?.name ?? ""; }
            catch { return ""; }
        }
    }
}
