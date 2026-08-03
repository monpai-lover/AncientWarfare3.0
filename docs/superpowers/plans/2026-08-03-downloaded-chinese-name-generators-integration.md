# Downloaded Chinese Name Generators Integration Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import every downloaded country, creature, and city Chinese name generator except the protected vanilla-human and AW3 monkey IDs while preserving AW3 Xia, western-human, monkey, and nomadic-orc naming behavior.

**Architecture:** Merge each JSON array by generator ID, replacing ordinary collisions with the downloaded object and retaining AW3-only objects. Keep the three `human_*` JSON objects and exclude all three downloaded `civ_monkey_*` objects because AW3 registers monkey generators at runtime. Extend the existing legacy-alias installer only for the missing Japanese suffix alias and a missing-Ganzhi fallback, then verify resource integrity and existing culture routing.

**Tech Stack:** C# 10-compatible AW3 naming rules, Newtonsoft.Json runtime loading, PowerShell structured JSON transformation, .NET 9 rules test executable.

---

### Task 1: Add Japanese and Ganzhi compatibility aliases

**Files:**
- Modify: `tests/AncientWarfare3.Rules.Tests/IntegratedNamingRulesTests.cs.txt:244`
- Modify: `Code/core/naming/AWWordLibraryManager.cs:61`
- Modify: `Code/core/naming/AWNamingResourceLoader.cs:158`

- [ ] **Step 1: Write the failing alias tests**

Add these inputs before the existing `InstallChineseNameLegacyAliases()` call and these assertions after the existing Arabic, Russian, and Jewish assertions:

```csharp
legacyLibraries.Submit("日本名字下", new[] { "子", "郎" });
legacyLibraries.Submit("天干", new[]
{
    "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"
});
legacyLibraries.Submit("地支", new[]
{
    "子", "丑", "寅", "卯", "辰", "巳",
    "午", "未", "申", "酉", "戌", "亥"
});
```

```csharp
Equal(2, legacyLibraries.GetWords("日本名字").Count,
    "downloaded Japanese templates reuse the existing lower given-name library");
Equal(60, legacyLibraries.GetWords("天干地支").Count,
    "a missing Ganzhi library is synthesized as the sexagenary cycle");
Equal(true, legacyLibraries.GetWords("天干地支").Contains("甲子"),
    "the synthesized Ganzhi cycle starts with a valid pair");
Equal(true, legacyLibraries.GetWords("天干地支").Contains("癸亥"),
    "the synthesized Ganzhi cycle contains the final valid pair");

var preloadedGanzhi = new AWWordLibraryManager();
preloadedGanzhi.Submit("天干地支", new[] { "现成词库" });
preloadedGanzhi.Submit("天干", new[] { "甲" });
preloadedGanzhi.Submit("地支", new[] { "子" });
preloadedGanzhi.InstallChineseNameLegacyAliases();
Equal("现成词库", preloadedGanzhi.GetWords("天干地支")[0],
    "the complete recursive Ganzhi library is never overwritten");

var missingCompatibilityWarnings = new List<string>();
new AWWordLibraryManager().InstallChineseNameLegacyAliases(
    missingCompatibilityWarnings.Add);
Equal(2, missingCompatibilityWarnings.Count,
    "missing Japanese and Ganzhi sources each emit one bounded warning");
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet run --project tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --integrated-naming-rules-slice
```

Expected: FAIL because `日本名字` and synthesized `天干地支` contain zero entries.

- [ ] **Step 3: Implement minimal alias installation**

Extend `InstallChineseNameLegacyAliases()` and add focused helpers:

```csharp
public void InstallChineseNameLegacyAliases(Action<string> pWarning = null)
{
    InstallMergedAlias("阿拉伯名字", "阿拉伯男名", "阿拉伯女名");
    InstallMergedAlias("罗斯名字", "罗斯男名", "罗斯女名");
    InstallMergedAlias("犹太人名", "犹太男名", "犹太女名");
    if (!InstallAlias("日本名字", "日本名字下"))
        pWarning?.Invoke("AW3 naming compatibility source is missing: 日本名字下.");
    if (!InstallCyclicPairAlias("天干地支", "天干", "地支"))
        pWarning?.Invoke("AW3 naming compatibility sources are missing: 天干/地支.");
}

private bool InstallAlias(string pAlias, string pSource)
{
    lock (_gate)
    {
        if (_libraries.ContainsKey(pAlias)) return true;
        IEnumerable<string> words = _libraries.TryGetValue(pSource,
            out AWWordLibraryAsset source)
            ? source.Words
            : Array.Empty<string>();
        var alias = new AWWordLibraryAsset(pAlias, words);
        _libraries[pAlias] = alias;
        return alias.Words.Count > 0;
    }
}

private bool InstallCyclicPairAlias(string pAlias, string pFirst,
    string pSecond)
{
    lock (_gate)
    {
        if (_libraries.ContainsKey(pAlias)) return true;
        string[] first = _libraries.TryGetValue(pFirst,
            out AWWordLibraryAsset firstAsset)
            ? firstAsset.Words.ToArray()
            : Array.Empty<string>();
        string[] second = _libraries.TryGetValue(pSecond,
            out AWWordLibraryAsset secondAsset)
            ? secondAsset.Words.ToArray()
            : Array.Empty<string>();
        if (first.Length == 0 || second.Length == 0)
        {
            _libraries[pAlias] = new AWWordLibraryAsset(pAlias,
                Array.Empty<string>());
            return false;
        }

        int count = LeastCommonMultiple(first.Length, second.Length);
        _libraries[pAlias] = new AWWordLibraryAsset(pAlias,
            Enumerable.Range(0, count)
                .Select(pIndex => first[pIndex % first.Length] +
                                  second[pIndex % second.Length]));
        return true;
    }
}

private static int LeastCommonMultiple(int pFirst, int pSecond)
{
    int first = pFirst;
    int second = pSecond;
    while (second != 0)
    {
        int remainder = first % second;
        first = second;
        second = remainder;
    }
    return checked(pFirst / first * pSecond);
}
```

Pass the existing loader warning sink when installing aliases:

```csharp
AWWordLibraryManager.Instance.InstallChineseNameLegacyAliases(
    ModClass.LogWarning);
```

- [ ] **Step 4: Run the focused test and verify it passes**

Run the command from Step 2.

Expected: `AW3 integrated naming rules passed.`

- [ ] **Step 5: Commit the compatibility change**

```powershell
git add Code/core/naming/AWWordLibraryManager.cs Code/core/naming/AWNamingResourceLoader.cs tests/AncientWarfare3.Rules.Tests/IntegratedNamingRulesTests.cs.txt
git commit -m "feat: add downloaded naming compatibility aliases"
```

### Task 2: Merge downloaded generator data by ID

**Files:**
- Modify: `name_generators/default/kingdoms.json`
- Modify: `name_generators/default/creatures.json`
- Modify: `name_generators/default/cities.json`

- [ ] **Step 1: Run a pre-merge assertion and verify it fails**

```powershell
$cases = @(
    @('kingdoms.json', 'human_kingdom', 'civ_monkey_kingdom', 51),
    @('creatures.json', 'human_name', 'civ_monkey_name', 69),
    @('cities.json', 'human_city', 'civ_monkey_city', 51)
)
foreach ($case in $cases) {
    $actual = Get-Content -Raw -Encoding UTF8 `
        (Join-Path 'name_generators/default' $case[0]) | ConvertFrom-Json
    $source = Get-Content -Raw -Encoding UTF8 `
        (Join-Path 'C:/Users/24908/Downloads' $case[0]) | ConvertFrom-Json
    $required = @($source | Where-Object {
        $_.id -notin @($case[1], $case[2])
    })
    $missing = @($required.id | Where-Object { $_ -notin $actual.id })
    if ($actual.Count -ne $case[3] -or $missing.Count -ne 0) {
        throw "$($case[0]) is not merged: count=$($actual.Count), missing=$($missing.Count)"
    }
}
```

Expected: FAIL with current counts `7`, `18`, and `7` and missing downloaded IDs.

- [ ] **Step 2: Perform the validated structured merge**

Run this transformation. It loads and validates all inputs before writing, writes all temporary files before replacement, retains the current human object, excludes monkey JSON objects, replaces ordinary collisions, and preserves AW3-only IDs:

```powershell
$cases = @(
    [pscustomobject]@{ File='kingdoms.json'; Human='human_kingdom'; Monkey='civ_monkey_kingdom'; Count=51 },
    [pscustomobject]@{ File='creatures.json'; Human='human_name'; Monkey='civ_monkey_name'; Count=69 },
    [pscustomobject]@{ File='cities.json'; Human='human_city'; Monkey='civ_monkey_city'; Count=51 }
)

function Assert-GeneratorCollection([object[]]$Items, [string]$Owner) {
    $duplicateIds = @($Items | Group-Object id | Where-Object Count -gt 1)
    if ($duplicateIds.Count -gt 0) { throw "$Owner contains duplicate IDs." }
    foreach ($item in $Items) {
        if ([string]::IsNullOrWhiteSpace([string]$item.id)) {
            throw "$Owner contains an empty generator ID."
        }
        if (@($item.templates).Count -eq 0) {
            throw "$Owner/$($item.id) has no templates."
        }
        foreach ($template in $item.templates) {
            if ($null -eq $template.format) {
                throw "$Owner/$($item.id) has a template without format."
            }
        }
    }
}

$pending = [Collections.Generic.List[object]]::new()
foreach ($case in $cases) {
    $target = Join-Path 'name_generators/default' $case.File
    $sourcePath = Join-Path 'C:/Users/24908/Downloads' $case.File
    [object[]]$current = Get-Content -Raw -Encoding UTF8 $target | ConvertFrom-Json
    [object[]]$source = Get-Content -Raw -Encoding UTF8 $sourcePath | ConvertFrom-Json
    Assert-GeneratorCollection $current $target
    Assert-GeneratorCollection $source $sourcePath

    $sourceById = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::Ordinal)
    foreach ($item in $source) { $sourceById.Add([string]$item.id, $item) }
    $currentIds = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($item in $current) { [void]$currentIds.Add([string]$item.id) }

    $humanBefore = ($current | Where-Object id -ceq $case.Human) |
        ConvertTo-Json -Depth 100 -Compress
    if ([string]::IsNullOrEmpty($humanBefore)) {
        throw "$target is missing its current human generator."
    }

    $result = [Collections.Generic.List[object]]::new()
    foreach ($item in $current) {
        if ($item.id -ceq $case.Monkey) { continue }
        if ($item.id -ceq $case.Human) {
            $result.Add($item)
        } elseif ($sourceById.ContainsKey([string]$item.id)) {
            $result.Add($sourceById[[string]$item.id])
        } else {
            $result.Add($item)
        }
    }
    foreach ($item in $source) {
        if ($item.id -in @($case.Human, $case.Monkey)) { continue }
        if (-not $currentIds.Contains([string]$item.id)) { $result.Add($item) }
    }

    [object[]]$final = $result.ToArray()
    Assert-GeneratorCollection $final "$target merged output"
    if ($final.Count -ne $case.Count) {
        throw "$target expected $($case.Count) generators, got $($final.Count)."
    }
    if ($case.Monkey -in $final.id) {
        throw "$target contains downloaded monkey generator $($case.Monkey)."
    }
    $humanAfter = ($final | Where-Object id -ceq $case.Human) |
        ConvertTo-Json -Depth 100 -Compress
    if ($humanAfter -cne $humanBefore) {
        throw "$target changed protected human generator $($case.Human)."
    }

    $json = ConvertTo-Json -InputObject $final -Depth 100
    $pending.Add([pscustomobject]@{
        Target = $target
        Temp = "$target.aw3-name-import.tmp"
        Json = $json + [Environment]::NewLine
    })
}

$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($entry in $pending) {
    [IO.File]::WriteAllText($entry.Temp, $entry.Json, $utf8)
}
foreach ($entry in $pending) {
    Move-Item -LiteralPath $entry.Temp -Destination $entry.Target -Force
}
```

Expected: no output and exit code `0`.

- [ ] **Step 3: Compare every approved source object with the merged output**

```powershell
$cases = @(
    @('kingdoms.json', 'human_kingdom', 'civ_monkey_kingdom', 51),
    @('creatures.json', 'human_name', 'civ_monkey_name', 69),
    @('cities.json', 'human_city', 'civ_monkey_city', 51)
)
foreach ($case in $cases) {
    $actual = Get-Content -Raw -Encoding UTF8 `
        (Join-Path 'name_generators/default' $case[0]) | ConvertFrom-Json
    $source = Get-Content -Raw -Encoding UTF8 `
        (Join-Path 'C:/Users/24908/Downloads' $case[0]) | ConvertFrom-Json
    if ($actual.Count -ne $case[3]) { throw "Unexpected final count" }
    if (@($actual.id | Group-Object | Where-Object Count -ne 1).Count -ne 0) {
        throw "Duplicate generator ID in $($case[0])"
    }
    foreach ($expected in $source) {
        if ($expected.id -in @($case[1], $case[2])) { continue }
        $observed = @($actual | Where-Object id -eq $expected.id)
        if ($observed.Count -ne 1) { throw "Missing $($expected.id)" }
        $expectedJson = $expected | ConvertTo-Json -Depth 100 -Compress
        $observedJson = $observed[0] | ConvertTo-Json -Depth 100 -Compress
        if ($observedJson -cne $expectedJson) {
            throw "Downloaded definition mismatch: $($expected.id)"
        }
    }
    if ($case[2] -in $actual.id) {
        throw "Downloaded monkey generator leaked into $($case[0])"
    }
}
```

Expected: no output and exit code `0`.

- [ ] **Step 4: Verify AW3-owned generator IDs remain present**

```powershell
$kingdoms = Get-Content -Raw -Encoding UTF8 name_generators/default/kingdoms.json | ConvertFrom-Json
$creatures = Get-Content -Raw -Encoding UTF8 name_generators/default/creatures.json | ConvertFrom-Json
$cities = Get-Content -Raw -Encoding UTF8 name_generators/default/cities.json | ConvertFrom-Json
foreach ($id in 'human_kingdom','Xia_kingdom','western_kingdom','orc_nomadic_kingdom') {
    if ($id -notin $kingdoms.id) { throw "Missing AW3 kingdom generator $id" }
}
foreach ($id in 'human_name','Xia_name','western_von_name','western_de_name','western_van_name','western_di_name','orc_nomadic_name') {
    if ($id -notin $creatures.id) { throw "Missing AW3 creature generator $id" }
}
foreach ($id in 'human_city','Xia_city','western_city','orc_nomadic_city') {
    if ($id -notin $cities.id) { throw "Missing AW3 city generator $id" }
}
```

Expected: no output and exit code `0`.

- [ ] **Step 5: Commit the merged data**

```powershell
git add name_generators/default/kingdoms.json name_generators/default/creatures.json name_generators/default/cities.json
git commit -m "feat: import downloaded Chinese name generators"
```

### Task 3: Validate templates and culture routing

**Files:**
- Test only; no production modification expected

- [ ] **Step 1: Validate all static word-library references**

```powershell
$libraries = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
Get-ChildItem word_libraries/default -Recurse -File -Filter '*.txt' |
    ForEach-Object { [void]$libraries.Add($_.BaseName) }
foreach ($alias in '阿拉伯名字','罗斯名字','犹太人名','日本名字') {
    [void]$libraries.Add($alias)
}

$missing = [Collections.Generic.List[string]]::new()
Get-ChildItem name_generators/default -Recurse -File -Filter '*.json' |
    ForEach-Object {
        $file = $_
        [object[]]$generators = Get-Content -Raw -Encoding UTF8 $file.FullName |
            ConvertFrom-Json
        foreach ($generator in $generators) {
            $formats = @($generator.templates.format)
            if ($null -ne $generator.default_template) {
                $formats += $generator.default_template.format
            }
            foreach ($format in $formats) {
                foreach ($match in [regex]::Matches([string]$format, '\{([^{}]+)\}')) {
                    $reference = $match.Groups[1].Value
                    if ($reference.Contains('$')) { continue }
                    $libraryId = ($reference -split ':', 2)[0]
                    if (-not $libraries.Contains($libraryId)) {
                        $missing.Add("$($file.Name)/$($generator.id): $libraryId")
                    }
                }
            }
        }
    }
if ($missing.Count -gt 0) {
    throw "Missing static word libraries:`n$($missing -join [Environment]::NewLine)"
}
```

Expected: no output and exit code `0`; `日本名字` resolves through the new alias and `天干地支` resolves to the existing `mobs/天干地支.txt`.

- [ ] **Step 2: Run focused naming regression suites**

```powershell
dotnet run --project tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --integrated-naming-rules-slice
dotnet run --project tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --xia-monkey-slice
dotnet run --project tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --western-lineage-naming-rules-slice
powershell -ExecutionPolicy Bypass -File Tests/WesternLineageNamingSourceGuard.ps1
```

Expected: all four commands exit `0`, with integrated naming, Xia/monkey, western naming, and source-guard success messages.

- [ ] **Step 3: Run the full rules suite**

```powershell
dotnet run --project tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: exit code `0` and the final all-rules success message.

- [ ] **Step 4: Inspect the final diff**

```powershell
git diff HEAD~2 --check
git status --short
```

Expected: no whitespace errors; only the unrelated pre-existing `Code/core/policy/HierarchicalVassalMapModeRules.cs` modification remains unstaged.
