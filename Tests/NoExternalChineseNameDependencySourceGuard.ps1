$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$codeRoot = Join-Path $repoRoot 'Code'
$projectPath = Join-Path $repoRoot 'AncientWarfare3.csproj'
$manifestPath = Join-Path $repoRoot 'mod.json'

$externalGuid = ([string][char]0x4E00) + ([string][char]0x7C73) + '_' +
    ([string][char]0x4E2D) + ([string][char]0x6587) + ([string][char]0x540D)
$forbiddenPatterns = @(
    'Chinese_Name'
    [regex]::Escape($externalGuid)
    'Chinese_Name\.dll'
)

$runtimeSources = @(
    Get-ChildItem -LiteralPath $codeRoot -Recurse -File -Filter '*.cs'
) + @(Get-Item -LiteralPath $projectPath)

foreach ($file in $runtimeSources) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($pattern in $forbiddenPatterns) {
        if ($text -cmatch $pattern) {
            $relative = $file.FullName.Substring($repoRoot.Length + 1)
            throw "External Chinese Name dependency remains in ${relative}: $pattern"
        }
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if (@($manifest.OptionalDependencies) -contains $externalGuid) {
    throw 'External Chinese Name must not remain an optional dependency.'
}
if (@($manifest.IncompatibleWith) -notcontains $externalGuid) {
    throw 'AW3 must declare the external Chinese Name mod incompatible.'
}

Write-Output 'No external Chinese Name dependency source guard passed.'
