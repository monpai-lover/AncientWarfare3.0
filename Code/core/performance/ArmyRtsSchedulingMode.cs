using System;

namespace AncientWarfare3.core.performance
{
    public enum ArmyRtsSchedulerMode
    {
        Native = 0,
        Aw3 = 1
    }

    public enum ArmyRtsSchedulerOwner
    {
        NativeArmyManager = 0,
        Aw3Authority = 1
    }

    public static class ArmyRtsSchedulingRules
    {
        public static ArmyRtsSchedulerMode ResolveStartupMode(bool configAw3)
        {
            return configAw3
                ? ArmyRtsSchedulerMode.Aw3
                : ArmyRtsSchedulerMode.Native;
        }

        public static bool ShouldRunOwner(ArmyRtsSchedulerMode pMode,
            ArmyRtsSchedulerOwner pOwner)
        {
            return pMode == ArmyRtsSchedulerMode.Native
                ? pOwner == ArmyRtsSchedulerOwner.NativeArmyManager
                : pOwner == ArmyRtsSchedulerOwner.Aw3Authority;
        }
    }

    public sealed class ArmyRtsSchedulingGate
    {
        private ArmyRtsSchedulerMode _owner;
        private bool _ownerFrozen;
        private long _lastToken = -1L;

        public void StartSession(bool configAw3)
        {
            _owner = ArmyRtsSchedulingRules.ResolveStartupMode(configAw3);
            _ownerFrozen = true;
            _lastToken = -1L;
        }

        public bool TryEnter(ArmyRtsSchedulerOwner pOwner, long pToken,
            bool allowed)
        {
            if (!allowed || !_ownerFrozen || pToken <= _lastToken ||
                !ArmyRtsSchedulingRules.ShouldRunOwner(_owner, pOwner))
                return false;
            _lastToken = pToken;
            return true;
        }

        public void Reset()
        {
            _owner = default;
            _ownerFrozen = false;
            _lastToken = -1L;
        }
    }

    internal static class ArmyRtsSchedulingMode
    {
        public static ArmyRtsSchedulerMode Current =>
            ArmyRtsSchedulingRules.ResolveStartupMode(
                AWPerformanceSettings.UseAw3ArmyRtsScheduler);
    }
}
