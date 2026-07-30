using System;
using System.Globalization;
using AncientWarfare3.core.performance;
using UnityEngine;

namespace AncientWarfare3.ui
{
    internal sealed class AWWorldAgeClockTooltipAdapter : MonoBehaviour
    {
        private MapStats _sampledMapStats;
        private int _sampledWorldSeedId = -1;
        private double _previousWorldTime;
        private double _accumulatedWorldTime;
        private float _accumulatedRealTime;
        private float _actualSpeed;
        private float _sampledRequestedSpeed = -1f;
        private bool _hasActualSpeed;
        private bool _sampledPaused;
        private bool _bound;
        private TooltipAction _showTooltipAction;

        public static void Attach(UiWorldAgeInfo pWorldAgeInfo)
        {
            if (pWorldAgeInfo == null) return;
            AWWorldAgeClockTooltipAdapter adapter =
                pWorldAgeInfo.GetComponent<AWWorldAgeClockTooltipAdapter>() ??
                pWorldAgeInfo.gameObject
                    .AddComponent<AWWorldAgeClockTooltipAdapter>();
            adapter.Bind();
        }

        private void Awake()
        {
            _showTooltipAction = ShowTooltip;
        }

        private void Bind()
        {
            if (_bound) return;
            _bound = true;
            if (_showTooltipAction == null)
                _showTooltipAction = ShowTooltip;

            TipButton tipButton = GetComponent<TipButton>() ??
                                  gameObject.AddComponent<TipButton>();
            tipButton.type = AW_RawTooltip.TYPE;
            tipButton.showOnClick = false;
            tipButton.clickAction = null;
            tipButton.setHoverAction(_showTooltipAction, false);
            ResetMeasurement();
        }

        private void OnEnable()
        {
            ResetMeasurement();
        }

        private void Update()
        {
            if (!_bound) return;

            MapBox world = World.world;
            MapStats mapStats = world?.map_stats;
            if (!Config.game_loaded || mapStats == null)
            {
                ResetMeasurement();
                return;
            }

            int worldSeedId = MapBox.current_world_seed_id;
            double worldTime = mapStats.world_time;
            bool paused = world.isPaused();
            float requestedSpeed = GetRequestedSpeed();
            if (AWWorldAgeClockRules.ShouldResetSample(
                    ReferenceEquals(_sampledMapStats, mapStats),
                    _sampledWorldSeedId, worldSeedId,
                    _previousWorldTime, worldTime,
                    _sampledRequestedSpeed, requestedSpeed,
                    _sampledPaused, paused))
            {
                BeginMeasurement(mapStats, worldSeedId, worldTime,
                    requestedSpeed, paused);
                return;
            }

            double worldDelta = Math.Max(0d,
                worldTime - _previousWorldTime);
            _previousWorldTime = worldTime;
            if (paused) return;

            _accumulatedWorldTime += worldDelta;
            _accumulatedRealTime += Math.Max(0f, Time.unscaledDeltaTime);
            if (!AWWorldAgeClockRules.HasCompleteSampleWindow(
                    _accumulatedRealTime)) return;

            float sampledSpeed = (float)(_accumulatedWorldTime /
                                         _accumulatedRealTime);
            _actualSpeed = AWWorldAgeClockRules.SmoothActualSpeed(
                _actualSpeed, sampledSpeed, _hasActualSpeed);
            _hasActualSpeed = true;
            _accumulatedWorldTime = 0d;
            _accumulatedRealTime = 0f;
        }

        private void ShowTooltip()
        {
            string title = "aw_world_age_clock_title".Localize();
            string description;
            float requestedSpeed = GetRequestedSpeed();
            MapBox world = World.world;
            if (!Config.game_loaded || world?.map_stats == null)
            {
                description = "aw_world_age_clock_unavailable".Localize();
            }
            else if (world.isPaused())
            {
                description = "aw_world_age_clock_paused".Localize() +
                              "\n" + FormatLocalized(
                                  "aw_world_age_clock_requested_speed",
                                  FormatNumber(requestedSpeed)) +
                              "\n" +
                              "aw_world_age_clock_actual_paused".Localize();
            }
            else if (!_hasActualSpeed)
            {
                description = "aw_world_age_clock_measuring".Localize() +
                              "\n" + FormatLocalized(
                                  "aw_world_age_clock_requested_speed",
                                  FormatNumber(requestedSpeed)) +
                              "\n" +
                              "aw_world_age_clock_actual_measuring"
                                  .Localize();
            }
            else
            {
                description = FormatLocalized(
                                  "aw_world_age_clock_requested_speed",
                                  FormatNumber(requestedSpeed)) +
                              "\n" + FormatLocalized(
                                  "aw_world_age_clock_actual_speed",
                                  FormatNumber(_actualSpeed));
                if (_actualSpeed <= 0.001f)
                {
                    description += "\n" +
                                   "aw_world_age_clock_not_advancing"
                                       .Localize();
                }
                else
                {
                    AWWorldAgeDuration gameDuration =
                        AWWorldAgeClockRules
                            .GameDurationForOneRealSecond(_actualSpeed);
                    AWWorldAgeDuration realDuration =
                        AWWorldAgeClockRules
                            .RealDurationForOneGameYear(_actualSpeed);
                    description += "\n" + FormatLocalized(
                                       "aw_world_age_clock_real_to_game",
                                       FormatDuration(gameDuration)) +
                                   "\n" + FormatLocalized(
                                       "aw_world_age_clock_game_to_real",
                                       FormatDuration(realDuration));
                }
            }

            Tooltip.show(gameObject, AW_RawTooltip.TYPE, new TooltipData
            {
                tip_name = title,
                tip_description = description
            });
        }

        private void BeginMeasurement(MapStats pMapStats, int pWorldSeedId,
            double pWorldTime, float pRequestedSpeed, bool pPaused)
        {
            _sampledMapStats = pMapStats;
            _sampledWorldSeedId = pWorldSeedId;
            _previousWorldTime = pWorldTime;
            _sampledRequestedSpeed = pRequestedSpeed;
            _sampledPaused = pPaused;
            _accumulatedWorldTime = 0d;
            _accumulatedRealTime = 0f;
            _actualSpeed = 0f;
            _hasActualSpeed = false;
        }

        private void ResetMeasurement()
        {
            _sampledMapStats = null;
            _sampledWorldSeedId = -1;
            _previousWorldTime = 0d;
            _sampledRequestedSpeed = -1f;
            _sampledPaused = false;
            _accumulatedWorldTime = 0d;
            _accumulatedRealTime = 0f;
            _actualSpeed = 0f;
            _hasActualSpeed = false;
        }

        private static float GetRequestedSpeed()
        {
            WorldTimeScaleAsset timeScale = Config.time_scale_asset;
            return timeScale == null
                ? 0f
                : AWWorldAgeClockRules.RequestedSpeed(timeScale.multiplier,
                    timeScale.ticks);
        }

        private static string FormatDuration(AWWorldAgeDuration pDuration)
        {
            string key;
            switch (pDuration.Unit)
            {
                case AWWorldAgeDurationUnit.GameDays:
                    key = "aw_world_age_clock_game_days";
                    break;
                case AWWorldAgeDurationUnit.GameMonths:
                    key = "aw_world_age_clock_game_months";
                    break;
                case AWWorldAgeDurationUnit.GameYears:
                    key = "aw_world_age_clock_game_years";
                    break;
                case AWWorldAgeDurationUnit.RealSeconds:
                    key = "aw_world_age_clock_real_seconds";
                    break;
                case AWWorldAgeDurationUnit.RealMinutes:
                    key = "aw_world_age_clock_real_minutes";
                    break;
                case AWWorldAgeDurationUnit.RealHours:
                    key = "aw_world_age_clock_real_hours";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return FormatLocalized(key, FormatNumber(pDuration.Value));
        }

        private static string FormatLocalized(string pKey,
            params object[] pArguments)
        {
            return string.Format(LocalizedTextManager.getCulture(),
                pKey.Localize(), pArguments);
        }

        private static string FormatNumber(double pValue)
        {
            CultureInfo culture = LocalizedTextManager.getCulture();
            return pValue.ToString("0.##", culture);
        }
    }
}
