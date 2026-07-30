namespace AncientWarfare3.core.lineage
{
    public static class RulerHouseholdNavigationRules
    {
        public static bool CanOpen(bool rowPresent, bool markedAlive,
            bool actorResolved, bool actorAlive, bool actorRekt)
        {
            return rowPresent && markedAlive && actorResolved &&
                   actorAlive && !actorRekt;
        }
    }
}
