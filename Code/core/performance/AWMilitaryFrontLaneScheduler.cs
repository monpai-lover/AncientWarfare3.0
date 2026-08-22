using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.performance
{
    internal static class AWMilitaryFrontLaneScheduler
    {
        private const double FrameBudgetMilliseconds = 2.5d;
        private static readonly List<long> Snapshot = new List<long>(256);
        private static double _debtSeconds;
        private static int _cursor;
        private static int _sweepRemaining;
        private static long _processed;
        private static double _lastMilliseconds;
        private static int _lastStep;
        private static int _maxDelayFrames;
        private static int _consecutiveDelayFrames;

        internal static bool HasActiveWork =>
            AWPerformanceSettings.Mode == AWSimulationMode.Large &&
            ArmyMilitaryMovementPriorityIndex.RtsMemberCount > 0;

        internal static double DebtSeconds => _debtSeconds;
        internal static long Processed => _processed;
        internal static double LastMilliseconds => _lastMilliseconds;
        internal static int LastStep => _lastStep;
        internal static int MaxDelayFrames => _maxDelayFrames;

        internal static void ProcessFrame()
        {
            long started = Stopwatch.GetTimestamp();
            _lastStep = 0;
            if (!ShouldRun())
            {
                ResetRuntimeDebt();
                _lastMilliseconds = ElapsedMilliseconds(started);
                return;
            }

            ArmyRtsTransportService.RefreshMilitaryP0Priority();
            if (ArmyMilitaryMovementPriorityIndex.RtsMemberCount <= 0)
            {
                ResetRuntimeDebt();
                _lastMilliseconds = ElapsedMilliseconds(started);
                return;
            }

            _debtSeconds = AWMilitaryFrontLaneRules.AddDebt(
                _debtSeconds, Math.Max(0d, Time.unscaledDeltaTime),
                ResolveRequestedSpeed());

            if (_sweepRemaining <= 0 || _cursor >= Snapshot.Count)
                StartSweep();

            while (AWMilitaryFrontLaneRules.HasStepDue(_debtSeconds) &&
                   _sweepRemaining > 0 &&
                   ElapsedMilliseconds(started) < FrameBudgetMilliseconds)
            {
                int limit = AWMilitaryFrontLaneRules.ResolveMaximumActors(
                    _sweepRemaining, 32);
                for (int i = 0; i < limit; i++)
                {
                    long actorId = Snapshot[_cursor++];
                    _sweepRemaining--;
                    try
                    {
                        AWCooperativeActorPostRunner.ProcessMilitaryP0Actor(
                            actorId,
                            (float)AWMilitaryFrontLaneRules.FixedStepSeconds,
                            allowAdditionalFrameStep: true);
                    }
                    catch (Exception error)
                    {
                        ModClass.LogError(
                            "AW military front lane actor failed: " + error);
                    }
                    _processed++;
                    if (ElapsedMilliseconds(started) >=
                        FrameBudgetMilliseconds) break;
                }

                if (_sweepRemaining == 0)
                {
                    _debtSeconds = AWMilitaryFrontLaneRules.
                        ConsumeCompletedSweep(_debtSeconds);
                    _lastStep++;
                    StartSweep();
                    _consecutiveDelayFrames = 0;
                }
                else
                {
                    _consecutiveDelayFrames++;
                    _maxDelayFrames = Math.Max(_maxDelayFrames,
                        _consecutiveDelayFrames);
                    break;
                }
            }

            _lastMilliseconds = ElapsedMilliseconds(started);
        }

        internal static void Reset()
        {
            Snapshot.Clear();
            ResetRuntimeDebt();
            _cursor = 0;
            _sweepRemaining = 0;
            _processed = 0L;
            _lastMilliseconds = 0d;
            _lastStep = 0;
            _maxDelayFrames = 0;
            _consecutiveDelayFrames = 0;
        }

        internal static string GetDiagnostics()
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "military_front={0}/{1}@{2:0.00}ms delay={3} " +
                "step={4} debt={5:0.000}",
                _processed, Snapshot.Count, _lastMilliseconds,
                _maxDelayFrames, _lastStep, _debtSeconds);
        }

        private static bool ShouldRun()
        {
            return AWPerformanceSettings.EnableFramePriorityScheduler &&
                   AWPerformanceSettings.Mode == AWSimulationMode.Large &&
                   Config.game_loaded && !SmoothLoader.isLoading() &&
                   !AW3MultiplayerReplicaScope.IsReplicaSession &&
                   World.world != null && !World.world.isPaused();
        }

        private static double ResolveRequestedSpeed()
        {
            WorldTimeScaleAsset asset = Config.time_scale_asset;
            if (asset == null) return 0d;
            return AWFrameSchedulerRules.RequestedSpeed(asset.multiplier,
                asset.ticks);
        }

        private static void StartSweep()
        {
            Snapshot.Clear();
            ArmyMilitaryMovementPriorityIndex.CopySnapshot(Snapshot);
            ArmyMilitaryMovementPriorityIndex.BeginMilitaryStep();
            _cursor = 0;
            _sweepRemaining = Snapshot.Count;
        }

        private static void ResetRuntimeDebt()
        {
            _debtSeconds = 0d;
            Snapshot.Clear();
            _cursor = 0;
            _sweepRemaining = 0;
            _consecutiveDelayFrames = 0;
        }

        private static double ElapsedMilliseconds(long pStarted)
        {
            return (Stopwatch.GetTimestamp() - pStarted) * 1000d /
                   Stopwatch.Frequency;
        }
    }
}
