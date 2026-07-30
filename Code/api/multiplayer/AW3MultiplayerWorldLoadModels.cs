using System;

namespace AncientWarfare3.api.multiplayer
{
    public enum AW3MultiplayerWorldLoadState : byte
    {
        AwaitingWorldData = 0,
        LoadingWorld = 1,
        RestoringAw3 = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    public enum AW3MultiplayerWorldLoadError : byte
    {
        None = 0,
        WrongThread = 1,
        InvalidGeneration = 2,
        Busy = 3,
        WorldLoadFallback = 4,
        WorldLoadTimeout = 5,
        Aw3RestoreFailed = 6,
        Cancelled = 7,
        UnknownOperation = 8
    }

    public sealed class AW3MultiplayerWorldLoadStartResult
    {
        internal AW3MultiplayerWorldLoadStartResult(bool pAccepted,
            Guid pOperationId, AW3MultiplayerWorldLoadError pError,
            string pDetail)
        {
            Accepted = pAccepted;
            OperationId = pOperationId;
            Error = pError;
            Detail = pDetail ?? string.Empty;
        }

        public bool Accepted { get; }
        public Guid OperationId { get; }
        public AW3MultiplayerWorldLoadError Error { get; }
        public string Detail { get; }

        internal static AW3MultiplayerWorldLoadStartResult Success(
            Guid pOperationId)
        {
            return new AW3MultiplayerWorldLoadStartResult(true, pOperationId,
                AW3MultiplayerWorldLoadError.None, string.Empty);
        }

        internal static AW3MultiplayerWorldLoadStartResult Failure(
            AW3MultiplayerWorldLoadError pError, string pDetail)
        {
            return new AW3MultiplayerWorldLoadStartResult(false, Guid.Empty,
                pError, pDetail);
        }
    }

    public sealed class AW3MultiplayerWorldLoadSnapshot
    {
        internal AW3MultiplayerWorldLoadSnapshot(Guid pOperationId,
            AW3MultiplayerWorldLoadState pState,
            AW3MultiplayerWorldLoadError pError, string pDetail,
            string pGenerationDirectory, long pRevision)
        {
            OperationId = pOperationId;
            State = pState;
            Error = pError;
            Detail = pDetail ?? string.Empty;
            GenerationDirectory = pGenerationDirectory ?? string.Empty;
            Revision = pRevision;
        }

        public Guid OperationId { get; }
        public AW3MultiplayerWorldLoadState State { get; }
        public AW3MultiplayerWorldLoadError Error { get; }
        public string Detail { get; }
        public string GenerationDirectory { get; }
        public long Revision { get; }

        internal static AW3MultiplayerWorldLoadSnapshot Unknown(
            Guid pOperationId)
        {
            return new AW3MultiplayerWorldLoadSnapshot(pOperationId,
                AW3MultiplayerWorldLoadState.Failed,
                AW3MultiplayerWorldLoadError.UnknownOperation,
                "World-load operation is not known.", string.Empty, 0L);
        }
    }
}
