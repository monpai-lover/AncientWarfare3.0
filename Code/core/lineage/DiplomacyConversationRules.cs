using System;

namespace AncientWarfare3.core.lineage
{
    public enum DiplomacyBubbleSide
    {
        Center = 0,
        Left = 1,
        Right = 2
    }

    public enum DiplomacyLetterStyle
    {
        Peer = 0,
        Imperial = 1,
        Suzerain = 2,
        Subject = 3
    }

    public enum DiplomacyLetterTone
    {
        Hostile = 0,
        Cold = 1,
        Neutral = 2,
        Cordial = 3
    }

    public enum DiplomacyPrimaryRelation
    {
        None = 0,
        War = 1,
        OurSuzerain = 2,
        OurVassal = 3,
        OurTributarySuzerain = 4,
        OurTributary = 5,
        Alliance = 6
    }

    public readonly struct DiplomacySelectorInsets
    {
        public DiplomacySelectorInsets(float pLeft, float pRight,
            float pTextWidth)
        {
            Left = pLeft;
            Right = pRight;
            TextWidth = pTextWidth;
        }

        public float Left { get; }
        public float Right { get; }
        public float TextWidth { get; }
    }

    public readonly struct DiplomacyKingdomPair : IEquatable<DiplomacyKingdomPair>
    {
        public DiplomacyKingdomPair(long firstKingdomId, long secondKingdomId)
        {
            FirstKingdomId = firstKingdomId;
            SecondKingdomId = secondKingdomId;
        }

        public long FirstKingdomId { get; }
        public long SecondKingdomId { get; }

        public bool Equals(DiplomacyKingdomPair other)
        {
            return FirstKingdomId == other.FirstKingdomId &&
                   SecondKingdomId == other.SecondKingdomId;
        }

        public override bool Equals(object obj)
        {
            return obj is DiplomacyKingdomPair other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (FirstKingdomId.GetHashCode() * 397) ^
                       SecondKingdomId.GetHashCode();
            }
        }
    }

    public static class DiplomacyConversationRules
    {
        public const int MaximumEventsPerPair = 80;
        public const float SecondarySelectorHeight = 46f;
        public const float SecondaryCommandHeight = 28f;
        public const float MinimumSelectorTextWidth = 52f;

        public static DiplomacySelectorInsets FitSelectorInsets(
            float panelWidth, float desiredLeft, float desiredRight)
        {
            float width = Math.Max(0f, panelWidth);
            float left = Math.Max(0f, desiredLeft);
            float right = Math.Max(0f, desiredRight);
            float availableInset = Math.Max(0f,
                width - MinimumSelectorTextWidth);
            float desiredInset = left + right;
            if (desiredInset > availableInset && desiredInset > 0f)
            {
                float scale = availableInset / desiredInset;
                left *= scale;
                right *= scale;
            }

            return new DiplomacySelectorInsets(left, right,
                Math.Max(0f, width - left - right));
        }

        public static DiplomacyKingdomPair NormalizePair(long pKingdomA,
            long pKingdomB)
        {
            return pKingdomA <= pKingdomB
                ? new DiplomacyKingdomPair(pKingdomA, pKingdomB)
                : new DiplomacyKingdomPair(pKingdomB, pKingdomA);
        }

        public static bool TryNormalizePair(long pKingdomA, long pKingdomB,
            out DiplomacyKingdomPair pPair)
        {
            pPair = NormalizePair(pKingdomA, pKingdomB);
            return pKingdomA >= 0 && pKingdomB >= 0 &&
                   pKingdomA != pKingdomB;
        }

        public static DiplomacyBubbleSide ResolveBubbleSide(
            long baseKingdomId, long otherKingdomId, long speakerKingdomId)
        {
            if (speakerKingdomId == baseKingdomId)
                return DiplomacyBubbleSide.Right;
            if (speakerKingdomId == otherKingdomId)
                return DiplomacyBubbleSide.Left;
            return DiplomacyBubbleSide.Center;
        }

        public static int ClampEventLimit(int pRequested)
        {
            return Math.Max(1, Math.Min(MaximumEventsPerPair, pRequested));
        }

        public static long CapitalDistanceSquared(int firstX, int firstY,
            int secondX, int secondY)
        {
            long deltaX = (long)firstX - secondX;
            long deltaY = (long)firstY - secondY;
            return deltaX * deltaX + deltaY * deltaY;
        }

        public static int CompareCapitalDistance(long leftDistanceSquared,
            long leftKingdomId, long rightDistanceSquared,
            long rightKingdomId)
        {
            int distanceOrder = leftDistanceSquared.CompareTo(
                rightDistanceSquared);
            return distanceOrder != 0
                ? distanceOrder
                : leftKingdomId.CompareTo(rightKingdomId);
        }

        public static int DisplayCapitalDistance(long pDistanceSquared)
        {
            if (pDistanceSquared < 0 || pDistanceSquared == long.MaxValue)
                return -1;
            return (int)Math.Round(Math.Sqrt(pDistanceSquared),
                MidpointRounding.AwayFromZero);
        }

        public static bool IsAutomaticWarSettlementTruce(
            DiplomacyProposalType pType, string pResponseReason)
        {
            if (pType != DiplomacyProposalType.Truce) return false;
            return string.Equals(pResponseReason, "war_settlement",
                       StringComparison.Ordinal) ||
                   string.Equals(pResponseReason, "kingdom_fell",
                       StringComparison.Ordinal);
        }

        public static DiplomacyLetterStyle ResolveLetterStyle(
            bool requesterHasMandate, bool requesterIsSubject)
        {
            return ResolveLetterStyle(requesterHasMandate,
                requesterIsDirectSuzerain: false,
                requesterIsDirectSubject: requesterIsSubject);
        }

        public static DiplomacyLetterStyle ResolveLetterStyle(
            bool requesterHasMandate, bool requesterIsDirectSuzerain,
            bool requesterIsDirectSubject)
        {
            if (requesterHasMandate) return DiplomacyLetterStyle.Imperial;
            if (requesterIsDirectSuzerain)
                return DiplomacyLetterStyle.Suzerain;
            return requesterIsDirectSubject
                ? DiplomacyLetterStyle.Subject
                : DiplomacyLetterStyle.Peer;
        }

        public static string LetterStyleId(DiplomacyLetterStyle pStyle)
        {
            return pStyle switch
            {
                DiplomacyLetterStyle.Imperial => "imperial",
                DiplomacyLetterStyle.Suzerain => "suzerain",
                DiplomacyLetterStyle.Subject => "subject",
                _ => "peer"
            };
        }

        public static DiplomacyLetterStyle ParseLetterStyle(string pStyle)
        {
            return pStyle switch
            {
                "imperial" => DiplomacyLetterStyle.Imperial,
                "suzerain" => DiplomacyLetterStyle.Suzerain,
                "subject" => DiplomacyLetterStyle.Subject,
                _ => DiplomacyLetterStyle.Peer
            };
        }

        public static string LetterToneId(DiplomacyLetterTone pTone)
        {
            return pTone switch
            {
                DiplomacyLetterTone.Hostile => "hostile",
                DiplomacyLetterTone.Cold => "cold",
                DiplomacyLetterTone.Cordial => "cordial",
                _ => "neutral"
            };
        }

        public static DiplomacyLetterTone ParseLetterTone(string pTone)
        {
            return pTone switch
            {
                "hostile" => DiplomacyLetterTone.Hostile,
                "cold" => DiplomacyLetterTone.Cold,
                "cordial" => DiplomacyLetterTone.Cordial,
                _ => DiplomacyLetterTone.Neutral
            };
        }

        public static DiplomacyLetterTone ResolveLetterTone(int pOpinion,
            bool atWar)
        {
            if (atWar || pOpinion <= -50) return DiplomacyLetterTone.Hostile;
            if (pOpinion < 0) return DiplomacyLetterTone.Cold;
            if (pOpinion >= 50) return DiplomacyLetterTone.Cordial;
            return DiplomacyLetterTone.Neutral;
        }

        public static DiplomacyPrimaryRelation ResolvePrimaryRelation(
            bool atWar, bool baseIsVassal, bool otherIsVassal,
            bool baseIsTributary, bool otherIsTributary, bool allied)
        {
            if (atWar) return DiplomacyPrimaryRelation.War;
            if (baseIsVassal)
                return DiplomacyPrimaryRelation.OurSuzerain;
            if (otherIsVassal)
                return DiplomacyPrimaryRelation.OurVassal;
            if (baseIsTributary)
                return DiplomacyPrimaryRelation.OurTributarySuzerain;
            if (otherIsTributary)
                return DiplomacyPrimaryRelation.OurTributary;
            return allied
                ? DiplomacyPrimaryRelation.Alliance
                : DiplomacyPrimaryRelation.None;
        }

        public static string FormatWarDeclaration(string pSpeaker,
            string pTarget, string pMiddle, string pSuffix, string pDetail)
        {
            return (pSpeaker ?? "") + (pMiddle ?? "") +
                   (pTarget ?? "") + (pSuffix ?? "") +
                   (pDetail ?? "");
        }
    }
}
