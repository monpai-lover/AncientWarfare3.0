using AncientWarfare3.core.multiplayer;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerStrategicStateFacade
    {
        private static readonly IAW3MultiplayerStrategicStateStore Store =
            new AW3MultiplayerStrategicWorldStore();

        public static AW3MultiplayerStrategicSnapshot Capture(long tick)
        {
            if (!ThreadHelper.isMainThread())
                throw new AW3MultiplayerStrategicCaptureException(
                    AW3MultiplayerStrategicError.WrongThread,
                    "Strategic capture requires the WorldBox main thread.");
            return AW3MultiplayerStrategicStateCoordinator.Capture(tick,
                Store);
        }

        public static AW3MultiplayerStrategicApplyResult Apply(
            AW3MultiplayerStrategicSnapshot snapshot)
        {
            if (!ThreadHelper.isMainThread())
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.WrongThread,
                    "Strategic apply requires the WorldBox main thread.");
            return AW3MultiplayerStrategicStateCoordinator.Apply(snapshot,
                Store);
        }
    }
}
