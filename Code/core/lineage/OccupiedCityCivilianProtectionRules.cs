namespace AncientWarfare3.core.lineage
{
    public static class OccupiedCityCivilianProtectionRules
    {
        public static bool IsMilitaryActor(bool isActor,
            bool currentProfessionIsWarrior, bool hasArmyIndex)
        {
            return IsMilitaryActor(isActor, currentProfessionIsWarrior,
                hasArmyIndex, isCombatBoat: false);
        }

        public static bool IsMilitaryActor(bool isActor,
            bool currentProfessionIsWarrior, bool hasArmyIndex,
            bool isCombatBoat)
        {
            return IsMilitaryActor(isActor, currentProfessionIsWarrior,
                hasArmyIndex, isCombatBoat, isArmyCaptain: false);
        }

        public static bool IsMilitaryActor(bool isActor,
            bool currentProfessionIsWarrior, bool hasArmyIndex,
            bool isCombatBoat, bool isArmyCaptain)
        {
            return IsMilitaryActor(isActor, currentProfessionIsWarrior,
                hasArmyIndex, isCombatBoat, isArmyCaptain, isKing: false,
                isCityLeader: false);
        }

        public static bool IsMilitaryActor(bool isActor,
            bool currentProfessionIsWarrior, bool hasArmyIndex,
            bool isCombatBoat, bool isArmyCaptain, bool isKing,
            bool isCityLeader)
        {
            return isActor && !isKing && !isCityLeader &&
                   (currentProfessionIsWarrior || isCombatBoat);
        }

        public static bool IsCombatBoat(bool isBoat, bool skipFightLogic)
        {
            return isBoat && !skipFightLogic;
        }

        public static bool ShouldSuppressActorCombat(bool activeWar,
            bool attackerIsActor, bool targetIsActor,
            bool attackerIsWarrior, bool targetIsWarrior,
            bool attackerIsCivilAuthority = false,
            bool targetIsCivilAuthority = false)
        {
            if (!attackerIsActor || !targetIsActor)
                return false;
            if (attackerIsCivilAuthority || targetIsCivilAuthority)
                return true;
            if (!activeWar) return false;
            if (!attackerIsWarrior && !targetIsWarrior)
                return false;
            return !attackerIsWarrior || !targetIsWarrior;
        }

        public static bool CanActorContributeCapturePoints(bool actorValid,
            bool currentProfessionIsWarrior, bool hasValidKingdom,
            bool isArmyCaptain = false)
        {
            return CanActorContributeCapturePoints(actorValid,
                currentProfessionIsWarrior, hasValidKingdom, isArmyCaptain,
                isKing: false, isCityLeader: false);
        }

        public static bool CanActorContributeCapturePoints(bool actorValid,
            bool currentProfessionIsWarrior, bool hasValidKingdom,
            bool isArmyCaptain, bool isKing, bool isCityLeader)
        {
            return actorValid && currentProfessionIsWarrior &&
                   hasValidKingdom && !isKing && !isCityLeader;
        }

        public static bool ShouldDetachArmyForAuthorityRole(
            bool hasArmyIndex, bool becomingKing, bool becomingLeader,
            bool isArmyCaptain = false)
        {
            return hasArmyIndex && (becomingKing || becomingLeader);
        }

        public static bool ShouldApplyEmergencySanctuary(
            bool frozenControlled, bool residentInsideCity,
            int cityPopulation)
        {
            return frozenControlled && residentInsideCity &&
                   cityPopulation > 0 && cityPopulation <= 10;
        }

        public static bool ShouldRestoreOccupiedResident(
            bool frozenControlled, bool residentAlive,
            bool residentInsideCity, int cityPopulation)
        {
            return residentAlive && ShouldApplyEmergencySanctuary(
                frozenControlled, residentInsideCity, cityPopulation);
        }

        public static bool ShouldSuppressWartimeHostility(
            bool activeWar,
            bool attackerIsMilitary,
            bool attackerBelongsToCityOwner,
            bool targetBelongsToCityOwner,
            bool targetInsideHomeCity,
            bool targetIsCivilian,
            bool targetIsCivilianBuilding)
        {
            if (!activeWar || !attackerIsMilitary ||
                attackerBelongsToCityOwner || !targetBelongsToCityOwner ||
                !targetInsideHomeCity)
                return false;

            return targetIsCivilian || targetIsCivilianBuilding;
        }

        public static bool ShouldSuppressHostility(
            bool frozenControlled,
            bool actorBelongsToHome,
            bool actorBelongsToController,
            bool targetBelongsToHome,
            bool targetBelongsToController,
            bool actorIsCivilian,
            bool targetIsCivilian,
            bool targetIsCivilianBuilding)
        {
            if (!frozenControlled) return false;

            if (actorBelongsToController && targetBelongsToHome)
                return targetIsCivilian || targetIsCivilianBuilding;

            return actorBelongsToHome && targetBelongsToController &&
                   actorIsCivilian;
        }
    }
}
