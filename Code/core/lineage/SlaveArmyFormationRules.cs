using System;

namespace AncientWarfare3.core.lineage
{
    public static class SlaveArmyFormationRules
    {
        public const int MaximumRoster = 25;
        public const int MinimumInitialSlaves = 4;
        public const int InitialRosterSize = 5;
        public const int MaxResidentsScannedPerWorkItem = 32;
        public const int MaxActorsChangedPerWorkItem = 4;

        public static bool CanForm(bool slaveryEnabled, bool capabilityEnabled,
            bool militaryEmergency, int existingKingdomVanguards)
        {
            return slaveryEnabled && capabilityEnabled && militaryEmergency && existingKingdomVanguards <= 0;
        }

        public static bool ShouldRestartCandidateScan(int remainingCitySlots)
        {
            return remainingCitySlots <= 0;
        }

        public static bool IsSlaveArmyComposition(int totalWarriors, int slaveWarriors, int nonSlaveWarriors,
            bool captainNonSlave)
        {
            if (totalWarriors < InitialRosterSize || totalWarriors > MaximumRoster) return false;
            if (!captainNonSlave) return false;
            if (slaveWarriors + nonSlaveWarriors != totalWarriors) return false;
            if (slaveWarriors < MinimumInitialSlaves || nonSlaveWarriors < 1) return false;
            return slaveWarriors * 5 >= totalWarriors * 4;
        }

        public static bool CanAddSlaveToArmy(int totalWarriors, int slaveWarriors, int nonSlaveWarriors)
        {
            if (totalWarriors < 0 || slaveWarriors < 0 || nonSlaveWarriors < 0) return false;
            return totalWarriors < MaximumRoster;
        }

        public static bool CanAddNonSlaveCadre(int nonSlaveWarriors, bool hasNonSlaveCaptain)
        {
            return !hasNonSlaveCaptain && nonSlaveWarriors <= 0;
        }

        public static bool CanAddNonSlaveCadre(int totalWarriors, int slaveWarriors,
            int nonSlaveWarriors, bool hasNonSlaveCaptain)
        {
            if (hasNonSlaveCaptain || totalWarriors >= MaximumRoster) return false;
            return IsSlaveArmyComposition(totalWarriors + 1, slaveWarriors,
                nonSlaveWarriors + 1, captainNonSlave: true);
        }
    }
}
