using System;

namespace AncientWarfare3.core.lineage
{
    public static class SlaveArmyFormationRules
    {
        public const int MaxNonSlaveCadres = 5;
        public const float TargetSlaveRatio = 0.8f;

        public static bool IsSlaveArmyComposition(int totalWarriors, int slaveWarriors, int nonSlaveWarriors,
            bool captainNonSlave)
        {
            if (totalWarriors <= 0) return false;
            if (!captainNonSlave) return false;
            if (nonSlaveWarriors > MaxNonSlaveCadres) return false;
            if (slaveWarriors + nonSlaveWarriors != totalWarriors) return false;
            return slaveWarriors >= Math.Ceiling(totalWarriors * TargetSlaveRatio) ||
                   nonSlaveWarriors <= MaxNonSlaveCadres && slaveWarriors > 0;
        }

        public static bool CanAddSlaveToArmy(int totalWarriors, int slaveWarriors, int nonSlaveWarriors)
        {
            if (totalWarriors < 0 || slaveWarriors < 0 || nonSlaveWarriors < 0) return false;
            return true;
        }

        public static bool CanAddNonSlaveCadre(int nonSlaveWarriors, bool hasNonSlaveCaptain)
        {
            int used = nonSlaveWarriors;
            if (!hasNonSlaveCaptain) used++;
            return used < MaxNonSlaveCadres;
        }
    }
}
