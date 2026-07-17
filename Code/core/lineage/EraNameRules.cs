using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum EraChangeKind
    {
        Accession,
        Voluntary,
        AiMajorEvent
    }

    public enum EraChangeBlockReason
    {
        None,
        NotHereditaryEmperor,
        BelowEmpireRank,
        NotIndependent,
        AtWar,
        Cooldown,
        InsufficientPoliticalPoints,
        InvalidName,
        DuplicateName,
        ArchiveUnavailable,
        MissingLineageIdentity,
        MissingReign,
        PersistenceFailed
    }

    public enum EraChangeReason
    {
        None,
        Accession,
        RestoredMandate,
        AutonomousRestoration,
        MajorVictory,
        CapitalRecovered,
        LegalCoreRecovered,
        EnteredRevival,
        CentralReform,
        CapitalRelocated,
        GrandSacrificeBlessing,
        PlayerRequested
    }

    public sealed class EraChangeContext
    {
        public bool IsHereditaryEmperor;
        public bool IsEmpireRank;
        public bool IsIndependent;
        public bool AtWar;
        public int PoliticalPoints;
        public int YearsSinceVoluntaryChange;
        public string Candidate = "";
        public ISet<string> UsedNames = new HashSet<string>(StringComparer.Ordinal);
    }

    public readonly struct EraChangeResult
    {
        public readonly bool Success;
        public readonly long EraId;
        public readonly string EraName;
        public readonly EraChangeBlockReason BlockReason;

        public EraChangeResult(bool pSuccess, long pEraId, string pEraName,
            EraChangeBlockReason pBlockReason)
        {
            Success = pSuccess;
            EraId = pEraId;
            EraName = pEraName ?? "";
            BlockReason = pBlockReason;
        }

        public static EraChangeResult Blocked(EraChangeBlockReason pReason)
        {
            return new EraChangeResult(false, -1, "", pReason);
        }
    }

    public readonly struct EffectiveChronology
    {
        public readonly long SourceKingdomId;
        public readonly string EraName;
        public readonly string YearText;
        public readonly bool UsesSuzerain;

        public EffectiveChronology(long pSourceKingdomId, string pEraName,
            string pYearText, bool pUsesSuzerain)
        {
            SourceKingdomId = pSourceKingdomId;
            EraName = pEraName ?? "";
            YearText = pYearText ?? "";
            UsesSuzerain = pUsesSuzerain;
        }
    }

    public readonly struct ChronologySourceChoice
    {
        public readonly long SourceKingdomId;
        public readonly bool UsesSuzerain;

        public ChronologySourceChoice(long pSourceKingdomId, bool pUsesSuzerain)
        {
            SourceKingdomId = pSourceKingdomId;
            UsesSuzerain = pUsesSuzerain;
        }
    }

    public static class EraNameRules
    {
        public static readonly IReadOnlyList<string> HistoricalSlots = new[]
        {
            "仁寿", "大业", "义宁", "龙化", "五凤", "天成", "应顺", "泰兴", "天福", "武德",
            "广顺", "显德", "建兴", "永徽", "乾封", "上元", "永淳", "弘道", "咸亨", "景隆",
            "太极", "乾元", "广德", "明道", "兴元", "贞元", "开成", "乾符", "广明", "嘉佑",
            "熙宁", "元丰", "天禧", "中和", "光启", "文德", "龙纪", "大顺", "光化", "天祐",
            "开平", "乾化", "贞明", "龙德", "同光", "天成", "长兴", "应顺", "清泰", "天福",
            "开运", "黄统", "中兴", "明昌", "开兴", "天宝", "交泰", "光天", "龙启", "武成",
            "广大", "乾德", "明德", "广政", "建隆", "开宝", "太平兴国", "端拱", "淳化", "至道",
            "天圣", "景祐", "康定", "庆历", "至和", "嘉祐", "治平", "元祐", "绍圣", "建中靖国",
            "咸通", "淳熙"
        };

        private static readonly char[] CompositionCharacters =
        {
            '建', '元', '天', '太', '景', '嘉', '永', '和', '平', '光',
            '兴', '隆', '宁', '康', '贞', '顺', '昌', '德', '宣', '武',
            '文', '成', '昭', '明', '章', '安', '定', '靖', '崇', '正',
            '乾', '泰', '延', '咸', '通', '丰', '熙', '祐', '弘', '至'
        };

        private static readonly string[] Digits =
            { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
        private static readonly string[] SmallUnits = { "", "十", "百", "千" };
        private static readonly string[] SectionUnits = { "", "万", "亿", "兆" };

        public static ulong StableHash(long shiId, long actorId, int reignIndex,
            string pSalt)
        {
            ulong value = 1469598103934665603UL;
            value = Add(value, unchecked((ulong)shiId));
            value = Add(value, unchecked((ulong)actorId));
            value = Add(value, unchecked((ulong)reignIndex));
            string salt = pSalt ?? "";
            foreach (char character in salt)
                value = Add(value, character);
            return Mix(value);
        }

        public static string SelectAutomatic(long shiId, long actorId, int reignIndex,
            IEnumerable<string> used)
        {
            var usedNames = used == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(used, StringComparer.Ordinal);
            ulong seed = StableHash(shiId, actorId, reignIndex, "era");
            bool historicalFirst = seed % 100UL < 80UL;
            string selected = historicalFirst
                ? SelectHistorical(seed, usedNames)
                : SelectComposition(seed, usedNames);
            if (!string.IsNullOrEmpty(selected)) return selected;
            selected = historicalFirst
                ? SelectComposition(seed, usedNames)
                : SelectHistorical(seed, usedNames);
            if (!string.IsNullOrEmpty(selected)) return selected;
            return HistoricalSlots.Count == 0 ? "" : HistoricalSlots[(int)(seed % (ulong)HistoricalSlots.Count)];
        }

        public static bool IsValidCustom(string pValue)
        {
            if (string.IsNullOrEmpty(pValue) || pValue.Length < 2 || pValue.Length > 4)
                return false;
            if (!string.Equals(pValue, pValue.Trim(), StringComparison.Ordinal)) return false;
            foreach (char character in pValue)
                if (!IsHanCharacter(character)) return false;
            return true;
        }

        public static string FormatYear(int pYear)
        {
            return pYear <= 1 ? "元年" : ToChineseNumber(pYear) + "年";
        }

        public static ChronologySourceChoice ResolveChronologySource(
            long localKingdomId, bool isEmpireRank, long rootSuzerainId,
            bool rootHasCommittedEra)
        {
            bool useRoot = !isEmpireRank && rootHasCommittedEra &&
                           rootSuzerainId >= 0 &&
                           rootSuzerainId != localKingdomId;
            return new ChronologySourceChoice(
                useRoot ? rootSuzerainId : localKingdomId, useRoot);
        }

        public static bool CanExposeChronology(bool isHereditaryMonarchy,
            bool isEmpireRank)
        {
            return isHereditaryMonarchy && isEmpireRank;
        }

        public static EraChangeBlockReason ValidateVoluntaryChange(EraChangeContext pContext)
        {
            if (pContext == null || !pContext.IsHereditaryEmperor)
                return EraChangeBlockReason.NotHereditaryEmperor;
            if (!pContext.IsEmpireRank) return EraChangeBlockReason.BelowEmpireRank;
            if (!pContext.IsIndependent) return EraChangeBlockReason.NotIndependent;
            if (pContext.AtWar) return EraChangeBlockReason.AtWar;
            if (pContext.YearsSinceVoluntaryChange < 10)
                return EraChangeBlockReason.Cooldown;
            if (pContext.PoliticalPoints < 30)
                return EraChangeBlockReason.InsufficientPoliticalPoints;
            return ValidateCandidate(pContext);
        }

        public static EraChangeBlockReason ValidateAccessionChange(EraChangeContext pContext)
        {
            if (pContext == null || !pContext.IsHereditaryEmperor)
                return EraChangeBlockReason.NotHereditaryEmperor;
            if (!pContext.IsEmpireRank) return EraChangeBlockReason.BelowEmpireRank;
            return ValidateCandidate(pContext);
        }

        public static EraChangeBlockReason Validate(EraChangeContext pContext,
            EraChangeKind pKind)
        {
            return pKind == EraChangeKind.Accession
                ? ValidateAccessionChange(pContext)
                : ValidateVoluntaryChange(pContext);
        }

        public static bool IsMajorAiReason(EraChangeReason pReason)
        {
            switch (pReason)
            {
                case EraChangeReason.RestoredMandate:
                case EraChangeReason.AutonomousRestoration:
                case EraChangeReason.MajorVictory:
                case EraChangeReason.CapitalRecovered:
                case EraChangeReason.LegalCoreRecovered:
                case EraChangeReason.EnteredRevival:
                case EraChangeReason.CentralReform:
                case EraChangeReason.CapitalRelocated:
                case EraChangeReason.GrandSacrificeBlessing:
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldAiConsider(EraChangeReason pReason,
            bool alreadyCheckedThisYear)
        {
            return !alreadyCheckedThisYear && IsMajorAiReason(pReason);
        }

        public static EraChangeReason StrongerReason(EraChangeReason pCurrent,
            EraChangeReason pCandidate)
        {
            return ReasonPriority(pCandidate) > ReasonPriority(pCurrent)
                ? pCandidate
                : pCurrent;
        }

        public static bool IsTerminalAiBlock(EraChangeBlockReason pReason)
        {
            switch (pReason)
            {
                case EraChangeBlockReason.NotHereditaryEmperor:
                case EraChangeBlockReason.BelowEmpireRank:
                case EraChangeBlockReason.NotIndependent:
                case EraChangeBlockReason.InvalidName:
                case EraChangeBlockReason.DuplicateName:
                case EraChangeBlockReason.MissingLineageIdentity:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsCentralReform(string pPolicyId)
        {
            switch (pPolicyId ?? "")
            {
                case "aw_tech_official_court":
                case "aw_policy_early_law":
                case "aw_policy_imperial_court":
                case "aw_policy_xia_law_institutions":
                    return true;
                default:
                    return false;
            }
        }

        private static int ReasonPriority(EraChangeReason pReason)
        {
            switch (pReason)
            {
                case EraChangeReason.RestoredMandate: return 800;
                case EraChangeReason.AutonomousRestoration: return 750;
                case EraChangeReason.CapitalRecovered: return 700;
                case EraChangeReason.LegalCoreRecovered: return 650;
                case EraChangeReason.MajorVictory: return 600;
                case EraChangeReason.EnteredRevival: return 500;
                case EraChangeReason.CentralReform: return 400;
                case EraChangeReason.CapitalRelocated: return 300;
                case EraChangeReason.GrandSacrificeBlessing: return 200;
                default: return 0;
            }
        }

        private static EraChangeBlockReason ValidateCandidate(EraChangeContext pContext)
        {
            if (!IsValidCustom(pContext.Candidate)) return EraChangeBlockReason.InvalidName;
            return pContext.UsedNames != null && pContext.UsedNames.Contains(pContext.Candidate)
                ? EraChangeBlockReason.DuplicateName
                : EraChangeBlockReason.None;
        }

        private static string SelectHistorical(ulong pSeed, ISet<string> pUsed)
        {
            if (HistoricalSlots.Count == 0) return "";
            int start = (int)(Mix(pSeed ^ 0x9E3779B97F4A7C15UL) %
                              (ulong)HistoricalSlots.Count);
            for (int offset = 0; offset < HistoricalSlots.Count; offset++)
            {
                string candidate = HistoricalSlots[(start + offset) % HistoricalSlots.Count];
                if (!IsValidCustom(candidate) || pUsed.Contains(candidate)) continue;
                return candidate;
            }
            return "";
        }

        private static string SelectComposition(ulong pSeed, ISet<string> pUsed)
        {
            int count = CompositionCharacters.Length;
            if (count < 2) return "";
            int pairCount = count * (count - 1);
            int start = (int)(Mix(pSeed ^ 0xD1B54A32D192ED03UL) % (ulong)pairCount);
            for (int offset = 0; offset < pairCount; offset++)
            {
                int pairIndex = (start + offset) % pairCount;
                int first = pairIndex / (count - 1);
                int second = pairIndex % (count - 1);
                if (second >= first) second++;
                string candidate = new string(new[]
                    { CompositionCharacters[first], CompositionCharacters[second] });
                if (!pUsed.Contains(candidate)) return candidate;
            }
            return "";
        }

        private static bool IsHanCharacter(char pCharacter)
        {
            return pCharacter >= '\u3400' && pCharacter <= '\u4DBF' ||
                   pCharacter >= '\u4E00' && pCharacter <= '\u9FFF' ||
                   pCharacter >= '\uF900' && pCharacter <= '\uFAFF';
        }

        private static string ToChineseNumber(int pValue)
        {
            if (pValue <= 0) return Digits[0];
            long value = pValue;
            string result = "";
            int sectionIndex = 0;
            bool zeroBetweenSections = false;
            while (value > 0)
            {
                int section = (int)(value % 10000L);
                if (section == 0)
                {
                    if (result.Length > 0) zeroBetweenSections = true;
                }
                else
                {
                    string sectionText = SectionToChinese(section) + SectionUnits[sectionIndex];
                    if (result.Length > 0 && (zeroBetweenSections || section < 1000))
                        sectionText += "零";
                    result = sectionText + result;
                    zeroBetweenSections = false;
                }
                value /= 10000L;
                sectionIndex++;
            }
            return result.StartsWith("一十", StringComparison.Ordinal)
                ? result.Substring(1)
                : result;
        }

        private static string SectionToChinese(int pSection)
        {
            string result = "";
            bool pendingZero = false;
            int unit = 0;
            int section = pSection;
            while (section > 0)
            {
                int digit = section % 10;
                if (digit == 0)
                {
                    if (result.Length > 0) pendingZero = true;
                }
                else
                {
                    string part = Digits[digit] + SmallUnits[unit];
                    if (pendingZero) part += "零";
                    result = part + result;
                    pendingZero = false;
                }
                section /= 10;
                unit++;
            }
            return result;
        }

        private static ulong Add(ulong pHash, ulong pValue)
        {
            unchecked
            {
                pHash ^= pValue;
                return pHash * 1099511628211UL;
            }
        }

        private static ulong Mix(ulong pValue)
        {
            unchecked
            {
                pValue ^= pValue >> 30;
                pValue *= 0xBF58476D1CE4E5B9UL;
                pValue ^= pValue >> 27;
                pValue *= 0x94D049BB133111EBUL;
                return pValue ^ (pValue >> 31);
            }
        }
    }
}
