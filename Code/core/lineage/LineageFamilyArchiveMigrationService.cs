using System;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class LineageFamilyArchiveMigrationService
    {
        internal static void Run()
        {
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.IsOperational)
                throw new InvalidOperationException(
                    "Lineage archive is unavailable for family migration.");

            try
            {
                LineageFamilyArchiveMigrationResult result =
                    LineageFamilyArchiveMigration.Run(db,
                        ResolveLivingSnapshot);
                LogSummary(result);
            }
            catch (LineageFamilyArchiveMigrationException error)
            {
                LogSummary(error.Result);
                throw;
            }
        }

        private static ActorArchiveTableItem ResolveLivingSnapshot(long pId)
        {
            Actor actor = World.world?.units?.get(pId);
            if (actor == null || actor.isRekt() || !actor.isAlive() ||
                actor.data == null) return null;
            ActorArchiveTableItem snapshot =
                LineageArchiveWriter.CaptureUnarchivedRelationshipSnapshot(
                    actor, pAlive: true);
            return snapshot != null && snapshot.id == pId &&
                   snapshot.is_alive == 1
                ? snapshot
                : null;
        }

        private static void LogSummary(
            LineageFamilyArchiveMigrationResult pResult)
        {
            ModClass.LogInfo("Lineage family archive migration: scanned=" +
                pResult.Scanned + " resolved=" + pResult.Resolved +
                " placeholders=" + pResult.Placeholders + " failure=" +
                pResult.Failures);
        }
    }
}
