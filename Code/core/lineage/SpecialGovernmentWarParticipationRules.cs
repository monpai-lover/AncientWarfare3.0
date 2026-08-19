namespace AncientWarfare3.core.lineage
{
    public enum SpecialWarGovernmentKind
    {
        Ordinary = 0,
        Bandit = 1,
        PeasantRebel = 2,
        MilitaryGovernorate = 3
    }

    public static class SpecialGovernmentWarParticipationRules
    {
        public static bool CanCommand(SpecialWarGovernmentKind pKind,
            bool pAlive, bool pHasActiveWar, bool pKing, bool pHeir,
            bool pCityLeader, bool pBoat)
        {
            return pKind != SpecialWarGovernmentKind.Ordinary &&
                   pAlive && pHasActiveWar && !pBoat &&
                   (pKing || pHeir || pCityLeader);
        }

        public static bool CanFightAsCivilian(
            SpecialWarGovernmentKind pKind, bool pAlive,
            bool pHasActiveWar, bool pBoat)
        {
            return (pKind == SpecialWarGovernmentKind.Bandit ||
                    pKind == SpecialWarGovernmentKind.PeasantRebel) &&
                   pAlive && pHasActiveWar && !pBoat;
        }

        public static bool CountsAsAdditionalCombatant(
            SpecialWarGovernmentKind pKind, bool pAlive,
            bool pHasActiveWar, bool pKing, bool pHeir,
            bool pCityLeader, bool pBoat, bool pCurrentWarrior)
        {
            if (pCurrentWarrior) return false;
            return CanCommand(pKind, pAlive, pHasActiveWar, pKing, pHeir,
                       pCityLeader, pBoat) ||
                   CanFightAsCivilian(pKind, pAlive, pHasActiveWar, pBoat);
        }
    }
}
