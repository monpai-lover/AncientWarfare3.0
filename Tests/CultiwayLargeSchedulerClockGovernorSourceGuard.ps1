. (Join-Path $PSScriptRoot `
    'CultiwayLargeSchedulerSourceGuardCommon.ps1')

$time = Read-Source 'Code/core/performance/AWSimulationTime.cs'
$governor = Read-Source 'Code/core/performance/AWFramePriorityGovernor.cs'
$settings = Read-Source 'Code/core/performance/AWPerformanceSettings.cs'
$context = Read-Source 'Code/core/performance/AWSimulationStepContext.cs'
$config = Read-Source 'default_config.json'

@(
    @('time bind', $time, 'BindWorld(MapBox pWorld)'),
    @('time begin', $time,
        'BeginTick(MapBox pWorld, float pDeltaTime)'),
    @('time complete', $time, 'CompleteTick(MapBox pWorld)'),
    @('time cancel', $time, 'CancelTick()'),
    @('time unbind', $time, 'UnbindWorld()'),
    @('domain enum', $governor, 'AWSimulationDomain'),
    @('remaining budget', $governor,
        'GetRemainingSimulationBudgetMilliseconds()'),
    @('domain starvation method', $governor,
        'CanUseStarvationSlice('),
    @('domain starvation parameter', $governor,
        'AWSimulationDomain pDomain'),
    @('every-frame starvation', $settings,
        'public const int StarvationFrameInterval = 1;'),
    @('background join budget', $settings,
        'public const float BackgroundJoinMilliseconds = 0.2f;'),
    @('vanilla batch size', $settings,
        'public const int SimulationBatchSize = 256;'),
    @('eight millisecond code default', $settings,
        'MaxSimulationMillisecondsPerFrame { get; private set; } = 8f;'),
    @('presentation smoothing code default', $settings,
        'EnablePresentationSmoothing { get; private set; } = true;'),
    @('thread-local step depth', $context, '[ThreadStatic]'),
    @('active step marker', $context,
        'internal static bool IsActive => _depth > 0;')
) | ForEach-Object {
    Require-Text $_[0] $_[1] $_[2]
}

if (-not [string]::IsNullOrWhiteSpace($config)) {
    $configObject = $config | ConvertFrom-Json
    $budgetSetting = $configObject.AWPerformanceSettings |
        Where-Object Id -eq 'AW3_MAX_SIMULATION_MS_PER_FRAME'
    $smoothingSetting = $configObject.AWPerformanceSettings |
        Where-Object Id -eq 'AW3_ENABLE_PRESENTATION_SMOOTHING'
    if ($null -eq $budgetSetting -or
        [double]$budgetSetting.FloatVal -ne 8d) {
        $script:GuardFailures.Add(
            'default config simulation budget must be 8ms')
    }
    if ($null -eq $smoothingSetting -or
        -not [bool]$smoothingSetting.BoolVal) {
        $script:GuardFailures.Add(
            'default config presentation smoothing must be enabled')
    }
}

Complete-Guard 'clock/governor guard' `
    'Cultiway large scheduler clock/governor guard passed.'
