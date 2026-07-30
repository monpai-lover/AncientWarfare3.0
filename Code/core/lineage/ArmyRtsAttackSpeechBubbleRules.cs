using System;
using System.Collections.Generic;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsAttackSpeechContext
    {
        GeneralCombat = 0,
        Assault = 1,
        CrossingWater = 2,
        Retreat = 3,
        Defense = 4,
        Pursuit = 5
    }

    public enum ArmyRtsAttackSpeechLine
    {
        None = -1,
        LastStand = 0,
        GreatAxe = 1,
        CalmWisdom = 2,
        Impossible = 3,
        CrossingRiver = 4,
        ProudArmy = 5,
        NoCourtesy = 6,
        BrotherInvincible = 7,
        XuzhouPass = 8,
        ThrowOut = 9,
        LookAtYourHead = 10,
        CaoTraitor = 11,
        ThreeSurnameSlave = 12,
        FourFathers = 13,
        DongZhuoTraitor = 14,
        DeathUnclear = 15,
        CommandTable = 16,
        FiveHundredYears = 17,
        NoSecondAmbush = 18,
        TenMyriadTroops = 19,
        XuHuangHeroes = 20,
        LiuSandao = 21,
        PanFeng = 22,
        DragonTiger = 23
    }

    public readonly struct ArmyRtsAttackSpeechEventKey :
        IEquatable<ArmyRtsAttackSpeechEventKey>
    {
        public ArmyRtsAttackSpeechEventKey(long pArmyId, long pWarId,
            long pTargetCityId, double pIssuedTime)
        {
            ArmyId = pArmyId;
            WarId = pWarId;
            TargetCityId = pTargetCityId;
            IssuedTime = pIssuedTime;
        }

        public long ArmyId { get; }
        public long WarId { get; }
        public long TargetCityId { get; }
        public double IssuedTime { get; }

        public bool IsValid => ArmyId >= 0L && WarId >= 0L &&
                               TargetCityId >= 0L && IssuedTime >= 0d &&
                               !double.IsNaN(IssuedTime) &&
                               !double.IsInfinity(IssuedTime);

        public bool HasValidCoordinates => ArmyId >= 0L && WarId >= 0L &&
                                           TargetCityId >= 0L;

        public bool Equals(ArmyRtsAttackSpeechEventKey pOther)
        {
            return ArmyId == pOther.ArmyId && WarId == pOther.WarId &&
                   TargetCityId == pOther.TargetCityId &&
                   IssuedTime.Equals(pOther.IssuedTime);
        }

        public override bool Equals(object pObject)
        {
            return pObject is ArmyRtsAttackSpeechEventKey other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + ArmyId.GetHashCode();
                hash = hash * 31 + WarId.GetHashCode();
                hash = hash * 31 + TargetCityId.GetHashCode();
                hash = hash * 31 + IssuedTime.GetHashCode();
                return hash;
            }
        }
    }

    public static class ArmyRtsAttackSpeechBubbleRules
    {
        private static readonly ArmyRtsAttackSpeechLine[] AssaultLines =
        {
            ArmyRtsAttackSpeechLine.LastStand,
            ArmyRtsAttackSpeechLine.GreatAxe,
            ArmyRtsAttackSpeechLine.NoCourtesy,
            ArmyRtsAttackSpeechLine.XuzhouPass,
            ArmyRtsAttackSpeechLine.FiveHundredYears,
            ArmyRtsAttackSpeechLine.TenMyriadTroops,
            ArmyRtsAttackSpeechLine.LiuSandao,
            ArmyRtsAttackSpeechLine.PanFeng,
            ArmyRtsAttackSpeechLine.DragonTiger
        };

        private static readonly ArmyRtsAttackSpeechLine[] DefenseLines =
        {
            ArmyRtsAttackSpeechLine.LastStand,
            ArmyRtsAttackSpeechLine.BrotherInvincible,
            ArmyRtsAttackSpeechLine.ProudArmy,
            ArmyRtsAttackSpeechLine.CommandTable,
            ArmyRtsAttackSpeechLine.NoSecondAmbush,
            ArmyRtsAttackSpeechLine.XuHuangHeroes
        };

        private static readonly ArmyRtsAttackSpeechLine[] PursuitLines =
        {
            ArmyRtsAttackSpeechLine.ThrowOut,
            ArmyRtsAttackSpeechLine.LookAtYourHead,
            ArmyRtsAttackSpeechLine.CaoTraitor,
            ArmyRtsAttackSpeechLine.ThreeSurnameSlave,
            ArmyRtsAttackSpeechLine.FourFathers,
            ArmyRtsAttackSpeechLine.DongZhuoTraitor,
            ArmyRtsAttackSpeechLine.NoCourtesy
        };

        private static readonly ArmyRtsAttackSpeechLine[] RetreatLines =
        {
            ArmyRtsAttackSpeechLine.Impossible,
            ArmyRtsAttackSpeechLine.CalmWisdom,
            ArmyRtsAttackSpeechLine.ProudArmy,
            ArmyRtsAttackSpeechLine.DeathUnclear,
            ArmyRtsAttackSpeechLine.NoSecondAmbush
        };

        private static readonly ArmyRtsAttackSpeechLine[] CrossingWaterLines =
        {
            ArmyRtsAttackSpeechLine.CrossingRiver
        };

        private static readonly ArmyRtsAttackSpeechLine[] GeneralCombatLines =
        {
            ArmyRtsAttackSpeechLine.LastStand,
            ArmyRtsAttackSpeechLine.GreatAxe,
            ArmyRtsAttackSpeechLine.BrotherInvincible,
            ArmyRtsAttackSpeechLine.CaoTraitor,
            ArmyRtsAttackSpeechLine.ThreeSurnameSlave,
            ArmyRtsAttackSpeechLine.DongZhuoTraitor,
            ArmyRtsAttackSpeechLine.DeathUnclear,
            ArmyRtsAttackSpeechLine.LiuSandao,
            ArmyRtsAttackSpeechLine.PanFeng,
            ArmyRtsAttackSpeechLine.DragonTiger
        };

        public const double CaptainCooldownSeconds = 20d;
        public const double GlobalEmissionIntervalSeconds = 3d;
        public const int MaximumActiveBubbles = 2;
        public const float ScanIntervalSeconds = 0.35f;
        public const float DisplayDurationSeconds = 3f;
        public const int MaximumVisibleCaptainsScanned = 64;
        public const float BubbleBaseScale = 0.16f;
        public const float TextLocalX = -1.8f;
        public const float TextLocalY = 9.59f;

        public static bool IsExpired(double expiresAt,
            double simulationTime)
        {
            return simulationTime >= expiresAt;
        }

        public static double AdvanceActivePlayTime(double currentTime,
            double unscaledDeltaSeconds, bool worldLoaded, bool paused)
        {
            if (!worldLoaded || paused || currentTime < 0d ||
                double.IsNaN(currentTime) || double.IsInfinity(currentTime) ||
                unscaledDeltaSeconds <= 0d ||
                double.IsNaN(unscaledDeltaSeconds) ||
                double.IsInfinity(unscaledDeltaSeconds)) return currentTime;
            return currentTime + unscaledDeltaSeconds;
        }

        public static bool ShouldSuppressPresentation(
            bool renderingMiniMap)
        {
            return renderingMiniMap;
        }

        public static bool IsEligible(bool talkBubblesEnabled,
            bool captainAlive, long captainId, long armyId, long warId,
            long targetCityId, ArmyRtsState state,
            ArmyRtsProposalKind proposalKind, ArmyRtsRole role,
            bool captainInCombat = false)
        {
            if (!talkBubblesEnabled || !captainAlive || captainId < 0L ||
                armyId < 0L || warId < 0L || targetCityId < 0L) return false;

            bool assault = state == ArmyRtsState.Assault &&
                           proposalKind == ArmyRtsProposalKind.Attack &&
                           role == ArmyRtsRole.Assault;
            bool retreat = state == ArmyRtsState.Retreat &&
                           proposalKind == ArmyRtsProposalKind.Retreat;
            bool pursuit = state == ArmyRtsState.Pursue;
            bool defense = captainInCombat &&
                           (state == ArmyRtsState.Hold ||
                            proposalKind == ArmyRtsProposalKind.Defend ||
                            role == ArmyRtsRole.Defense);
            return assault || retreat || pursuit || defense;
        }

        public static bool IsSoldierCombatEligible(
            bool talkBubblesEnabled, bool soldierAlive, long soldierId,
            bool isMilitary, bool isArmyCaptain, bool inCombat)
        {
            return talkBubblesEnabled && soldierAlive && soldierId >= 0L &&
                   isMilitary && !isArmyCaptain && inCombat;
        }

        public static ArmyRtsAttackSpeechContext ClassifyContext(
            ArmyRtsState state, ArmyRtsProposalKind proposalKind,
            ArmyRtsRole role, bool activeVoyage,
            bool embarkedOrInsideBoat)
        {
            if (state == ArmyRtsState.Retreat ||
                proposalKind == ArmyRtsProposalKind.Retreat)
                return ArmyRtsAttackSpeechContext.Retreat;
            if (activeVoyage || embarkedOrInsideBoat)
                return ArmyRtsAttackSpeechContext.CrossingWater;
            if (state == ArmyRtsState.Pursue)
                return ArmyRtsAttackSpeechContext.Pursuit;
            if (state == ArmyRtsState.Hold ||
                proposalKind == ArmyRtsProposalKind.Defend ||
                role == ArmyRtsRole.Defense)
                return ArmyRtsAttackSpeechContext.Defense;
            if (state == ArmyRtsState.Assault &&
                proposalKind == ArmyRtsProposalKind.Attack &&
                role == ArmyRtsRole.Assault)
                return ArmyRtsAttackSpeechContext.Assault;
            return ArmyRtsAttackSpeechContext.GeneralCombat;
        }

        public static int LineCountFor(ArmyRtsAttackSpeechContext pContext)
        {
            return PoolFor(pContext).Length;
        }

        public static bool ContextContainsLine(
            ArmyRtsAttackSpeechContext pContext,
            ArmyRtsAttackSpeechLine pLine)
        {
            ArmyRtsAttackSpeechLine[] pool = PoolFor(pContext);
            for (int index = 0; index < pool.Length; index++)
                if (pool[index] == pLine) return true;
            return false;
        }

        public static ArmyRtsAttackSpeechLine SelectLine(
            ArmyRtsAttackSpeechContext pContext, int randomValue,
            ArmyRtsAttackSpeechLine previousLine)
        {
            int count = LineCountFor(pContext);
            if (count <= 0) return ArmyRtsAttackSpeechLine.None;
            if (count == 1) return LineAt(pContext, 0);

            bool skipPrevious = ContextContainsLine(pContext, previousLine);
            int availableCount = skipPrevious ? count - 1 : count;
            int selected = PositiveModulo(randomValue, availableCount);
            for (int index = 0; index < count; index++)
            {
                ArmyRtsAttackSpeechLine line = LineAt(pContext, index);
                if (skipPrevious && line == previousLine) continue;
                if (selected-- == 0) return line;
            }
            return ArmyRtsAttackSpeechLine.None;
        }

        public static string LocalizationKeyFor(
            ArmyRtsAttackSpeechLine pLine)
        {
            switch (pLine)
            {
                case ArmyRtsAttackSpeechLine.LastStand:
                    return "aw_army_rts_attack_oath";
                case ArmyRtsAttackSpeechLine.GreatAxe:
                    return "aw_army_rts_speech_great_axe";
                case ArmyRtsAttackSpeechLine.CalmWisdom:
                    return "aw_army_rts_speech_calm_wisdom";
                case ArmyRtsAttackSpeechLine.Impossible:
                    return "aw_army_rts_speech_impossible";
                case ArmyRtsAttackSpeechLine.CrossingRiver:
                    return "aw_army_rts_speech_crossing_river";
                case ArmyRtsAttackSpeechLine.ProudArmy:
                    return "aw_army_rts_speech_proud_army";
                case ArmyRtsAttackSpeechLine.NoCourtesy:
                    return "aw_army_rts_speech_no_courtesy";
                case ArmyRtsAttackSpeechLine.BrotherInvincible:
                    return "aw_army_rts_speech_brother_invincible";
                case ArmyRtsAttackSpeechLine.XuzhouPass:
                    return "aw_army_rts_speech_xuzhou_pass";
                case ArmyRtsAttackSpeechLine.ThrowOut:
                    return "aw_army_rts_speech_throw_out";
                case ArmyRtsAttackSpeechLine.LookAtYourHead:
                    return "aw_army_rts_speech_look_at_your_head";
                case ArmyRtsAttackSpeechLine.CaoTraitor:
                    return "aw_army_rts_speech_cao_traitor";
                case ArmyRtsAttackSpeechLine.ThreeSurnameSlave:
                    return "aw_army_rts_speech_three_surname_slave";
                case ArmyRtsAttackSpeechLine.FourFathers:
                    return "aw_army_rts_speech_four_fathers";
                case ArmyRtsAttackSpeechLine.DongZhuoTraitor:
                    return "aw_army_rts_speech_dong_zhuo_traitor";
                case ArmyRtsAttackSpeechLine.DeathUnclear:
                    return "aw_army_rts_speech_death_unclear";
                case ArmyRtsAttackSpeechLine.CommandTable:
                    return "aw_army_rts_speech_command_table";
                case ArmyRtsAttackSpeechLine.FiveHundredYears:
                    return "aw_army_rts_speech_five_hundred_years";
                case ArmyRtsAttackSpeechLine.NoSecondAmbush:
                    return "aw_army_rts_speech_no_second_ambush";
                case ArmyRtsAttackSpeechLine.TenMyriadTroops:
                    return "aw_army_rts_speech_ten_myriad_troops";
                case ArmyRtsAttackSpeechLine.XuHuangHeroes:
                    return "aw_army_rts_speech_xu_huang_heroes";
                case ArmyRtsAttackSpeechLine.LiuSandao:
                    return "aw_army_rts_speech_liu_sandao";
                case ArmyRtsAttackSpeechLine.PanFeng:
                    return "aw_army_rts_speech_pan_feng";
                case ArmyRtsAttackSpeechLine.DragonTiger:
                    return "aw_army_rts_speech_dragon_tiger";
                default:
                    return string.Empty;
            }
        }

        public static string FormatText(string pText)
        {
            string text = (pText ?? string.Empty).Trim();
            if (text.Length >= 2 && text[0] == '"' &&
                text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2).Trim();
            if (text.Length == 0) return string.Empty;

            if (ContainsCjk(text))
                return FormatCjkText(text);

            const int maximumLineLength = 17;
            const int maximumLines = 4;
            string[] words = text.Split(new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>(maximumLines);
            var current = new StringBuilder(maximumLineLength);
            for (int index = 0; index < words.Length; index++)
            {
                string word = words[index];
                int required = current.Length == 0
                    ? word.Length
                    : current.Length + 1 + word.Length;
                if (current.Length > 0 && required > maximumLineLength &&
                    lines.Count < maximumLines - 1)
                {
                    lines.Add(current.ToString());
                    current.Length = 0;
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
            if (current.Length > 0) lines.Add(current.ToString());
            return string.Join("\n", lines);
        }

        public static float TextScaleFor(string pText)
        {
            int cjkCount = CountCjkCharacters(pText);
            if (cjkCount <= 0) return 1.1f;
            if (cjkCount <= 10) return 1.4f;
            return cjkCount <= 16 ? 1.2f : 1.0f;
        }

        public static float BubbleScaleFor(float pActorScale)
        {
            return BubbleBaseScale * Math.Max(0.75f, pActorScale);
        }

        public static int VisibleScanIndex(int cursor, int offset,
            int visibleCount)
        {
            if (visibleCount <= 0) return -1;
            int normalizedCursor = cursor % visibleCount;
            if (normalizedCursor < 0) normalizedCursor += visibleCount;
            int normalizedOffset = Math.Max(0, offset) % visibleCount;
            return (normalizedCursor + normalizedOffset) % visibleCount;
        }

        public static int AdvanceVisibleScanCursor(int cursor,
            int visitedCount, int visibleCount)
        {
            if (visibleCount <= 0) return 0;
            return VisibleScanIndex(cursor, Math.Max(1, visitedCount),
                visibleCount);
        }

        private static bool ContainsCjk(string pText)
        {
            return CountCjkCharacters(pText) > 0;
        }

        private static ArmyRtsAttackSpeechLine LineAt(
            ArmyRtsAttackSpeechContext pContext, int pIndex)
        {
            ArmyRtsAttackSpeechLine[] pool = PoolFor(pContext);
            return pIndex >= 0 && pIndex < pool.Length
                ? pool[pIndex]
                : ArmyRtsAttackSpeechLine.None;
        }

        private static ArmyRtsAttackSpeechLine[] PoolFor(
            ArmyRtsAttackSpeechContext pContext)
        {
            switch (pContext)
            {
                case ArmyRtsAttackSpeechContext.Assault:
                    return AssaultLines;
                case ArmyRtsAttackSpeechContext.Defense:
                    return DefenseLines;
                case ArmyRtsAttackSpeechContext.Pursuit:
                    return PursuitLines;
                case ArmyRtsAttackSpeechContext.CrossingWater:
                    return CrossingWaterLines;
                case ArmyRtsAttackSpeechContext.Retreat:
                    return RetreatLines;
                default:
                    return GeneralCombatLines;
            }
        }

        private static string FormatCjkText(string pText)
        {
            const int maximumCjkPerLine = 8;
            const int maximumLines = 4;
            var lines = new List<string>(maximumLines);
            var current = new StringBuilder(maximumCjkPerLine + 1);
            int cjkOnLine = 0;

            for (int index = 0; index < pText.Length; index++)
            {
                char value = pText[index];
                if (value == '\r' || value == '\n')
                {
                    FlushCjkLine(lines, current, ref cjkOnLine);
                    continue;
                }

                current.Append(value);
                if (IsCjk(value)) cjkOnLine++;
                bool punctuation = IsCjkBreakPunctuation(value);
                bool nextPunctuation = index + 1 < pText.Length &&
                                       IsCjkBreakPunctuation(
                                           pText[index + 1]);
                bool mayAddLine = lines.Count < maximumLines - 1;
                if (mayAddLine && (punctuation ||
                                   cjkOnLine >= maximumCjkPerLine &&
                                   !nextPunctuation))
                    FlushCjkLine(lines, current, ref cjkOnLine);
            }

            FlushCjkLine(lines, current, ref cjkOnLine);
            return string.Join("\n", lines);
        }

        private static void FlushCjkLine(List<string> pLines,
            StringBuilder pCurrent, ref int pCjkOnLine)
        {
            if (pCurrent.Length <= 0) return;
            pLines.Add(pCurrent.ToString());
            pCurrent.Length = 0;
            pCjkOnLine = 0;
        }

        private static int CountCjkCharacters(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return 0;
            int count = 0;
            for (int index = 0; index < pText.Length; index++)
                if (IsCjk(pText[index])) count++;
            return count;
        }

        private static bool IsCjk(char pValue)
        {
            return pValue >= '\u3400' && pValue <= '\u9fff';
        }

        private static bool IsCjkBreakPunctuation(char pValue)
        {
            return pValue == '，' || pValue == '；' || pValue == '。' ||
                   pValue == '！' || pValue == '？' || pValue == '、' ||
                   pValue == '：';
        }

        private static int PositiveModulo(int pValue, int pModulus)
        {
            if (pModulus <= 0) return 0;
            int remainder = pValue % pModulus;
            return remainder < 0 ? remainder + pModulus : remainder;
        }
    }

    public sealed class ArmyRtsAttackSpeechBubbleLedger
    {
        private readonly Dictionary<long, double> _captainEmissionTimes =
            new Dictionary<long, double>();
        private double _lastGlobalEmissionTime = double.NegativeInfinity;

        public bool TryReserve(ArmyRtsAttackSpeechEventKey pEventKey,
            long pCaptainId, double now, int activeCount)
        {
            return pEventKey.HasValidCoordinates &&
                   TryReserveCombatant(pCaptainId, now, activeCount);
        }

        public bool TryReserveCombatant(long pActorId, double now,
            int activeCount)
        {
            if (pActorId < 0L || double.IsNaN(now) ||
                double.IsInfinity(now) || activeCount < 0 ||
                activeCount >=
                ArmyRtsAttackSpeechBubbleRules.MaximumActiveBubbles)
                return false;

            if (_captainEmissionTimes.TryGetValue(pActorId,
                    out double captainEmissionTime) &&
                now - captainEmissionTime <
                ArmyRtsAttackSpeechBubbleRules.CaptainCooldownSeconds)
                return false;
            if (now - _lastGlobalEmissionTime <
                ArmyRtsAttackSpeechBubbleRules.GlobalEmissionIntervalSeconds)
                return false;

            _captainEmissionTimes[pActorId] = now;
            _lastGlobalEmissionTime = now;
            return true;
        }

        public void Clear()
        {
            _captainEmissionTimes.Clear();
            _lastGlobalEmissionTime = double.NegativeInfinity;
        }
    }
}
