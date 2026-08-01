$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$rulePath = Join-Path $repo `
    'Code/core/lineage/LineageDisplayNameRules.cs'
if (-not [IO.File]::Exists($rulePath)) {
    throw 'LineageDisplayNameRules.cs is missing.'
}

$compileSupport = @'
namespace AncientWarfare3.core.naming
{
    public enum NamingProfileId { None, Xia, Monkey, OrcNomadic, Western }

    public static class AWCultureNamingTraditionRules
    {
        public static NamingProfileId ParseProfile(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "xia": return NamingProfileId.Xia;
                case "monkey": return NamingProfileId.Monkey;
                case "orc_nomadic": return NamingProfileId.OrcNomadic;
                case "western": return NamingProfileId.Western;
                default: return NamingProfileId.None;
            }
        }
    }

    public static class AWWesternFamilyNameRules
    {
        public static string BuildActor(string given, string family,
            bool noble)
        {
            return noble && !string.IsNullOrWhiteSpace(family)
                ? (given ?? string.Empty) + " " + family.Trim()
                : given ?? string.Empty;
        }
    }
}

namespace AncientWarfare3.core.lineage
{
    using AncientWarfare3.core.naming;

    public sealed class FamilyBranchIdentityProjection { }

    public static class WesternFamilyIdentityRules
    {
        public static FamilyBranchIdentityProjection ProjectBranch(
            NamingProfileId profile, string tradition, long parentShiId,
            string originCityName, string displayStem)
        {
            return new FamilyBranchIdentityProjection();
        }

        public static string BuildActor(FamilyBranchIdentityProjection identity,
            string givenName, bool noble)
        {
            return givenName ?? string.Empty;
        }
    }

    internal static class LineageStatus
    {
        public const string NOBLE = "noble";
    }
}
'@

$supportPath = Join-Path ([IO.Path]::GetTempPath()) `
    ('aw3_lineage_display_test_' + [guid]::NewGuid().ToString('N') + '.cs')
try {
    [IO.File]::WriteAllText($supportPath, $compileSupport,
        [Text.UTF8Encoding]::new($false))
    Add-Type -Path $supportPath, $rulePath
} finally {
    Remove-Item -LiteralPath $supportPath -Force -ErrorAction SilentlyContinue
}
$rules = [AncientWarfare3.core.lineage.LineageDisplayNameRules]

function Assert-Equal([string]$name, [string]$expected, [string]$actual) {
    if ($expected -ne $actual) {
        throw "$name expected '$expected' but got '$actual'"
    }
}

$given = [string][char]0x53D1
$family = [string][char]0x59EC
$clan = [string][char]0x5468
$specialName = [string][char]0x5B89 + [char]0x4E50 + [char]0x516C + [char]0x4E3B

Assert-Equal 'pre-integration noble woman keeps lineage surname' ($given + $family) `
    ($rules::Build($given, $family, $clan, $true, $false, $false))
Assert-Equal 'pre-integration noble woman falls back to Shi before given name' ($clan + $given) `
    ($rules::Build($given, '', $clan, $true, $false, $false))
Assert-Equal 'stored single given name is repaired through Shi fallback' ($clan + $given) `
    ($rules::ProjectStored($given, $given, '', $clan, $true, $false, $false))
Assert-Equal 'structured lineage identity replaces a stale special name' `
    ($clan + $given) `
    ($rules::ProjectStored($specialName, $given, '', $clan, $true, $false, $false))
Assert-Equal 'post-integration name uses Shi before given name' ($clan + $given) `
    ($rules::Build($given, $family, $clan, $true, $false, $true))

Write-Output 'Lineage display-name rule tests passed.'
