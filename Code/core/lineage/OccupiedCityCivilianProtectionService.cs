namespace AncientWarfare3.core.lineage
{
    internal static class OccupiedCityCivilianProtectionService
    {
        public static bool ShouldSuppressHostility(BaseSimObject pAttacker,
            BaseSimObject pTarget)
        {
            if (pAttacker == null || pTarget == null ||
                pAttacker == pTarget)
                return false;
            if (ShouldSuppressCivilAuthorityCombat(pAttacker, pTarget))
                return true;

            Kingdom attackerKingdom = SafeKingdom(pAttacker);
            Kingdom targetKingdom = SafeKingdom(pTarget);
            if (attackerKingdom?.data == null ||
                targetKingdom?.data == null ||
                attackerKingdom == targetKingdom)
                return false;

            bool activeWar;
            try
            {
                activeWar = attackerKingdom.isInWarWith(targetKingdom);
            }
            catch { activeWar = false; }

            bool attackerIsWarrior = IsMilitaryActor(pAttacker);
            bool targetIsWarrior = IsMilitaryActor(pTarget);
            bool attackerIsCivilAuthority = IsCivilAuthorityActor(pAttacker);
            bool targetIsCivilAuthority = IsCivilAuthorityActor(pTarget);

            if (IsEmergencyResident(pTarget, attackerKingdom) ||
                IsEmergencyResident(pAttacker, targetKingdom))
                return true;

            if (OccupiedCityCivilianProtectionRules.
                    ShouldSuppressActorCombat(
                        activeWar,
                        pAttacker.isActor(),
                        pTarget.isActor(),
                        attackerIsWarrior,
                        targetIsWarrior,
                        attackerIsCivilAuthority,
                        targetIsCivilAuthority))
                return true;

            if (!attackerIsWarrior) return false;

            bool targetCivilian = IsCivilianActor(pTarget);
            bool targetCivilianBuilding = IsCivilianBuilding(pTarget);
            if (!targetCivilian && !targetCivilianBuilding)
                return false;

            City targetCity = SafeTileCity(pTarget);
            Kingdom cityOwner = targetCity?.kingdom;
            if (targetCity?.data == null || cityOwner?.data == null)
                return false;

            try { activeWar = attackerKingdom.isInWarWith(cityOwner); }
            catch { activeWar = false; }

            return OccupiedCityCivilianProtectionRules.
                ShouldSuppressWartimeHostility(
                    activeWar,
                    attackerIsMilitary: true,
                    attackerBelongsToCityOwner:
                        attackerKingdom == cityOwner,
                    targetBelongsToCityOwner:
                        targetKingdom == cityOwner,
                    targetInsideHomeCity:
                        targetKingdom == cityOwner,
                    targetIsCivilian: targetCivilian,
                    targetIsCivilianBuilding: targetCivilianBuilding);
        }

        private static bool ShouldSuppressCivilAuthorityCombat(
            BaseSimObject pAttacker, BaseSimObject pTarget)
        {
            bool attackerIsActor;
            bool targetIsActor;
            try
            {
                attackerIsActor = pAttacker?.isActor() == true;
                targetIsActor = pTarget?.isActor() == true;
            }
            catch { return false; }
            if (!attackerIsActor || !targetIsActor) return false;
            return OccupiedCityCivilianProtectionRules.
                ShouldSuppressActorCombat(
                    activeWar: false,
                    attackerIsActor: true,
                    targetIsActor: true,
                    attackerIsWarrior: false,
                    targetIsWarrior: false,
                    attackerIsCivilAuthority:
                        IsCivilAuthorityActor(pAttacker),
                    targetIsCivilAuthority:
                        IsCivilAuthorityActor(pTarget));
        }

        public static bool ShouldSuppressDamage(BaseSimObject pVictim,
            BaseSimObject pAttacker)
        {
            return pVictim != null && pAttacker != null &&
                   ShouldSuppressHostility(pAttacker, pVictim);
        }

        public static bool CanActorContributeCapturePoints(
            BaseSimObject pObject)
        {
            Actor actor;
            try { actor = pObject?.isActor() == true ? pObject.a : null; }
            catch { actor = null; }
            if (actor?.data == null) return false;

            bool actorValid;
            bool currentProfessionIsWarrior;
            bool hasValidKingdom;
            bool isKing;
            bool isCityLeader;
            try
            {
                actorValid = actor.isAlive() && !actor.isRekt();
                currentProfessionIsWarrior = actor.is_profession_warrior;
                hasValidKingdom = actor.kingdom?.data != null;
                isKing = actor.isKing();
                isCityLeader = actor.isCityLeader();
            }
            catch { return false; }

            return OccupiedCityCivilianProtectionRules.
                CanActorContributeCapturePoints(actorValid,
                    currentProfessionIsWarrior, hasValidKingdom,
                    actor.hasArmy(), isKing, isCityLeader);
        }

        public static bool TryRestoreEmergencyResident(Actor pActor)
        {
            if (pActor?.data == null) return false;
            City city;
            bool alive;
            try
            {
                city = pActor.city;
                alive = pActor.isAlive() && !pActor.isRekt();
            }
            catch { return false; }
            if (city?.data == null) return false;

            int population;
            try { population = city.units.Count; }
            catch { return false; }
            if (population <= 0 || population > 10) return false;

            bool restore = OccupiedCityCivilianProtectionRules.
                ShouldRestoreOccupiedResident(
                    IsHostileOccupation(city), alive,
                    IsResidentInsideCity(pActor, city),
                    population);
            if (!restore) return false;
            try
            {
                pActor.setNutrition(pActor.getMaxNutrition());
                pActor.restoreHealthPercent(1f);
                return true;
            }
            catch { return false; }
        }

        private static bool IsEmergencyResident(BaseSimObject pResident,
            Kingdom pOtherKingdom)
        {
            if (pResident == null || pOtherKingdom?.data == null)
                return false;

            City city = SafeTileCity(pResident);
            if (city?.data == null) return false;

            int population;
            try { population = city.units.Count; }
            catch { return false; }
            if (population <= 0 || population > 10 ||
                !IsResidentInsideCity(pResident, city) ||
                !IsHostileOccupation(city))
                return false;

            try
            {
                Kingdom owner = city.kingdom;
                return owner?.data != null && owner != pOtherKingdom &&
                       owner.isInWarWith(pOtherKingdom) &&
                       OccupiedCityCivilianProtectionRules.
                           ShouldApplyEmergencySanctuary(
                               frozenControlled: true,
                               residentInsideCity: true,
                               cityPopulation: population);
            }
            catch { return false; }
        }

        private static City SafeTileCity(BaseSimObject pObject)
        {
            try { return pObject?.current_tile?.zone?.city; }
            catch { return null; }
        }

        private static Kingdom SafeKingdom(BaseSimObject pObject)
        {
            try { return pObject?.kingdom; }
            catch { return null; }
        }

        private static bool IsHostileOccupation(City pCity)
        {
            try
            {
                Kingdom owner = pCity?.kingdom;
                Kingdom occupier = pCity?.being_captured_by;
                return pCity?.data != null && owner?.data != null &&
                       occupier?.data != null && occupier != owner &&
                       owner.isInWarWith(occupier);
            }
            catch { return false; }
        }

        private static bool IsResidentInsideCity(BaseSimObject pObject,
            City pCity)
        {
            try
            {
                return pObject?.isActor() == true &&
                       pObject.a?.city == pCity &&
                       SafeTileCity(pObject) == pCity;
            }
            catch { return false; }
        }

        private static bool IsCivilianActor(BaseSimObject pObject)
        {
            try
            {
                return pObject?.isActor() == true &&
                       pObject.a?.profession_asset?.is_civilian == true;
            }
            catch { return false; }
        }

        private static bool IsCivilAuthorityActor(BaseSimObject pObject)
        {
            try
            {
                Actor actor = pObject?.isActor() == true ? pObject.a : null;
                return actor?.data != null &&
                       (actor.isKing() || actor.isCityLeader());
            }
            catch { return false; }
        }

        private static bool IsMilitaryActor(BaseSimObject pObject)
        {
            try
            {
                bool isActor = pObject?.isActor() == true;
                Actor actor = isActor ? pObject.a : null;
                bool isCombatBoat = actor?.asset != null &&
                                    OccupiedCityCivilianProtectionRules.
                                        IsCombatBoat(actor.asset.is_boat,
                                            actor.asset.skip_fight_logic);
                bool isKing = actor?.isKing() == true;
                bool isCityLeader = actor?.isCityLeader() == true;
                return OccupiedCityCivilianProtectionRules.IsMilitaryActor(
                    isActor,
                    actor?.is_profession_warrior == true,
                    actor?.hasArmy() == true,
                    isCombatBoat,
                    actor?.isArmyGroupLeader() == true,
                    isKing,
                    isCityLeader);
            }
            catch { return false; }
        }

        private static bool IsCivilianBuilding(BaseSimObject pObject)
        {
            try
            {
                if (pObject?.isBuilding() != true ||
                    pObject.b?.asset?.city_building != true)
                    return false;

                BuildingAsset asset = pObject.b.asset;
                return !asset.tower && asset.type != "type_barracks";
            }
            catch { return false; }
        }
    }
}
