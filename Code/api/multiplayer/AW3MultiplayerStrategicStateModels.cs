using System;
using System.Collections.Generic;
using System.Text;

namespace AncientWarfare3.api.multiplayer
{
    public enum AW3MultiplayerStrategicTargetKind : byte
    {
        None = 0,
        Country = 1,
        City = 2,
        Army = 3,
        Actor = 4,
        Tile = 5
    }

    public enum AW3MultiplayerStrategicError : byte
    {
        None = 0,
        WrongThread = 1,
        InvalidSnapshot = 2,
        MissingArmy = 3,
        MissingActor = 4,
        CaptureFailed = 5,
        ApplyFailed = 6
    }

    public sealed class AW3MultiplayerStrategicCaptureException : Exception
    {
        internal AW3MultiplayerStrategicCaptureException(
            AW3MultiplayerStrategicError pError, string pDetail,
            Exception pInnerException = null)
            : base(pDetail ?? string.Empty, pInnerException)
        {
            Error = pError;
        }

        public AW3MultiplayerStrategicError Error { get; }
    }

    public sealed class AW3MultiplayerArmyProjection
    {
        public AW3MultiplayerArmyProjection(long armyId, string roleId,
            long anchorCityId, string currentOrderId,
            AW3MultiplayerStrategicTargetKind targetKind, long targetId,
            int targetTileX, int targetTileY,
            string operationalStateId = "", string postureId = "",
            long warId = -1L, long frontId = -1L, int supply = 100,
            int organization = 100, bool playerOrder = false,
            string rtsRoleId = "reserve")
        {
            AW3MultiplayerStrategicValidation.RequiredId(armyId,
                nameof(armyId));
            AW3MultiplayerStrategicValidation.OptionalId(anchorCityId,
                nameof(anchorCityId));
            if (!Enum.IsDefined(typeof(AW3MultiplayerStrategicTargetKind),
                    targetKind))
                throw new ArgumentOutOfRangeException(nameof(targetKind));
            AW3MultiplayerStrategicValidation.OptionalId(targetId,
                nameof(targetId));
            AW3MultiplayerStrategicValidation.OptionalCoordinate(targetTileX,
                nameof(targetTileX));
            AW3MultiplayerStrategicValidation.OptionalCoordinate(targetTileY,
                nameof(targetTileY));
            AW3MultiplayerStrategicValidation.OptionalId(warId,
                nameof(warId));
            AW3MultiplayerStrategicValidation.OptionalId(frontId,
                nameof(frontId));
            AW3MultiplayerStrategicValidation.Percent(supply,
                nameof(supply));
            AW3MultiplayerStrategicValidation.Percent(organization,
                nameof(organization));

            ArmyId = armyId;
            RoleId = AW3MultiplayerStrategicValidation.Text(roleId,
                nameof(roleId));
            AnchorCityId = anchorCityId;
            CurrentOrderId = AW3MultiplayerStrategicValidation.Text(
                currentOrderId, nameof(currentOrderId));
            TargetKind = targetKind;
            TargetId = targetId;
            TargetTileX = targetTileX;
            TargetTileY = targetTileY;
            OperationalStateId = AW3MultiplayerStrategicValidation.Text(
                operationalStateId, nameof(operationalStateId));
            PostureId = AW3MultiplayerStrategicValidation.Text(postureId,
                nameof(postureId));
            WarId = warId;
            FrontId = frontId;
            Supply = supply;
            Organization = organization;
            PlayerOrder = playerOrder;
            RtsRoleId = AW3MultiplayerStrategicValidation.Text(rtsRoleId,
                nameof(rtsRoleId));
        }

        public long ArmyId { get; }
        public string RoleId { get; }
        public long AnchorCityId { get; }
        public string CurrentOrderId { get; }
        public AW3MultiplayerStrategicTargetKind TargetKind { get; }
        public long TargetId { get; }
        public int TargetTileX { get; }
        public int TargetTileY { get; }
        public string OperationalStateId { get; }
        public string PostureId { get; }
        public long WarId { get; }
        public long FrontId { get; }
        public int Supply { get; }
        public int Organization { get; }
        public bool PlayerOrder { get; }
        public string RtsRoleId { get; }
    }

    public sealed class AW3MultiplayerActorProjection
    {
        public AW3MultiplayerActorProjection(long actorId, bool isGeneral,
            int generalMerit)
        {
            AW3MultiplayerStrategicValidation.RequiredId(actorId,
                nameof(actorId));
            if (generalMerit < 0)
                throw new ArgumentOutOfRangeException(nameof(generalMerit));
            ActorId = actorId;
            IsGeneral = isGeneral;
            GeneralMerit = generalMerit;
        }

        public long ActorId { get; }
        public bool IsGeneral { get; }
        public int GeneralMerit { get; }
    }

    public sealed class AW3MultiplayerStrategicSnapshot
    {
        public AW3MultiplayerStrategicSnapshot(long authoritativeTick,
            IReadOnlyList<AW3MultiplayerArmyProjection> armies,
            IReadOnlyList<AW3MultiplayerActorProjection> actors)
        {
            if (authoritativeTick < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(authoritativeTick));
            AuthoritativeTick = authoritativeTick;
            Armies = AW3MultiplayerStrategicValidation.SortUnique(
                armies, value => value.ArmyId, nameof(armies));
            Actors = AW3MultiplayerStrategicValidation.SortUnique(
                actors, value => value.ActorId, nameof(actors));
        }

        public long AuthoritativeTick { get; }
        public IReadOnlyList<AW3MultiplayerArmyProjection> Armies { get; }
        public IReadOnlyList<AW3MultiplayerActorProjection> Actors { get; }
    }

    public sealed class AW3MultiplayerStrategicApplyResult
    {
        private AW3MultiplayerStrategicApplyResult(
            AW3MultiplayerStrategicError pError, string pDetail,
            long pFailedIdentityId, int pAppliedArmyCount,
            int pAppliedActorCount)
        {
            Error = pError;
            Detail = pDetail ?? string.Empty;
            FailedIdentityId = pFailedIdentityId;
            AppliedArmyCount = pAppliedArmyCount;
            AppliedActorCount = pAppliedActorCount;
        }

        public bool IsSuccess => Error == AW3MultiplayerStrategicError.None;
        public AW3MultiplayerStrategicError Error { get; }
        public string Detail { get; }
        public long FailedIdentityId { get; }
        public int AppliedArmyCount { get; }
        public int AppliedActorCount { get; }

        internal static AW3MultiplayerStrategicApplyResult Success(
            int pArmyCount, int pActorCount)
        {
            return new AW3MultiplayerStrategicApplyResult(
                AW3MultiplayerStrategicError.None, string.Empty, -1L,
                pArmyCount, pActorCount);
        }

        internal static AW3MultiplayerStrategicApplyResult Failure(
            AW3MultiplayerStrategicError pError, string pDetail,
            long pFailedIdentityId = -1L)
        {
            if (pError == AW3MultiplayerStrategicError.None)
                throw new ArgumentOutOfRangeException(nameof(pError));
            return new AW3MultiplayerStrategicApplyResult(pError, pDetail,
                pFailedIdentityId, 0, 0);
        }
    }

    internal static class AW3MultiplayerStrategicValidation
    {
        private const int MaxTextBytes = 1024;
        private const int MaxRecords = 4096;
        private static readonly Encoding Utf8 =
            new UTF8Encoding(false, true);

        internal static void RequiredId(long pValue, string pParameter)
        {
            if (pValue < 0)
                throw new ArgumentOutOfRangeException(pParameter);
        }

        internal static void OptionalId(long pValue, string pParameter)
        {
            if (pValue < -1)
                throw new ArgumentOutOfRangeException(pParameter);
        }

        internal static void OptionalCoordinate(int pValue,
            string pParameter)
        {
            if (pValue < -1)
                throw new ArgumentOutOfRangeException(pParameter);
        }

        internal static void Percent(int pValue, string pParameter)
        {
            if (pValue < 0 || pValue > 100)
                throw new ArgumentOutOfRangeException(pParameter);
        }

        internal static string Text(string pValue, string pParameter)
        {
            if (pValue == null) throw new ArgumentNullException(pParameter);
            int bytes;
            try
            {
                bytes = Utf8.GetByteCount(pValue);
            }
            catch (EncoderFallbackException error)
            {
                throw new ArgumentException("Text is not valid UTF-8.",
                    pParameter, error);
            }
            if (bytes > MaxTextBytes)
                throw new ArgumentException(
                    "Strategic projection text is too long.", pParameter);
            return pValue;
        }

        internal static IReadOnlyList<T> SortUnique<T>(
            IReadOnlyList<T> pValues, Func<T, long> pKey,
            string pParameter) where T : class
        {
            if (pValues == null) throw new ArgumentNullException(pParameter);
            if (pValues.Count > MaxRecords)
                throw new ArgumentException(
                    "Too many strategic projection records.", pParameter);
            var copy = new T[pValues.Count];
            for (var index = 0; index < pValues.Count; index++)
                copy[index] = pValues[index] ??
                    throw new ArgumentException(
                        "Strategic projection record cannot be null.",
                        pParameter);
            Array.Sort(copy, (left, right) =>
                pKey(left).CompareTo(pKey(right)));
            for (var index = 1; index < copy.Length; index++)
            {
                if (pKey(copy[index - 1]) == pKey(copy[index]))
                    throw new ArgumentException(
                        "Strategic projection IDs must be unique.",
                        pParameter);
            }
            return Array.AsReadOnly(copy);
        }
    }
}
