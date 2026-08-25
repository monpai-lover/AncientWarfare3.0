using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.court
{
    internal static class CityLeaderVacancyRepairService
    {
        // Candidate qualification can touch durable court state. Spread a
        // vacancy search across bounded catalog windows instead of scanning a
        // whole kingdom in one deferred-work item.
        private const int MaximumAttempts = 8;
        private static readonly Dictionary<string, int> LastAttemptFrames =
            new Dictionary<string, int>(StringComparer.Ordinal);

        internal static void Request(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom?.data == null) return;
            long kingdomId = pCity.kingdom.id;
            long cityId = pCity.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "city-leader-vacancy:" + kingdomId + ":" + cityId,
                DeferredWorkClass.CriticalRuntime,
                () => Repair(kingdomId, cityId, 0));
        }

        private static void Repair(long pKingdomId, long pCityId,
            int pAttempt)
        {
            string retryKey = pKingdomId + ":" + pCityId;
            int currentFrame = Time.frameCount;
            if (LastAttemptFrames.TryGetValue(retryKey,
                    out int lastAttemptFrame) &&
                CityBureauRetryRules.ShouldSkipSameFrame(
                    lastAttemptFrame, currentFrame))
            {
                if (pAttempt + 1 < MaximumAttempts)
                    DeferredRuntimeWorkService.EnqueueCoalesced(
                        "city-leader-vacancy:" + pKingdomId + ":" + pCityId,
                        DeferredWorkClass.CriticalRuntime,
                        () => Repair(pKingdomId, pCityId, pAttempt));
                return;
            }
            LastAttemptFrames[retryKey] = currentFrame;
            Kingdom kingdom = FindKingdom(pKingdomId);
            City city = FindCity(pCityId);
            if (kingdom?.data == null || city?.data == null ||
                kingdom.isRekt() || city.isRekt() || city.kingdom != kingdom ||
                city.hasLeader() || city.isGettingCaptured())
            {
                LastAttemptFrames.Remove(retryKey);
                return;
            }

            // The Harmony prefix now delegates to the same local appointment
            // reconciler used by the court window.  There is deliberately no
            // second local-only candidate selector here.
            AncientWarfare3.patch.AW_CityLeaderPatch.
                CheckFindLeader_Prefix(city);
            if (city.hasLeader())
            {
                LastAttemptFrames.Remove(retryKey);
                return;
            }
            if (pAttempt + 1 < MaximumAttempts)
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    "city-leader-vacancy:" + pKingdomId + ":" + pCityId,
                    DeferredWorkClass.CriticalRuntime,
                    () => Repair(pKingdomId, pCityId, pAttempt + 1));
            else
                LastAttemptFrames.Remove(retryKey);
        }

        internal static void ClearRuntime()
        {
            LastAttemptFrames.Clear();
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return World.world?.cities?.get(pId); }
            catch { return null; }
        }
    }
}
