using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.content.figures
{
    /// <summary>
    /// The player card pool. Legacy figures are projected into this catalogue,
    /// while the emperor directory adds identities that are not auto-spawn slots.
    /// </summary>
public static class HistoricalFigureCardCatalog
{
    public const int UnknownYear = int.MinValue;
    private const int MinimumBlueMinisterCards = 8;

        private static readonly Dictionary<string, string> LegacyCardIds =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aw_figure_ying_zheng"] = "qin_shihuang",
                ["aw_figure_liu_bang"] = "han_gaozu",
                ["aw_figure_cao_pi"] = "wei_wendi",
                ["aw_figure_sima_yan"] = "jin_wudi",
                ["aw_figure_wang_mang"] = "xin_wangmang",
                ["aw_figure_liu_xiu"] = "han_guangwu",
                ["aw_figure_liu_bei"] = "shu_han_zhaolie",
                ["aw_figure_sun_quan"] = "wu_dadi",
                ["aw_figure_sima_rui"] = "jin_yuandi",
                ["aw_figure_liu_yu"] = "song_wudi",
                ["aw_figure_xiao_daocheng"] = "qi_gaodi",
                ["aw_figure_xiao_yan"] = "liang_wudi",
                ["aw_figure_chen_baxian"] = "chen_wudi",
                ["aw_figure_yuwen_jue"] = "zhou_xiaomin",
                ["aw_figure_yang_jian"] = "sui_wendi",
                ["aw_figure_li_yuan"] = "tang_gaozu",
                ["aw_figure_wu_zhao"] = "zhou_wuzetian",
                ["aw_figure_zhu_wen"] = "later_liang_taizu",
                ["aw_figure_li_cunxu"] = "later_tang_zhuangzong",
                ["aw_figure_shi_jingtang"] = "later_jin_gaozu",
                ["aw_figure_liu_zhiyuan"] = "later_han_gaozu",
                ["aw_figure_guo_wei"] = "later_zhou_taizu",
                ["aw_figure_zhao_kuangyin"] = "song_taizu",
                ["aw_figure_yelu_abaoji"] = "liao_taizu",
                ["aw_figure_nurhaci"] = "qing_taizu",
                ["aw_figure_gongsun_shu"] = "chengjia_gongsun_shu",
                ["aw_figure_yuan_shu"] = "zhong_hou_shu",
                ["aw_figure_li_zicheng"] = "dashun_li_zicheng",
                ["aw_figure_tuoba_gui"] = "beiwei_taizu",
                ["aw_figure_gao_yang"] = "beiqi_wenxuan",
                ["aw_figure_li_bian"] = "nantang_liezu"
            };

        private static readonly Dictionary<string, string[]> LegacyParents =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["aw_figure_ji_fa"] = new[] { "姬昌", "太姒" },
                ["aw_figure_ying_zheng"] = new[] { "嬴异人", "赵姬" },
                ["aw_figure_liu_bang"] = new[] { "刘煓", "刘媪" },
                ["aw_figure_cao_pi"] = new[] { "曹操", "卞氏" },
                ["aw_figure_sima_yan"] = new[] { "司马昭", "王元姬" },
                ["aw_figure_wang_mang"] = new[] { "王曼", "渠氏" },
                ["aw_figure_liu_xiu"] = new[] { "刘钦", "樊娴都" },
                ["aw_figure_liu_bei"] = new[] { "刘弘", "" },
                ["aw_figure_sun_quan"] = new[] { "孙坚", "吴夫人" },
                ["aw_figure_sima_rui"] = new[] { "司马觐", "夏侯光姬" },
                ["aw_figure_liu_yu"] = new[] { "刘翘", "赵安宗" },
                ["aw_figure_xiao_daocheng"] = new[] { "萧承之", "陈道止" },
                ["aw_figure_xiao_yan"] = new[] { "萧顺之", "张尚柔" },
                ["aw_figure_chen_baxian"] = new[] { "陈文赞", "董氏" },
                ["aw_figure_yang_jian"] = new[] { "杨忠", "吕苦桃" },
                ["aw_figure_li_yuan"] = new[] { "李昞", "独孤氏" },
                ["aw_figure_wu_zhao"] = new[] { "武士彟", "杨氏" },
                ["aw_figure_zhu_wen"] = new[] { "朱诚", "王氏" },
                ["aw_figure_li_cunxu"] = new[] { "李克用", "曹氏" },
                ["aw_figure_shi_jingtang"] = new[] { "石绍雍", "何氏" },
                ["aw_figure_liu_zhiyuan"] = new[] { "刘琠", "安氏" },
                ["aw_figure_guo_wei"] = new[] { "郭简", "王氏" },
                ["aw_figure_zhao_kuangyin"] = new[] { "赵弘殷", "杜氏" },
                ["aw_figure_yelu_abaoji"] = new[] { "耶律撒剌的", "萧岩母斤" },
                ["aw_figure_nurhaci"] = new[] { "塔克世", "喜塔腊氏" },
                ["aw_figure_gongsun_shu"] = new[] { "公孙仁", "" },
                ["aw_figure_yuan_shu"] = new[] { "袁逢", "" },
                ["aw_figure_li_zicheng"] = new[] { "李守忠", "" },
                ["aw_figure_tuoba_gui"] = new[] { "拓跋寔", "贺氏" },
                ["aw_figure_gao_yang"] = new[] { "高欢", "娄昭君" },
                ["aw_figure_li_bian"] = new[] { "李荣", "" }
            };

        private static readonly Dictionary<string, int> FameByCardId =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["qin_shihuang"] = 100,
                ["han_gaozu"] = 96,
                ["han_wudi"] = 97,
                ["han_guangwu"] = 94,
                ["tang_taizong"] = 99,
                ["song_taizu"] = 92,
                ["ming_taizu"] = 96,
                ["qing_shengzu"] = 93,
                ["qing_gaozong"] = 90,
                ["yuan_shizu"] = 94,
                ["zhou_wuzetian"] = 91,
                ["sui_wendi"] = 89,
                ["wei_wendi"] = 82,
                ["jin_wudi"] = 86,
                ["later_liang_taizu"] = 78,
                ["liao_taizu"] = 84,
                ["qing_taizu"] = 87
            };

        private static readonly Dictionary<string, string> PortraitPathByCardId =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ming_taizu"] = "ui/historical_cards/ming_taizu",
                ["mengpai"] = "ui/historical_cards/mengpai"
            };

        public static readonly IReadOnlyList<HistoricalFigureCardDefinition> All =
            BuildAll();

        public static readonly IReadOnlyList<string> ValidationIssues =
            Validate(All);

        public static bool IsValid => ValidationIssues.Count == 0;

        public static HistoricalFigureCardDefinition Get(string pCardId)
        {
            if (string.IsNullOrWhiteSpace(pCardId)) return null;
            return All.FirstOrDefault(p => p != null &&
                string.Equals(p.CardId, pCardId.Trim(), StringComparison.Ordinal));
        }

        public static IReadOnlyList<HistoricalFigureCardDefinition> GetCards(
            string pCrateId)
        {
            HistoricalFigureCardCrate crate = HistoricalFigureCardCrates.Get(pCrateId);
            if (crate == null) return Array.Empty<HistoricalFigureCardDefinition>();
            return SortForDisplay(All.Where(p => p != null &&
                (string.Equals(p.CollectionId, crate.Id,
                    StringComparison.Ordinal) ||
                 string.IsNullOrEmpty(p.CollectionId) &&
                    crate.ContainsYear(p.HistoricalYear)))).ToArray();
        }

        public static IReadOnlyList<HistoricalFigureCardDefinition> GetCards(
            string pCrateId, HistoricalFigureCardRole pRole)
        {
            return GetCards(pCrateId).Where(p => p != null &&
                p.Role == pRole).ToArray();
        }

        public static IReadOnlyList<HistoricalFigureCardDefinition> SortForDisplay(
            IEnumerable<HistoricalFigureCardDefinition> pCards)
        {
            return (pCards ?? Enumerable.Empty<HistoricalFigureCardDefinition>())
                .Where(p => p != null)
                .OrderByDescending(p => p.FameScore)
                .ThenBy(p => p.HistoricalYear < 0 ? int.MaxValue : p.HistoricalYear)
                .ThenBy(p => p.CardId, StringComparer.Ordinal)
                .ToArray();
        }

        public static string ParentDisplayName(string pName)
        {
            return string.IsNullOrWhiteSpace(pName) ? "史料不详" : pName;
        }

        public static bool HasGeographicPrefix(string pName)
        {
            if (string.IsNullOrWhiteSpace(pName)) return false;
            string value = pName.Trim();
            return value.StartsWith("前", StringComparison.Ordinal) ||
                   value.StartsWith("后", StringComparison.Ordinal) ||
                   value.StartsWith("後", StringComparison.Ordinal) ||
                   value.StartsWith("东", StringComparison.Ordinal) ||
                   value.StartsWith("東", StringComparison.Ordinal) ||
                   value.StartsWith("西", StringComparison.Ordinal) ||
                   value.StartsWith("南", StringComparison.Ordinal) ||
                   value.StartsWith("北", StringComparison.Ordinal);
        }

        internal static string NormalizeShortKingdomName(string pName)
        {
            string value = (pName ?? "").Trim();
            while (value.Length > 1 && HasGeographicPrefix(value))
                value = value.Substring(1);
            if (value.StartsWith("刘", StringComparison.Ordinal) && value.Length > 1)
                value = value.Substring(1);
            if (value.StartsWith("林", StringComparison.Ordinal) && value.Length > 1)
                value = value.Substring(1);
            if (value.StartsWith("萧", StringComparison.Ordinal) && value.Length > 1)
                value = value.Substring(1);
            if (value.StartsWith("瓦岗", StringComparison.Ordinal) && value.Length > 2)
                value = value.Substring(2);
            if (value.StartsWith("大", StringComparison.Ordinal) && value.Length > 1 &&
                (value == "大齐" || value == "大顺" || value == "大越"))
                value = value.Substring(1);
            return value;
        }

        private static IReadOnlyList<HistoricalFigureCardDefinition> BuildAll()
        {
            var cards = new Dictionary<string, HistoricalFigureCardDefinition>(
                StringComparer.Ordinal);
            foreach (HistoricalFigureDef figure in HistoricalFigureDef.All)
            {
                string cardId = LegacyCardId(figure);
                string[] parents;
                LegacyParents.TryGetValue(figure.Id, out parents);
                int fame = FameByCardId.TryGetValue(cardId, out int knownFame)
                    ? knownFame
                    : LegacyFame(figure);
                cards[cardId] = new HistoricalFigureCardDefinition(
                    cardId, figure.Key, figure.FamilyName, figure.ClanName,
                    figure.GivenName, figure.DynastyName,
                    NormalizeShortKingdomName(figure.KingdomName),
                    figure.DynastyName, UnknownYear, UnknownYear,
                    figure.FoundingYear, fame,
                    RarityForFame(fame), figure.Sex,
                    figure.Key + "，历史人物。", "", ParentAt(parents, 0), "",
                    ParentAt(parents, 1), "", figure.Id, figure.RegistryIndex,
                    figure.CombatHealth, figure.CombatTraits,
                    BuildBackgroundSummary(figure.Key, figure.DynastyName,
                        NormalizeShortKingdomName(figure.KingdomName),
                        figure.FoundingYear, cardId),
                    BuildDetailedBiography(cardId, figure.Key, figure.DynastyName,
                        NormalizeShortKingdomName(figure.KingdomName),
                        figure.FoundingYear, figure.Key + "\uff0c\u5386\u53f2\u4eba\u7269\u3002"),
                    RoleForCard(cardId));
            }

            foreach (EmperorSeed seed in EmperorSeeds())
            {
                HistoricalFigureCardDefinition legacy;
                cards.TryGetValue(seed.CardId, out legacy);
                cards[seed.CardId] = seed.ToDefinition(legacy);
            }
            foreach (HistoricalFigureCardDefinition minister in
                     HistoricalFigureCardMinisterSeeds.All)
            {
                cards[minister.CardId] = minister;
            }
            foreach (HistoricalFigureCardDefinition supporter in
                     HistoricalFigureCardSupporterSeeds.All)
            {
                cards[supporter.CardId] = supporter;
            }
            return SortForDisplay(cards.Values).ToArray();
        }

        private static string LegacyCardId(HistoricalFigureDef pFigure)
        {
            if (pFigure == null) return "";
            if (LegacyCardIds.TryGetValue(pFigure.Id, out string cardId))
                return cardId;
            const string prefix = "aw_figure_";
            return pFigure.Id.StartsWith(prefix, StringComparison.Ordinal)
                ? pFigure.Id.Substring(prefix.Length)
                : pFigure.Id;
        }

        private static int LegacyFame(HistoricalFigureDef pFigure)
        {
            if (pFigure == null) return 0;
            if (pFigure.Id == "aw_figure_ji_fa") return 88;
            if (pFigure.Id == "aw_figure_liu_bei") return 86;
            if (pFigure.Id == "aw_figure_zhu_wen") return 78;
            return 45;
        }

        private static string BuildBackgroundSummary(string pName,
            string pDynasty, string pKingdom, int pHistoricalYear,
            string pCardId = "")
        {
            return HistoricalFigureCardNarratives.Background(pCardId, pName,
                pDynasty, pKingdom);
        }

        private static string BuildDetailedBiography(string pCardId, string pName,
            string pDynasty, string pKingdom, int pHistoricalYear,
            string pBiography)
        {
            return HistoricalFigureCardNarratives.Detailed(
                pCardId, pName, pDynasty, pKingdom, pBiography);
        }

        private static HistoricalFigureCardRarity RarityForFame(int pFame)
        {
            if (pFame >= 98) return HistoricalFigureCardRarity.Gold;
            if (pFame >= 90) return HistoricalFigureCardRarity.Red;
            if (pFame >= 75) return HistoricalFigureCardRarity.Pink;
            if (pFame >= 55) return HistoricalFigureCardRarity.Purple;
            return HistoricalFigureCardRarity.Blue;
        }

        private static string ParentAt(string[] pParents, int pIndex)
        {
            return pParents != null && pIndex < pParents.Length
                ? pParents[pIndex] ?? ""
                : "";
        }

        private static IReadOnlyList<string> Validate(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards)
        {
            var issues = new List<string>();
            if (pCards == null || pCards.Count == 0)
            {
                issues.Add("card catalogue is empty");
                return issues;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (HistoricalFigureCardDefinition card in pCards)
            {
                if (card == null)
                {
                    issues.Add("null card definition");
                    continue;
                }
                if (string.IsNullOrEmpty(card.CardId) || !ids.Add(card.CardId))
                    issues.Add("duplicate or empty card id: " + card.CardId);
                if (string.IsNullOrEmpty(card.DisplayName))
                    issues.Add("empty display name: " + card.CardId);
                if (string.IsNullOrEmpty(card.HistoricalKingdomName))
                    issues.Add("empty historical kingdom: " + card.CardId);
                if (HasGeographicPrefix(card.HistoricalKingdomName))
                    issues.Add("geographic prefix in kingdom: " + card.CardId);
                if (card.Rarity == null || !HistoricalFigureCardRarity.All.Contains(card.Rarity))
                    issues.Add("invalid rarity: " + card.CardId);
                if (card.FameScore < 0 || card.FameScore > 100)
                    issues.Add("invalid fame score: " + card.CardId);
                if (!string.Equals(card.CollectionId,
                        HistoricalFigureCardSupporterSeeds.CollectionId,
                        StringComparison.Ordinal) &&
                    !HistoricalFigureCardNarratives.HasDetailed(card.CardId))
                    issues.Add("missing curated biography: " + card.CardId);
                if (card.Role == HistoricalFigureCardRole.Minister)
                {
                    if (card.MinisterType == HistoricalFigureCardMinisterType.None)
                        issues.Add("minister has no subtype: " + card.CardId);
                    HistoricalFigureCardCrate collection =
                        HistoricalFigureCardCrates.Get(card.CollectionId);
                    if (collection == null)
                        issues.Add("invalid minister collection: " + card.CardId);
                    if (string.IsNullOrWhiteSpace(card.BackgroundSummary) ||
                        string.IsNullOrWhiteSpace(card.DetailedBiography))
                        issues.Add("incomplete minister biography: " + card.CardId);
                }
                else if (card.MinisterType != HistoricalFigureCardMinisterType.None)
                {
                    issues.Add("non-minister has minister subtype: " + card.CardId);
                }
                if (!card.ParentReferencesAreValid(pCards))
                    issues.Add("invalid parent reference: " + card.CardId);
                if (string.Equals(card.DisplayName, card.FatherDisplayName,
                        StringComparison.Ordinal) ||
                    string.Equals(card.DisplayName, card.MotherDisplayName,
                        StringComparison.Ordinal))
                    issues.Add("self parent reference: " + card.CardId);
                if (IsKnownYear(card.BirthYear) && IsKnownYear(card.DeathYear) &&
                    card.BirthYear > card.DeathYear)
                    issues.Add("invalid life span: " + card.CardId);
                if (IsKnownYear(card.BirthYear) &&
                    card.HistoricalYear < card.BirthYear)
                    issues.Add("historical year before birth: " + card.CardId);
                if (IsKnownYear(card.DeathYear) &&
                    card.HistoricalYear > card.DeathYear)
                    issues.Add("historical year after death: " + card.CardId);
            }
            foreach (HistoricalFigureCardCrate crate in
                     HistoricalFigureCardCrates.All)
            {
                HistoricalFigureCardDefinition[] ministers = pCards.Where(card =>
                    card != null && card.Role == HistoricalFigureCardRole.Minister &&
                    IsCardInCrate(card, crate)).ToArray();
                if (string.Equals(crate.Id,
                        HistoricalFigureCardSupporterSeeds.CollectionId,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                if (ministers.Length < 40)
                    issues.Add("minister pool below minimum: " + crate.Id);
                int blueCount = ministers.Count(card => card.Rarity != null &&
                    card.Rarity.Equals(HistoricalFigureCardRarity.Blue));
                if (blueCount < MinimumBlueMinisterCards)
                    issues.Add("minister pool below blue rarity minimum: " +
                        crate.Id);
                if (!ministers.Any(card => card.MinisterType ==
                        HistoricalFigureCardMinisterType.CivilOfficial))
                    issues.Add("minister pool has no civil officials: " + crate.Id);
                if (!ministers.Any(card => card.MinisterType ==
                        HistoricalFigureCardMinisterType.MilitaryGeneral))
                    issues.Add("minister pool has no military generals: " + crate.Id);
            }
            if (Math.Abs(HistoricalFigureCardRarity.TotalProbability - 1f) > 0.00001f)
                issues.Add("rarity probabilities do not total one");
            return issues;
        }

        private static bool IsCardInCrate(
            HistoricalFigureCardDefinition pCard,
            HistoricalFigureCardCrate pCrate)
        {
            if (pCard == null || pCrate == null) return false;
            return string.Equals(pCard.CollectionId, pCrate.Id,
                       StringComparison.Ordinal) ||
                   string.IsNullOrEmpty(pCard.CollectionId) &&
                   pCrate.ContainsYear(pCard.HistoricalYear);
        }

        private static bool IsKnownYear(int pYear)
        {
            return pYear != UnknownYear;
        }

        private sealed class EmperorSeed
        {
            public readonly string CardId;
            public readonly string DisplayName;
            public readonly string DynastyName;
            public readonly string HistoricalKingdomName;
            public readonly int BirthYear;
            public readonly int DeathYear;
            public readonly int HistoricalYear;
            public readonly int FameScore;
            public readonly string FatherDisplayName;
            public readonly string MotherDisplayName;
            public readonly string Biography;
            public readonly string BackgroundSummary;
            public readonly string DetailedBiography;
            public readonly HistoricalFigureSex Sex;
            public readonly string FamilyName;
            public readonly string ClanName;
            public readonly string GivenName;
            public readonly string HistoricalEra;

            public EmperorSeed(string pCardId, string pDisplayName,
                string pDynastyName, string pHistoricalKingdomName,
                int pBirthYear, int pDeathYear, int pHistoricalYear,
                int pFameScore, string pFatherDisplayName,
                string pMotherDisplayName, string pBiography = "",
                HistoricalFigureSex pSex = HistoricalFigureSex.Male,
                string pFamilyName = "", string pClanName = "",
                string pGivenName = "", string pHistoricalEra = "")
            {
                CardId = pCardId;
                DisplayName = pDisplayName;
                DynastyName = pDynastyName;
                HistoricalKingdomName = NormalizeShortKingdomName(pHistoricalKingdomName);
                BirthYear = pBirthYear;
                DeathYear = pDeathYear;
                HistoricalYear = pHistoricalYear;
                FameScore = pFameScore;
                FatherDisplayName = pFatherDisplayName ?? "";
                MotherDisplayName = pMotherDisplayName ?? "";
                Biography = string.IsNullOrEmpty(pBiography)
                    ? pDisplayName + "，" + pDynastyName + "君主。"
                    : pBiography;
                Sex = pSex;
                FamilyName = pFamilyName ?? "";
                ClanName = pClanName ?? "";
                GivenName = pGivenName ?? "";
                HistoricalEra = string.IsNullOrEmpty(pHistoricalEra)
                    ? pDynastyName
                    : pHistoricalEra;
                BackgroundSummary = BuildBackgroundSummary(pDisplayName,
                    HistoricalEra, HistoricalKingdomName, pHistoricalYear,
                    pCardId);
                DetailedBiography = BuildDetailedBiography(pCardId, pDisplayName,
                    HistoricalEra, HistoricalKingdomName, pHistoricalYear,
                    string.IsNullOrEmpty(pBiography) ? "" : Biography);
                FatherDisplayName = UnknownParent(FatherDisplayName);
                MotherDisplayName = UnknownParent(MotherDisplayName);
            }

            public HistoricalFigureCardDefinition ToDefinition(
                HistoricalFigureCardDefinition pLegacy)
            {
                NameParts(DisplayName, out string family, out string given);
                if (!string.IsNullOrEmpty(FamilyName)) family = FamilyName;
                string clan = string.IsNullOrEmpty(ClanName) ? family : ClanName;
                if (!string.IsNullOrEmpty(GivenName)) given = GivenName;
                return new HistoricalFigureCardDefinition(
                    CardId, DisplayName, family, clan, given, DynastyName,
                    HistoricalKingdomName, HistoricalEra, BirthYear, DeathYear,
                    HistoricalYear, FameScore, RarityForFame(FameScore),
                    Sex, Biography, "", FatherDisplayName,
                    "", MotherDisplayName,
                    PortraitPathByCardId.TryGetValue(CardId,
                        out string portraitPath)
                        ? portraitPath : pLegacy?.PortraitPath ?? "",
                    pLegacy?.LegacyFigureId, pLegacy?.LegacyRegistryIndex ?? -1,
                    pLegacy?.CombatHealth ?? 1500,
                    pLegacy?.CombatTraits ?? Enumerable.Empty<string>(),
                    BackgroundSummary, DetailedBiography,
                    RoleForCard(CardId), MinisterTypeForCard(CardId),
                    HistoricalFigureCardCrates.ForYear(HistoricalYear)?.Id ?? "");
            }
        }

        private static void NameParts(string pName, out string pFamily, out string pGiven)
        {
            string[] compoundFamilies =
            {
                "爱新觉罗", "乞伏", "沮渠", "慕容", "拓跋", "宇文", "完颜",
                "耶律", "司马", "赫连", "秃发", "皇甫", "长孙", "上官"
            };
            string value = pName ?? "";
            pFamily = compoundFamilies.FirstOrDefault(value.StartsWith) ??
                      (value.Length > 0 ? value.Substring(0, 1) : "");
            pGiven = value.Length > pFamily.Length
                ? value.Substring(pFamily.Length)
                : "";
        }

        private static EmperorSeed E(string pCardId, string pName,
            string pDynasty, string pKingdom, int pBirth, int pDeath,
            int pHistoricalYear, int pFame, string pFather = "",
            string pMother = "", string pBiography = "",
            HistoricalFigureSex pSex = HistoricalFigureSex.Male,
            string pFamilyName = "", string pClanName = "",
            string pGivenName = "", string pHistoricalEra = "")
        {
            return new EmperorSeed(pCardId, pName, pDynasty, pKingdom,
                pBirth, pDeath, pHistoricalYear, pFame, pFather, pMother,
                pBiography, pSex, pFamilyName, pClanName, pGivenName,
                pHistoricalEra);
        }

        private static string UnknownParent(string pName)
        {
            if (string.IsNullOrWhiteSpace(pName)) return "";
            string value = pName.Trim();
            return value == "不详" || value == "不詳" || value == "未知" ||
                   value == "史料不详" || value == "史料不詳"
                ? ""
                : value;
        }

        private static HistoricalFigureCardRole RoleForCard(string pCardId)
        {
            return MinisterTypeForCard(pCardId) !=
                   HistoricalFigureCardMinisterType.None
                ? HistoricalFigureCardRole.Minister
                : HistoricalFigureCardRole.Monarch;
        }

        public static HistoricalFigureCardMinisterType MinisterTypeForCard(
            string pCardId)
        {
            switch (pCardId ?? "")
            {
                case "han_han_xin":
                case "han_ban_chao":
                    return HistoricalFigureCardMinisterType.MilitaryGeneral;
                case "han_xiao_he":
                case "han_zhang_liang":
                case "han_sima_qian":
                case "han_huo_guang":
                    return HistoricalFigureCardMinisterType.CivilOfficial;
                default:
                    return HistoricalFigureCardMinisterType.None;
            }
        }

        private static IEnumerable<EmperorSeed> EmperorSeeds()
        {
            // Xia and Shang dates follow the conventional chronology where the
            // exact birth and death years are not preserved in reliable records.
            yield return E("xia_yu", "禹", "夏", "夏", UnknownYear,
                UnknownYear, -2070, 94, "鲧", "修己",
                "传世文献中的治水领袖和夏代奠基者。",
                pFamilyName: "姒", pClanName: "夏后", pGivenName: "文命");
            yield return E("xia_qi", "启", "夏", "夏", UnknownYear,
                UnknownYear, -1970, 66, "禹", "涂山氏",
                "传世文献记载的夏代君主，承继禹的政治权力。",
                pFamilyName: "姒", pClanName: "夏后", pGivenName: "启");
            yield return E("xia_taikang", "太康", "夏", "夏", UnknownYear,
                UnknownYear, -1955, 42, "启", "",
                "夏代早期君主，相关事迹主要见于传世文献。",
                pFamilyName: "姒", pClanName: "夏后", pGivenName: "太康");
            yield return E("xia_shaokang", "少康", "夏", "夏", UnknownYear,
                UnknownYear, -1875, 61, "相", "后缗",
                "传世文献称其复兴夏室，后世称少康中兴。",
                pFamilyName: "姒", pClanName: "夏后", pGivenName: "少康");
            yield return E("xia_jie", "履癸", "夏", "夏", UnknownYear,
                UnknownYear, -1600, 73, "发", "",
                "传世文献中的夏末君主，后世通常称夏桀。",
                pFamilyName: "姒", pClanName: "夏后", pGivenName: "履癸");

            yield return E("shang_tang", "子履", "商", "商", UnknownYear,
                UnknownYear, -1600, 94, "主癸", "扶都",
                "推翻夏末统治并建立商朝，后世称商汤。",
                pFamilyName: "子", pClanName: "商", pGivenName: "履");
            yield return E("shang_taijia", "子至", "商", "商", UnknownYear,
                UnknownYear, -1580, 48, "太丁", "",
                "商代早期君主，传世文献记有伊尹辅政事迹。",
                pFamilyName: "子", pClanName: "商", pGivenName: "至");
            yield return E("shang_pangeng", "子旬", "商", "商", UnknownYear,
                UnknownYear, -1300, 72, "祖丁", "",
                "商代君主，迁都于殷并稳定王朝统治。",
                pFamilyName: "子", pClanName: "商", pGivenName: "旬");
            yield return E("shang_wuding", "子昭", "商", "商", UnknownYear,
                UnknownYear, -1250, 88, "小乙", "",
                "商代重要君主，其时代留下大量甲骨卜辞。",
                pFamilyName: "子", pClanName: "商", pGivenName: "昭");
            yield return E("shang_diyi", "子羡", "商", "商", UnknownYear,
                UnknownYear, -1101, 45, "文丁", "",
                "商代晚期君主，帝辛之父。",
                pFamilyName: "子", pClanName: "商", pGivenName: "羡");
            yield return E("shang_dixin", "子受", "商", "商", UnknownYear,
                -1046, -1075, 82, "帝乙", "",
                "商代末代君主，周人文献称其为纣王。",
                pFamilyName: "子", pClanName: "商", pGivenName: "受");

            yield return E("ji_fa", "姬发", "周", "周", UnknownYear,
                -1043, -1046, 100, "姬昌", "太姒",
                "灭商建立西周，后世称周武王。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "发",
                pHistoricalEra: "西周");
            yield return E("zhou_chengwang", "姬诵", "周", "周", UnknownYear,
                UnknownYear, -1042, 62, "姬发", "邑姜",
                "西周君主，在周公辅政后亲政。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "诵",
                pHistoricalEra: "西周");
            yield return E("zhou_kangwang", "姬钊", "周", "周", UnknownYear,
                UnknownYear, -1020, 56, "姬诵", "王姒",
                "西周君主，成康时期以政局稳定著称。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "钊",
                pHistoricalEra: "西周");
            yield return E("zhou_zhaowang", "姬瑕", "周", "周", UnknownYear,
                UnknownYear, -995, 43, "姬钊", "",
                "西周君主，曾多次向南方用兵。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "瑕",
                pHistoricalEra: "西周");
            yield return E("zhou_muwang", "姬满", "周", "周", UnknownYear,
                UnknownYear, -976, 65, "姬瑕", "",
                "西周君主，在位时间较长，传世文献事迹丰富。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "满",
                pHistoricalEra: "西周");
            yield return E("zhou_liwang", "姬胡", "周", "周", UnknownYear,
                -828, -877, 58, "姬夷", "",
                "西周君主，其统治末期发生国人暴动。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "胡",
                pHistoricalEra: "西周");
            yield return E("zhou_xuanwang", "姬静", "周", "周", UnknownYear,
                -782, -827, 64, "姬胡", "姜后",
                "西周君主，史称宣王中兴。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "静",
                pHistoricalEra: "西周");
            yield return E("zhou_youwang", "姬宫湦", "周", "周", UnknownYear,
                -771, -781, 68, "姬静", "姜后",
                "西周末代君主，犬戎攻破镐京后身亡。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "宫湦",
                pHistoricalEra: "西周");
            yield return E("zhou_pingwang", "姬宜臼", "周", "周", UnknownYear,
                -720, -770, 62, "姬宫湦", "申后",
                "迁都洛邑，开启东周时期。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "宜臼",
                pHistoricalEra: "东周");
            yield return E("zhou_huanwang", "姬林", "周", "周", UnknownYear,
                -697, -719, 40, "姬泄父", "",
                "东周君主，其时周王室与诸侯关系进一步变化。",
                pFamilyName: "姬", pClanName: "周", pGivenName: "林",
                pHistoricalEra: "东周");

            yield return E("qi_huangong", "姜小白", "齐", "齐", UnknownYear,
                -643, -685, 91, "姜禄甫", "",
                "任用管仲改革齐国，成为春秋时期的重要霸主。",
                pFamilyName: "姜", pClanName: "吕", pGivenName: "小白",
                pHistoricalEra: "春秋");
            yield return E("jin_wengong", "姬重耳", "晋", "晋", -697,
                -628, -636, 90, "姬诡诸", "狐姬",
                "长期流亡后即位，使晋国成为春秋霸主。",
                pFamilyName: "姬", pClanName: "晋", pGivenName: "重耳",
                pHistoricalEra: "春秋");
            yield return E("qin_mugong", "嬴任好", "秦", "秦", UnknownYear,
                -621, -659, 86, "嬴德", "",
                "经营关中并向西拓展，是春秋时期秦国重要君主。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "任好",
                pHistoricalEra: "春秋");
            yield return E("chu_zhuangwang", "芈旅", "楚", "楚", UnknownYear,
                -591, -613, 89, "芈商臣", "",
                "整顿楚国内政并北上争霸，是春秋重要霸主。",
                pFamilyName: "芈", pClanName: "熊", pGivenName: "旅",
                pHistoricalEra: "春秋");
            yield return E("wu_helv", "姬光", "吴", "吴", UnknownYear,
                -496, -514, 83, "姬诸樊", "",
                "任用伍子胥、孙武，使吴国一度攻入楚都。",
                pFamilyName: "姬", pClanName: "吴", pGivenName: "光",
                pHistoricalEra: "春秋");
            yield return E("yue_goujian", "姒鸠浅", "越", "越", UnknownYear,
                -464, -496, 90, "姒允常", "",
                "卧薪尝胆、灭吴称霸，是春秋末期越国君主。",
                pFamilyName: "姒", pClanName: "越", pGivenName: "鸠浅",
                pHistoricalEra: "春秋");

            yield return E("wei_wenhou", "姬斯", "魏", "魏", UnknownYear,
                -396, -445, 79, "姬驹", "",
                "任用李悝等人变法，使魏国率先强盛。",
                pFamilyName: "姬", pClanName: "魏", pGivenName: "斯",
                pHistoricalEra: "战国");
            yield return E("qi_weiwang", "妫因齐", "齐", "齐", -378,
                -320, -356, 80, "妫午", "",
                "整顿吏治并任用贤能，使田齐国势强盛。",
                pFamilyName: "妫", pClanName: "田", pGivenName: "因齐",
                pHistoricalEra: "战国");
            yield return E("chu_weiwang", "芈商", "楚", "楚", UnknownYear,
                -329, -339, 65, "芈良夫", "",
                "战国时期楚国君主，楚国在其时保持强盛。",
                pFamilyName: "芈", pClanName: "熊", pGivenName: "商",
                pHistoricalEra: "战国");
            yield return E("zhao_wulingwang", "嬴雍", "赵", "赵", -340,
                -295, -325, 88, "嬴语", "",
                "推行胡服骑射改革，增强赵国军事实力。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "雍",
                pHistoricalEra: "战国");
            yield return E("yan_zhaowang", "姬职", "燕", "燕", UnknownYear,
                -279, -311, 78, "姬哙", "",
                "筑黄金台招贤，任用乐毅振兴燕国。",
                pFamilyName: "姬", pClanName: "燕", pGivenName: "职",
                pHistoricalEra: "战国");
            yield return E("qin_xiaogong", "嬴渠梁", "秦", "秦", -381,
                -338, -361, 87, "嬴师隰", "",
                "任用商鞅变法，为秦国统一奠定制度基础。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "渠梁",
                pHistoricalEra: "战国");
            yield return E("qin_huiwenwang", "嬴驷", "秦", "秦", -356,
                -311, -338, 82, "嬴渠梁", "",
                "承接商鞅变法成果并称王，继续扩张秦国。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "驷",
                pHistoricalEra: "战国");
            yield return E("qin_wuwang", "嬴荡", "秦", "秦", -329,
                -307, -310, 55, "嬴驷", "魏纾",
                "战国时期秦国君主，在位时间较短。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "荡",
                pHistoricalEra: "战国");
            yield return E("qin_zhaoxiangwang", "嬴稷", "秦", "秦", -325,
                -251, -306, 89, "嬴驷", "芈八子",
                "长期执政并持续东进，使秦国形成统一优势。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "稷",
                pHistoricalEra: "战国");
            yield return E("qin_xiaowenwang", "嬴柱", "秦", "秦", -302,
                -250, -250, 40, "嬴稷", "唐八子",
                "秦国君主，正式在位时间很短。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "柱",
                pHistoricalEra: "战国");
            yield return E("qin_zhuangxiangwang", "嬴异人", "秦", "秦", -281,
                -247, -249, 67, "嬴柱", "夏姬",
                "在吕不韦支持下即位，是秦王政之父。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "异人",
                pHistoricalEra: "战国");
            yield return E("chu_huaiwang", "芈槐", "楚", "楚", UnknownYear,
                -296, -328, 72, "芈商", "",
                "战国时期楚国君主，后被秦国扣留并死于秦地。",
                pFamilyName: "芈", pClanName: "熊", pGivenName: "槐",
                pHistoricalEra: "战国");

            yield return E("qin_shihuang", "嬴政", "秦", "秦", -259, -210, -221, 100,
                "嬴异人", "赵姬", "统一六国，建立中国历史上第一个中央集权帝国。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "政");
            yield return E("qin_er_shi", "胡亥", "秦", "秦", -230, -207,
                -209, 55, "嬴政", "胡姬", "秦朝第二位皇帝。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "胡亥");
            yield return E("qin_ziying", "子婴", "秦", "秦", UnknownYear,
                -206, -207, 40, "", "",
                "秦朝末代统治者，其与秦宗室的具体亲缘关系存在争议。",
                pFamilyName: "嬴", pClanName: "赵", pGivenName: "子婴");

            yield return E("chu_xiang_ji", "项籍", "秦汉之际", "楚", -232,
                -202, -206, 93, "", "",
                "秦末起兵反秦，号西楚霸王，与刘邦争夺天下。",
                pFamilyName: "姬", pClanName: "项", pGivenName: "籍",
                pHistoricalEra: "楚汉争霸");
            yield return E("han_han_xin", "韩信", "汉", "汉", -231,
                -196, -206, 98, "", "",
                "汉初军事家，统兵平定诸国，为汉朝统一建立重要功绩。",
                pFamilyName: "韩", pClanName: "韩", pGivenName: "信",
                pHistoricalEra: "西汉");
            yield return E("han_xiao_he", "萧何", "汉", "汉", UnknownYear,
                -193, -206, 88, "", "",
                "汉初丞相，主持制度建设并保障楚汉战争后方。",
                pFamilyName: "萧", pClanName: "萧", pGivenName: "何",
                pHistoricalEra: "西汉");
            yield return E("han_zhang_liang", "张良", "汉", "汉", -250,
                -186, -206, 98, "张平", "",
                "辅佐刘邦夺取天下，是汉初重要谋臣。",
                pFamilyName: "张", pClanName: "张", pGivenName: "良",
                pHistoricalEra: "西汉");
            yield return E("han_lu_zhi", "吕雉", "汉", "汉", -241,
                -180, -195, 86, "吕文", "",
                "汉高祖皇后，刘邦死后长期掌握西汉最高政治权力。",
                HistoricalFigureSex.Female, pFamilyName: "吕",
                pClanName: "吕", pGivenName: "雉", pHistoricalEra: "西汉");
            yield return E("han_wei_qing", "卫青", "汉", "汉", UnknownYear,
                -106, -129, 86, "郑季", "卫媪",
                "西汉名将，多次出击匈奴并收复河套地区。",
                pFamilyName: "卫", pClanName: "卫", pGivenName: "青",
                pHistoricalEra: "西汉");
            yield return E("han_huo_qubing", "霍去病", "汉", "汉", -140,
                -117, -121, 90, "霍仲孺", "卫少儿",
                "西汉名将，深入河西与漠北打击匈奴。",
                pFamilyName: "霍", pClanName: "霍", pGivenName: "去病",
                pHistoricalEra: "西汉");
            yield return E("han_sima_qian", "司马迁", "汉", "汉", -145,
                -86, -104, 95, "司马谈", "",
                "西汉史学家，撰写纪传体通史《史记》。",
                pFamilyName: "司马", pClanName: "司马", pGivenName: "迁",
                pHistoricalEra: "西汉");
            yield return E("han_huo_guang", "霍光", "汉", "汉", UnknownYear,
                -68, -87, 82, "霍仲孺", "",
                "西汉辅政大臣，历事武帝、昭帝和宣帝。",
                pFamilyName: "霍", pClanName: "霍", pGivenName: "光",
                pHistoricalEra: "西汉");
            yield return E("han_ban_chao", "班超", "汉", "汉", 32,
                102, 73, 84, "班彪", "",
                "东汉将领与外交家，经营西域三十余年。",
                pFamilyName: "班", pClanName: "班", pGivenName: "超",
                pHistoricalEra: "东汉");

            yield return E("han_gaozu", "刘邦", "汉", "汉", -256, -195, -202, 99, "刘煓", "刘媪", "击败群雄建立汉朝，奠定汉帝国制度。");
            yield return E("han_huidi", "刘盈", "汉", "汉", -210, -188, -195, 58, "刘邦", "吕雉");
            yield return E("han_qianshao", "刘恭", "汉", "汉", -192, -184, -187, 30, "刘盈", "张皇后");
            yield return E("han_houshao", "刘弘", "汉", "汉", -190, -180, -184, 30, "刘盈", "不详");
            yield return E("han_wendi", "刘恒", "汉", "汉", -203, -157, -180, 80, "刘邦", "薄姬", "以黄老之治休养生息，开创文景之治。");
            yield return E("han_jingdi", "刘启", "汉", "汉", -188, -141, -157, 72, "刘恒", "窦姬");
            yield return E("han_wudi", "刘彻", "汉", "汉", -156, -87, -141, 99, "刘启", "王娡", "拓展汉帝国疆域，确立儒学为国家政治的重要基础。");
            yield return E("han_zhaodi", "刘弗陵", "汉", "汉", -94, -74, -87, 55, "刘彻", "赵婕妤");
            yield return E("han_xuandi", "刘询", "汉", "汉", -91, -49, -74, 72, "刘进", "王翁须");
            yield return E("han_yuandi", "刘奭", "汉", "汉", -75, -33, -49, 52, "刘询", "许平君");
            yield return E("han_chengdi", "刘骜", "汉", "汉", -51, -7, -33, 48, "刘奭", "王政君");
            yield return E("han_aidi", "刘欣", "汉", "汉", -27, 1, -7, 42, "刘康", "丁姬");
            yield return E("han_pingdi", "刘衎", "汉", "汉", -9, 6, 1, 35, "刘兴", "卫姬");
            yield return E("han_ruzi", "刘婴", "汉", "汉", 5, 25, 6, 28, "刘显", "不详");
            yield return E("xin_wangmang", "王莽", "新", "新", -45, 23, 9, 82, "王曼", "渠氏", "以新朝取代西汉，推动了一系列制度改革。");

            yield return E("han_guangwu", "刘秀", "汉", "汉", -5, 57, 25, 98, "刘钦", "樊娴都", "重建汉朝并完成全国统一，史称光武中兴。");
            yield return E("han_mingdi", "刘庄", "汉", "汉", 28, 75, 57, 70, "刘秀", "阴丽华");
            yield return E("han_zhangdi", "刘炟", "汉", "汉", 56, 88, 75, 68, "刘庄", "贾贵人");
            yield return E("han_hedi", "刘肇", "汉", "汉", 79, 106, 88, 68, "刘炟", "梁贵人");
            yield return E("han_shangdi", "刘隆", "汉", "汉", 105, 106, 106, 25, "刘肇", "邓绥");
            yield return E("han_andi", "刘祜", "汉", "汉", 94, 125, 106, 45, "刘庆", "左小娥");
            yield return E("han_shundi", "刘保", "汉", "汉", 115, 144, 125, 53, "刘庆", "李氏");
            yield return E("han_chongdi", "刘炳", "汉", "汉", 143, 145, 144, 20, "刘保", "梁妠");
            yield return E("han_zhidi", "刘缵", "汉", "汉", 138, 146, 145, 20, "刘翼", "不详");
            yield return E("han_huandi", "刘志", "汉", "汉", 132, 168, 146, 38, "刘翼", "匽明");
            yield return E("han_lingdi", "刘宏", "汉", "汉", 156, 189, 168, 48, "刘苌", "董氏");
            yield return E("han_shaodi", "刘辩", "汉", "汉", 173, 190, 189, 26, "刘宏", "何皇后");
            yield return E("han_xiandi", "刘协", "汉", "汉", 181, 234, 190, 70, "刘宏", "王美人");
            yield return E("zhong_hou_shu", "袁术", "东汉", "仲", 155,
                199, 197, 65, "袁逢", "",
                "东汉末年军阀，197年称帝并建立仲氏政权。",
                pFamilyName: "袁", pClanName: "袁", pGivenName: "术",
                pHistoricalEra: "仲氏政权");

            yield return E("wei_wendi", "曹丕", "魏", "魏", 187, 226, 220, 82, "曹操", "卞氏");
            yield return E("wei_mingdi", "曹叡", "魏", "魏", 204, 239, 226, 64, "曹丕", "甄氏");
            yield return E("wei_feidi", "曹芳", "魏", "魏", 232, 274, 239, 30, "曹楷", "不详");
            yield return E("wei_zhengdi", "曹髦", "魏", "魏", 241, 260, 254, 43, "曹霖", "不详");
            yield return E("wei_yuandi", "曹奂", "魏", "魏", 246, 302, 260, 32, "曹宇", "不详");
            yield return E("shu_han_zhaolie", "刘备", "蜀汉", "蜀汉", 161, 223, 221, 98, "刘弘", "不详", "在群雄割据中建立蜀汉政权，成为三国政治核心人物。");
            yield return E("shu_hou_zhu", "刘禅", "蜀汉", "蜀汉", 207, 271, 223, 62, "刘备", "甘夫人");
            yield return E("wu_dadi", "孙权", "吴", "吴", 182, 252, 229, 98, "孙坚", "吴夫人", "经营江东并建立吴国，维持三国鼎立格局。");
            yield return E("wu_hui_zhu", "孙亮", "吴", "吴", 243, 260, 252, 28, "孙权", "潘皇后");
            yield return E("wu_jingdi", "孙休", "吴", "吴", 235, 264, 258, 32, "孙权", "王夫人");
            yield return E("wu_moding", "孙皓", "吴", "吴", 242, 284, 264, 40, "孙和", "何姬");

            yield return E("jin_wudi", "司马炎", "晋", "晋", 236, 290, 266, 84, "司马昭", "王元姬");
            yield return E("jin_huidi", "司马衷", "晋", "晋", 259, 307, 290, 45, "司马炎", "杨艳");
            yield return E("jin_huaidi", "司马炽", "晋", "晋", 284, 313, 307, 30, "司马炎", "杨芷");
            yield return E("jin_mindi", "司马邺", "晋", "晋", 300, 318, 313, 28, "司马晏", "不详");
            yield return E("jin_yuandi", "司马睿", "晋", "晋", 276, 323, 317, 63, "司马觐", "夏侯光姬");
            yield return E("jin_mingdi", "司马绍", "晋", "晋", 299, 325, 323, 45, "司马睿", "虞孟母");
            yield return E("jin_chengdi", "司马衍", "晋", "晋", 321, 342, 325, 34, "司马绍", "庾文君");
            yield return E("jin_kangdi", "司马岳", "晋", "晋", 322, 344, 342, 25, "司马绍", "郑阿春");
            yield return E("jin_mudi", "司马聃", "晋", "晋", 343, 361, 344, 34, "司马岳", "褚蒜子");
            yield return E("jin_aidi", "司马丕", "晋", "晋", 341, 365, 361, 28, "司马绍", "周贵人");
            yield return E("jin_feidi", "司马奕", "晋", "晋", 342, 386, 365, 23, "司马岳", "不详");
            yield return E("jin_xiaowudi", "司马曜", "晋", "晋", 362, 396, 372, 55, "司马昱", "李陵容");
            yield return E("jin_andi", "司马德宗", "晋", "晋", 382, 419, 396, 30, "司马曜", "文陈太后");
            yield return E("jin_gongdi", "司马德文", "晋", "晋", 386, 421, 419, 29, "司马曜", "陈归女");

            yield return E("song_wudi", "刘裕", "宋", "宋", 363, 422, 420, 88, "刘翘", "赵安宗");
            yield return E("song_shaodi", "刘义符", "宋", "宋", 406, 424, 422, 25, "刘裕", "张夫人");
            yield return E("song_wendi", "刘义隆", "宋", "宋", 407, 453, 424, 65, "刘裕", "胡道安");
            yield return E("song_xiaowu", "刘骏", "宋", "宋", 430, 464, 453, 50, "刘义隆", "路惠男");
            yield return E("song_qianfeidi", "刘子业", "宋", "宋", 449, 465, 464, 28, "刘骏", "王宪嫄");
            yield return E("song_mingdi", "刘彧", "宋", "宋", 439, 472, 465, 36, "刘义隆", "沈容姬");
            yield return E("song_houfeidi", "刘昱", "宋", "宋", 463, 477, 472, 24, "刘彧", "陈妙登");
            yield return E("song_shundi", "刘准", "宋", "宋", 467, 479, 477, 22, "刘休范", "陈法容");
            yield return E("qi_gaodi", "萧道成", "齐", "齐", 427, 482, 479, 65, "萧承之", "陈道止");
            yield return E("qi_wudi", "萧赜", "齐", "齐", 440, 493, 482, 43, "萧道成", "刘智容");
            yield return E("qi_mingdi", "萧鸾", "齐", "齐", 452, 498, 494, 37, "萧道生", "江氏");
            yield return E("qi_donghunhou", "萧宝卷", "齐", "齐", 483, 501, 498, 22, "萧鸾", "刘惠端");
            yield return E("qi_heshandi", "萧宝融", "齐", "齐", 488, 502, 501, 22, "萧鸾", "王蕣华");
            yield return E("liang_wudi", "萧衍", "梁", "梁", 464, 549, 502, 76, "萧顺之", "张尚柔", "建立梁朝并长期执政，推动南朝佛教和文化发展。");
            yield return E("liang_jianwendi", "萧纲", "梁", "梁", 503, 551, 549, 35, "萧衍", "丁令光");
            yield return E("liang_yuandi", "萧绎", "梁", "梁", 508, 555, 552, 40, "萧衍", "阮令嬴");
            yield return E("liang_jingdi", "萧方智", "梁", "梁", 543, 558, 555, 23, "萧绎", "夏太妃");
            yield return E("chen_wudi", "陈霸先", "陈", "陈", 503, 559, 557, 61, "陈文赞", "董氏");
            yield return E("chen_wendi", "陈蒨", "陈", "陈", 522, 566, 559, 38, "陈道谭", "龚氏");
            yield return E("chen_feidi", "陈伯宗", "陈", "陈", 554, 570, 566, 21, "陈蒨", "沈妙容");
            yield return E("chen_xuandi", "陈顼", "陈", "陈", 530, 582, 569, 40, "陈道谭", "吴氏");
            yield return E("chen_houzhudi", "陈叔宝", "陈", "陈", 553, 604, 582, 45, "陈顼", "柳敬言");

            yield return E("beiwei_taizu", "拓跋珪", "北魏", "魏", 371, 409, 386, 65, "拓跋寔", "贺氏");
            yield return E("beiwei_mingyuandi", "拓跋嗣", "北魏", "魏", 392, 423, 409, 43, "拓跋珪", "刘贵人");
            yield return E("beiwei_taiwudi", "拓跋焘", "北魏", "魏", 408, 452, 423, 68, "拓跋嗣", "杜贵人");
            yield return E("beiwei_wenchengdi", "拓跋濬", "北魏", "魏", 440, 465, 452, 40, "拓跋晃", "闾氏");
            yield return E("beiwei_xianwendi", "拓跋弘", "北魏", "魏", 454, 476, 465, 35, "拓跋濬", "李夫人");
            yield return E("beiwei_xiaowendi", "元宏", "北魏", "魏", 467, 499, 471, 72, "拓跋弘", "李夫人", "推行孝文帝改革，促进北魏鲜卑与汉族制度文化融合。");
            yield return E("beiwei_xuanwudi", "元恪", "北魏", "魏", 483, 515, 499, 48, "元宏", "高照容");
            yield return E("beiwei_xiaomingdi", "元诩", "北魏", "魏", 510, 528, 515, 25, "元恪", "胡充华");
            yield return E("beiwei_xiaozhuangdi", "元子攸", "北魏", "魏", 507, 531, 528, 25, "元勰", "李媛华");
            yield return E("dongwei_xiaojingdi", "元善见", "东魏", "魏", 524, 552, 534, 25, "元亶", "不详");
            yield return E("xiwei_wendi", "元宝炬", "西魏", "魏", 507, 551, 535, 30, "元愉", "杨奥妃");
            yield return E("xiwei_feidi", "元钦", "西魏", "魏", 525, 554, 551, 20, "元宝炬", "乙弗皇后");
            yield return E("xiwei_gongdi", "拓跋廓", "西魏", "魏", 537, 557, 554, 20, "元宝炬", "不详");
            yield return E("beiqi_wenxuan", "高洋", "北齐", "齐", 529, 559, 550, 48, "高欢", "娄昭君");
            yield return E("beiqi_feidi", "高殷", "北齐", "齐", 545, 561, 559, 18, "高洋", "李祖娥");
            yield return E("beiqi_xiaozhaodi", "高演", "北齐", "齐", 535, 561, 560, 35, "高欢", "娄昭君");
            yield return E("beiqi_wuchengdi", "高湛", "北齐", "齐", 537, 568, 561, 38, "高欢", "娄昭君");
            yield return E("beiqi_houzhudi", "高纬", "北齐", "齐", 556, 577, 565, 27, "高湛", "胡皇后");
            yield return E("beizhou_xiaomindi", "宇文觉", "北周", "周", 542, 557, 557, 32, "宇文泰", "元胡摩");
            yield return E("beizhou_mingdi", "宇文毓", "北周", "周", 534, 560, 557, 33, "宇文泰", "姚夫人");
            yield return E("beizhou_wudi", "宇文邕", "北周", "周", 543, 578, 560, 58, "宇文泰", "叱奴太后");
            yield return E("beizhou_xuandi", "宇文赟", "北周", "周", 559, 580, 578, 22, "宇文邕", "李娥姿");
            yield return E("beizhou_jingdi", "宇文阐", "北周", "周", 573, 581, 580, 18, "宇文赟", "朱满月");

            yield return E("sui_wendi", "杨坚", "隋", "隋", 541, 604, 581, 98, "杨忠", "吕苦桃", "结束南北朝长期分裂并建立隋朝，完成全国统一。");
            yield return E("sui_yangdi", "杨广", "隋", "隋", 569, 618, 604, 68, "杨坚", "独孤伽罗");
            yield return E("sui_gongdi", "杨侑", "隋", "隋", 605, 619, 617, 18, "杨昭", "韦妃");
            yield return E("sui_yangtong", "杨侗", "隋", "隋", 605, 619, 618, 20, "杨昭", "刘良娣");
            yield return E("tang_gaozu", "李渊", "唐", "唐", 566, 635, 618, 82, "李昞", "独孤氏", "建立唐朝并完成关中与中原的统一。");
            yield return E("tang_taizong", "李世民", "唐", "唐", 598, 649, 626, 99, "李渊", "窦氏", "开创贞观之治，形成古代中国盛世政治的典范。");
            yield return E("tang_gaozong", "李治", "唐", "唐", 628, 683, 649, 72, "李世民", "长孙皇后");
            yield return E("tang_zhongzong", "李显", "唐", "唐", 656, 710, 684, 48, "李治", "武曌");
            yield return E("tang_ruizong", "李旦", "唐", "唐", 662, 716, 684, 43, "李治", "武曌");
            yield return E("zhou_wuzetian", "武曌", "周", "周", 624, 705, 690, 98, "武士彟", "杨氏", "中国历史上唯一得到普遍承认的女皇帝，重用人才并稳定国家治理。", HistoricalFigureSex.Female);
            yield return E("tang_shang", "李重茂", "唐", "唐", 695, 714, 710, 18, "李显", "韦皇后");
            yield return E("tang_xuanzong_li_longji", "李隆基", "唐", "唐", 685, 762, 712, 91, "李旦", "窦德妃", "前期开创开元盛世，后期因政治与军事危机导致政局转折。");
            yield return E("tang_suzong", "李亨", "唐", "唐", 711, 762, 756, 51, "李隆基", "杨贵妃");
            yield return E("tang_daizong", "李豫", "唐", "唐", 726, 779, 762, 53, "李亨", "章敬皇后");
            yield return E("tang_dezong", "李适", "唐", "唐", 742, 805, 779, 50, "李豫", "沈氏");
            yield return E("tang_shunzong", "李诵", "唐", "唐", 761, 806, 805, 31, "李适", "王氏");
            yield return E("tang_xianzong", "李纯", "唐", "唐", 778, 820, 806, 63, "李诵", "王淑妃");
            yield return E("tang_muzong", "李恒", "唐", "唐", 795, 824, 820, 39, "李纯", "郭贵妃");
            yield return E("tang_jingzong", "李湛", "唐", "唐", 809, 827, 824, 27, "李恒", "王太后");
            yield return E("tang_wenzong", "李昂", "唐", "唐", 809, 840, 827, 38, "李恒", "萧皇后");
            yield return E("tang_wuzong", "李炎", "唐", "唐", 814, 846, 840, 35, "李恒", "韦妃");
            yield return E("tang_xuanzong_li_chen", "李忱", "唐", "唐", 810, 859, 846, 45, "李宪", "郑氏");
            yield return E("tang_yizong", "李漼", "唐", "唐", 833, 873, 859, 30, "李忱", "晁美人");
            yield return E("tang_xizong", "李儇", "唐", "唐", 862, 888, 873, 27, "李漼", "王氏");
            yield return E("tang_zhaozong", "李晔", "唐", "唐", 867, 904, 888, 34, "李漼", "王氏");
            yield return E("tang_aidi", "李柷", "唐", "唐", 892, 908, 904, 18, "李晔", "何皇后");

            yield return E("later_liang_taizu", "朱温", "后梁", "梁", 852, 912, 907, 78, "朱诚", "王氏", "结束唐朝统治并建立后梁，开启五代时期。");
            yield return E("later_liang_yingwang", "朱友珪", "后梁", "梁", 884, 913, 912, 20, "朱温", "张氏");
            yield return E("later_liang_modi", "朱友贞", "后梁", "梁", 888, 923, 913, 27, "朱温", "张皇后");
            yield return E("later_tang_zhuangzong", "李存勖", "后唐", "唐", 885, 926, 923, 67, "李克用", "曹氏", "灭后梁建立后唐，善于用兵并完成中原局部统一。");
            yield return E("later_tang_mingzong", "李嗣源", "后唐", "唐", 867, 933, 926, 48, "李国昌", "刘氏");
            yield return E("later_tang_mindi", "李从厚", "后唐", "唐", 914, 934, 933, 18, "李嗣源", "夏氏");
            yield return E("later_tang_modi", "李从珂", "后唐", "唐", 885, 936, 934, 25, "王氏", "不详");
            yield return E("later_jin_gaozu", "石敬瑭", "后晋", "晋", 892, 942, 936, 43, "石绍雍", "何氏");
            yield return E("later_jin_chudi", "石重贵", "后晋", "晋", 914, 974, 942, 25, "石敬瑭", "安太妃");
            yield return E("later_han_gaozu", "刘知远", "后汉", "汉", 895, 948, 947, 45, "刘琠", "安氏");
            yield return E("later_han_yindi", "刘承祐", "后汉", "汉", 931, 951, 948, 21, "刘知远", "李皇后");
            yield return E("later_zhou_taizu", "郭威", "后周", "周", 904, 954, 951, 52, "郭简", "王氏");
            yield return E("later_zhou_shizong", "柴荣", "后周", "周", 921, 959, 954, 62, "柴守礼", "不详");
            yield return E("later_zhou_gongdi", "郭宗训", "后周", "周", 953, 973, 959, 17, "柴荣", "符皇后");

            yield return E("song_taizu", "赵匡胤", "宋", "宋", 927, 976, 960, 98, "赵弘殷", "杜氏", "陈桥兵变建立宋朝，确立重文抑武的国家制度。");
            yield return E("song_taizong", "赵光义", "宋", "宋", 939, 997, 976, 64, "赵弘殷", "杜氏");
            yield return E("song_zhenzong", "赵恒", "宋", "宋", 968, 1022, 997, 54, "赵光义", "李贤妃");
            yield return E("song_renzong", "赵祯", "宋", "宋", 1010, 1063, 1022, 72, "赵恒", "李宸妃", "在位时期政治相对宽和，文化与制度发展显著。");
            yield return E("song_yingzong", "赵曙", "宋", "宋", 1032, 1067, 1063, 34, "赵允让", "任氏");
            yield return E("song_shenzong", "赵顼", "宋", "宋", 1048, 1085, 1067, 67, "赵曙", "高滔滔");
            yield return E("song_zhezong", "赵煦", "宋", "宋", 1077, 1100, 1085, 48, "赵顼", "朱氏");
            yield return E("song_huizong", "赵佶", "宋", "宋", 1082, 1135, 1100, 78, "赵顼", "陈氏", "以书画艺术闻名，北宋末年政局在内外压力下崩解。");
            yield return E("song_qinzong", "赵桓", "宋", "宋", 1100, 1161, 1126, 43, "赵佶", "王氏");
            yield return E("song_gaozong", "赵构", "宋", "宋", 1107, 1187, 1127, 59, "赵佶", "韦贤妃");
            yield return E("song_xiaozong", "赵昚", "宋", "宋", 1127, 1194, 1162, 58, "赵子偁", "张氏");
            yield return E("song_guangzong", "赵惇", "宋", "宋", 1147, 1200, 1189, 29, "赵昚", "郭皇后");
            yield return E("song_ningzong", "赵扩", "宋", "宋", 1168, 1224, 1194, 39, "赵惇", "李凤娘");
            yield return E("song_lizong", "赵昀", "宋", "宋", 1205, 1264, 1224, 50, "赵与莒", "全氏");
            yield return E("song_duzong", "赵禥", "宋", "宋", 1240, 1274, 1264, 31, "赵昀", "黄氏");
            yield return E("song_gongdi", "赵显", "宋", "宋", 1271, 1323, 1274, 26, "赵禥", "全皇后");
            yield return E("song_duanzong", "赵昰", "宋", "宋", 1269, 1278, 1276, 22, "赵禥", "杨淑妃");
            yield return E("song_weizhu", "赵昺", "宋", "宋", 1272, 1279, 1278, 24, "赵禥", "杨淑妃");

            yield return E("liao_taizu", "耶律阿保机", "辽", "辽", 872, 926, 916, 84, "耶律撒剌的", "萧岩母斤", "统一契丹各部并建立辽国，奠定北方政权格局。");
            yield return E("liao_taizong", "耶律德光", "辽", "辽", 902, 947, 926, 48, "耶律阿保机", "述律平");
            yield return E("liao_shizong", "耶律阮", "辽", "辽", 919, 951, 947, 33, "耶律倍", "萧氏");
            yield return E("liao_muzong", "耶律璟", "辽", "辽", 931, 969, 951, 25, "耶律德光", "萧氏");
            yield return E("liao_jingzong", "耶律贤", "辽", "辽", 948, 982, 969, 41, "耶律李胡", "萧撒葛只");
            yield return E("liao_shengzong", "耶律隆绪", "辽", "辽", 972, 1031, 982, 61, "耶律贤", "萧绰");
            yield return E("liao_xingzong", "耶律宗真", "辽", "辽", 1016, 1055, 1031, 31, "耶律隆绪", "萧菩萨哥");
            yield return E("liao_daozong", "耶律洪基", "辽", "辽", 1032, 1101, 1055, 38, "耶律宗真", "萧观音");
            yield return E("liao_tianzhuo", "耶律延禧", "辽", "辽", 1075, 1128, 1101, 35, "耶律浚", "贞顺皇后");

            yield return E("xi_xia_jingzong", "李元昊", "西夏", "夏", 1003, 1048, 1038, 64, "李德明", "卫慕氏");
            yield return E("xi_xia_yizong", "李谅祚", "西夏", "夏", 1047, 1068, 1048, 29, "李元昊", "没藏皇后");
            yield return E("xi_xia_huizong", "李秉常", "西夏", "夏", 1061, 1086, 1068, 30, "李谅祚", "没藏氏");
            yield return E("xi_xia_chongzong", "李乾顺", "西夏", "夏", 1083, 1139, 1086, 45, "李秉常", "梁氏");
            yield return E("xi_xia_renzong", "李仁孝", "西夏", "夏", 1124, 1193, 1139, 47, "李乾顺", "曹氏");
            yield return E("xi_xia_huanzong", "李纯祐", "西夏", "夏", 1177, 1206, 1193, 24, "李仁孝", "罗氏");
            yield return E("xi_xia_xiangzong", "李安全", "西夏", "夏", 1170, 1211, 1206, 22, "李仁孝", "不详");
            yield return E("xi_xia_shenzong", "李遵顼", "西夏", "夏", 1163, 1226, 1211, 28, "李彦宗", "不详");
            yield return E("xi_xia_xianzong", "李德旺", "西夏", "夏", 1181, 1226, 1223, 20, "李遵顼", "不详");
            yield return E("xi_xia_modi", "李睍", "西夏", "夏", 1203, 1227, 1226, 19, "李德旺", "不详");

            yield return E("jin_taizu", "完颜阿骨打", "金", "金", 1068, 1123, 1115, 72, "劾里钵", "徒单氏", "统一女真各部并建立金国，改变宋辽之间的力量格局。");
            yield return E("jin_taizong", "完颜吴乞买", "金", "金", 1075, 1135, 1123, 42, "劾里钵", "徒单氏");
            yield return E("jin_xizong", "完颜亶", "金", "金", 1119, 1150, 1135, 32, "完颜宗峻", "蒲察氏");
            yield return E("jin_hailing", "完颜亮", "金", "金", 1122, 1161, 1150, 40, "完颜宗干", "大氏");
            yield return E("jin_shizong", "完颜雍", "金", "金", 1123, 1189, 1161, 57, "完颜宗辅", "李氏");
            yield return E("jin_zhangzong", "完颜璟", "金", "金", 1168, 1208, 1189, 44, "完颜允恭", "徒单氏");
            yield return E("jin_weishao", "完颜永济", "金", "金", 1168, 1213, 1208, 22, "完颜世宗", "李氏");
            yield return E("jin_xuanzong", "完颜珣", "金", "金", 1163, 1224, 1213, 33, "完颜允恭", "王氏");
            yield return E("jin_aizong", "完颜守绪", "金", "金", 1198, 1234, 1224, 38, "完颜珣", "王皇后");
            yield return E("jin_modi", "完颜承麟", "金", "金", 1202, 1234, 1234, 18, "不详", "不详");

            yield return E("yuan_taizu", "铁木真", "元", "元", 1162, 1227, 1206, 98, "也速该", "诃额仑", "统一蒙古诸部并建立横跨欧亚的蒙古帝国。");
            yield return E("yuan_taizong", "窝阔台", "元", "元", 1186, 1241, 1229, 70, "铁木真", "孛儿帖");
            yield return E("yuan_dingzong", "贵由", "元", "元", 1206, 1248, 1246, 34, "窝阔台", "乃马真后");
            yield return E("yuan_xianzong", "蒙哥", "元", "元", 1209, 1259, 1251, 51, "拖雷", "唆鲁禾帖尼");
            yield return E("yuan_shizu", "忽必烈", "元", "元", 1215, 1294, 1271, 98, "拖雷", "唆鲁禾帖尼", "建立元朝并完成对中国大部的征服，建立多民族大一统王朝。");
            yield return E("yuan_chengzong", "铁穆耳", "元", "元", 1265, 1307, 1294, 48, "真金", "阔阔真");
            yield return E("yuan_wuzong", "海山", "元", "元", 1281, 1311, 1307, 34, "答剌麻八剌", "答己");
            yield return E("yuan_renzong", "爱育黎拔力八达", "元", "元", 1285, 1320, 1311, 46, "答剌麻八剌", "答己");
            yield return E("yuan_yingzong", "硕德八剌", "元", "元", 1303, 1323, 1320, 27, "爱育黎拔力八达", "阿纳失失里");
            yield return E("yuan_taiding", "也孙铁木儿", "元", "元", 1293, 1328, 1323, 28, "甘麻剌", "不详");
            yield return E("yuan_tianzhen", "阿速吉八", "元", "元", 1320, 1328, 1328, 17, "也孙铁木儿", "不详");
            yield return E("yuan_wenzong", "图帖睦尔", "元", "元", 1304, 1332, 1328, 30, "海山", "唐兀氏");
            yield return E("yuan_mingzong", "和世㻋", "元", "元", 1300, 1329, 1329, 21, "海山", "迈来迪");
            yield return E("yuan_ningzong", "懿璘质班", "元", "元", 1326, 1332, 1332, 17, "和世㻋", "八不沙");
            yield return E("yuan_shundi", "妥懽帖睦尔", "元", "元", 1320, 1370, 1333, 58, "图帖睦尔", "不详");

            yield return E("ming_taizu", "朱元璋", "明", "明", 1328, 1398, 1368, 99, "朱世珍", "陈氏", "推翻元朝建立明朝，重建中央集权国家体系。");
            yield return E("ming_huidi", "朱允炆", "明", "明", 1377, 1402, 1398, 45, "朱标", "吕氏");
            yield return E("ming_chengzu", "朱棣", "明", "明", 1360, 1424, 1402, 88, "朱元璋", "马皇后", "通过靖难夺取帝位，迁都北京并推动郑和下西洋。");
            yield return E("ming_renzong", "朱高炽", "明", "明", 1378, 1425, 1424, 54, "朱棣", "徐皇后");
            yield return E("ming_xuanzong", "朱瞻基", "明", "明", 1399, 1435, 1425, 66, "朱高炽", "张氏");
            yield return E("ming_yingzong", "朱祁镇", "明", "明", 1427, 1464, 1435, 54, "朱瞻基", "孙皇后");
            yield return E("ming_daizong", "朱祁钰", "明", "明", 1428, 1457, 1449, 42, "朱瞻基", "吴贤妃");
            yield return E("ming_xianzong", "朱见深", "明", "明", 1447, 1487, 1464, 55, "朱祁镇", "孝肃皇后");
            yield return E("ming_xiaozong", "朱祐樘", "明", "明", 1470, 1505, 1487, 67, "朱见深", "纪淑妃");
            yield return E("ming_wuzong", "朱厚照", "明", "明", 1491, 1521, 1505, 57, "朱祐樘", "张皇后");
            yield return E("ming_shizong", "朱厚熜", "明", "明", 1507, 1567, 1521, 61, "朱祐杬", "蒋氏");
            yield return E("ming_muzong", "朱载垕", "明", "明", 1537, 1572, 1567, 45, "朱厚熜", "杜康妃");
            yield return E("ming_shenzong", "朱翊钧", "明", "明", 1563, 1620, 1572, 75, "朱载垕", "李贵妃", "在位时间很长，晚明政治、财政和边疆格局在其时期发生重要变化。");
            yield return E("ming_guangzong", "朱常洛", "明", "明", 1582, 1620, 1620, 31, "朱翊钧", "王恭妃");
            yield return E("ming_xizong", "朱由校", "明", "明", 1605, 1627, 1620, 30, "朱常洛", "王才人");
            yield return E("ming_sizong", "朱由检", "明", "明", 1611, 1644, 1627, 60, "朱常洛", "刘妃", "明朝末代皇帝，面对内忧外患守国至最后阶段。");
            yield return E("nanming_hongguang", "朱由崧", "南明", "明", 1607, 1646, 1644, 34, "朱常洵", "姚氏");
            yield return E("nanming_longwu", "朱聿键", "南明", "明", 1602, 1646, 1645, 32, "朱器墭", "毛氏");
            yield return E("nanming_yongli", "朱由榔", "南明", "明", 1623, 1662, 1646, 48, "朱常瀛", "马氏");

            yield return E("qing_taizu", "努尔哈赤", "清", "清", 1559, 1626, 1616, 87, "塔克世", "喜塔腊氏", "统一女真各部并建立后金，为清朝入关奠定基础。");
            yield return E("qing_taizong", "皇太极", "清", "清", 1592, 1643, 1636, 74, "努尔哈赤", "叶赫那拉氏", "改国号为清并完善国家制度，推动清军入关前的国家转型。");
            yield return E("qing_shizu", "福临", "清", "清", 1638, 1661, 1644, 65, "皇太极", "孝庄文皇后");
            yield return E("qing_shengzu", "玄烨", "清", "清", 1654, 1722, 1661, 98, "福临", "佟佳氏", "平定三藩、收复台湾并稳定清朝早期统治，形成康熙盛世。");
            yield return E("qing_shizong", "胤禛", "清", "清", 1678, 1735, 1722, 78, "玄烨", "德妃");
            yield return E("qing_gaozong", "弘历", "清", "清", 1711, 1799, 1735, 90, "胤禛", "钮祜禄氏", "乾隆时期疆域与文化事业达到高峰，也埋下晚清财政与社会问题。");
            yield return E("qing_renzong", "颙琰", "清", "清", 1760, 1820, 1796, 51, "弘历", "魏佳氏");
            yield return E("qing_xuanzong", "旻宁", "清", "清", 1782, 1850, 1820, 53, "颙琰", "喜塔腊氏");
            yield return E("qing_wenzong", "奕詝", "清", "清", 1831, 1861, 1850, 42, "旻宁", "钮祜禄氏");
            yield return E("qing_muzong", "载淳", "清", "清", 1856, 1875, 1861, 42, "奕詝", "叶赫那拉氏");
            yield return E("qing_dezong", "载湉", "清", "清", 1871, 1908, 1875, 57, "奕譞", "叶赫那拉氏");
            yield return E("qing_xuantong", "溥仪", "清", "清", 1906, 1967, 1908, 78, "载沣", "苏完瓜尔佳氏", "清朝末代皇帝，也是中国帝制时代最后一位君主。");

            yield return E("mengpai", "\u8499\u6d3e", "\u5927\u6c49",
                "\u7334\u6c49", UnknownYear, UnknownYear, UnknownYear, 100,
                "", "",
                "\u4f5c\u8005\u5316\u8eab\uff0c\u4ece\u5357\u6d0b\u6253\u56de\u795e\u5dde\u7684\u5f00\u62d3\u8005\uff0c\u7387\u4f17\u5efa\u7acb\u7334\u6c49\u5e1d\u56fd\uff0c\u5e76\u4ee5\u5927\u6c49\u4e4b\u540d\u91cd\u6574\u5929\u4e0b\u3002",
                pHistoricalEra: "\u5927\u6c49");
        }
    }
}
