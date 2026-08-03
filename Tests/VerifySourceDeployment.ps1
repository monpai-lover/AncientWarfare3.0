[CmdletBinding()]
param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DestinationRoot,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

$productionDirectories = @(
    'ABPackages',
    'EmbededResources',
    'fonts',
    'GameResources',
    'Locales',
    'name_generators',
    'word_libraries',
    'THIRD_PARTY_NOTICES'
)
$productionRootFiles = @(
    'AncientWarfare3.csproj',
    'mod.json',
    'default_config.json',
    'icon.png',
    'README.md',
    'sponsor_qr.jpg',
    'supporters.csv',
    'THIRD_PARTY_NOTICES.md'
)
$excludedDirectoryNames = @(
    'Assemblies', 'bin', 'obj', 'Tests', 'docs', '.git', '.worktrees',
    '.codex', '.superpowers', 'release', 'log', 'logs', 'db', 'temp',
    'tmp', '.runtime'
)
$temporaryExtensions = @(
    '.log', '.tmp', '.temp', '.bak', '.db', '.db-shm', '.db-wal',
    '.sqlite', '.sqlite3', '.pdb'
)

function ConvertTo-RelativePath([string]$Root, [string]$FullName) {
    return $FullName.Substring($Root.Length + 1).Replace('\', '/')
}

function Test-IsExcludedFile([System.IO.FileInfo]$File, [string]$CollectionRoot) {
    $relative = ConvertTo-RelativePath $CollectionRoot $File.FullName
    $segments = $relative.Split('/')
    if ($segments.Count -gt 1) {
        foreach ($segment in $segments[0..($segments.Count - 2)]) {
            if ($excludedDirectoryNames -icontains $segment) {
                return $true
            }
        }
    }

    if ($File.Name.StartsWith('~$', [System.StringComparison]::Ordinal) -or
        $File.Name.EndsWith('~', [System.StringComparison]::Ordinal)) {
        return $true
    }

    return $temporaryExtensions -icontains $File.Extension
}

function Get-ProductionManifest([string]$Root, [bool]$IsSource,
    [System.Collections.Generic.List[string]]$Failures) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        $Failures.Add("Root directory does not exist: $Root")
        return @{}
    }

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    $manifest = @{}

    $codeRoot = Join-Path $resolvedRoot 'Code'
    if (-not (Test-Path -LiteralPath $codeRoot -PathType Container)) {
        $Failures.Add("Missing production directory: Code")
    } else {
        Get-ChildItem -LiteralPath $codeRoot -Recurse -File | ForEach-Object {
            $relative = ConvertTo-RelativePath $resolvedRoot $_.FullName
            if ($IsSource -and $_.Extension -ieq '.dll') {
                $Failures.Add("Source production collection contains forbidden DLL: $relative")
            }
            if ($_.Extension -ieq '.cs') {
                $manifest[$relative] = $_.FullName
            }
        }
    }

    foreach ($directory in $productionDirectories) {
        $directoryRoot = Join-Path $resolvedRoot $directory
        if (-not (Test-Path -LiteralPath $directoryRoot -PathType Container)) {
            $Failures.Add("Missing production directory: $directory")
            continue
        }
        Get-ChildItem -LiteralPath $directoryRoot -Recurse -File |
            ForEach-Object {
                if (Test-IsExcludedFile $_ $resolvedRoot) {
                    return
                }
                $relative = ConvertTo-RelativePath $resolvedRoot $_.FullName
                if ($IsSource -and $_.Extension -ieq '.dll') {
                    $Failures.Add("Source production collection contains forbidden DLL: $relative")
                }
                $manifest[$relative] = $_.FullName
            }
    }

    foreach ($fileName in $productionRootFiles) {
        $fullName = Join-Path $resolvedRoot $fileName
        if (-not (Test-Path -LiteralPath $fullName -PathType Leaf)) {
            $Failures.Add("Missing production root file: $fileName")
            continue
        }
        if ([System.IO.Path]::GetExtension($fileName) -ieq '.dll' -and $IsSource) {
            $Failures.Add("Source production collection contains forbidden DLL: $fileName")
        }
        $manifest[$fileName] = $fullName
    }

    return $manifest
}

function Test-SourceDeployment([string]$ActualSourceRoot,
    [string]$ActualDestinationRoot) {
    $failures = [System.Collections.Generic.List[string]]::new()
    $sourceManifest = Get-ProductionManifest $ActualSourceRoot $true $failures
    $destinationManifest = Get-ProductionManifest $ActualDestinationRoot $false $failures

    foreach ($relative in @($sourceManifest.Keys | Sort-Object)) {
        if (-not $destinationManifest.ContainsKey($relative)) {
            $failures.Add("Missing destination file: $relative")
            continue
        }
        $sourceHash = (Get-FileHash -LiteralPath $sourceManifest[$relative] -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $destinationManifest[$relative] -Algorithm SHA256).Hash
        if ($sourceHash -cne $destinationHash) {
            $failures.Add("SHA256 mismatch: $relative (source=$sourceHash destination=$destinationHash)")
        }
    }

    foreach ($relative in @($destinationManifest.Keys | Sort-Object)) {
        if (-not $sourceManifest.ContainsKey($relative)) {
            $failures.Add("Extra destination production file: $relative")
        }
    }

    return [pscustomobject]@{
        SourceCount = $sourceManifest.Count
        DestinationCount = $destinationManifest.Count
        Failures = @($failures)
    }
}

function Format-SuccessMessage([int]$FileCount) {
    return "Source deployment verified: $FileCount files, all relative paths and SHA256 hashes match."
}

function New-SelfTestFixture([string]$Root) {
    New-Item -ItemType Directory -Path (Join-Path $Root 'Code') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Root 'Code/core/db') -Force | Out-Null
    foreach ($directory in $productionDirectories) {
        New-Item -ItemType Directory -Path (Join-Path $Root $directory) -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $Root "$directory/content.txt") `
            -Value "fixture-$directory" -NoNewline
    }
    Set-Content -LiteralPath (Join-Path $Root 'Code/Keep.cs') `
        -Value 'internal sealed class Keep {}' -NoNewline
    Set-Content -LiteralPath (Join-Path $Root 'Code/core/db/Database.cs') `
        -Value 'internal sealed class Database {}' -NoNewline
    foreach ($fileName in $productionRootFiles) {
        Set-Content -LiteralPath (Join-Path $Root $fileName) `
            -Value "fixture-$fileName" -NoNewline
    }
}

function Invoke-SelfTest {
    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
        ('aw3-source-deployment-' + [guid]::NewGuid().ToString('N'))
    $source = Join-Path $fixtureRoot 'source'
    $destination = Join-Path $fixtureRoot 'destination'
    $selfTestFailures = [System.Collections.Generic.List[string]]::new()

    try {
        New-SelfTestFixture $source
        Copy-Item -LiteralPath $source -Destination $destination -Recurse

        $identical = Test-SourceDeployment $source $destination
        if ($identical.Failures.Count -ne 0) {
            $selfTestFailures.Add('identical fixture was rejected: ' +
                ($identical.Failures -join '; '))
        }
        $expectedFixtureCount = 2 + $productionDirectories.Count +
            $productionRootFiles.Count
        if ($identical.SourceCount -ne $expectedFixtureCount) {
            $selfTestFailures.Add("Code/core/db source was omitted: expected $expectedFixtureCount fixture files, found $($identical.SourceCount)")
        }
        $successMessage = Format-SuccessMessage $identical.SourceCount
        if ($successMessage -cne
            "Source deployment verified: $($identical.SourceCount) files, all relative paths and SHA256 hashes match.") {
            $selfTestFailures.Add("success summary was malformed: $successMessage")
        }

        Remove-Item -LiteralPath (Join-Path $destination 'Code/Keep.cs')
        $missing = Test-SourceDeployment $source $destination
        if (-not ($missing.Failures -match '^Missing destination file: Code/Keep\.cs$')) {
            $selfTestFailures.Add('missing source counterpart was not rejected clearly')
        }
        Copy-Item -LiteralPath (Join-Path $source 'Code/Keep.cs') `
            -Destination (Join-Path $destination 'Code/Keep.cs')

        Set-Content -LiteralPath (Join-Path $destination 'README.md') `
            -Value 'changed' -NoNewline
        $mismatch = Test-SourceDeployment $source $destination
        if (-not ($mismatch.Failures -match '^SHA256 mismatch: README\.md ')) {
            $selfTestFailures.Add('hash mismatch was not rejected clearly')
        }
        Copy-Item -LiteralPath (Join-Path $source 'README.md') `
            -Destination (Join-Path $destination 'README.md') -Force

        Set-Content -LiteralPath (Join-Path $destination 'Code/Extra.cs') `
            -Value 'internal sealed class Extra {}' -NoNewline
        $extra = Test-SourceDeployment $source $destination
        if (-not ($extra.Failures -match '^Extra destination production file: Code/Extra\.cs$')) {
            $selfTestFailures.Add('extra Code source was not rejected clearly')
        }
        Remove-Item -LiteralPath (Join-Path $destination 'Code/Extra.cs')

        Set-Content -LiteralPath (Join-Path $source 'ABPackages/Forbidden.dll') `
            -Value 'not-a-real-dll' -NoNewline
        $dll = Test-SourceDeployment $source $destination
        if (-not ($dll.Failures -match '^Source production collection contains forbidden DLL:')) {
            $selfTestFailures.Add('source DLL was not rejected clearly')
        }

        if ($selfTestFailures.Count -gt 0) {
            throw "Source deployment verifier self-test failures:`n - " +
                ($selfTestFailures -join "`n - ")
        }
        Write-Output 'PASS: identical deployment accepted'
        Write-Output 'PASS: missing destination file rejected'
        Write-Output 'PASS: SHA256 mismatch rejected'
        Write-Output 'PASS: extra Code source rejected'
        Write-Output 'PASS: source DLL rejected'
    } finally {
        if (Test-Path -LiteralPath $fixtureRoot) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    return
}

if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    throw 'DestinationRoot is required unless -SelfTest is used.'
}

$result = Test-SourceDeployment $SourceRoot $DestinationRoot
if ($result.Failures.Count -gt 0) {
    throw "Source deployment verification failed ($($result.Failures.Count) issue(s)):`n - " +
        ($result.Failures -join "`n - ")
}

Write-Output (Format-SuccessMessage $result.SourceCount)
