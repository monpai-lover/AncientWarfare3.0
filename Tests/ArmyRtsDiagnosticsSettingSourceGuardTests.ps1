$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not [IO.File]::Exists($path)) {
        $failures.Add("missing source file $relativePath")
        return ''
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

function Require-Text([string]$source, [string]$needle,
    [string]$message) {
    if (-not $source.Contains($needle)) { $failures.Add($message) }
}

function Method-Body([string]$source, [string]$startToken,
    [string]$endToken, [string]$name) {
    $start = $source.IndexOf($startToken, [StringComparison]::Ordinal)
    $end = if ($start -lt 0) { -1 } else {
        $source.IndexOf($endToken, $start + $startToken.Length,
            [StringComparison]::Ordinal)
    }
    if ($start -lt 0 -or $end -le $start) {
        $failures.Add("unable to inspect $name")
        return ''
    }
    return $source.Substring($start, $end - $start)
}

function Require-DiagnosticGate([string]$source, [string]$startToken,
    [string]$endToken, [string]$name) {
    $body = Method-Body $source $startToken $endToken $name
    if ([string]::IsNullOrEmpty($body)) { return }
    Require-Text $body `
        'if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;' `
        "$name is not controlled by the immediate RTS diagnostics setting"
}

$settings = Read-Source `
    'Code/core/performance/AWPerformanceSettings.cs'
$configText = Read-Source 'default_config.json'
$localeText = Read-Source 'Locales/aw3_performance.csv'
$controller = Read-Source `
    'Code/core/lineage/ArmyRtsControllerService.cs'
$transport = Read-Source `
    'Code/core/lineage/ArmyRtsTransportService.cs'
$transportProduction = Read-Source `
    'Code/core/lineage/ArmyRtsTransportProductionService.cs'
$watchdog = Read-Source `
    'Code/core/lineage/ArmyStallWatchdogService.cs'
$armySafety = Read-Source 'Code/patch/AW_ArmySafetyPatch.cs'
$rtsContent = Read-Source 'Code/content/ArmyRtsContent.cs'
$schedulerPatch = Read-Source 'Code/patch/AW_ArmyRtsSchedulerPatch.cs'
$visualization = Read-Source `
    'Code/core/presentation/ArmyRtsVisualizationService.cs'
$snapshotService = Read-Source `
    'Code/core/presentation/ArmyRtsPlanSnapshotService.cs'
$modClass = Read-Source 'Code/ModClass.cs'

Require-Text $settings `
    'public static bool ArmyRtsDiagnosticsEnabled { get; private set; }' `
    'the shared public RTS diagnostics property is missing'
Require-Text $settings `
    'public static void SwitchArmyRtsDiagnostics(bool pValue)' `
    'the immediate RTS diagnostics callback is missing'
Require-Text $settings `
    'ArmyRtsDiagnosticsEnabled = pValue;' `
    'the RTS diagnostics callback does not update the live property'
$diagnosticsSwitch = Method-Body $settings `
    'public static void SwitchArmyRtsDiagnostics(bool pValue)' `
    'public static void SetTargetRenderFps(float pValue)' `
    'RTS diagnostics switch callback'
Require-Text $diagnosticsSwitch `
    'if (!pValue) ArmyRtsDiagnosticsDisabled?.Invoke();' `
    'turning diagnostics off does not immediately discard pending GIF sequences'
Require-Text $snapshotService `
    'AWPerformanceSettings.ArmyRtsDiagnosticsDisabled +=' `
    'the RTS plan service does not subscribe to immediate diagnostics disable'

$disableDiagnostics = Method-Body $snapshotService `
    'internal static void DisableDiagnostics()' `
    'private static string NormalizeDirectory(' `
    'RTS diagnostics disable cleanup'
Require-Text $disableDiagnostics 'Pending.Clear();' `
    'diagnostics disable does not clear pending requests immediately'
Require-Text $disableDiagnostics 'Revisions.Clear();' `
    'diagnostics disable does not clear pending revisions immediately'
Require-Text $disableDiagnostics '_writer?.DiscardPending();' `
    'diagnostics disable does not clear retained GIF sequences immediately'

$captureArmy = Method-Body $snapshotService `
    'private static ArmyRtsPlanArmy CaptureArmy(' `
    'private static bool IsFriendlyRecovery(' `
    'RTS plan army capture'
Require-Text $captureArmy `
    'mission.ProposalKind, mission.Role, mission.Posture' `
    'RTS GIF capture drops proposal kind, role, or posture'

$applicationQuit = Method-Body $modClass `
    'private void OnApplicationQuit()' `
    'private void OnDestroy()' `
    'normal application quit lifecycle'
Require-Text $applicationQuit `
    'ShutdownRuntime(pPublishArmyRtsPlans: true);' `
    'normal application quit does not publish retained RTS GIF plans'
$destroy = Method-Body $modClass `
    'private void OnDestroy()' `
    'private void ShutdownRuntime(' `
    'component destroy lifecycle'
Require-Text $destroy `
    'ShutdownRuntime(pPublishArmyRtsPlans: false);' `
    'component destroy can still publish retained RTS GIF plans'
$shutdownRuntime = Method-Body $modClass `
    'private void ShutdownRuntime(bool pPublishArmyRtsPlans)' `
    'public void Reload()' `
    'runtime shutdown publishing split'
Require-Text $shutdownRuntime `
    'if (pPublishArmyRtsPlans) ArmyRtsPlanSnapshotService.Shutdown();' `
    'normal shutdown no longer reaches the RTS GIF publisher'
Require-Text $shutdownRuntime `
    'else ArmyRtsPlanSnapshotService.DiscardAndShutdown();' `
    'hot destroy does not discard pending RTS GIF plans before cleanup'

try {
    $config = $configText | ConvertFrom-Json
    $entry = @($config.AWPerformanceSettings) |
        Where-Object { $_.Id -eq 'AW3_ENABLE_ARMY_RTS_DIAGNOSTICS' }
    if ($null -eq $entry) {
        $failures.Add('the persisted RTS diagnostics SWITCH is missing')
    }
    else {
        if ($entry.Type -ne 'SWITCH') {
            $failures.Add('the RTS diagnostics setting must use the native SWITCH type')
        }
        if ($entry.BoolVal -ne $false) {
            $failures.Add('RTS diagnostics must default off for performance')
        }
        if ($entry.Callback -ne
            'AWPerformanceSettings:SwitchArmyRtsDiagnostics') {
            $failures.Add('the persisted RTS diagnostics callback is incorrect')
        }
    }
}
catch {
    $failures.Add('default_config.json is not valid JSON: ' + $_.Exception.Message)
}

try {
    $locale = @($localeText | ConvertFrom-Csv)
    $label = $locale |
        Where-Object { $_.key -eq 'AW3_ENABLE_ARMY_RTS_DIAGNOSTICS' }
    $description = $locale |
        Where-Object {
            $_.key -eq 'AW3_ENABLE_ARMY_RTS_DIAGNOSTICS Description'
        }
    $simplifiedLabel = 'RTS ' + [char]0x8BCA + [char]0x65AD +
        [char]0x8F93 + [char]0x51FA
    $simplifiedLog = [string]([char]0x8BCA) + [char]0x65AD +
        [char]0x65E5 + [char]0x5FD7
    $simplifiedTactical = [string]([char]0x6218) + [char]0x672F
    $traditionalLog = [string]([char]0x8A3A) + [char]0x65B7 +
        [char]0x65E5 + [char]0x8A8C
    $traditionalTactical = [string]([char]0x6230) + [char]0x8853
    if ($null -eq $label) {
        $failures.Add('the RTS diagnostics label localization is missing')
    }
    else {
        if ($label.cz -ne $simplifiedLabel) {
            $failures.Add('the Simplified Chinese RTS diagnostics label is incorrect')
        }
        if ([string]::IsNullOrWhiteSpace($label.en)) {
            $failures.Add('the English RTS diagnostics label is missing')
        }
        if ([string]::IsNullOrWhiteSpace($label.ch)) {
            $failures.Add('the Traditional Chinese RTS diagnostics label is missing')
        }
    }
    if ($null -eq $description) {
        $failures.Add('the RTS diagnostics description localization is missing')
    }
    else {
        if ($description.cz -notmatch
            "RTS.*$simplifiedLog.*$simplifiedTactical GIF") {
            $failures.Add('the Simplified Chinese description must cover RTS diagnostic logs and tactical GIF output')
        }
        if ($description.en -notmatch
            '(?i)RTS diagnostic logs.*tactical GIF output') {
            $failures.Add('the English description must cover RTS diagnostic logs and tactical GIF output')
        }
        if ($description.ch -notmatch
            "RTS.*$traditionalLog.*$traditionalTactical GIF") {
            $failures.Add('the Traditional Chinese description must cover RTS diagnostic logs and tactical GIF output')
        }
    }
}
catch {
    $failures.Add('aw3_performance.csv is not valid CSV: ' + $_.Exception.Message)
}

$routeFailure = Method-Body $controller `
    'private static void LogStrategicRouteFailure(' `
    'private static ArmyRtsState ResolvePursuitState(' `
    'strategic route diagnostics'
Require-Text $routeFailure `
    'if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;' `
    'strategic route diagnostics are not controlled by the live setting'
if ($routeFailure.Contains('Environment.GetEnvironmentVariable')) {
    $failures.Add('strategic route diagnostics still depend on an environment variable')
}

Require-DiagnosticGate $controller `
    'private static void LogMissionChanged(' `
    'private static bool IsMissionValid(' `
    'RTS health/director diagnostics'
Require-DiagnosticGate $transport `
    'private static void LogPhase(' `
    "`n    }`n}" `
    'RTS transport diagnostics'
Require-DiagnosticGate $transportProduction `
    'private static void LogOutcomeOnce(' `
    "`n    }`n}" `
    'RTS transport-production diagnostics'
Require-DiagnosticGate $watchdog `
    'private static void LogRecoveryAction(' `
    'private static void RetreatArmy(' `
    'RTS stall audit/recovery diagnostics'
Require-DiagnosticGate $armySafety `
    'private static void ArmySetCaptainDiagnostic_Postfix(' `
    '[HarmonyPrefix]' `
    'RTS captain-change diagnostics'

# These are actionable faults, not verbose state reports, and must remain visible.
Require-Text $rtsContent `
    '[Army RTS] Missing vanilla decision asset:' `
    'the missing-decision fault warning was removed'
Require-Text $schedulerPatch `
    'AW native Army RTS scheduling failed; game paused:' `
    'the scheduler exception warning was removed'
Require-Text $visualization `
    'Army RTS visualization failed:' `
    'the visualization exception warning was removed'

if ($failures.Count -gt 0) {
    Write-Host "Army RTS diagnostics setting guard failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Army RTS diagnostics setting source guards passed.'
