namespace AncientWarfare3.api.history
{
    public sealed class AW3HistorySubscription
    {
        private AW3HistorySubscription(string domain, string eventType,
            long actorId, long kingdomId)
        {
            Domain = domain ?? "";
            EventType = eventType ?? "";
            ActorId = actorId;
            KingdomId = kingdomId;
        }

        public static AW3HistorySubscription All { get; } =
            new AW3HistorySubscription("", "", -1L, -1L);

        public string Domain { get; }
        public string EventType { get; }
        public long ActorId { get; }
        public long KingdomId { get; }

        public static AW3HistorySubscription ForKingdom(long kingdomId)
        {
            return new AW3HistorySubscription("", "", -1L, kingdomId);
        }

        public static AW3HistorySubscription Create(string domain = "",
            string eventType = "", long actorId = -1L,
            long kingdomId = -1L)
        {
            return new AW3HistorySubscription(domain, eventType, actorId,
                kingdomId);
        }

        internal bool Matches(AW3HistoryEvent item)
        {
            if (item == null) return false;
            return (Domain == "" || Domain == item.Domain) &&
                   (EventType == "" || EventType == item.EventType) &&
                   (ActorId < 0L || ActorId == item.SubjectId) &&
                   (KingdomId < 0L || KingdomId == item.KingdomId ||
                    KingdomId == item.ContextKingdomId);
        }
    }
}
