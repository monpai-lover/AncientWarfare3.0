using System;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.naming
{
    internal static class AWLocalizedNameMigrationService
    {
        private const string OperationKeyPrefix = "localized-name:v1:";
        private static readonly object Gate = new object();
        private static readonly AWBoundedLocalizedNameWriteQueue PendingWrites =
            new AWBoundedLocalizedNameWriteQueue(
                AWLocalizedNameMigrationLimits.PendingWriteCapacity);

        internal static bool Enqueue(string pMetaType, long pObjectId,
            BaseSystemData pData)
        {
            if (pData == null || pObjectId < 0) return false;
            AWLocalizedNameIdentitySnapshot snapshot =
                AWLocalizedNamePersistence.Capture(pData);
            lock (Gate)
                return PendingWrites.Enqueue(pMetaType, pObjectId, snapshot);
        }

        internal static void Reset()
        {
            lock (Gate) PendingWrites.Clear();
        }

        internal static void RebuildVisibleProjections()
        {
            AWLocalizedNameRefreshService.Request();
        }

        internal static void ProcessAuthorityCycle()
        {
            ProcessAuthorityCycle(
                AWLocalizedNameMigrationLimits.DefaultBatchSize);
        }

        internal static void ProcessAuthorityCycle(int pBudget)
        {
            if (pBudget <= 0 || World.world == null ||
                !HistoricalWriteService.Ready) return;
            lock (Gate) PendingWrites.Flush(pBudget, TrySubmitAsync);
        }

        private static bool TrySubmitAsync(
            AWLocalizedNamePendingWrite pPending)
        {
            if (pPending?.Snapshot == null) return true;

            string identityKey = AWLocalizedNamePersistence.IdentityKey(
                pPending.MetaType, pPending.ObjectId);
            if (string.IsNullOrEmpty(identityKey)) return true;

            AWLocalizedNameIdentitySnapshot identity = pPending.Snapshot;
            var keys = new[]
            {
                new HistoricalSqlColumn("META_TYPE", pPending.MetaType),
                new HistoricalSqlColumn("OBJECT_ID", pPending.ObjectId)
            };
            var updates = new[]
            {
                new HistoricalSqlColumn("IDENTITY_KEY", identityKey),
                new HistoricalSqlColumn("NATIVE_NAME",
                    identity.NativeName ?? string.Empty),
                new HistoricalSqlColumn("CHINESE_NAME",
                    identity.ChineseName ?? string.Empty),
                new HistoricalSqlColumn("GIVEN_NAME",
                    identity.GivenName ?? string.Empty),
                new HistoricalSqlColumn("FAMILY_COMPONENT",
                    identity.FamilyComponent ?? string.Empty),
                new HistoricalSqlColumn("GENERATOR_ID",
                    identity.GeneratorId ?? string.Empty),
                new HistoricalSqlColumn("CULTURE_ID", identity.CultureId),
                new HistoricalSqlColumn("SCHEMA_VERSION",
                    identity.SchemaVersion),
                new HistoricalSqlColumn("UPDATED_TIME",
                    World.world?.getCurWorldTime() ?? 0d)
            };
            var inserts = new HistoricalSqlColumn[keys.Length +
                updates.Length];
            Array.Copy(keys, 0, inserts, 0, keys.Length);
            Array.Copy(updates, 0, inserts, keys.Length, updates.Length);

            return HistoricalWriteService.TryUpsertState(
                OperationKeyPrefix + identityKey,
                LocalizedNameIdentityTableItem.GetTableName(), keys, updates,
                inserts, pOnCommitted: null, out _, out _);
        }
    }
}
