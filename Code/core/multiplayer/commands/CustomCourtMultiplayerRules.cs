namespace AncientWarfare3.core.multiplayer.commands
{
    public static class CustomCourtMultiplayerRules
    {
        public static bool CanApply(bool isHost, bool templateHashMatches,
            bool instanceRevisionMatches)
        {
            return isHost && templateHashMatches && instanceRevisionMatches;
        }
    }
}
