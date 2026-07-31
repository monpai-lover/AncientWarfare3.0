using System;

namespace AncientWarfare3.core.performance
{
    internal static class AWSimulationStepContext
    {
        [ThreadStatic] private static int _depth;

        internal static bool IsActive => _depth > 0;

        public static void Run(MapBox pMap, bool pPaused,
            float pSimulationElapsed, bool pNormalizeTimeScale,
            WorldTimeScaleAsset pSimulationTimeScale, Action pAction)
        {
            if (pMap == null) throw new ArgumentNullException(nameof(pMap));
            if (pAction == null) throw new ArgumentNullException(nameof(pAction));

            WorldTimeScaleAsset previousTimeScaleAsset =
                Config.time_scale_asset;
            WorldTimeScaleAsset timeScale = pNormalizeTimeScale
                ? previousTimeScaleAsset
                : pSimulationTimeScale;
            if (timeScale == null)
                throw new InvalidOperationException(
                    "AW scheduler cannot run without a time scale asset.");

            float previousElapsed = pMap.elapsed;
            float previousDeltaTime = pMap.delta_time;
            float previousFixedDeltaTime = pMap.fixed_delta_time;
            bool previousPaused = pMap._is_paused;
            float previousMultiplier = timeScale.multiplier;
            int previousTicks = timeScale.ticks;
            int previousConwayTicks = timeScale.conway_ticks;
            bool previousSonic = timeScale.sonic;

            Config.time_scale_asset = timeScale;
            pMap.elapsed = pSimulationElapsed;
            pMap.delta_time = AWFrameSchedulerRules.FixedSimulationStepSeconds;
            pMap.fixed_delta_time =
                AWFrameSchedulerRules.FixedSimulationStepSeconds;
            pMap._is_paused = pPaused;
            if (pNormalizeTimeScale)
            {
                timeScale.multiplier = 1f;
                timeScale.ticks = 1;
                timeScale.conway_ticks = 1;
                timeScale.sonic = false;
            }

            _depth++;
            try
            {
                pAction();
            }
            finally
            {
                _depth--;
                timeScale.multiplier = previousMultiplier;
                timeScale.ticks = previousTicks;
                timeScale.conway_ticks = previousConwayTicks;
                timeScale.sonic = previousSonic;
                Config.time_scale_asset = previousTimeScaleAsset;
                pMap.elapsed = previousElapsed;
                pMap.delta_time = previousDeltaTime;
                pMap.fixed_delta_time = previousFixedDeltaTime;
                pMap._is_paused = previousPaused;
            }
        }
    }
}
