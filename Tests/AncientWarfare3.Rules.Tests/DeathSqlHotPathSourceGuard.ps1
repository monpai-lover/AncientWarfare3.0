param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$writer = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/LineageArchiveWriter.cs'))
$facts = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/RulerTitleFactService.cs'))
$lineage = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/lineage/LineageService.cs'))
$manager = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/db/LineageArchiveManager.cs'))
$presence = [IO.File]::ReadAllText((Join-Path $root `
    'Code/core/db/ActorArchivePresenceIndex.cs'))
$failures = [System.Collections.Generic.List[string]]::new()

$queueStart = $writer.IndexOf(
    'public static bool QueueDeath(Actor pActor, bool pTraceOnly)',
    [StringComparison]::Ordinal)
$queueEnd = $writer.IndexOf('private static bool Upsert(', $queueStart,
    [StringComparison]::Ordinal)
if ($queueStart -lt 0 -or $queueEnd -le $queueStart) {
    $failures.Add('QueueDeath method boundary is missing')
}
else {
    $queueDeath = $writer.Substring($queueStart, $queueEnd - $queueStart)
    if ($queueDeath.Contains('return Upsert(')) {
        $failures.Add('death archive capture must not enter the synchronous previous-row Upsert path')
    }
    if ($queueDeath.Contains('LineageArchiveReader.ReadRow')) {
        $failures.Add('death archive capture must not synchronously read SQLite')
    }
}

$traceStart = $lineage.IndexOf(
    'public static bool HasTraceableArchive(Actor pActor)',
    [StringComparison]::Ordinal)
$traceEnd = $lineage.IndexOf(
    'public static void ArchiveTraceableActor(', $traceStart,
    [StringComparison]::Ordinal)
if ($traceStart -lt 0 -or $traceEnd -le $traceStart) {
    $failures.Add('HasTraceableArchive method boundary is missing')
}
else {
    $traceable = $lineage.Substring($traceStart, $traceEnd - $traceStart)
    foreach ($required in @('ActorArchivePresenceIndex.Contains(',
            'ActorArchivePendingStore.TryRead(')) {
        if (-not $traceable.Contains($required)) {
            $failures.Add("traceable archive lookup must use $required")
        }
    }
    foreach ($syncCall in @('LineageArchiveReader.', 'SQLiteCommand',
            'DB.Select', 'DB.CheckKeyExist(')) {
        if ($traceable.Contains($syncCall)) {
            $failures.Add("traceable archive lookup must not call $syncCall")
        }
    }
}

if (-not $presence.Contains('ConcurrentDictionary<long, byte>')) {
    $failures.Add('living archive presence must use the in-memory concurrent index')
}
foreach ($required in @('ActorArchivePresenceIndex.ResetEmpty();',
        'ActorArchivePresenceIndex.ClearUnknown();')) {
    if (-not $manager.Contains($required)) {
        $failures.Add("archive lifecycle is missing presence-index boundary: $required")
    }
}
$rebuildCount = [regex]::Matches($manager,
    [regex]::Escape('ActorArchivePresenceIndex.Rebuild(_db);')).Count
if ($rebuildCount -lt 4) {
    $failures.Add('archive presence index must rebuild after load, install, and both rollback reopen paths')
}
if (-not $writer.Contains('ActorArchivePresenceIndex.Remove(pActor.data.id);')) {
    $failures.Add('accepted death archive capture must remove the actor from the living presence index')
}

$factsStart = $facts.IndexOf(
    'public static void ArchivePersonalSnapshot(Actor pActor)',
    [StringComparison]::Ordinal)
$factsEnd = $facts.IndexOf('public static bool TryReadPersonalSnapshot(',
    $factsStart, [StringComparison]::Ordinal)
if ($factsStart -lt 0 -or $factsEnd -le $factsStart) {
    $failures.Add('ArchivePersonalSnapshot method boundary is missing')
}
else {
    $archiveFacts = $facts.Substring($factsStart,
        $factsEnd - $factsStart)
    if (-not $archiveFacts.Contains(
            'HistoricalWriteService.TryUpsertState(')) {
        $failures.Add('ruler death facts must use the async historical writer')
    }
    foreach ($syncCall in @('DB.CheckKeyExist(', 'DB.UpdateValue(',
            'DB.Insert(')) {
        if ($archiveFacts.Contains($syncCall)) {
            $failures.Add("ruler death facts must not call $syncCall on the main thread")
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Death SQL hot-path failures: $($failures.Count)"
    foreach ($failure in $failures) { Write-Host " - $failure" }
    exit 1
}

Write-Host 'Death SQL hot-path guard passed.'
