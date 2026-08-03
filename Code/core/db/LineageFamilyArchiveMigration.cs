using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
using AncientWarfare3.attributes;

namespace AncientWarfare3.core.db
{
    internal readonly struct LineageFamilyArchiveMigrationResult
    {
        internal LineageFamilyArchiveMigrationResult(int pScanned,
            int pResolved, int pPlaceholders, int pFailures)
        {
            Scanned = pScanned;
            Resolved = pResolved;
            Placeholders = pPlaceholders;
            Failures = pFailures;
        }

        internal int Scanned { get; }
        internal int Resolved { get; }
        internal int Placeholders { get; }
        internal int Failures { get; }
    }

    internal sealed class LineageFamilyArchiveMigrationException : Exception
    {
        internal LineageFamilyArchiveMigrationException(
            LineageFamilyArchiveMigrationResult pResult, Exception pInner)
            : base("Lineage family archive migration failed.", pInner)
        {
            Result = pResult;
        }

        internal LineageFamilyArchiveMigrationResult Result { get; }
    }

    internal static class LineageFamilyArchiveMigration
    {
        internal const int CurrentVersion = 1;
        internal const string MigrationTable =
            "LineageFamilyArchiveMigrationState";
        internal const string Resolved = "resolved";
        internal const string UnresolvedLegacy = "unresolved_legacy";

        private const string MigrationKey = "lineage_family_archive";

        internal static bool IsVersionOwnedColumn(string pTableName,
            string pColumnName)
        {
            return string.Equals(pTableName, "ActorArchive",
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(pColumnName, "ARCHIVE_RESOLUTION",
                       StringComparison.OrdinalIgnoreCase);
        }

        internal static LineageFamilyArchiveMigrationResult Run(
            SQLiteConnection pDb,
            Func<long, ActorArchiveTableItem> pLivingActorResolver)
        {
            if (pDb == null) throw new ArgumentNullException(nameof(pDb));
            if (pLivingActorResolver == null)
                throw new ArgumentNullException(nameof(pLivingActorResolver));

            var scanned = 0;
            var resolved = 0;
            var placeholders = 0;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction(IsolationLevel.Serializable);
                EnsureSchema(pDb, transaction);
                if (ReadVersion(pDb, transaction) >= CurrentVersion)
                {
                    transaction.Commit();
                    return new LineageFamilyArchiveMigrationResult(0, 0, 0,
                        0);
                }

                IReadOnlyList<long> orphanIds = ReadOrphanChildIds(pDb,
                    transaction);
                foreach (long orphanId in orphanIds)
                {
                    scanned++;
                    ActorArchiveTableItem snapshot =
                        pLivingActorResolver(orphanId);
                    if (IsValidLivingSnapshot(orphanId, snapshot))
                    {
                        snapshot.archive_resolution = Resolved;
                        InsertArchive(pDb, transaction, snapshot);
                        resolved++;
                    }
                    else
                    {
                        InsertArchive(pDb, transaction,
                            BuildPlaceholder(pDb, transaction, orphanId));
                        placeholders++;
                    }
                }

                long remaining = CountOrphans(pDb, transaction);
                if (remaining != 0L)
                    throw new InvalidOperationException(
                        "Lineage family archive migration left " + remaining +
                        " orphan child ids.");

                WriteVersion(pDb, transaction);
                transaction.Commit();
                return new LineageFamilyArchiveMigrationResult(scanned,
                    resolved, placeholders, 0);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                throw new LineageFamilyArchiveMigrationException(
                    new LineageFamilyArchiveMigrationResult(scanned,
                        resolved, placeholders, 1), error);
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static void EnsureSchema(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            if (!ColumnExists(pDb, pTransaction, "ActorArchive",
                    "ARCHIVE_RESOLUTION"))
            {
                using var alter = new SQLiteCommand(pDb)
                    { Transaction = pTransaction };
                alter.CommandText = "ALTER TABLE ActorArchive ADD COLUMN " +
                    "ARCHIVE_RESOLUTION TEXT DEFAULT 'resolved'";
                alter.ExecuteNonQuery();
            }

            using var create = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            create.CommandText = "CREATE TABLE IF NOT EXISTS " +
                MigrationTable + " (MIGRATION_KEY TEXT PRIMARY KEY," +
                "VERSION INTEGER NOT NULL)";
            create.ExecuteNonQuery();
        }

        private static bool ColumnExists(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, string pColumn)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "PRAGMA table_info(" + pTable + ")";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), pColumn,
                        StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int ReadVersion(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT VERSION FROM " + MigrationTable +
                " WHERE MIGRATION_KEY=@key LIMIT 1";
            command.Parameters.AddWithValue("@key", MigrationKey);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 0
                : Convert.ToInt32(value);
        }

        private static IReadOnlyList<long> ReadOrphanChildIds(
            SQLiteConnection pDb, SQLiteTransaction pTransaction)
        {
            var result = new List<long>();
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT DISTINCT edge.CHILD_ID " +
                "FROM FamilyEdge edge LEFT JOIN ActorArchive actor " +
                "ON actor.ID=edge.CHILD_ID WHERE edge.CHILD_ID>=0 " +
                "AND actor.ID IS NULL ORDER BY edge.CHILD_ID";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(reader.GetInt64(0));
            return result;
        }

        private static bool IsValidLivingSnapshot(long pExpectedId,
            ActorArchiveTableItem pSnapshot)
        {
            return pSnapshot != null && pSnapshot.id == pExpectedId &&
                   pSnapshot.is_alive == 1;
        }

        private static ActorArchiveTableItem BuildPlaceholder(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            long pActorId)
        {
            long parent1 = -1L;
            long parent2 = -1L;
            long lineageId = -1L;
            using (var command = new SQLiteCommand(pDb)
                   { Transaction = pTransaction })
            {
                command.CommandText =
                    "SELECT IFNULL(MAX(CASE WHEN PARENT_SLOT=1 THEN " +
                    "PARENT_ID END),-1),IFNULL(MAX(CASE WHEN PARENT_SLOT=2 " +
                    "THEN PARENT_ID END),-1),IFNULL(MAX(CASE WHEN " +
                    "CHILD_LINEAGE_ID>=0 THEN CHILD_LINEAGE_ID END),-1) " +
                    "FROM FamilyEdge WHERE CHILD_ID=@child";
                command.Parameters.AddWithValue("@child", pActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    parent1 = reader.GetInt64(0);
                    parent2 = reader.GetInt64(1);
                    lineageId = reader.GetInt64(2);
                }
            }

            return new ActorArchiveTableItem
            {
                id = pActorId,
                given_name = string.Empty,
                display_name = string.Empty,
                family_name = string.Empty,
                clan_name = string.Empty,
                lineage_id = lineageId,
                shi_id = -1L,
                asset_id = string.Empty,
                subspecies_id = -1L,
                subspecies_name = string.Empty,
                sex = -1,
                status = string.Empty,
                kingdom_id = -1L,
                kingdom_name = string.Empty,
                kingdom_color = string.Empty,
                city_id = -1L,
                city_name = string.Empty,
                social_title = string.Empty,
                social_title_color = string.Empty,
                original_clan_id = -1L,
                clan_color_text = string.Empty,
                clan_color_id = -1,
                clan_banner_icon_id = -1,
                clan_banner_background_id = -1,
                parent_id_1 = parent1,
                parent_id_2 = parent2,
                generation = 0,
                noble_distance = 99,
                ever_noble_blood = 0,
                noble_origin_actor_id = -1L,
                noble_origin_name = string.Empty,
                noble_origin_distance = 99,
                birth_time = 0d,
                death_time = -1d,
                death_cause = string.Empty,
                is_alive = 0,
                name_integrated = 0,
                head = 0,
                skin = 0,
                skin_set = 0,
                age_overgrowth = 1,
                phenotype_index = 0,
                phenotype_shade = 0,
                founded_branch_shi_id = -1L,
                archive_resolution = UnresolvedLegacy
            };
        }

        private static void InsertArchive(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, ActorArchiveTableItem pRow)
        {
            FieldInfo[] fields = typeof(ActorArchiveTableItem).GetFields();
            string[] columns = fields.Select(ColumnName).ToArray();
            string[] parameters = fields.Select((_, index) => "@value" + index)
                .ToArray();
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "INSERT INTO ActorArchive (" +
                string.Join(",", columns) + ") VALUES (" +
                string.Join(",", parameters) + ")";
            for (int index = 0; index < fields.Length; index++)
            {
                object value = fields[index].GetValue(pRow);
                if (fields[index].FieldType == typeof(string))
                    value = value as string ?? string.Empty;
                command.Parameters.AddWithValue(parameters[index],
                    value ?? DBNull.Value);
            }
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "Lineage family archive insert did not affect one row.");
        }

        private static string ColumnName(FieldInfo pField)
        {
            TableItemDefAttribute attribute =
                pField.GetCustomAttribute<TableItemDefAttribute>();
            return attribute == null || string.IsNullOrEmpty(attribute.Name)
                ? pField.Name.ToUpperInvariant()
                : attribute.Name;
        }

        private static long CountOrphans(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT COUNT(DISTINCT edge.CHILD_ID) " +
                "FROM FamilyEdge edge LEFT JOIN ActorArchive actor " +
                "ON actor.ID=edge.CHILD_ID WHERE edge.CHILD_ID>=0 " +
                "AND actor.ID IS NULL";
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static void WriteVersion(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "INSERT OR REPLACE INTO " + MigrationTable +
                " (MIGRATION_KEY,VERSION) VALUES (@key,@version)";
            command.Parameters.AddWithValue("@key", MigrationKey);
            command.Parameters.AddWithValue("@version", CurrentVersion);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "Lineage family archive migration version was not written.");
        }
    }
}
