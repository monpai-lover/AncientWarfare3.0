using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal sealed class DiplomaticWarDeclarationRecord
    {
        public string Signature = "";
        public long AttackerId = -1L;
        public long DefenderId = -1L;
        public string GoalType = "";
        public string WarType = "";
        public string ReasonKey = "";
        public string ReasonLabel = "";
        public long TargetCityId = -1L;
        public string TargetCityName = "";
        public long SourceClaimId = -1L;
        public long SourceCoreId = -1L;
        public long RestorationClaimId = -1L;
        public long ClaimantActorId = -1L;
        public string NoticeSignature = "";
        public int NoticeYear = -1;
        public int EarliestWarYear = -1;
        public int ForcedWarYear = -1;
        public bool NoticeRecorded;
        public string Lifecycle = "pending";
        public string TerminalReason = "";
        public int TerminalYear = -1;
    }

    internal static class DiplomaticWarDeclarationLedgerService
    {
        private const int SchemaVersion = 1;
        private const int MaximumPayloadLength = 32768;

        private sealed class Envelope
        {
            public int Version = SchemaVersion;
            public List<DiplomaticWarDeclarationRecord> Records =
                new List<DiplomaticWarDeclarationRecord>();
        }

        internal static List<DiplomaticWarDeclarationRecord> GetPending(
            Kingdom pAttacker)
        {
            List<DiplomaticWarDeclarationRecord> all = ReadAll(pAttacker);
            var result = new List<DiplomaticWarDeclarationRecord>();
            for (int i = 0; i < all.Count; i++)
            {
                DiplomaticWarDeclarationRecord record = all[i];
                if (record != null && DiplomaticWarDeclarationLedgerRules.
                        IsPending(record.Lifecycle)) result.Add(record);
            }
            return result;
        }

        internal static bool HasPending(Kingdom pAttacker)
        {
            return GetPending(pAttacker).Count > 0;
        }

        internal static bool HasPendingForPair(Kingdom pAttacker,
            Kingdom pDefender)
        {
            if (pAttacker?.data == null || pDefender?.data == null)
                return false;
            return HasPendingForPair(pAttacker, pDefender.id);
        }

        internal static bool HasPendingForPair(Kingdom pAttacker,
            long pDefenderId)
        {
            if (pAttacker?.data == null || pDefenderId < 0L) return false;
            List<DiplomaticWarDeclarationRecord> pending = GetPending(
                pAttacker);
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].DefenderId == pDefenderId) return true;
            return false;
        }

        internal static bool Append(Kingdom pAttacker,
            DiplomaticWarDeclarationRecord pRecord)
        {
            if (pAttacker?.data == null || pRecord == null ||
                pRecord.AttackerId != pAttacker.id ||
                pRecord.DefenderId < 0L ||
                string.IsNullOrEmpty(pRecord.Signature)) return false;
            if (!TryReadAll(pAttacker,
                    out List<DiplomaticWarDeclarationRecord> records))
                return false;
            bool duplicate = false;
            for (int i = 0; i < records.Count; i++)
            {
                DiplomaticWarDeclarationRecord current = records[i];
                if (current != null && DiplomaticWarDeclarationLedgerRules.
                        IsPending(current.Lifecycle) &&
                    current.DefenderId == pRecord.DefenderId)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!DiplomaticWarDeclarationLedgerRules.CanAppendForPair(
                    duplicate)) return false;
            pRecord.Lifecycle = "pending";
            records.Add(pRecord);
            return Write(pAttacker, records);
        }

        internal static bool MarkTerminal(Kingdom pAttacker,
            string pSignature, string pLifecycle, string pReason)
        {
            if (pAttacker?.data == null || string.IsNullOrEmpty(pSignature))
                return false;
            if (!TryReadAll(pAttacker,
                    out List<DiplomaticWarDeclarationRecord> records))
                return false;
            for (int i = 0; i < records.Count; i++)
            {
                DiplomaticWarDeclarationRecord record = records[i];
                if (record == null || record.Signature != pSignature)
                    continue;
                record.Lifecycle = string.IsNullOrEmpty(pLifecycle)
                    ? "cancelled"
                    : pLifecycle;
                record.TerminalReason = pReason ?? "";
                record.TerminalYear = Date.getCurrentYear();
                return Write(pAttacker, records);
            }
            return false;
        }

        internal static void SyncNoticeProjection(Kingdom pAttacker,
            string pSignature)
        {
            if (pAttacker?.data == null || string.IsNullOrEmpty(pSignature))
                return;
            if (!TryReadAll(pAttacker,
                    out List<DiplomaticWarDeclarationRecord> records))
                return;
            DiplomaticWarDeclarationRecord record = Find(records,
                pSignature);
            if (record == null) return;
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_ID,
                out record.TargetCityId, record.TargetCityId);
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_NAME,
                out record.TargetCityName, record.TargetCityName ?? "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_NOTICE_SIGNATURE,
                out record.NoticeSignature, record.NoticeSignature ?? "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_NOTICE_YEAR,
                out record.NoticeYear, record.NoticeYear);
            pAttacker.data.get(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_EARLIEST_YEAR,
                out record.EarliestWarYear, record.EarliestWarYear);
            pAttacker.data.get(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_FORCED_YEAR,
                out record.ForcedWarYear, record.ForcedWarYear);
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_NOTICE_RECORDED,
                out record.NoticeRecorded, record.NoticeRecorded);
            Write(pAttacker, records);
        }

        private static List<DiplomaticWarDeclarationRecord> ReadAll(
            Kingdom pAttacker)
        {
            return TryReadAll(pAttacker,
                out List<DiplomaticWarDeclarationRecord> records)
                ? records
                : new List<DiplomaticWarDeclarationRecord>();
        }

        private static bool TryReadAll(Kingdom pAttacker,
            out List<DiplomaticWarDeclarationRecord> pRecords)
        {
            pRecords = new List<DiplomaticWarDeclarationRecord>();
            if (pAttacker?.data == null) return false;
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_LEDGER,
                out string payload, "");
            if (string.IsNullOrEmpty(payload))
            {
                pRecords = MigrateLegacyProjection(pAttacker);
                return DiplomaticWarDeclarationLedgerRules.
                    CanMutateStoredPayload(false, false);
            }
            if (payload.Length > MaximumPayloadLength)
                return DiplomaticWarDeclarationLedgerRules.
                    CanMutateStoredPayload(true, false);
            try
            {
                Envelope envelope = JsonConvert.DeserializeObject<Envelope>(
                    payload);
                if (envelope?.Records == null || envelope.Version !=
                    SchemaVersion)
                    return DiplomaticWarDeclarationLedgerRules.
                        CanMutateStoredPayload(true, false);
                pRecords = envelope.Records;
                return DiplomaticWarDeclarationLedgerRules.
                    CanMutateStoredPayload(true, true);
            }
            catch
            {
                return DiplomaticWarDeclarationLedgerRules.
                    CanMutateStoredPayload(true, false);
            }
        }

        private static List<DiplomaticWarDeclarationRecord>
            MigrateLegacyProjection(Kingdom pAttacker)
        {
            var records = new List<DiplomaticWarDeclarationRecord>();
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_PENDING,
                out bool pending, false);
            if (!pending) return records;
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TARGET_KINGDOM_ID,
                out long defenderId, -1L);
            if (defenderId < 0L) return records;
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_GOAL_TYPE,
                out string goalType, "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_ID,
                out long cityId, -1L);
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_NOTICE_SIGNATURE,
                out string noticeSignature, "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_NOTICE_YEAR,
                out int noticeYear, -1);
            pAttacker.data.get(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_EARLIEST_YEAR,
                out int earliestYear, -1);
            pAttacker.data.get(
                LineageKeys.DIPLOMATIC_WAR_NOTICE_FORCED_YEAR,
                out int forcedYear, -1);
            string signature = string.IsNullOrEmpty(noticeSignature)
                ? "legacy:" + pAttacker.id + ":" + defenderId + ":" +
                  (goalType ?? "") + ":" + cityId
                : noticeSignature;
            var record = new DiplomaticWarDeclarationRecord
            {
                Signature = signature,
                AttackerId = pAttacker.id,
                DefenderId = defenderId,
                GoalType = goalType ?? "",
                TargetCityId = cityId,
                NoticeSignature = noticeSignature ?? "",
                NoticeYear = noticeYear,
                EarliestWarYear = earliestYear,
                ForcedWarYear = forcedYear
            };
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TYPE,
                out record.WarType, "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_REASON_KEY,
                out record.ReasonKey, "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_REASON_LABEL,
                out record.ReasonLabel, "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_TARGET_CITY_NAME,
                out record.TargetCityName, "");
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_SOURCE_CLAIM_ID,
                out record.SourceClaimId, -1L);
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_SOURCE_CORE_ID,
                out record.SourceCoreId, -1L);
            pAttacker.data.get(
                LineageKeys.DIPLOMATIC_WAR_RESTORATION_CLAIM_ID,
                out record.RestorationClaimId, -1L);
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_CLAIMANT_ACTOR_ID,
                out record.ClaimantActorId, -1L);
            pAttacker.data.get(LineageKeys.DIPLOMATIC_WAR_NOTICE_RECORDED,
                out record.NoticeRecorded, false);
            records.Add(record);
            Write(pAttacker, records);
            return records;
        }

        private static bool Write(Kingdom pAttacker,
            List<DiplomaticWarDeclarationRecord> pRecords)
        {
            if (pAttacker?.data == null) return false;
            try
            {
                string payload = JsonConvert.SerializeObject(new Envelope
                {
                    Version = SchemaVersion,
                    Records = pRecords ??
                        new List<DiplomaticWarDeclarationRecord>()
                }, Formatting.None);
                if (payload.Length > MaximumPayloadLength) return false;
                pAttacker.data.set(LineageKeys.DIPLOMATIC_WAR_LEDGER,
                    payload);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static DiplomaticWarDeclarationRecord Find(
            List<DiplomaticWarDeclarationRecord> pRecords,
            string pSignature)
        {
            if (pRecords == null) return null;
            for (int i = 0; i < pRecords.Count; i++)
                if (pRecords[i]?.Signature == pSignature) return pRecords[i];
            return null;
        }
    }
}
