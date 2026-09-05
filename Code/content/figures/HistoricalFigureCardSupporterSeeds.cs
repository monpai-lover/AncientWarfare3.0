using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AncientWarfare3.content.supporters;

namespace AncientWarfare3.content.figures
{
    public static class HistoricalFigureCardSupporterSeeds
    {
        public const string CollectionId = "supporters";

        public static readonly IReadOnlyList<HistoricalFigureCardDefinition> All =
            Build(SupporterRosterData.Read());

        public static IReadOnlyList<HistoricalFigureCardDefinition> Build(
            IEnumerable<SupporterRosterEntry> pEntries)
        {
            return (pEntries ?? Enumerable.Empty<SupporterRosterEntry>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(BuildCard)
                .OrderBy(p => p.CardId, StringComparer.Ordinal)
                .ToArray();
        }

        private static HistoricalFigureCardDefinition BuildCard(
            IGrouping<string, SupporterRosterEntry> pGroup)
        {
            SupporterRosterEntry[] records = pGroup
                .OrderBy(p => p.Rank)
                .ThenBy(p => p.Date, StringComparer.Ordinal)
                .ToArray();
            string name = records[0].Name.Trim();
            string detail = BuildBiography(name, records);
            string family = StringInfo.GetNextTextElement(name, 0);
            string given = name.Substring(family.Length);
            return new HistoricalFigureCardDefinition(
                StableCardId(name), name, family, family, given,
                "赞助者", "赞助者", "赞助者",
                HistoricalFigureCardCatalog.UnknownYear,
                HistoricalFigureCardCatalog.UnknownYear,
                HistoricalFigureCardCatalog.UnknownYear, 80,
                HistoricalFigureCardRarity.Pink, HistoricalFigureSex.Male,
                detail, "", "", "", "", "", "", -1, 5000,
                Array.Empty<string>(),
                name + "是 Ancient Warfare 3 的赞助者与贡献者。",
                detail, HistoricalFigureCardRole.Minister,
                HistoricalFigureCardMinisterType.CivilOfficial, CollectionId);
        }

        private static string BuildBiography(string pName,
            IEnumerable<SupporterRosterEntry> pRecords)
        {
            string[] supportRecords = pRecords
                .Where(p => !string.IsNullOrWhiteSpace(p.Amount))
                .Select(p => string.IsNullOrWhiteSpace(p.Date)
                    ? "¥" + p.Amount.Trim()
                    : "¥" + p.Amount.Trim() + "（" + p.Date.Trim() + "）")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            string[] contributions = pRecords
                .Where(p => !string.IsNullOrWhiteSpace(p.Description))
                .Select(p => p.Description.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var parts = new List<string>
            {
                pName + "支持了 Ancient Warfare 3 的持续开发，并作为赞助者彩蛋人物被收录进历史人物卡池。部署后会以大臣身份进入目标国家，不改变该国国号。"
            };
            if (supportRecords.Length > 0)
                parts.Add("赞助记录：" + string.Join("；", supportRecords) + "。");
            if (contributions.Length > 0)
                parts.Add("贡献记录：" + string.Join("；", contributions) + "。");
            return string.Join("\n", parts);
        }

        private static string StableCardId(string pName)
        {
            string normalized = (pName ?? "").Trim()
                .Normalize(NormalizationForm.FormKC).ToUpperInvariant();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var hex = new StringBuilder(16);
                for (int i = 0; i < 8; i++) hex.Append(hash[i].ToString("x2"));
                return "supporter_" + hex;
            }
        }
    }
}
