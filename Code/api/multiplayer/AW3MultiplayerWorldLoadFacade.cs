using System;
using AncientWarfare3.core.multiplayer;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerWorldLoadFacade
    {
        public static AW3MultiplayerWorldLoadStartResult TryBeginGenerationLoad(
            string generationDirectory)
        {
            if (!ThreadHelper.isMainThread())
                return AW3MultiplayerWorldLoadStartResult.Failure(
                    AW3MultiplayerWorldLoadError.WrongThread,
                    "World loading must start on the WorldBox main thread.");

            AW3MultiplayerSnapshotResult validation =
                AW3MultiplayerSnapshotFacade.ValidateGeneration(
                    generationDirectory);
            if (!validation.IsSuccess)
                return AW3MultiplayerWorldLoadStartResult.Failure(
                    AW3MultiplayerWorldLoadError.InvalidGeneration,
                    validation.Error + ": " + validation.Detail);

            return AW3WorldLoadCoordinator.TryBeginGenerationLoad(
                validation.PendingDirectory);
        }

        public static AW3MultiplayerWorldLoadSnapshot GetStatus(
            Guid operationId)
        {
            return AW3WorldLoadCoordinator.GetStatus(operationId);
        }

        public static bool Cancel(Guid operationId)
        {
            return AW3WorldLoadCoordinator.Cancel(operationId);
        }

        public static void Tick()
        {
            if (!ThreadHelper.isMainThread()) return;
            AW3WorldLoadCoordinator.Tick();
        }
    }
}
