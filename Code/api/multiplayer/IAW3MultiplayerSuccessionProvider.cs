using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AncientWarfare3.api.multiplayer
{
    public enum AW3SuccessionInstallStatus : byte
    {
        Installed = 1,
        SuccessorUnavailable = 2,
        NoLegalSuccessor = 3,
        Failed = 4
    }

    public sealed class AW3SuccessionCandidate
    {
        public AW3SuccessionCandidate(long actorId, string displayName,
            string relationKey, string successionMode, bool isDefault)
        {
            if (actorId <= 0)
                throw new ArgumentOutOfRangeException(nameof(actorId));
            ActorId = actorId;
            DisplayName = AW3SuccessionValidation.DisplayName(displayName,
                nameof(displayName));
            RelationKey = AW3SuccessionValidation.Token(relationKey,
                nameof(relationKey));
            SuccessionMode = AW3SuccessionValidation.Token(successionMode,
                nameof(successionMode));
            IsDefault = isDefault;
        }

        public long ActorId { get; }
        public string DisplayName { get; }
        public string RelationKey { get; }
        public string SuccessionMode { get; }
        public bool IsDefault { get; }
    }

    public sealed class AW3SuccessionOffer
    {
        public const int MaximumCandidates = 32;
        private readonly IReadOnlyList<AW3SuccessionCandidate> _candidates;

        public AW3SuccessionOffer(long countryId, long formerRulerActorId,
            long defaultActorId,
            IReadOnlyList<AW3SuccessionCandidate> candidates)
        {
            if (countryId <= 0)
                throw new ArgumentOutOfRangeException(nameof(countryId));
            if (formerRulerActorId <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(formerRulerActorId));
            if (defaultActorId <= 0)
                throw new ArgumentOutOfRangeException(nameof(defaultActorId));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            AW3SuccessionCandidate[] copy = candidates.Select(candidate =>
                    candidate == null
                        ? null
                        : new AW3SuccessionCandidate(candidate.ActorId,
                            candidate.DisplayName, candidate.RelationKey,
                            candidate.SuccessionMode, candidate.IsDefault))
                .ToArray();
            if (copy.Length > MaximumCandidates ||
                copy.Any(candidate => candidate == null) ||
                copy.Select(candidate => candidate.ActorId).Distinct()
                    .Count() != copy.Length)
                throw new ArgumentException(
                    "Succession candidates are invalid.",
                    nameof(candidates));
            if (copy.Length > 0 &&
                (copy.Count(candidate => candidate.IsDefault) != 1 ||
                 !copy.Any(candidate => candidate.IsDefault &&
                     candidate.ActorId == defaultActorId)))
                throw new ArgumentException(
                    "Selectable candidates must identify the AW3 default.",
                    nameof(candidates));

            Array.Sort(copy, (left, right) =>
                left.ActorId.CompareTo(right.ActorId));
            CountryId = countryId;
            FormerRulerActorId = formerRulerActorId;
            DefaultActorId = defaultActorId;
            _candidates = Array.AsReadOnly(copy);
        }

        public long CountryId { get; }
        public long FormerRulerActorId { get; }
        public long DefaultActorId { get; }
        public IReadOnlyList<AW3SuccessionCandidate> Candidates =>
            _candidates;
    }

    public sealed class AW3SuccessionInstallResult
    {
        private AW3SuccessionInstallResult(AW3SuccessionInstallStatus status,
            long installedRulerActorId)
        {
            if (!Enum.IsDefined(typeof(AW3SuccessionInstallStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if ((status == AW3SuccessionInstallStatus.Installed) !=
                (installedRulerActorId > 0))
                throw new ArgumentException(
                    "Installed status and ruler identity must agree.");
            Status = status;
            InstalledRulerActorId = installedRulerActorId;
        }

        public AW3SuccessionInstallStatus Status { get; }
        public long InstalledRulerActorId { get; }

        public static AW3SuccessionInstallResult Installed(long actorId)
        {
            if (actorId <= 0)
                throw new ArgumentOutOfRangeException(nameof(actorId));
            return new AW3SuccessionInstallResult(
                AW3SuccessionInstallStatus.Installed, actorId);
        }

        public static AW3SuccessionInstallResult SuccessorUnavailable()
        {
            return new AW3SuccessionInstallResult(
                AW3SuccessionInstallStatus.SuccessorUnavailable, -1L);
        }

        public static AW3SuccessionInstallResult NoLegalSuccessor()
        {
            return new AW3SuccessionInstallResult(
                AW3SuccessionInstallStatus.NoLegalSuccessor, -1L);
        }

        public static AW3SuccessionInstallResult Failed()
        {
            return new AW3SuccessionInstallResult(
                AW3SuccessionInstallStatus.Failed, -1L);
        }
    }

    public interface IAW3MultiplayerSuccessionProvider
    {
        bool TryBegin(AW3SuccessionOffer offer);

        void OnInstalled(long countryId, long formerRulerActorId,
            long installedRulerActorId);

        void OnReleased(long countryId, long formerRulerActorId);
    }

    internal static class AW3SuccessionValidation
    {
        private const int MaximumDisplayElements = 128;
        private const int MaximumDisplayBytes = 512;
        private const int MaximumTokenBytes = 64;
        private static readonly Encoding Utf8 =
            new UTF8Encoding(false, true);

        internal static string DisplayName(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Display name is required.",
                    parameter);
            string normalized = value.Trim();
            if (StringInfo.ParseCombiningCharacters(normalized).Length >
                    MaximumDisplayElements ||
                Utf8.GetByteCount(normalized) > MaximumDisplayBytes)
                throw new ArgumentException("Display name is too long.",
                    parameter);
            for (var index = 0; index < normalized.Length; index++)
                if (char.IsControl(normalized[index]))
                    throw new ArgumentException("Display name is invalid.",
                        parameter);
            return normalized;
        }

        internal static string Token(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Token is required.", parameter);
            string normalized = value.Trim();
            if (Utf8.GetByteCount(normalized) > MaximumTokenBytes)
                throw new ArgumentException("Token is too long.", parameter);
            for (var index = 0; index < normalized.Length; index++)
            {
                char character = normalized[index];
                if (!(char.IsLetterOrDigit(character) || character == '_' ||
                      character == '-' || character == '.'))
                    throw new ArgumentException("Token is invalid.",
                        parameter);
            }
            return normalized;
        }
    }
}
