using System;
using System.Data;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal sealed class NativeSiniticIdentityMigrationCommitResult
    {
        internal NativeSiniticIdentityMigrationCommitResult(bool pSuccess,
            bool pChanged, string pFamilyName, string pFailure)
        {
            Success = pSuccess;
            Changed = pChanged;
            FamilyName = pFamilyName ?? string.Empty;
            Failure = pFailure ?? string.Empty;
        }

        internal bool Success { get; }
        internal bool Changed { get; }
        internal string FamilyName { get; }
        internal string Failure { get; }
    }

    internal static class NativeSiniticIdentityMigrationPersistence
    {
        internal static NativeSiniticIdentityMigrationCommitResult TryCommit(
            SQLiteConnection pDb, long pShiId, string pCandidateFamily,
            Func<bool> pFailAfterWrite = null)
        {
            if (pDb == null || pShiId < 0L ||
                string.IsNullOrWhiteSpace(pCandidateFamily))
                return Failed("invalid_request");

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction(IsolationLevel.Serializable);
                string profile;
                string family;
                using (var read = new SQLiteCommand(pDb)
                       { Transaction = transaction })
                {
                    read.CommandText = "SELECT IFNULL(NAMING_PROFILE,'')," +
                        "IFNULL(CLAN_NAME,'') FROM ShiBranch " +
                        "WHERE SHI_ID=@shi LIMIT 1";
                    read.Parameters.AddWithValue("@shi", pShiId);
                    using SQLiteDataReader reader = read.ExecuteReader();
                    if (!reader.Read()) return Rollback(transaction,
                        "branch_missing");
                    profile = reader.GetString(0);
                    family = reader.GetString(1).Trim();
                }

                if (string.Equals(profile, "native_sinitic",
                        StringComparison.Ordinal))
                {
                    transaction.Commit();
                    return string.IsNullOrWhiteSpace(family)
                        ? Failed("native_branch_family_missing")
                        : new NativeSiniticIdentityMigrationCommitResult(
                            true, false, family, string.Empty);
                }
                if (!string.Equals(profile, "western",
                        StringComparison.Ordinal) &&
                    !string.Equals(profile, "orc_nomadic",
                        StringComparison.Ordinal))
                    return Rollback(transaction, "branch_profile_mismatch");

                family = pCandidateFamily.Trim();
                using (var update = new SQLiteCommand(pDb)
                       { Transaction = transaction })
                {
                    update.CommandText = "UPDATE ShiBranch SET " +
                        "CLAN_NAME=@family,NAMING_PROFILE='native_sinitic'," +
                        "WESTERN_NAMING_TRADITION='',DISPLAY_STEM=@family " +
                        "WHERE SHI_ID=@shi AND NAMING_PROFILE=@profile";
                    update.Parameters.AddWithValue("@family", family);
                    update.Parameters.AddWithValue("@shi", pShiId);
                    update.Parameters.AddWithValue("@profile", profile);
                    if (update.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "branch_update_conflict");
                }
                if (pFailAfterWrite?.Invoke() == true)
                    throw new InvalidOperationException("injected_failure");
                transaction.Commit();
                return new NativeSiniticIdentityMigrationCommitResult(true,
                    true, family, string.Empty);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                return Failed(error.Message);
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static NativeSiniticIdentityMigrationCommitResult Rollback(
            SQLiteTransaction pTransaction, string pFailure)
        {
            try { pTransaction?.Rollback(); }
            catch { }
            return Failed(pFailure);
        }

        private static NativeSiniticIdentityMigrationCommitResult Failed(
            string pFailure)
        {
            return new NativeSiniticIdentityMigrationCommitResult(false,
                false, string.Empty, pFailure);
        }
    }
}
