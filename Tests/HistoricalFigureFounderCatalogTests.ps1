$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$definitionPath = Join-Path $repo `
    'Code/content/figures/HistoricalFigureDef.cs'
$rulesPath = Join-Path $repo `
    'Code/content/figures/HistoricalFigureSpawnRules.cs'
$displayRulesPath = Join-Path $repo `
    'Code/core/lineage/LineageDisplayNameRules.cs'
$givenNameRulesPath = Join-Path $repo `
    'Code/core/lineage/LineageGivenNameNormalizationRules.cs'
$servicePath = Join-Path $repo `
    'Code/content/figures/HistoricalFigureService.cs'
$stateStorePath = Join-Path $repo `
    'Code/core/db/FigureStateTableItem.cs'
$pendingRecoveryPath = Join-Path $repo `
    'Code/core/db/FigureStatePendingRecovery.cs'
$gitIgnorePath = Join-Path $repo '.gitignore'
$localePath = Join-Path $repo 'Locales/aw3_historical_figures.csv'
$toggleLocalePath = Join-Path $repo 'Locales/others.csv'

$displayCompileSupport = @'
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
    ('aw3_historical_display_test_' + [guid]::NewGuid().ToString('N') + '.cs')
try {
    [IO.File]::WriteAllText($supportPath, $displayCompileSupport,
        [Text.UTF8Encoding]::new($false))
    Add-Type -Path $supportPath, $definitionPath, $rulesPath, `
        $displayRulesPath, $givenNameRulesPath
} finally {
    Remove-Item -LiteralPath $supportPath -Force -ErrorAction SilentlyContinue
}

$expectedRows = @'
0|0|aw_figure_ji_fa|姬发|姬|姬|发|周|周|-1046
1|1|aw_figure_ying_zheng|嬴政|嬴|赵|政|秦|秦|-221
2|2|aw_figure_liu_bang|刘邦|刘|刘|邦|漢|漢|-202
3|7|aw_figure_cao_pi|曹丕|曹|曹|丕|魏|魏|220
4|10|aw_figure_sima_yan|司马炎|司马|司马|炎|晋|晋|266
5|3|aw_figure_wang_mang|王莽|王|王|莽|新|新|9
6|5|aw_figure_liu_xiu|刘秀|刘|刘|秀|漢|漢|25
7|8|aw_figure_liu_bei|刘备|刘|刘|备|漢|漢|221
8|9|aw_figure_sun_quan|孙权|孙|孙|权|吴|吴|229
9|11|aw_figure_zhang_gui|张轨|张|张|轨|凉|凉|301
10|12|aw_figure_liu_yuan|刘渊|刘|刘|渊|漢|漢|304
11|13|aw_figure_li_xiong|李雄|李|李|雄|漢|漢|304
12|14|aw_figure_sima_rui|司马睿|司马|司马|睿|晋|晋|317
13|15|aw_figure_shi_le|石勒|石|石|勒|赵|赵|319
14|16|aw_figure_murong_huang|慕容皝|慕容|慕容|皝|燕|燕|337
15|18|aw_figure_fu_jian_351|苻健|苻|苻|健|秦|秦|351
16|21|aw_figure_yao_chang|姚苌|姚|姚|苌|秦|秦|384
17|22|aw_figure_qifu_guoren|乞伏国仁|乞伏|乞伏|国仁|秦|秦|385
18|19|aw_figure_murong_chui|慕容垂|慕容|慕容|垂|燕|燕|384
19|23|aw_figure_lu_guang|吕光|吕|吕|光|凉|凉|386
20|24|aw_figure_tuoba_gui|拓跋珪|拓跋|拓跋|珪|魏|魏|386
21|25|aw_figure_tufa_wugu|秃发乌孤|秃发|秃发|乌孤|凉|凉|397
22|26|aw_figure_murong_de|慕容德|慕容|慕容|德|燕|燕|398
23|27|aw_figure_li_gao|李暠|李|李|暠|凉|凉|400
24|28|aw_figure_juqu_mengxun|沮渠蒙逊|沮渠|沮渠|蒙逊|凉|凉|401
25|30|aw_figure_helian_bobo|赫连勃勃|赫连|赫连|勃勃|胡夏|夏|407
26|31|aw_figure_feng_ba|冯跋|冯|冯|跋|燕|燕|409
27|32|aw_figure_liu_yu|刘裕|刘|刘|裕|刘宋|宋|420
28|33|aw_figure_xiao_daocheng|萧道成|萧|萧|道成|齐|齐|479
29|34|aw_figure_xiao_yan|萧衍|萧|萧|衍|梁|梁|502
30|35|aw_figure_gao_huan|高欢|高|高|欢|魏|魏|534
31|36|aw_figure_yuwen_tai|宇文泰|宇文|宇文|泰|魏|魏|535
32|37|aw_figure_gao_yang|高洋|高|高|洋|齐|齐|550
33|40|aw_figure_yuwen_jue|宇文觉|宇文|宇文|觉|周|周|557
34|41|aw_figure_chen_baxian|陈霸先|陈|陈|霸先|陈|陈|557
35|42|aw_figure_yang_jian|杨坚|杨|杨|坚|隋|隋|581
36|44|aw_figure_lin_shihong|林士弘|林|林|士弘|林楚|楚|616
37|45|aw_figure_xue_ju|薛举|薛|薛|举|薛秦|秦|617
38|46|aw_figure_liu_wuzhou|刘武周|刘|刘|武周|定杨|定杨|617
39|47|aw_figure_liang_shidu|梁师都|梁|梁|师都|梁|梁|617
40|48|aw_figure_xiao_xian|萧铣|萧|萧|铣|萧梁|梁|617
41|49|aw_figure_li_mi|李密|李|李|密|瓦岗魏|魏|617
42|50|aw_figure_dou_jiande|窦建德|窦|窦|建德|窦夏|夏|617
43|51|aw_figure_li_gui|李轨|李|李|轨|李凉|凉|617
44|52|aw_figure_zhu_can|朱粲|朱|朱|粲|朱楚|楚|617
45|53|aw_figure_yuwen_huaji|宇文化及|宇文|宇文|化及|许|许|618
46|54|aw_figure_li_yuan|李渊|李|李|渊|唐|唐|618
47|55|aw_figure_wang_shichong|王世充|王|王|世充|郑|郑|619
48|56|aw_figure_li_zitong|李子通|李|李|子通|吴|吴|619
49|57|aw_figure_shen_faxing|沈法兴|沈|沈|法兴|梁|梁|619
50|58|aw_figure_gao_kaidao|高开道|高|高|开道|燕|燕|619
51|60|aw_figure_fu_gongshi|辅公祏|辅|辅|公祏|宋|宋|623
52|61|aw_figure_wu_zhao|武曌|武|武|曌|周|周|690
53|70|aw_figure_yang_xingmi|杨行密|杨|杨|行密|吴|吴|902
54|71|aw_figure_zhu_wen|朱温|朱|朱|温|梁|梁|907
55|72|aw_figure_wang_jian|王建|王|王|建|蜀|蜀|907
56|73|aw_figure_qian_liu|钱镠|钱|钱|镠|吴越|吴越|907
57|74|aw_figure_ma_yin|马殷|马|马|殷|马楚|楚|907
58|75|aw_figure_wang_shenzhi|王审知|王|王|审知|闽|闽|909
59|78|aw_figure_liu_yan|刘岩|刘|刘|岩|漢|漢|917
60|79|aw_figure_li_cunxu|李存勖|李|李|存勖|唐|唐|923
61|80|aw_figure_gao_jixing|高季兴|高|高|季兴|荆|荆|924
62|81|aw_figure_meng_zhixiang|孟知祥|孟|孟|知祥|蜀|蜀|934
63|82|aw_figure_shi_jingtang|石敬瑭|石|石|敬瑭|晋|晋|936
64|84|aw_figure_li_bian|李昪|李|李|昪|唐|唐|937
65|85|aw_figure_liu_zhiyuan|刘知远|刘|刘|知远|漢|漢|947
66|86|aw_figure_guo_wei|郭威|郭|郭|威|周|周|951
67|87|aw_figure_liu_chong|刘崇|刘|刘|崇|漢|漢|951
68|88|aw_figure_zhao_kuangyin|赵匡胤|赵|赵|匡胤|宋|宋|960
69|43|aw_figure_du_fuwei|杜伏威|杜|杜|伏威|杜吴|吴|613
70|59|aw_figure_xu_yuanlang|徐圆朗|徐|徐|圆朗|徐鲁|鲁|621
71|4|aw_figure_gongsun_shu|公孙述|公孙|公孙|述|成家|成|25
72|6|aw_figure_yuan_shu|袁术|袁|袁|术|仲氏|仲|197
73|17|aw_figure_ran_min|冉闵|冉|冉|闵|冉魏|魏|350
74|20|aw_figure_murong_hong|慕容泓|慕容|慕容|泓|燕|燕|384
75|29|aw_figure_huan_xuan|桓玄|桓|桓|玄|桓楚|楚|403
76|38|aw_figure_hou_jing|侯景|侯|侯|景|侯漢|漢|551
77|39|aw_figure_xiao_cha|萧詧|萧|萧|詧|梁|梁|555
78|62|aw_figure_da_zuorong|大祚荣|大|大|祚荣|渤海|渤海|698
79|63|aw_figure_pi_luoge|皮逻阁|皮|皮|逻阁|诏|诏|738
80|64|aw_figure_an_lushan|安禄山|安|安|禄山|燕|燕|756
81|65|aw_figure_zhu_ci|朱泚|朱|朱|泚|朱秦|秦|783
82|66|aw_figure_li_xilie|李希烈|李|李|希烈|楚|楚|784
83|67|aw_figure_huang_chao|黄巢|黄|黄|巢|大齐|齐|881
84|68|aw_figure_dong_chang|董昌|董|董|昌|大越罗平|越|895
85|69|aw_figure_li_maozhen|李茂贞|李|李|茂贞|岐|岐|901
86|76|aw_figure_liu_shouguang|刘守光|刘|刘|守光|燕|燕|911
87|77|aw_figure_yelu_abaoji|耶律阿保机|耶律|耶律|阿保机|辽|辽|916
88|83|aw_figure_duan_siping|段思平|段|段|思平|大理|大理|937
'@ -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object {
        $parts = $_ -split '\|'
        [pscustomobject]@{
            RegistryIndex = [int]$parts[0]
            SpawnOrder = [int]$parts[1]
            Id = $parts[2]
            Name = $parts[3]
            Family = $parts[4]
            Clan = $parts[5]
            Given = $parts[6]
            Dynasty = $parts[7]
            Kingdom = $parts[8]
            FoundingYear = [int]$parts[9]
        }
    }

function Assert-Equal([string]$name, $expected, $actual) {
    if ($expected -ne $actual) {
        throw "$name expected '$expected' but got '$actual'"
    }
}

function Assert-True([string]$name, [bool]$condition) {
    if (-not $condition) { throw "$name expected true" }
}

$definitionType = [AncientWarfare3.content.figures.HistoricalFigureDef]
$spawnRulesType = `
    [AncientWarfare3.content.figures.HistoricalFigureSpawnRules]
Assert-Equal 'durable persistence gates figure evaluation' $false `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        CanEvaluate($false))
Assert-Equal 'ready persistence permits figure evaluation' $true `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        CanEvaluate($true))
Assert-Equal 'failed reservation blocks actor mutation' $false `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        CanMutate($false))
Assert-Equal 'committed reservation permits actor mutation' $true `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        CanMutate($true))
Assert-Equal 'available spawn state is stable' 0 $spawnRulesType::Available
Assert-Equal 'committed spawn state remains legacy value one' 1 `
    $spawnRulesType::Committed
Assert-Equal 'pending spawn state is distinct' 2 $spawnRulesType::Pending
Assert-Equal 'legacy available state survives load' $spawnRulesType::Available `
    ($spawnRulesType::NormalizeLoadedSpawnState($spawnRulesType::Available))
Assert-Equal 'legacy committed state survives load' $spawnRulesType::Committed `
    ($spawnRulesType::NormalizeLoadedSpawnState($spawnRulesType::Committed))
Assert-Equal 'crash-left pending state recovers on load' $spawnRulesType::Available `
    ($spawnRulesType::NormalizeLoadedSpawnState($spawnRulesType::Pending))
Assert-Equal 'unknown persisted state recovers as available' `
    $spawnRulesType::Available `
    ($spawnRulesType::NormalizeLoadedSpawnState(99))
Assert-Equal 'reserve changes only available to pending' $spawnRulesType::Pending `
    ($spawnRulesType::ReserveSpawnState($spawnRulesType::Available))
Assert-Equal 'reserve cannot replace committed state' $spawnRulesType::Committed `
    ($spawnRulesType::ReserveSpawnState($spawnRulesType::Committed))
Assert-Equal 'commit changes only pending to committed' $spawnRulesType::Committed `
    ($spawnRulesType::CommitSpawnState($spawnRulesType::Pending))
Assert-Equal 'commit cannot bypass reservation' $spawnRulesType::Available `
    ($spawnRulesType::CommitSpawnState($spawnRulesType::Available))
Assert-Equal 'abort releases only pending state' $spawnRulesType::Available `
    ($spawnRulesType::AbortSpawnState($spawnRulesType::Pending))
Assert-Equal 'abort cannot reopen committed state' $spawnRulesType::Committed `
    ($spawnRulesType::AbortSpawnState($spawnRulesType::Committed))
Assert-Equal 'pending figure is not alive' $false `
    ($spawnRulesType::IsCommittedAlive($spawnRulesType::Pending, $false))
Assert-Equal 'committed non-dead figure is alive' $true `
    ($spawnRulesType::IsCommittedAlive($spawnRulesType::Committed, $false))
Assert-Equal 'committed dead figure is not alive' $false `
    ($spawnRulesType::IsCommittedAlive($spawnRulesType::Committed, $true))
foreach ($fieldName in @(
        'RegistryIndex', 'SpawnOrder', 'Order', 'Id', 'Key', 'FamilyName',
        'ClanName', 'GivenName', 'DynastyName', 'KingdomName',
        'NameLocaleKey', 'DynastyLocaleKey', 'FoundingYear', 'Sex',
        'RequiresIntegration', 'Chance')) {
    if ($null -eq $definitionType.GetField($fieldName)) {
        throw "historical founder definition missing stable field: $fieldName"
    }
}
Assert-True 'historical sex has a stable runtime projection rule' `
    ($null -ne $spawnRulesType.GetMethod('IsFemale'))
Assert-True 'spawn integration has a stable display-name projection rule' `
    ($null -ne $spawnRulesType.GetMethod('ShouldUseIntegratedName'))
Assert-True 'localized founder labels have a stable formatting rule' `
    ($null -ne $spawnRulesType.GetMethod('FormatLocalizedLabel'))

$all = [Array]$definitionType.GetField('All').GetValue($null)
$spawnSequence = [Array]$definitionType.GetField(
    'SpawnSequence').GetValue($null)
Assert-Equal 'registered founder count' $expectedRows.Count $all.Count
Assert-Equal 'spawn-sequence count' $expectedRows.Count $spawnSequence.Count
Assert-Equal 'declared catalog count' $expectedRows.Count `
    $definitionType.GetField('Count').GetRawConstantValue()

$ids = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
$registryIndexes = [Collections.Generic.HashSet[int]]::new()
$spawnOrders = [Collections.Generic.HashSet[int]]::new()
$byRegistry = @{}
foreach ($definition in $all) {
    Assert-True "unique stable id $($definition.Id)" $ids.Add($definition.Id)
    Assert-True "unique registry index $($definition.RegistryIndex)" `
        $registryIndexes.Add($definition.RegistryIndex)
    Assert-True "unique spawn order $($definition.SpawnOrder)" `
        $spawnOrders.Add($definition.SpawnOrder)
    Assert-Equal "legacy order alias $($definition.Id)" `
        $definition.RegistryIndex $definition.Order
    Assert-True "spawnable definition $($definition.Id)" `
        ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
            IsDefinitionSpawnable($definition.Id,
                $definition.RegistryIndex, $definition.SpawnOrder,
                $definition.Chance))
    Assert-True "integrated spawn gate $($definition.Id)" `
        ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
            CanAttemptDefinition($definition.RequiresIntegration,
                $true, $definition.Chance))
    $expectedSex = if ($definition.Id -eq 'aw_figure_wu_zhao') {
        'Female'
    } else {
        'Male'
    }
    Assert-Equal "historical sex $($definition.Id)" `
        $expectedSex $definition.Sex.ToString()
    Assert-Equal "runtime female projection $($definition.Id)" `
        ($expectedSex -eq 'Female') `
        ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
            IsFemale($definition.Sex))
    if ($definition.RequiresIntegration) {
        Assert-Equal "pre-integration gate $($definition.Id)" $false `
            ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
                CanAttemptDefinition($true, $false, $definition.Chance))
    }
    $byRegistry[$definition.RegistryIndex] = $definition
}

$wuZhao = $byRegistry[52]
$wuZhaoIntegratedName = `
    [AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        ShouldUseIntegratedName($wuZhao.RequiresIntegration, $true)
Assert-Equal 'integrated spawn preserves Wu Zhao name order' $wuZhao.Key `
    ([AncientWarfare3.core.lineage.LineageDisplayNameRules]::Build(
        $wuZhao.GivenName, $wuZhao.FamilyName, $wuZhao.ClanName,
        $true, $false, $wuZhaoIntegratedName))
Assert-Equal 'pre-integration spawn does not project an integrated name' `
    $false `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        ShouldUseIntegratedName($true, $false))
Assert-Equal 'pre-Qin founders retain pre-integration naming' $false `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        ShouldUseIntegratedName($false, $true))
$piLuoge = $byRegistry[79]
Assert-Equal 'Pi Luoge runtime projection preserves the canonical name' `
    $piLuoge.Key `
    ([AncientWarfare3.core.lineage.LineageDisplayNameRules]::Build(
        $piLuoge.GivenName, $piLuoge.FamilyName, $piLuoge.ClanName,
        $true, $true, $true))

$serviceSource = [IO.File]::ReadAllText($servicePath)
Assert-True 'service maps definition sex through the runtime projection' `
    $serviceSource.Contains(
        'HistoricalFigureSpawnRules.IsFemale(pDef.Sex)')
Assert-True 'service persists the accepted integration state before naming' `
    $serviceSource.Contains(
        'HistoricalFigureSpawnRules.ShouldUseIntegratedName(')
Assert-True 'world log consumes the founder name locale key' `
    $serviceSource.Contains('pDef.NameLocaleKey, pDef.Key')
Assert-True 'world log projects the canonical founder state name' `
    $serviceSource.Contains(
        'ProjectStateName(pDef.DynastyName, pDef.KingdomName)')
Assert-True 'world log consumes the founder dynasty locale key' `
    $serviceSource.Contains('pDef.DynastyLocaleKey, canonicalStateName')
Assert-Equal 'Chinese world log uses the canonical state name' '漢' `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        ProjectLocalizedStateName('漢', 'Western Han', $true))
Assert-Equal 'non-Chinese world log uses the localized dynasty name' `
    'Western Han' `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        ProjectLocalizedStateName('漢', 'Western Han', $false))
Assert-Equal 'missing dynasty localization falls back to canonical state' `
    '漢' `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        ProjectLocalizedStateName('漢', '', $false))
Assert-True 'world log uses the localized founder-label formatter' `
    $serviceSource.Contains('HistoricalFigureSpawnRules.FormatLocalizedLabel(')
Assert-Equal 'localized founder label includes name and dynasty' `
    'Wu Zhao · Wu Zhou' `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        FormatLocalizedLabel('Wu Zhao', 'Wu Zhou'))

foreach ($expected in $expectedRows) {
    Assert-True "registry contains $($expected.RegistryIndex)" `
        $byRegistry.ContainsKey($expected.RegistryIndex)
    $actual = $byRegistry[$expected.RegistryIndex]
    Assert-Equal "id registry $($expected.RegistryIndex)" `
        $expected.Id $actual.Id
    Assert-Equal "spawn order $($expected.Id)" `
        $expected.SpawnOrder $actual.SpawnOrder
    Assert-Equal "canonical name $($expected.Id)" $expected.Name $actual.Key
    Assert-Equal "family name $($expected.Id)" `
        $expected.Family $actual.FamilyName
    Assert-Equal "clan name $($expected.Id)" $expected.Clan $actual.ClanName
    Assert-Equal "given name $($expected.Id)" $expected.Given $actual.GivenName
    Assert-Equal "dynasty $($expected.Id)" $expected.Dynasty $actual.DynastyName
    Assert-Equal "kingdom $($expected.Id)" $expected.Kingdom $actual.KingdomName
    Assert-Equal "founding year $($expected.Id)" `
        $expected.FoundingYear $actual.FoundingYear
    Assert-Equal "name locale key $($expected.Id)" `
        $expected.Id $actual.NameLocaleKey
    Assert-Equal "dynasty locale key $($expected.Id)" `
        ($expected.Id + '_dynasty') $actual.DynastyLocaleKey
    Assert-Equal "registry lookup $($expected.Id)" $expected.Id `
        ($definitionType.GetMethod('Get').Invoke(
            $null, @($expected.RegistryIndex))).Id
}

$expectedSpawnIds = @($expectedRows |
    Sort-Object SpawnOrder | ForEach-Object { $_.Id })
$actualSpawnIds = @($spawnSequence | ForEach-Object { $_.Id })
Assert-Equal 'chronological spawn registry order' `
    ($expectedSpawnIds -join ',') ($actualSpawnIds -join ',')
for ($i = 1; $i -lt $spawnSequence.Count; $i++) {
    Assert-True "founding year is monotonic at order $i" `
        ($spawnSequence[$i - 1].FoundingYear -le
            $spawnSequence[$i].FoundingYear)
}

Assert-Equal 'legacy Cao Pi registry index' 'aw_figure_cao_pi' `
    $byRegistry[3].Id
Assert-Equal 'legacy Sima Yan registry index' 'aw_figure_sima_yan' `
    $byRegistry[4].Id
Assert-Equal 'legacy Cao Pi order' 3 $byRegistry[3].Order
Assert-Equal 'legacy Sima Yan order' 4 $byRegistry[4].Order

$registryOrder = [int[]]@($spawnSequence |
    ForEach-Object { $_.RegistryIndex })
$spawned = New-Object bool[] $expectedRows.Count
$dead = New-Object bool[] $expectedRows.Count
Assert-Equal 'empty history begins with Ji Fa' 0 `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        NextSpawnableRegistryIndex($registryOrder, $spawned, $dead))
foreach ($legacyIndex in @(0, 1, 2)) {
    $spawned[$legacyIndex] = $true
    $dead[$legacyIndex] = $true
}
Assert-Equal 'expanded history inserts Wang Mang after Liu Bang' 5 `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        NextSpawnableRegistryIndex($registryOrder, $spawned, $dead))
$spawned[5] = $true
Assert-Equal 'living founder blocks the next chronological founder' -1 `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        NextSpawnableRegistryIndex($registryOrder, $spawned, $dead))
$dead[5] = $true
Assert-Equal 'Wang Mang death unlocks Gongsun Shu' 71 `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        NextSpawnableRegistryIndex($registryOrder, $spawned, $dead))
$spawned[71] = $true
$dead[71] = $true
Assert-Equal 'Gongsun Shu death unlocks Liu Xiu' 6 `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        NextSpawnableRegistryIndex($registryOrder, $spawned, $dead))

$spawnStates = New-Object int[] $expectedRows.Count
$stateDead = New-Object bool[] $expectedRows.Count
$spawnStates[0] = $spawnRulesType::ReserveSpawnState($spawnStates[0])
Assert-Equal 'pending reservation blocks duplicate selection' -1 `
    ($spawnRulesType::NextSpawnableRegistryIndex(
        $registryOrder, $spawnStates, $stateDead))
$spawnStates[0] = $spawnRulesType::AbortSpawnState($spawnStates[0])
Assert-Equal 'aborted reservation makes the slot selectable again' 0 `
    ($spawnRulesType::NextSpawnableRegistryIndex(
        $registryOrder, $spawnStates, $stateDead))
$spawnStates[0] = $spawnRulesType::ReserveSpawnState($spawnStates[0])
$spawnStates[0] = $spawnRulesType::CommitSpawnState($spawnStates[0])
Assert-Equal 'committed living founder blocks chronological successor' -1 `
    ($spawnRulesType::NextSpawnableRegistryIndex(
        $registryOrder, $spawnStates, $stateDead))
$stateDead[0] = $true
Assert-Equal 'committed founder death unlocks chronological successor' 1 `
    ($spawnRulesType::NextSpawnableRegistryIndex(
        $registryOrder, $spawnStates, $stateDead))

Assert-True 'pre-Qin founder can spawn before surname integration' `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        CanAttemptDefinition($false, $false, 0.8))
Assert-Equal 'zero probability is never spawnable' $false `
    ([AncientWarfare3.content.figures.HistoricalFigureSpawnRules]::
        IsDefinitionSpawnable('aw_figure_invalid', 69, 69, 0.0))

if (-not (Test-Path $localePath)) {
    throw 'historical founder locale catalog is missing'
}
$localeRows = @(Import-Csv -Encoding UTF8 $localePath)
$localeByKey = @{}
foreach ($row in $localeRows) {
    Assert-True "unique locale key $($row.key)" `
        (-not $localeByKey.ContainsKey($row.key))
    $localeByKey[$row.key] = $row
}
foreach ($expected in $expectedRows) {
    $dynastyKey = $expected.Id + '_dynasty'
    foreach ($key in @($expected.Id, $dynastyKey)) {
        Assert-True "locale contains $key" $localeByKey.ContainsKey($key)
        Assert-True "locale English is populated for $key" `
            (-not [string]::IsNullOrWhiteSpace($localeByKey[$key].en))
        Assert-True "locale traditional Chinese is populated for $key" `
            (-not [string]::IsNullOrWhiteSpace($localeByKey[$key].ch))
    }
    Assert-Equal "locale canonical name $($expected.Id)" `
        $expected.Name $localeByKey[$expected.Id].cz
    Assert-True "locale dynasty is populated $($expected.Id)" `
        (-not [string]::IsNullOrWhiteSpace($localeByKey[$dynastyKey].cz))
}

$toggleLocale = [IO.File]::ReadAllText($toggleLocalePath)
Assert-True 'toggle description describes the expanded founder sequence' `
    $toggleLocale.Contains('开关历代开国君主按时代顺序降临')

$gitIgnoreLines = [IO.File]::ReadAllLines($gitIgnorePath)
Assert-True 'founder catalog regression test is trackable' `
    ($gitIgnoreLines -contains `
        '!/Tests/HistoricalFigureFounderCatalogTests.ps1')
Assert-True 'figure SQLite regression source is trackable' `
    ($gitIgnoreLines -contains `
        '!/Tests/SQLiteHelperSchemaMigrationTests.cs.txt')
Assert-True 'figure SQLite regression project is trackable' `
    ($gitIgnoreLines -contains `
        '!/Tests/SQLiteHelperSchemaMigrationTests.csproj')

$stateStoreSource = [IO.File]::ReadAllText($stateStorePath)
Assert-True 'state store exposes reservation' `
    $stateStoreSource.Contains('TryReserveSpawn(')
Assert-True 'state store exposes commit' `
    $stateStoreSource.Contains('TryCommitSpawn(')
Assert-True 'state store exposes abort' `
    $stateStoreSource.Contains('TryAbortSpawn(')
Assert-True 'state store persists pending lineage ownership' `
    ($stateStoreSource.Contains('pending_lineage_id') -and
     $stateStoreSource.Contains('pending_shi_id') -and
     $stateStoreSource.Contains('TryBindPendingLineage('))
Assert-True 'pending owner columns migrate old rows as unowned' `
    ($stateStoreSource.Contains(
        '[TableItemDef(pDefaultValue: "-1")] public long pending_lineage_id') -and
     $stateStoreSource.Contains(
        '[TableItemDef(pDefaultValue: "-1")] public long pending_shi_id'))
Assert-True 'reservation compare-and-sets only available rows' `
    $stateStoreSource.Contains('AND SPAWNED=0')
Assert-True 'commit and abort compare-and-set the reserved actor' `
    ($stateStoreSource.Contains('WHERE FIGURE_INDEX=@index AND SPAWNED=2') -and
     $stateStoreSource.Contains('AND ACTOR_ID=@actor'))
$pendingRecoverySource = if (Test-Path $pendingRecoveryPath) {
    [IO.File]::ReadAllText($pendingRecoveryPath)
} else { '' }
Assert-True 'load repairs crash-left pending rows' `
    ($stateStoreSource.Contains('FigureStatePendingRecovery.Recover(') -and
     $pendingRecoverySource.Contains('WHERE SPAWNED=2') -and
     $pendingRecoverySource.Contains('ACTOR_ID=-1'))
Assert-True 'stale pending cleanup owns the exact shi row' `
    ($pendingRecoverySource.Contains('SHI_ID=@shi') -and
     $pendingRecoverySource.Contains('LINEAGE_ID=@lineage') -and
     $pendingRecoverySource.Contains('FOUNDER_ACTOR_ID=@actor') -and
     $pendingRecoverySource.Contains('SOURCE_TYPE=@source'))
Assert-True 'stale pending cleanup owns the exact figure reservation' `
    ($pendingRecoverySource.Contains('FIGURE_INDEX=@index') -and
     $pendingRecoverySource.Contains('PENDING_LINEAGE_ID=@lineage') -and
     $pendingRecoverySource.Contains('PENDING_SHI_ID=@shi'))
Assert-True 'commit and abort clear pending ownership' `
    ($stateStoreSource.Contains(
        'SET SPAWNED=1,PENDING_LINEAGE_ID=-1,') -and
     $pendingRecoverySource.Contains(
        'PENDING_LINEAGE_ID=-1,PENDING_SHI_ID=-1'))

$reserveAt = $serviceSource.IndexOf('FigureStateStore.TryReserveSpawn(')
$commitAt = $serviceSource.IndexOf('FigureStateStore.TryCommitSpawn(')
$abortAt = $serviceSource.IndexOf('FigureStateStore.TryAbortSpawn(')
$bindAt = $serviceSource.IndexOf('FigureStateStore.TryBindPendingLineage(')
$promotionAt = $serviceSource.IndexOf('LineageService.OnActorPromoted(')
$announceAt = $serviceSource.IndexOf('AnnounceFigure(pActor, pDef)')
$historyAt = $serviceSource.IndexOf('HistoryWriter.RecordPerson(')
Assert-True 'service reserves before initialization' `
    ($reserveAt -ge 0 -and $commitAt -gt $reserveAt)
Assert-True 'service has an initialization abort path' `
    ($abortAt -gt $reserveAt)
Assert-True 'service binds pending lineage before creating rows' `
    ($bindAt -gt $reserveAt -and
     $bindAt -lt $serviceSource.IndexOf('LineageService.InsertLineageGroup('))
Assert-True 'promotion side effects occur only after commit catch is closed' `
    ($promotionAt -gt $commitAt -and $promotionAt -gt $abortAt)
Assert-True 'promotion failure cannot abort a committed figure' `
    ($serviceSource.IndexOf('FigureStateStore.TryAbortSpawn(',
        $promotionAt) -eq -1)
Assert-True 'promotion failure is queued for runtime repair' `
    ($serviceSource.Contains('DeferredRuntimeWorkService.EnqueueCoalesced(') -and
     $serviceSource.Contains('historical_figure_promotion'))
Assert-True 'world announcement occurs only after commit' `
    ($announceAt -gt $commitAt)
Assert-True 'history write occurs only after commit' `
    ($historyAt -gt $commitAt)
Assert-True 'initialization failure restores actor state' `
    $serviceSource.Contains('snapshot.Restore(pActor)')
Assert-True 'initialization abort atomically removes owned lineage rows' `
    $stateStoreSource.Contains('FigureStatePendingRecovery.TryAbort(')

Write-Output 'Historical founder catalog tests passed.'
