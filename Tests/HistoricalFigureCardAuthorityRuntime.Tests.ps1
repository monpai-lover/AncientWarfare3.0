$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$service = [IO.File]::ReadAllText((Join-Path $repo 'Code/core/lineage/HistoricalFigureCardDeploymentService.cs'))
$requestStart = $service.IndexOf('    public sealed class HistoricalFigureCardDeploymentRequest')
$serviceStart = $service.IndexOf('    public static class HistoricalFigureCardDeploymentService')
$entryStart = $service.IndexOf('        public static HistoricalFigureCardDeploymentResult TryDeploy(')
$continuation = $service.IndexOf('            Actor actor = null;', $entryStart)
if ($requestStart -lt 0 -or $serviceStart -le $requestStart -or
    $entryStart -lt 0 -or $continuation -le $entryStart) {
    throw 'Cannot locate the production deployment entry boundary.'
}
# Compile the actual request/result and untouched entry code, including TryBegin.
# Replace only the later Unity/world transaction with an observed continuation;
# this test does not claim to simulate a full deployment or rendering.
$models = $service.Substring($requestStart, $serviceStart - $requestStart)
$entry = $service.Substring($entryStart, $continuation - $entryStart)
$entrySource = @"
using AncientWarfare3.api.multiplayer;
namespace AncientWarfare3.core.lineage {
$models
    public static class HistoricalFigureCardDeploymentService {
$entry
            BoundaryProbe.Continuations++;
            return HistoricalFigureCardDeploymentResult.Success(null, null, null);
        }
    }
}
"@
$harness = @'
using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
public sealed class Data { public long id; }
public sealed class Actor { public Data data; }
public sealed class Kingdom { public Data data; public string name; }
public sealed class City { public Data data; public WorldTile getTile() => null; }
public sealed class WorldTile { }
namespace AncientWarfare3.content.figures {
    public enum HistoricalFigureCardRole { Monarch, Minister }
    public static class HistoricalFigureCardCatalog {
        public static bool HasGeographicPrefix(string name) => false;
    }
}
namespace AncientWarfare3.core.lineage {
    public static class HistoricalFigureCardRoleRules {
        public static bool IsKingdomFoundingRole(
            AncientWarfare3.content.figures.HistoricalFigureCardRole role) =>
            role == AncientWarfare3.content.figures.HistoricalFigureCardRole.Monarch;
    }
    public static class BoundaryProbe { public static int Continuations; }
}
public static class HistoricalFigureCardAuthorityCheck {
    public static void Run() {
        object replica = new object();
        const string deploymentId = "authority-boundary";
        var request = new HistoricalFigureCardDeploymentRequest(
            "han_gaozu", "owned-draw", deploymentId, (WorldTile)null);
        try {
            Check(AW3MultiplayerReplicaScope.Activate(replica), "activate replica");
            var result = HistoricalFigureCardDeploymentService.TryDeploy(request);
            Check(!result.Succeeded && result.Error == "replica_session",
                "replica must be rejected at the service entry");
            Check(BoundaryProbe.Continuations == 0,
                "replica must not reach world, inventory or archive work");
            Check(!HistoricalFigureCardDeploymentRules.IsActive(deploymentId),
                "replica rejection must not reserve the deployment id");
            using (AW3MultiplayerReplicaScope.EnterApply()) {
                result = HistoricalFigureCardDeploymentService.TryDeploy(request);
                Check(!result.Succeeded && result.Error == "replica_session",
                    "replica apply scope does not authorize local card spawning");
            }
            Check(BoundaryProbe.Continuations == 0 &&
                !HistoricalFigureCardDeploymentRules.IsActive(deploymentId),
                "replica apply rejection has no transaction side effects");
            Check(AW3MultiplayerReplicaScope.Deactivate(replica), "deactivate replica");
            result = HistoricalFigureCardDeploymentService.TryDeploy(request);
            Check(result.Succeeded && BoundaryProbe.Continuations == 1 &&
                HistoricalFigureCardDeploymentRules.IsActive(deploymentId),
                "host or standalone can enter the existing deployment transaction");
            result = HistoricalFigureCardDeploymentService.TryDeploy(request);
            Check(!result.Succeeded && result.Error == "deployment_already_active" &&
                BoundaryProbe.Continuations == 1,
                "existing duplicate-deployment protection is preserved");
            result = HistoricalFigureCardDeploymentService.TryDeploy(null);
            Check(!result.Succeeded && result.Error == "request_missing",
                "missing request still fails without entering the transaction");
        } finally {
            AW3MultiplayerReplicaScope.Deactivate(replica);
            HistoricalFigureCardDeploymentRules.End(deploymentId);
        }
    }
    private static void Check(bool value, string message) {
        if (!value) throw new InvalidOperationException(message);
    }
}
'@
$temp = Join-Path ([IO.Path]::GetTempPath()) ('aw3-card-authority-' + [guid]::NewGuid().ToString('N'))
try {
    $null = [IO.Directory]::CreateDirectory($temp)
    $entryPath = Join-Path $temp 'Entry.cs'
    $harnessPath = Join-Path $temp 'Harness.cs'
    [IO.File]::WriteAllText($entryPath, $entrySource)
    [IO.File]::WriteAllText($harnessPath, $harness)
    Add-Type -Path @($entryPath, $harnessPath,
        (Join-Path $repo 'Code/api/multiplayer/AW3MultiplayerReplicaScope.cs'),
        (Join-Path $repo 'Code/core/lineage/HistoricalFigureCardDeploymentRules.cs'))
    [HistoricalFigureCardAuthorityCheck]::Run()
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
Write-Output 'Historical card authority boundary runtime tests passed.'
