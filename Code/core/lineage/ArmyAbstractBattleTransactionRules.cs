using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyAbstractBattlePhase
    {
        None = 0,
        Prepared = 1,
        Transferred = 2,
        Demobilizing = 3,
        Complete = 4
    }

    public sealed class ArmyAbstractBattleTransactionSnapshot
    {
        public long WarId { get; set; } = -1L;
        public long TargetCityId { get; set; } = -1L;
        public long TransferredCityId { get; set; } = -1L;
        public long Sequence { get; set; }
        public ArmyAbstractBattlePhase Phase { get; set; }
        public ArmyAbstractBattleOutcome Outcome { get; set; }
        public long ReceiverKingdomId { get; set; } = -1L;
        public long PrimaryAttackerArmyId { get; set; } = -1L;
        public ulong ParticipantHash { get; set; }
        public int DemobilizationCursor { get; set; }
        public IReadOnlyList<long> ParticipantArmyIds { get; set; } =
            Array.Empty<long>();
        public IReadOnlyList<long> ParticipantActorIds { get; set; } =
            Array.Empty<long>();
        public IReadOnlyList<long> LoserArmyIds { get; set; } =
            Array.Empty<long>();
        public IReadOnlyList<long> LoserActorIds { get; set; } =
            Array.Empty<long>();

        public ArmyAbstractBattleTransactionSnapshot Clone()
        {
            return new ArmyAbstractBattleTransactionSnapshot
            {
                WarId = WarId,
                TargetCityId = TargetCityId,
                TransferredCityId = TransferredCityId,
                Sequence = Sequence,
                Phase = Phase,
                Outcome = Outcome,
                ReceiverKingdomId = ReceiverKingdomId,
                PrimaryAttackerArmyId = PrimaryAttackerArmyId,
                ParticipantHash = ParticipantHash,
                DemobilizationCursor = DemobilizationCursor,
                ParticipantArmyIds = CopyIds(ParticipantArmyIds),
                ParticipantActorIds = CopyIds(ParticipantActorIds),
                LoserArmyIds = CopyIds(LoserArmyIds),
                LoserActorIds = CopyIds(LoserActorIds)
            };
        }

        private static IReadOnlyList<long> CopyIds(
            IReadOnlyList<long> pIds)
        {
            return pIds == null ? Array.Empty<long>() :
                new List<long>(pIds).ToArray();
        }
    }

    public static class ArmyAbstractBattleTransactionRules
    {
        private const int FormatVersion = 1;
        private const int FieldCount = 15;

        public static ArmyAbstractBattleTransactionSnapshot Prepare(
            long warId, long targetCityId, long transferredCityId,
            long sequence, ArmyAbstractBattleOutcome outcome,
            long receiverKingdomId, long primaryAttackerArmyId,
            ulong participantHash, IEnumerable<long> participantArmyIds,
            IEnumerable<long> participantActorIds, IEnumerable<long> loserArmyIds,
            IEnumerable<long> loserActorIds)
        {
            return new ArmyAbstractBattleTransactionSnapshot
            {
                WarId = warId,
                TargetCityId = targetCityId,
                TransferredCityId = transferredCityId,
                Sequence = Math.Max(0L, sequence),
                Phase = ArmyAbstractBattlePhase.Prepared,
                Outcome = outcome,
                ReceiverKingdomId = receiverKingdomId,
                PrimaryAttackerArmyId = primaryAttackerArmyId,
                ParticipantHash = participantHash,
                DemobilizationCursor = 0,
                ParticipantArmyIds = NormalizeIds(participantArmyIds),
                ParticipantActorIds = NormalizeIds(participantActorIds),
                LoserArmyIds = NormalizeIds(loserArmyIds),
                LoserActorIds = NormalizeIds(loserActorIds)
            };
        }

        public static ArmyAbstractBattleTransactionSnapshot Advance(
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            if (pSnapshot == null) return null;
            ArmyAbstractBattlePhase next = pSnapshot.Phase;
            if (next < ArmyAbstractBattlePhase.Complete)
                next = (ArmyAbstractBattlePhase)((int)next + 1);
            return Advance(pSnapshot, next);
        }

        public static ArmyAbstractBattleTransactionSnapshot Advance(
            ArmyAbstractBattleTransactionSnapshot pSnapshot,
            ArmyAbstractBattlePhase pRequestedPhase)
        {
            if (pSnapshot == null) return null;
            ArmyAbstractBattleTransactionSnapshot next = pSnapshot.Clone();
            if (pRequestedPhase > next.Phase)
                next.Phase = pRequestedPhase;
            return next;
        }

        public static bool IsDemobilizationComplete(
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            return pSnapshot != null && pSnapshot.DemobilizationCursor >=
                (pSnapshot.LoserActorIds?.Count ?? 0);
        }

        public static string Encode(
            ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            if (pSnapshot == null) return string.Empty;
            return string.Join("|", new[]
            {
                FormatVersion.ToString(CultureInfo.InvariantCulture),
                pSnapshot.WarId.ToString(CultureInfo.InvariantCulture),
                pSnapshot.TargetCityId.ToString(CultureInfo.InvariantCulture),
                pSnapshot.TransferredCityId.ToString(CultureInfo.InvariantCulture),
                pSnapshot.Sequence.ToString(CultureInfo.InvariantCulture),
                ((int)pSnapshot.Phase).ToString(CultureInfo.InvariantCulture),
                ((int)pSnapshot.Outcome).ToString(CultureInfo.InvariantCulture),
                pSnapshot.ReceiverKingdomId.ToString(CultureInfo.InvariantCulture),
                pSnapshot.PrimaryAttackerArmyId.ToString(CultureInfo.InvariantCulture),
                pSnapshot.ParticipantHash.ToString(CultureInfo.InvariantCulture),
                Math.Max(0, pSnapshot.DemobilizationCursor).ToString(
                    CultureInfo.InvariantCulture),
                EncodeIds(pSnapshot.ParticipantArmyIds),
                EncodeIds(pSnapshot.ParticipantActorIds),
                EncodeIds(pSnapshot.LoserArmyIds),
                EncodeIds(pSnapshot.LoserActorIds)
            });
        }

        public static ArmyAbstractBattleTransactionSnapshot Decode(
            string pEncoded)
        {
            if (string.IsNullOrWhiteSpace(pEncoded)) return null;
            string[] fields = pEncoded.Split('|');
            if (fields.Length != FieldCount) return null;
            if (!TryParseInt(fields[0], out int version) ||
                version != FormatVersion) return null;
            if (!TryParseLong(fields[1], out long warId) ||
                !TryParseLong(fields[2], out long targetCityId) ||
                !TryParseLong(fields[3], out long transferredCityId) ||
                !TryParseLong(fields[4], out long sequence) ||
                !TryParseInt(fields[5], out int phase) ||
                !TryParseInt(fields[6], out int outcome) ||
                !TryParseLong(fields[7], out long receiverKingdomId) ||
                !TryParseLong(fields[8], out long primaryAttackerArmyId) ||
                !ulong.TryParse(fields[9], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out ulong participantHash) ||
                !TryParseInt(fields[10], out int demobilizationCursor) ||
                !IsPhase(phase) || !IsOutcome(outcome)) return null;

            return new ArmyAbstractBattleTransactionSnapshot
            {
                WarId = warId,
                TargetCityId = targetCityId,
                TransferredCityId = transferredCityId,
                Sequence = Math.Max(0L, sequence),
                Phase = (ArmyAbstractBattlePhase)phase,
                Outcome = (ArmyAbstractBattleOutcome)outcome,
                ReceiverKingdomId = receiverKingdomId,
                PrimaryAttackerArmyId = primaryAttackerArmyId,
                ParticipantHash = participantHash,
                DemobilizationCursor = Math.Max(0, demobilizationCursor),
                ParticipantArmyIds = DecodeIds(fields[11]),
                ParticipantActorIds = DecodeIds(fields[12]),
                LoserArmyIds = DecodeIds(fields[13]),
                LoserActorIds = DecodeIds(fields[14])
            };
        }

        public static bool TryDecode(string pEncoded,
            out ArmyAbstractBattleTransactionSnapshot pSnapshot)
        {
            pSnapshot = Decode(pEncoded);
            return pSnapshot != null;
        }

        private static bool IsPhase(int pPhase)
        {
            return pPhase >= (int)ArmyAbstractBattlePhase.None &&
                pPhase <= (int)ArmyAbstractBattlePhase.Complete;
        }

        private static bool IsOutcome(int pOutcome)
        {
            return pOutcome >= (int)ArmyAbstractBattleOutcome.NoBattle &&
                pOutcome <= (int)ArmyAbstractBattleOutcome.DefenseVictory;
        }

        private static bool TryParseLong(string pValue, out long pResult)
        {
            return long.TryParse(pValue, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out pResult);
        }

        private static bool TryParseInt(string pValue, out int pResult)
        {
            return int.TryParse(pValue, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out pResult);
        }

        private static string EncodeIds(IEnumerable<long> pIds)
        {
            return string.Join(",", NormalizeIds(pIds));
        }

        private static IReadOnlyList<long> DecodeIds(string pEncoded)
        {
            if (string.IsNullOrWhiteSpace(pEncoded)) return Array.Empty<long>();
            var ids = new List<long>();
            foreach (string value in pEncoded.Split(','))
            {
                if (!TryParseLong(value, out long id) || id < 0L) continue;
                ids.Add(id);
            }
            return NormalizeIds(ids);
        }

        private static IReadOnlyList<long> NormalizeIds(
            IEnumerable<long> pIds)
        {
            if (pIds == null) return Array.Empty<long>();
            return pIds.Where(pId => pId >= 0L).Distinct().OrderBy(
                pId => pId).ToArray();
        }
    }
}
