namespace AncientWarfare3.core.lineage
{
    internal static class ActiveMilitaryLifecycleService
    {
        public static bool HasActiveMilitaryIdentity(Actor pActor)
        {
            if (!IsAlive(pActor)) return false;
            Army army = pActor.army;
            return SafeIsWarrior(pActor) || army?.data != null ||
                   IsCurrentCaptain(pActor, army) ||
                   GeneralService.IsGeneral(pActor) ||
                   RoyalGuardService.IsRoyalGuard(pActor) ||
                   ArmyRtsControllerService.HasValidMission(army);
        }

        public static bool HasWartimeMilitaryLock(Actor pActor)
        {
            if (!IsAlive(pActor)) return false;
            Army army = pActor.army;
            bool validMission = ArmyRtsControllerService.
                HasValidMission(army);
            return ActiveMilitaryLifecycleRules.HasWartimeMilitaryLock(
                actorAlive: true,
                kingdomAtWar: IsKingdomAtWar(pActor.kingdom),
                isWarrior: SafeIsWarrior(pActor),
                hasArmy: army?.data != null,
                isCurrentCaptain: IsCurrentCaptain(pActor, army),
                isGeneral: GeneralService.IsGeneral(pActor),
                isRoyalGuard: RoyalGuardService.IsRoyalGuard(pActor),
                hasValidRtsMission: validMission);
        }

        public static bool IsWartimeMilitaryActor(Actor pActor)
        {
            if (!IsAlive(pActor) || !IsKingdomAtWar(pActor?.kingdom))
                return false;
            return HasActiveMilitaryIdentity(pActor);
        }

        public static bool TryPrepareCivilAppointment(Actor pActor)
        {
            if (!IsAlive(pActor) || HasActiveMilitaryIdentity(pActor))
                return false;
            return ActiveMilitaryLifecycleRules.CanBecomeCivilGovernor(
                actorAlive: true,
                hasWartimeMilitaryLock: false,
                isKing: SafeIsKing(pActor),
                isCityLeader: SafeIsCityLeader(pActor));
        }

        public static bool CanCommitRetirement(Actor pActor)
        {
            if (pActor?.data == null) return false;
            return ActiveMilitaryLifecycleRules.CanCommitRetirement(
                retirementRequested: true,
                hasWartimeMilitaryLock: HasWartimeMilitaryLock(pActor),
                remainsWarrior: SafeIsWarrior(pActor),
                remainsInArmy: pActor.army?.data != null);
        }

        private static bool IsKingdomAtWar(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            try
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (war?.data != null && !war.hasEnded() &&
                        war.hasKingdom(pKingdom)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsCurrentCaptain(Actor pActor, Army pArmy)
        {
            try
            {
                return pActor?.data != null && pArmy?.data != null &&
                       ReferenceEquals(pArmy.getCaptain(), pActor);
            }
            catch { return false; }
        }

        private static bool IsAlive(Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt();
            }
            catch { return false; }
        }

        private static bool SafeIsWarrior(Actor pActor)
        {
            try { return pActor?.isWarrior() == true; }
            catch { return false; }
        }

        private static bool SafeIsKing(Actor pActor)
        {
            try { return pActor?.isKing() == true; }
            catch { return false; }
        }

        private static bool SafeIsCityLeader(Actor pActor)
        {
            try { return pActor?.isCityLeader() == true; }
            catch { return false; }
        }
    }
}
