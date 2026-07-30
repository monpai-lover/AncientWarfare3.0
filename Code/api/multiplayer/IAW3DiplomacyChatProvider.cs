using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AncientWarfare3.api.multiplayer
{
    public enum AW3DiplomacyChatAvailabilityStatus : byte
    {
        Available = 0,
        SessionUnavailable = 1,
        BaseCountryNotControlled = 2,
        TargetCountryNotControlled = 3,
        CountryUnavailable = 4,
        SendPending = 5
    }

    public enum AW3DiplomacyChatSendStatus : byte
    {
        Queued = 0,
        Unavailable = 1,
        InvalidText = 2,
        Failed = 3
    }

    public sealed class AW3DiplomacyChatAvailability
    {
        public AW3DiplomacyChatAvailability(
            AW3DiplomacyChatAvailabilityStatus status, string detail)
        {
            if (!Enum.IsDefined(typeof(AW3DiplomacyChatAvailabilityStatus),
                    status))
                throw new ArgumentOutOfRangeException(nameof(status));
            Status = status;
            Detail = detail ?? string.Empty;
        }

        public AW3DiplomacyChatAvailabilityStatus Status { get; }
        public string Detail { get; }
        public bool CanSend =>
            Status == AW3DiplomacyChatAvailabilityStatus.Available;
    }

    public sealed class AW3DiplomacyChatEntry
    {
        public AW3DiplomacyChatEntry(long hostSequence,
            string senderPlayerId, long senderCountryId,
            long senderRulerActorId, long hostTimestampTicks, string text)
        {
            if (hostSequence <= 0)
                throw new ArgumentOutOfRangeException(nameof(hostSequence));
            if (senderCountryId <= 0)
                throw new ArgumentOutOfRangeException(nameof(senderCountryId));
            if (senderRulerActorId <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(senderRulerActorId));
            if (hostTimestampTicks < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(hostTimestampTicks));
            HostSequence = hostSequence;
            SenderPlayerId = AW3DiplomacyChatValidation.Identifier(
                senderPlayerId, nameof(senderPlayerId));
            SenderCountryId = senderCountryId;
            SenderRulerActorId = senderRulerActorId;
            HostTimestampTicks = hostTimestampTicks;
            Text = AW3DiplomacyChatValidation.Chat(text, nameof(text));
        }

        public long HostSequence { get; }
        public string SenderPlayerId { get; }
        public long SenderCountryId { get; }
        public long SenderRulerActorId { get; }
        public long HostTimestampTicks { get; }
        public string Text { get; }
    }

    public sealed class AW3DiplomacyChatSendResult
    {
        public AW3DiplomacyChatSendResult(AW3DiplomacyChatSendStatus status,
            string detail)
        {
            if (!Enum.IsDefined(typeof(AW3DiplomacyChatSendStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            Status = status;
            Detail = detail ?? string.Empty;
        }

        public AW3DiplomacyChatSendStatus Status { get; }
        public string Detail { get; }
        public bool Accepted => Status == AW3DiplomacyChatSendStatus.Queued;
    }

    public interface IAW3DiplomacyChatProvider
    {
        event Action Changed;

        AW3DiplomacyChatAvailability GetAvailability(
            long baseCountryId, long targetCountryId);

        IReadOnlyList<AW3DiplomacyChatEntry> Read(
            long baseCountryId, long targetCountryId);

        AW3DiplomacyChatSendResult Send(
            long baseCountryId, long targetCountryId, string text);
    }

    internal static class AW3DiplomacyChatValidation
    {
        private const int MaxPlayerIdBytes = 128;
        private const int MaxChatTextElements = 256;
        private const int MaxChatTextBytes = 2048;
        private static readonly Encoding Utf8 =
            new UTF8Encoding(false, true);

        internal static string Identifier(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Player ID is required.",
                    parameter);
            string normalized = value.Trim();
            if (Utf8.GetByteCount(normalized) > MaxPlayerIdBytes)
                throw new ArgumentException("Player ID is too long.",
                    parameter);
            for (var index = 0; index < normalized.Length; index++)
                if (char.IsControl(normalized[index]))
                    throw new ArgumentException("Player ID is invalid.",
                        parameter);
            return normalized;
        }

        internal static string Chat(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Chat text is required.",
                    parameter);
            string normalized = value.Trim();
            if (StringInfo.ParseCombiningCharacters(normalized).Length >
                    MaxChatTextElements ||
                Utf8.GetByteCount(normalized) > MaxChatTextBytes)
                throw new ArgumentException("Chat text is too long.",
                    parameter);
            for (var index = 0; index < normalized.Length; index++)
                if (char.IsControl(normalized[index]) &&
                    normalized[index] != '\t')
                    throw new ArgumentException("Chat text is invalid.",
                        parameter);
            return normalized;
        }
    }
}
