namespace AncientWarfare3.core.lineage
{
    public static class WarScoreTotalOccupationRules
    {
        public static bool TryResolveWinner(int pAttackerInitialCities,
            int pDefenderInitialCities, int pAttackerCurrentCities,
            int pDefenderCurrentCities, bool pAttackerControlsAllDefenderCities,
            bool pDefenderControlsAllAttackerCities, out WarScoreSide pWinner)
        {
            pWinner = WarScoreSide.None;
            bool validRoster = pAttackerInitialCities > 0 &&
                pDefenderInitialCities > 0;
            bool attackerComplete = validRoster &&
                pAttackerControlsAllDefenderCities;
            bool defenderComplete = validRoster &&
                pDefenderControlsAllAttackerCities;
            if (attackerComplete == defenderComplete) return false;
            pWinner = attackerComplete
                ? WarScoreSide.Attackers
                : WarScoreSide.Defenders;
            return true;
        }
    }
}
