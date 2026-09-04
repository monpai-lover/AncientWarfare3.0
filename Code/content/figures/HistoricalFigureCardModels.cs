using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.content.figures
{
    public enum HistoricalFigureCardRole
    {
        Monarch,
        Minister
    }

    public enum HistoricalFigureCardMinisterType
    {
        None,
        CivilOfficial,
        MilitaryGeneral
    }

    /// <summary>
    /// Stable card rarity. The values are data objects rather than enum values so
    /// the UI and persistence layer can share the same probability/color metadata.
    /// </summary>
    public sealed class HistoricalFigureCardRarity : IEquatable<HistoricalFigureCardRarity>
    {
        public static readonly HistoricalFigureCardRarity Gold =
            new HistoricalFigureCardRarity("gold", "金", "帝统", "#FFD700", 0.0026f);
        public static readonly HistoricalFigureCardRarity Red =
            new HistoricalFigureCardRarity("red", "红", "雄主", "#eb4b4b", 0.0064f);
        public static readonly HistoricalFigureCardRarity Pink =
            new HistoricalFigureCardRarity("pink", "粉", "显赫", "#d32ce6", 0.0320f);
        public static readonly HistoricalFigureCardRarity Purple =
            new HistoricalFigureCardRarity("purple", "紫", "名世", "#8847ff", 0.1598f);
        public static readonly HistoricalFigureCardRarity Blue =
            new HistoricalFigureCardRarity("blue", "蓝", "史载", "#4b69ff", 0.7992f);

        public static readonly IReadOnlyList<HistoricalFigureCardRarity> All =
            new[] { Gold, Red, Pink, Purple, Blue };

        private HistoricalFigureCardRarity(string pId, string pShortName,
            string pDisplayName, string pColorHex, float pProbability)
        {
            Id = pId;
            ShortName = pShortName;
            DisplayName = pDisplayName;
            ColorHex = pColorHex;
            Probability = pProbability;
        }

        public string Id { get; }
        public string ShortName { get; }
        public string DisplayName { get; }
        public string ColorHex { get; }
        public float Probability { get; }
        public static float TotalProbability => All.Sum(p => p.Probability);

        public static HistoricalFigureCardRarity FromId(string pId)
        {
            if (string.IsNullOrEmpty(pId)) return null;
            return All.FirstOrDefault(p =>
                string.Equals(p.Id, pId, StringComparison.OrdinalIgnoreCase));
        }

        public bool Equals(HistoricalFigureCardRarity pOther)
        {
            return pOther != null && string.Equals(Id, pOther.Id,
                StringComparison.Ordinal);
        }

        public override bool Equals(object pObject)
        {
            return Equals(pObject as HistoricalFigureCardRarity);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Id);
        }

        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>
    /// Player-facing historical identity. This model is deliberately separate
    /// from HistoricalFigureDef so automatic spawn slots remain unchanged.
    /// </summary>
    public sealed class HistoricalFigureCardDefinition
    {
        public HistoricalFigureCardDefinition(string pCardId, string pDisplayName,
            string pFamilyName, string pClanName, string pGivenName,
            string pDynastyName, string pHistoricalKingdomName,
            string pHistoricalEra, int pBirthYear, int pDeathYear,
            int pHistoricalYear, int pFameScore,
            HistoricalFigureCardRarity pRarity, HistoricalFigureSex pSex,
            string pBiography, string pFatherCardId, string pFatherDisplayName,
            string pMotherCardId, string pMotherDisplayName, string pPortraitPath,
            string pLegacyFigureId, int pLegacyRegistryIndex, int pCombatHealth,
            IEnumerable<string> pCombatTraits, string pBackgroundSummary = "",
            string pDetailedBiography = "",
            HistoricalFigureCardRole pRole = HistoricalFigureCardRole.Monarch,
            HistoricalFigureCardMinisterType pMinisterType =
                HistoricalFigureCardMinisterType.None,
            string pCollectionId = "")
        {
            CardId = pCardId ?? "";
            DisplayName = pDisplayName ?? "";
            FamilyName = pFamilyName ?? "";
            ClanName = pClanName ?? "";
            GivenName = pGivenName ?? "";
            DynastyName = pDynastyName ?? "";
            HistoricalKingdomName = pHistoricalKingdomName ?? "";
            HistoricalEra = pHistoricalEra ?? "";
            BirthYear = pBirthYear;
            DeathYear = pDeathYear;
            HistoricalYear = pHistoricalYear;
            FameScore = pFameScore;
            Rarity = pRarity;
            Sex = pSex;
            Biography = pBiography ?? "";
            BackgroundSummary = pBackgroundSummary ?? "";
            DetailedBiography = string.IsNullOrWhiteSpace(pDetailedBiography)
                ? Biography : pDetailedBiography;
            Role = pRole;
            MinisterType = pMinisterType;
            CollectionId = pCollectionId ?? "";
            FatherCardId = pFatherCardId ?? "";
            FatherDisplayName = pFatherDisplayName ?? "";
            MotherCardId = pMotherCardId ?? "";
            MotherDisplayName = pMotherDisplayName ?? "";
            PortraitPath = pPortraitPath ?? "";
            LegacyFigureId = pLegacyFigureId;
            LegacyRegistryIndex = pLegacyRegistryIndex;
            CombatHealth = pCombatHealth;
            CombatTraits = (pCombatTraits ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrEmpty(p)).ToArray();
        }

        public string CardId { get; }
        public string DisplayName { get; }
        public string FamilyName { get; }
        public string ClanName { get; }
        public string GivenName { get; }
        public string DynastyName { get; }
        public string HistoricalKingdomName { get; }
        public string HistoricalEra { get; }
        public int BirthYear { get; }
        public int DeathYear { get; }
        public int HistoricalYear { get; }
        public int FameScore { get; }
        public HistoricalFigureCardRarity Rarity { get; }
        public HistoricalFigureSex Sex { get; }
        public string Biography { get; }
        public string BackgroundSummary { get; }
        public string DetailedBiography { get; }
        public HistoricalFigureCardRole Role { get; }
        public HistoricalFigureCardMinisterType MinisterType { get; }
        public string CollectionId { get; }
        public bool IsMilitaryGeneral =>
            Role == HistoricalFigureCardRole.Minister &&
            MinisterType == HistoricalFigureCardMinisterType.MilitaryGeneral;
        public string FatherCardId { get; }
        public string FatherDisplayName { get; }
        public string MotherCardId { get; }
        public string MotherDisplayName { get; }
        public string PortraitPath { get; }
        public string LegacyFigureId { get; }
        public int LegacyRegistryIndex { get; }
        public int CombatHealth { get; }
        public IReadOnlyList<string> CombatTraits { get; }

        public bool ParentReferencesAreValid(IEnumerable<HistoricalFigureCardDefinition> pCards)
        {
            var ids = new HashSet<string>((pCards ?? Enumerable.Empty<HistoricalFigureCardDefinition>())
                .Where(p => p != null && !string.IsNullOrEmpty(p.CardId))
                .Select(p => p.CardId), StringComparer.Ordinal);
            return (string.IsNullOrEmpty(FatherCardId) || ids.Contains(FatherCardId)) &&
                   (string.IsNullOrEmpty(MotherCardId) || ids.Contains(MotherCardId));
        }

        public string ParentDisplayName(bool pFather)
        {
            return HistoricalFigureCardCatalog.ParentDisplayName(
                pFather ? FatherDisplayName : MotherDisplayName);
        }
    }
}
