namespace AncientWarfare3.core.grandstrategy
{
    public static class GrandStrategyArmyRules
    {
        public static bool CanMerge(GrandStrategyArmy first,
            GrandStrategyArmy second)
        {
            return first != null && second != null && first != second &&
                !first.Disbanded && !second.Disbanded &&
                first.KingdomId == second.KingdomId &&
                first.WarId == second.WarId &&
                first.PositionTileId >= 0 &&
                first.PositionTileId == second.PositionTileId;
        }

        public static bool CanCommand(GrandStrategyArmy army,
            long selectedKingdomId)
        {
            return army != null && !army.Disbanded &&
                army.KingdomId == selectedKingdomId;
        }
    }
}
