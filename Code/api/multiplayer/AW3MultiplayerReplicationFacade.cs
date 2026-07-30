using System;
using AncientWarfare3.core.multiplayer;

namespace AncientWarfare3.api.multiplayer
{
    public static class AW3MultiplayerReplicationFacade
    {
        private static readonly IAW3MultiplayerReplicationStore Store =
            new AW3MultiplayerReplicationWorldStore();

        public static AW3MultiplayerArchiveCaptureResult
            CaptureArchiveKeyframe(string emptyDestinationDirectory,
                Guid epoch, long revision)
        {
            return AW3MultiplayerReplicationCoordinator
                .CaptureArchiveKeyframe(emptyDestinationDirectory, epoch,
                    revision, Store);
        }

        public static AW3MultiplayerArchiveInstallResult
            InstallArchiveKeyframe(string verifiedArchivePath, Guid epoch,
                long revision, byte[] expectedSha256)
        {
            return AW3MultiplayerReplicationCoordinator
                .InstallArchiveKeyframe(verifiedArchivePath, epoch, revision,
                    expectedSha256, Store);
        }
    }
}
