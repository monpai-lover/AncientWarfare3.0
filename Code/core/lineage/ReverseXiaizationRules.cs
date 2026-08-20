using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct ReverseXiaizationCultureFact
    {
        public ReverseXiaizationCultureFact(long pCultureId, int pPopulation,
            bool pCurrentCityCulture, bool pXiaAssociated)
        {
            CultureId = pCultureId;
            Population = Math.Max(0, pPopulation);
            CurrentCityCulture = pCurrentCityCulture;
            XiaAssociated = pXiaAssociated;
        }

        public long CultureId { get; }
        public int Population { get; }
        public bool CurrentCityCulture { get; }
        public bool XiaAssociated { get; }
    }

    public readonly struct ReverseXiaizationBudget
    {
        public ReverseXiaizationBudget(int pWholeConversions,
            float pRemainder)
        {
            WholeConversions = Math.Max(0, pWholeConversions);
            Remainder = Math.Max(0f, pRemainder);
        }

        public int WholeConversions { get; }
        public float Remainder { get; }
    }

    public static class ReverseXiaizationRules
    {
        public const float StartRatio = 0.35f;
        public const float StopRatio = 0.40f;
        public const float AcceleratedRatio = 0.15f;
        public const float NormalRate = 0.02f;
        public const float AcceleratedRate = 0.05f;

        public static bool ShouldRemainActive(bool pWasActive,
            float pXiaRatio, bool pHasTarget)
        {
            if (!pHasTarget) return false;
            float ratio = Clamp01(pXiaRatio);
            return pWasActive ? ratio < StopRatio : ratio < StartRatio;
        }

        public static float YearlyRate(float pXiaRatio)
        {
            return Clamp01(pXiaRatio) < AcceleratedRatio
                ? AcceleratedRate
                : NormalRate;
        }

        public static bool IsXiaAssociatedCulture(bool pNativeXia,
            bool pIntegrated, bool pFullyIntegrated)
        {
            return pNativeXia || pIntegrated || pFullyIntegrated;
        }

        public static float ContactFactor(bool pNativeXiaKingdom,
            string pSourceMask)
        {
            if (pNativeXiaKingdom) return 0f;

            float protection = 0f;
            var sources = new HashSet<string>(
                (pSourceMask ?? string.Empty).Split(
                    new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            if (sources.Contains("nearby")) protection += 0.15f;
            if (sources.Contains("border")) protection += 0.35f;
            if (sources.Contains("diplomacy")) protection += 0.25f;
            if (sources.Contains("vassal")) protection += 0.45f;
            if (sources.Contains("mixed")) protection += 0.10f;
            if (sources.Contains("official")) protection += 0.25f;

            // Occupation is deliberately ignored: occupying more Xia land
            // must not protect an invader from reverse Xiaization.
            if (protection >= 0.75f) return 0f;
            return Clamp01(1f - protection);
        }

        public static long SelectTargetCultureId(
            IReadOnlyList<ReverseXiaizationCultureFact> pCultures)
        {
            if (pCultures == null) return -1L;
            ReverseXiaizationCultureFact best = default;
            bool found = false;
            for (int i = 0; i < pCultures.Count; i++)
            {
                ReverseXiaizationCultureFact candidate = pCultures[i];
                if (candidate.XiaAssociated || candidate.Population <= 0 ||
                    candidate.CultureId < 0) continue;
                if (!found || candidate.Population > best.Population ||
                    candidate.Population == best.Population &&
                    candidate.CurrentCityCulture &&
                    !best.CurrentCityCulture ||
                    candidate.Population == best.Population &&
                    candidate.CurrentCityCulture == best.CurrentCityCulture &&
                    candidate.CultureId < best.CultureId)
                {
                    best = candidate;
                    found = true;
                }
            }
            return found ? best.CultureId : -1L;
        }

        public static ReverseXiaizationBudget CalculateBudget(
            int pXiaPopulation, float pRate, float pContactFactor,
            float pSavedRemainder)
        {
            int population = Math.Max(0, pXiaPopulation);
            float raw = Math.Max(0f, pSavedRemainder) + population *
                Math.Max(0f, pRate) * Clamp01(pContactFactor);
            int whole = Math.Min(population, (int)Math.Floor(raw));
            float remainder = whole >= population
                ? 0f
                : raw - whole;
            return new ReverseXiaizationBudget(whole, remainder);
        }

        public static bool ShouldSwitchCityCulture(int pTargetPopulation,
            int pTotalPopulation)
        {
            return pTotalPopulation > 0 && pTargetPopulation >
                pTotalPopulation / 2f;
        }

        private static float Clamp01(float pValue)
        {
            return Math.Max(0f, Math.Min(1f, pValue));
        }
    }
}
