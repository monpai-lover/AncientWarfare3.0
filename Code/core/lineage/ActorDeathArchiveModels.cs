namespace AncientWarfare3.core.lineage
{
    public readonly struct ActorDeathArchiveStamp
    {
        public ActorDeathArchiveStamp(long pWorldGeneration, long pActorId,
            long pDeathRevision, string pStage)
        {
            WorldGeneration = pWorldGeneration;
            ActorId = pActorId;
            DeathRevision = pDeathRevision;
            Stage = pStage ?? "unknown";
        }

        public long WorldGeneration { get; }
        public long ActorId { get; }
        public long DeathRevision { get; }
        public string Stage { get; }
    }

    public readonly struct ActorDeathArchiveResult
    {
        public ActorDeathArchiveResult(ActorDeathArchiveStamp pStamp,
            bool pSucceeded, bool pRetry, long pCommittedRevision)
        {
            Stamp = pStamp;
            Succeeded = pSucceeded;
            Retry = pRetry;
            CommittedRevision = pCommittedRevision;
        }

        public ActorDeathArchiveStamp Stamp { get; }
        public bool Succeeded { get; }
        public bool Retry { get; }
        public long CommittedRevision { get; }
    }
}
