$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rules = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtOfficerRecordRules.cs')
$court = Get-Content -Raw (Join-Path $root 'Code\core\court\CourtService.cs')
$identity = Get-Content -Raw (Join-Path $root 'Code\core\lineage\SocialIdentityService.cs')
$migration = Get-Content -Raw (Join-Path $root 'Code\core\lineage\SocialIdentityMigrationService.cs')
$locale = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Locales\aw3_social_identity.csv')
$icon = Join-Path $root 'GameResources\ui\Icons\traits\iconshidafu.png'
$noble = Get-Content -Raw (Join-Path $root 'Code\core\lineage\NobleRankService.cs')
$chronicle = Get-Content -Raw (Join-Path $root 'Code\core\lineage\ChronicleEvents.cs')
$dynasty = Get-Content -Raw (Join-Path $root 'Code\core\lineage\DynastyRecordWriter.cs')

# A noble title is granted by a state, so it dies with that state. Leaving it
# alive lets a fallen court keep handing out inheritable standing.
if (-not $noble.Contains('RevokeKingdomTitles(')) {
    throw 'Kingdom-granted noble titles must be revocable as a set'
}
if ($noble -notmatch
    'RevokeKingdomTitles[\s\S]{0,1600}current\.KingdomId != pKingdom\.id') {
    throw 'Only titles granted by the ending kingdom may be revoked'
}
if ($noble -notmatch
    'RevokeKingdomTitles[\s\S]{0,2000}ChronicleEvents\.OnNobleRankExtinct') {
    throw 'Revoked titles must be recorded in history'
}
if (-not $chronicle.Contains('NobleRankService.RevokeKingdomTitles(pKingdom, "kingdom_fell")')) {
    throw 'A destroyed kingdom must revoke the titles it granted'
}
if (-not $dynasty.Contains('"dynasty_replaced"')) {
    throw 'A dynastic replacement must revoke the previous dynasty titles'
}

$codeText = (Get-ChildItem -Path (Join-Path $root 'Code') -Recurse -Filter '*.cs' |
    ForEach-Object { Get-Content -Raw $_.FullName }) -join "`n"

if (-not $rules.Contains('ShouldGrantNobleIdentity')) {
    throw 'Formal official noble identity decision rule is missing'
}
if ($court -notmatch
    'ShouldGrantNobleIdentity\(\s*careerResult\.IsCommitted, pActing\)[\s\S]{0,180}EnsureOfficialShiAndClan') {
    throw 'Noble identity must be granted only after a committed formal appointment'
}
if (([regex]::Matches($court, 'EnsureOfficialShiAndClan')).Count -ne 2) {
    throw 'Noble identity admission must have committed and restore boundaries'
}
if ($court -notmatch
    'if \(!pAppointment\.IsActing\)[\s\S]{0,120}EnsureOfficialShiAndClan') {
    throw 'Formal appointments must repair noble identity during bounded restore'
}
if ($identity -notmatch
    'addTrait\(LineageKeys\.TRAIT_GUIZU,\s*pRemoveOpposites:\s*true\)' -or
    $identity -notmatch
    'addTrait\(LineageKeys\.TRAIT_SHIDAFU,\s*pRemoveOpposites:\s*true\)') {
    throw 'Social identity traits must use the engine opposite-removal path'
}
if ($codeText -match
    'addTrait\(LineageKeys\.TRAIT_GUIZU\s*\)') {
    throw 'Noble trait grants must not bypass opposite-trait removal'
}
$dbRead = $migration.IndexOf('var db = LineageArchiveManager.Instance?.OperatingDB;')
$completeWrite = $migration.IndexOf('_completed = true;')
if ($dbRead -lt 0 -or $completeWrite -lt 0 -or $completeWrite -lt $dbRead -or
    $migration -notmatch 'if \(db == null\) return 0;') {
    throw 'Social identity migration cannot complete before the archive database is ready'
}
if (-not $migration.Contains('RepairCurrentRealmLeaders')) {
    throw 'Legacy city leaders without career rows must receive social identity repair'
}
if (-not $locale.Contains('trait_shidafu_info,')) {
    throw 'Scholar-official trait must use the WorldBox trait _info localization key'
}
if ($locale.Contains('trait_shidafu_description,')) {
    throw 'Unsupported scholar-official _description localization key must be removed'
}
if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
    throw 'Scholar-official trait icon is missing'
}
Write-Output 'Formal official noble identity source guard PASS'
