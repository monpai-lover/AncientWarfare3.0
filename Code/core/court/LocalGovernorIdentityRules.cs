namespace AncientWarfare3.core.court
{
    public static class LocalGovernorIdentityRules
    {
        public static bool RootOfficeMatchesLeader(long officeActorId, long leaderActorId, bool leaderLive)
        {
            return leaderLive && officeActorId > 0 && officeActorId == leaderActorId;
        }

        public static long ResolveRegionalGovernorActorId(bool seatControlled, long seatLeaderId, bool seatLeaderLive)
        {
            return seatControlled && seatLeaderLive && seatLeaderId > 0 ? seatLeaderId : -1L;
        }

        public static bool IsCurrentSeatLeader(bool seatControlled,
            bool actorIsLeader, bool actorLive)
        {
            return seatControlled && actorIsLeader && actorLive;
        }

        public static bool IsCurrentCityLeader(bool cityControlled,
            bool actorLive)
        {
            return cityControlled && actorLive;
        }
    }
}
