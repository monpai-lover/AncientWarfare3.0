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
        private long _lastToken;
        private bool _hasLastToken;

        public bool TryEnter(ArmyRtsSchedulerMode pMode,
            ArmyRtsSchedulerOwner pOwner, long pToken,
            bool pAuthorityAllowed)
        {
            if (!pAuthorityAllowed ||
                !ArmyRtsSchedulingRules.ShouldRunOwner(pMode, pOwner) ||
                (_hasLastToken && _lastToken == pToken))
                return false;
            _lastToken = pToken;
            _hasLastToken = true;
            return true;
        }

        public void Reset()
        {
            _lastToken = 0L;
            _hasLastToken = false;
        }
    }

    internal static class ArmyRtsSchedulingMode
    {
        public static ArmyRtsSchedulerMode Current =>
            ArmyRtsSchedulingRules.ResolveStartupMode(
                AWPerformanceSettings.UseAw3ArmyRtsScheduler);
    }
}
