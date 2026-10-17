$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$data = Join-Path $repo 'supporters.csv'
$qr = Join-Path $repo 'sponsor_qr.jpg'
$window = Join-Path $repo 'Code/ui/windows/SupporterLeaderboardWindow.cs'
$item = Join-Path $repo 'Code/ui/items/SupporterLeaderboardListItem.cs'
$service = Join-Path $repo 'Code/ui/SupporterLeaderboardData.cs'
$roster = Join-Path $repo 'Code/content/supporters/SupporterRosterData.cs'
$tab = Join-Path $repo 'Code/ui/AW_LineageTab.cs'
$ids = Join-Path $repo 'Code/ui/AW_LineageWindowIds.cs'
$locale = Join-Path $repo 'Locales/aw3_supporters.csv'
foreach ($path in @($data, $qr, $window, $item, $service, $roster, $tab, $ids, $locale)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "missing supporter leaderboard source: $path" }
}
$csv = Get-Content -Raw -Encoding UTF8 -LiteralPath $data
if ($csv -notmatch '(?m)^rank,name,amount,date(?:,description(?:,contribution_weight)?)?\r?$') {
    throw 'supporters.csv header is invalid'
}
# Compile both shipped readers: leaderboard rows share the card roster parser.
$harness = @'
namespace AncientWarfare3 {
    public class ModClass {
        public static ModClass Instance { get; } = new ModClass();
        public Declaration Declaration { get; } = new Declaration();
        public Declaration GetDeclaration() => Declaration;
    }
    public class Declaration { public string FolderPath; }
}
namespace AncientWarfare3.ui {
    public static class SupporterDataCheck {
        public static void Run(string csvPath) {
            ModClass.Instance.Declaration.FolderPath = System.IO.Path.GetDirectoryName(csvPath);
            var expected = content.supporters.SupporterRosterData.Parse(System.IO.File.ReadAllLines(csvPath));
            var actual = SupporterLeaderboardData.Read();
            if (expected.Count == 0 || expected.Count != actual.Count)
                throw new System.Exception("Leaderboard must include every shared-roster supporter.");
            for (int i = 0; i < expected.Count; i++) {
                var a = expected[i]; var b = actual[i];
                var amount = a.Amount == "" && a.Description == "" ? "-" : a.Amount;
                var date = a.Date == "" && a.Description == "" ? "-" : a.Date;
                if (a.Rank != b.Rank || a.Name != b.Name || amount != b.Amount ||
                    date != b.Date || a.Description != b.Description)
                    throw new System.Exception("Leaderboard differs from shared roster at rank " + a.Rank);
            }
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "aw3-supporter-check-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            try {
                ModClass.Instance.Declaration.FolderPath = folder;
                if (SupporterLeaderboardData.Read().Count != 0)
                    throw new System.Exception("Missing shared roster must not resurrect an obsolete embedded roster.");
                System.IO.File.WriteAllLines(System.IO.Path.Combine(folder, SupporterLeaderboardData.FileName), new[] {
                    "rank,name,amount,date,description,contribution_weight", "# comment",
                    "2,Technical,,,Artwork,80", "1,\"Name, with comma\",12.50,2026-08-19",
                    "3,Unspecified,,," });
                var parsed = SupporterLeaderboardData.Read();
                var roster = content.supporters.SupporterRosterData.Read();
                if (parsed.Count != 3 || parsed[0].Name != "Name, with comma" ||
                    parsed[0].Amount != "12.50" || parsed[1].Description != "Artwork" ||
                    parsed[1].Amount != "" || parsed[1].Date != "" ||
                    parsed[2].Amount != "-" || parsed[2].Date != "-" ||
                    roster[1].ContributionWeight != "80")
                    throw new System.Exception("Shared parsing must preserve quotes, contribution weights and display placeholders.");
            } finally {
                ModClass.Instance.Declaration.FolderPath = null;
                System.IO.Directory.Delete(folder, true);
            }
        }
    }
}
'@
$harnessPath = Join-Path ([IO.Path]::GetTempPath()) ("aw3-supporter-" + [guid]::NewGuid().ToString('N') + '.cs')
try {
    [IO.File]::WriteAllText($harnessPath, $harness)
    Add-Type -Path @($roster, $service, $harnessPath)
    [AncientWarfare3.ui.SupporterDataCheck]::Run($data)
} finally {
    Remove-Item -LiteralPath $harnessPath -ErrorAction SilentlyContinue
}
$itemSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $item
foreach ($required in @('decimal.TryParse', 'amountPrefix + amount', 'Description', 'description')) {
    if ($itemSource -notmatch [regex]::Escape($required)) {
        throw "supporter non-monetary display handling missing: $required"
    }
}
$windowSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $window
foreach ($required in @('sponsor_qr.jpg', 'ImageConversion.LoadImage', 'SponsorQr')) {
    if ($windowSource -notmatch [regex]::Escape($required)) {
        throw "supporter QR integration missing: $required"
    }
}
$tabSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $tab
if ($tabSource -notmatch 'aw_supporter_leaderboard_btn' -or
    $tabSource -notmatch 'SupporterLeaderboardWindow\.Open') {
    throw 'supporter leaderboard tab entry is missing'
}
$idSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $ids
if ($idSource -notmatch 'SUPPORTERS\s*=\s*"aw_supporters"') {
    throw 'supporter window id is missing'
}
Write-Output 'Supporter leaderboard source guard passed.'
