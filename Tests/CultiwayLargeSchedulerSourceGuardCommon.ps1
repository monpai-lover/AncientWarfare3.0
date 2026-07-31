$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:GuardRoot = Split-Path -Parent $PSScriptRoot
$script:GuardFailures = [System.Collections.Generic.List[string]]::new()

function Read-Source([string]$Path) {
    $fullPath = Join-Path $script:GuardRoot $Path
    if (-not [System.IO.File]::Exists($fullPath)) {
        $script:GuardFailures.Add("missing source file: $Path")
        return ''
    }

    return [System.IO.File]::ReadAllText($fullPath)
}

function Require-Text([string]$Name, [string]$Text, [string]$Needle) {
    if (-not $Text.Contains($Needle)) {
        $script:GuardFailures.Add("${Name}: missing '$Needle'")
    }
}

function Require-Count([string]$Name, [string]$Text,
    [string]$Needle, [int]$Expected) {
    $count = ([regex]::Matches(
        $Text,
        [regex]::Escape($Needle))).Count
    if ($count -ne $Expected) {
        $script:GuardFailures.Add(
            "${Name}: expected $Expected occurrences of '$Needle', found $count")
    }
}

function Require-Before([string]$Name, [string]$Text,
    [string]$First, [string]$Second) {
    $firstIndex = $Text.IndexOf($First,
        [System.StringComparison]::Ordinal)
    $secondIndex = $Text.IndexOf($Second,
        [System.StringComparison]::Ordinal)
    if ($firstIndex -lt 0 -or $secondIndex -lt 0 -or
        $firstIndex -ge $secondIndex) {
        $script:GuardFailures.Add(
            "${Name}: '$First' must occur before '$Second'")
    }
}

function Forbid-Text([string]$Name, [string]$Text,
    [string]$Needle) {
    if ($Text.Contains($Needle)) {
        $script:GuardFailures.Add("${Name}: forbidden '$Needle'")
    }
}

function Complete-Guard([string]$Name, [string]$SuccessMessage) {
    if ($script:GuardFailures.Count -gt 0) {
        throw "${Name} failures:`n - " +
            ($script:GuardFailures -join "`n - ")
    }

    Write-Output $SuccessMessage
}
