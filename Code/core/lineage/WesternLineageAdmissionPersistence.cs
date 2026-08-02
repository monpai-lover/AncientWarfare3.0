using System;
using System.Data;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal enum WesternLineageAdmissionCommitStage
    {
        AfterLineageWrite,
        AfterBranchWrite,
        AfterActorWrite
    }

    internal sealed class WesternLineageAdmissionCommitRequest
    {
        public WesternLineageAdmissionAction Action;
        public long ActorId = -1L;
        public long ExistingLineageId = -1L;
        public long ExistingShiId = -1L;
        public long ParentShiId = -1L;
        public string GivenName = string.Empty;
        public string DisplayName = string.Empty;
        public string FamilyName = string.Empty;
        public string ClanName = string.Empty;
        public string AssetId = string.Empty;
        public int Sex;
        public string NamingProfile = string.Empty;
        public string WesternNamingTradition = string.Empty;
        public string OriginCityChineseName = string.Empty;
        public string DisplayStem = string.Empty;
        public string SourceType = string.Empty;
        public long OriginKingdomId = -1L;
        public long OriginCityId = -1L;
        public long OriginOriginalClanId = -1L;
        public double CreatedTime;
    }

    internal readonly struct WesternLineageAdmissionCommitResult
    {
        internal WesternLineageAdmissionCommitResult(bool pSuccess,
            long pLineageId, long pShiId, string pFailure)
        {
            Success = pSuccess;
            LineageId = pLineageId;
            ShiId = pShiId;
            Failure = pFailure ?? string.Empty;
        }

        internal bool Success { get; }
        internal long LineageId { get; }
        internal long ShiId { get; }
        internal string Failure { get; }
    }

    internal static class WesternLineageAdmissionPersistence
    {
        private const string LineageGroup = "LineageGroup";
        private const string ShiBranch = "ShiBranch";
        private const string ActorArchive = "ActorArchive";

        internal static WesternLineageAdmissionCommitResult TryCommit(
            SQLiteConnection pDb,
            WesternLineageAdmissionCommitRequest pRequest,
            Func<WesternLineageAdmissionCommitStage, bool> pFail = null)
        {
            string validation = Validate(pDb, pRequest);
            if (validation.Length > 0)
                return Failed(validation);

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction(IsolationLevel.Serializable);
                long lineageId = pRequest.ExistingLineageId;
                long shiId = pRequest.ExistingShiId;

                long boundLineage = -1L;
                long boundShi = -1L;
                string boundFailure = string.Empty;
                if (pRequest.Action !=
                    WesternLineageAdmissionAction.CreateRoot &&
                    TryResolveExistingActorBinding(pDb, transaction,
                        pRequest, out boundLineage,
                        out boundShi, out boundFailure))
                {
                    transaction.Commit();
                    return new WesternLineageAdmissionCommitResult(true,
                        boundLineage, boundShi, string.Empty);
                }
                if (pRequest.Action !=
                    WesternLineageAdmissionAction.CreateRoot &&
                    !string.IsNullOrEmpty(boundFailure))
                    throw new InvalidOperationException(boundFailure);

                if (pRequest.Action ==
                    WesternLineageAdmissionAction.CreateRoot)
                {
                    if (TryResolveExistingActorBinding(pDb, transaction,
                            pRequest, out long existingLineage,
                            out long existingShi, out string bindingFailure))
                    {
                        transaction.Commit();
                        return new WesternLineageAdmissionCommitResult(true,
                            existingLineage, existingShi, string.Empty);
                    }
                    if (!string.IsNullOrEmpty(bindingFailure))
                        throw new InvalidOperationException(bindingFailure);
                    lineageId = NextId(pDb, transaction, LineageGroup,
                        "LINEAGE_ID");
                    InsertLineage(pDb, transaction, lineageId, pRequest);
                }
                else
                {
                    RequireLineage(pDb, transaction, lineageId);
                }
                Inject(pFail,
                    WesternLineageAdmissionCommitStage.AfterLineageWrite);

                if (pRequest.Action ==
                    WesternLineageAdmissionAction.CreateRoot)
                {
                    shiId = NextId(pDb, transaction, ShiBranch, "SHI_ID");
                    InsertBranch(pDb, transaction, lineageId, shiId,
                        pRequest);
                }
                else if (pRequest.Action ==
                         WesternLineageAdmissionAction.CompletePartialBranch)
                {
                    if (shiId < 0L)
                    {
                        shiId = NextId(pDb, transaction, ShiBranch,
                            "SHI_ID");
                        InsertBranch(pDb, transaction, lineageId, shiId,
                            pRequest);
                    }
                    else
                    {
                        string branchProfile = ReadBranchProfile(pDb,
                            transaction, lineageId, shiId);
                        if (string.Equals(branchProfile, "xia",
                                StringComparison.Ordinal))
                        {
                            // Xia is a real profile transition. Keep the old
                            // branch intact and create a western/orc child.
                            long parentShiId = shiId;
                            shiId = NextId(pDb, transaction, ShiBranch,
                                "SHI_ID");
                            pRequest.ParentShiId = parentShiId;
                            InsertBranch(pDb, transaction, lineageId, shiId,
                                pRequest);
                        }
                        else
                        {
                            CompleteBranchInPlace(pDb, transaction, lineageId,
                                shiId, pRequest);
                        }
                    }
                }
                else
                {
                    RequireCompatibleBranch(pDb, transaction, lineageId,
                        shiId, pRequest.NamingProfile);
                }
                Inject(pFail,
                    WesternLineageAdmissionCommitStage.AfterBranchWrite);

                UpsertActor(pDb, transaction, lineageId, shiId, pRequest);
                Inject(pFail,
                    WesternLineageAdmissionCommitStage.AfterActorWrite);

                transaction.Commit();
                return new WesternLineageAdmissionCommitResult(true,
                    lineageId, shiId, string.Empty);
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

        private static string Validate(SQLiteConnection pDb,
            WesternLineageAdmissionCommitRequest pRequest)
        {
            if (pDb == null || pDb.State != ConnectionState.Open)
                return "database_unavailable";
            if (pRequest == null || pRequest.ActorId < 0L)
                return "invalid_actor";
            if (pRequest.Action == WesternLineageAdmissionAction.Reject)
                return "admission_rejected";
            if (pRequest.Action != WesternLineageAdmissionAction.CreateRoot &&
                pRequest.ExistingLineageId < 0L)
                return "missing_lineage";
            if ((pRequest.Action ==
                     WesternLineageAdmissionAction.ReuseComplete ||
                 pRequest.Action ==
                     WesternLineageAdmissionAction.InheritRelative) &&
                pRequest.ExistingShiId < 0L)
                return "missing_branch";
            if (!string.Equals(pRequest.NamingProfile, "western",
                    StringComparison.Ordinal) &&
                !string.Equals(pRequest.NamingProfile, "orc_nomadic",
                    StringComparison.Ordinal))
                return "unsupported_profile";
            if (string.IsNullOrWhiteSpace(pRequest.FamilyName) ||
                string.IsNullOrWhiteSpace(pRequest.ClanName) ||
                string.IsNullOrWhiteSpace(pRequest.DisplayStem))
                return "missing_family_identity";
            return string.Empty;
        }

        private static long NextId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable,
            string pColumn)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(" + pColumn +
                "),0)+1 FROM " + pTable;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static void InsertLineage(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pLineageId,
            WesternLineageAdmissionCommitRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + LineageGroup +
                " (LINEAGE_ID,FAMILY_NAME,FOUNDER_ACTOR_ID,FOUNDER_NAME," +
                "CREATED_TIME,ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID," +
                "IS_EXTINCT) VALUES (@lineage,@family,@actor,@founder," +
                "@time,@kingdom,@city,0)";
            command.Parameters.AddWithValue("@lineage", pLineageId);
            command.Parameters.AddWithValue("@family", pRequest.FamilyName);
            command.Parameters.AddWithValue("@actor", pRequest.ActorId);
            command.Parameters.AddWithValue("@founder", pRequest.DisplayName);
            command.Parameters.AddWithValue("@time", pRequest.CreatedTime);
            command.Parameters.AddWithValue("@kingdom",
                pRequest.OriginKingdomId);
            command.Parameters.AddWithValue("@city", pRequest.OriginCityId);
            RequireOne(command.ExecuteNonQuery(), "lineage insert");
        }

        private static void InsertBranch(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pLineageId, long pShiId,
            WesternLineageAdmissionCommitRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "INSERT INTO " + ShiBranch +
                " (SHI_ID,LINEAGE_ID,CLAN_NAME,PARENT_SHI_ID," +
                "NAMING_PROFILE,WESTERN_NAMING_TRADITION," +
                "ORIGIN_CITY_CHINESE_NAME,DISPLAY_STEM," +
                "FOUNDER_ACTOR_ID,SOURCE_TYPE,ORIGIN_KINGDOM_ID," +
                "ORIGIN_CITY_ID,ORIGIN_ORIGINAL_CLAN_ID,CREATED_TIME," +
                "IS_EXTINCT) VALUES (@shi,@lineage,@clan,@parent," +
                "@profile,@tradition,@origin_name,@stem,@actor,@source," +
                "@kingdom,@city,@original,@time,0)";
            command.Parameters.AddWithValue("@shi", pShiId);
            command.Parameters.AddWithValue("@lineage", pLineageId);
            command.Parameters.AddWithValue("@clan", pRequest.ClanName);
            command.Parameters.AddWithValue("@parent", pRequest.ParentShiId);
            command.Parameters.AddWithValue("@profile",
                pRequest.NamingProfile);
            command.Parameters.AddWithValue("@tradition",
                pRequest.WesternNamingTradition ?? string.Empty);
            command.Parameters.AddWithValue("@origin_name",
                pRequest.OriginCityChineseName ?? string.Empty);
            command.Parameters.AddWithValue("@stem", pRequest.DisplayStem);
            command.Parameters.AddWithValue("@actor", pRequest.ActorId);
            command.Parameters.AddWithValue("@source", pRequest.SourceType ??
                string.Empty);
            command.Parameters.AddWithValue("@kingdom",
                pRequest.OriginKingdomId);
            command.Parameters.AddWithValue("@city", pRequest.OriginCityId);
            command.Parameters.AddWithValue("@original",
                pRequest.OriginOriginalClanId);
            command.Parameters.AddWithValue("@time", pRequest.CreatedTime);
            RequireOne(command.ExecuteNonQuery(), "branch insert");
        }

        private static void RequireLineage(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pLineageId)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT COUNT(*) FROM " + LineageGroup +
                " WHERE LINEAGE_ID=@lineage";
            command.Parameters.AddWithValue("@lineage", pLineageId);
            if (Convert.ToInt64(command.ExecuteScalar()) != 1L)
                throw new InvalidOperationException("lineage_not_found");
        }

        private static void RequireCompatibleBranch(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pLineageId, long pShiId,
            string pNamingProfile)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(NAMING_PROFILE,'xia')" +
                " FROM " + ShiBranch +
                " WHERE SHI_ID=@shi AND LINEAGE_ID=@lineage";
            command.Parameters.AddWithValue("@shi", pShiId);
            command.Parameters.AddWithValue("@lineage", pLineageId);
            object value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                throw new InvalidOperationException("branch_not_found");
            if (!string.Equals(Convert.ToString(value), pNamingProfile,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("branch_profile_mismatch");
        }

        private static void CompleteBranchInPlace(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pLineageId, long pShiId,
            WesternLineageAdmissionCommitRequest pRequest)
        {
            string profile;
            string existingClan;
            string existingTradition;
            string existingOrigin;
            string existingStem;
            using (var lookup = new SQLiteCommand(pDb)
                   { Transaction = pTransaction })
            {
                lookup.CommandText = "SELECT IFNULL(NAMING_PROFILE,'')," +
                    "IFNULL(CLAN_NAME,''),IFNULL(WESTERN_NAMING_TRADITION,'')," +
                    "IFNULL(ORIGIN_CITY_CHINESE_NAME,''),IFNULL(DISPLAY_STEM,'')" +
                    " FROM " + ShiBranch +
                    " WHERE SHI_ID=@shi AND LINEAGE_ID=@lineage";
                lookup.Parameters.AddWithValue("@shi", pShiId);
                lookup.Parameters.AddWithValue("@lineage", pLineageId);
                using SQLiteDataReader reader = lookup.ExecuteReader();
                if (!reader.Read())
                    throw new InvalidOperationException("branch_not_found");
                profile = reader.GetString(0);
                existingClan = reader.GetString(1);
                existingTradition = reader.GetString(2);
                existingOrigin = reader.GetString(3);
                existingStem = reader.GetString(4);
            }
            if (string.Equals(profile, "xia", StringComparison.Ordinal) ||
                (profile.Length > 0 && !string.Equals(profile,
                    pRequest.NamingProfile, StringComparison.Ordinal)))
                throw new InvalidOperationException("branch_profile_mismatch");

            RejectIdentityConflict(existingClan, pRequest.ClanName,
                "clan_identity_conflict");
            RejectIdentityConflict(existingTradition,
                pRequest.WesternNamingTradition,
                "tradition_identity_conflict");
            RejectIdentityConflict(existingOrigin,
                pRequest.OriginCityChineseName,
                "origin_identity_conflict");
            RejectIdentityConflict(existingStem, pRequest.DisplayStem,
                "stem_identity_conflict");

            using var update = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            update.CommandText = "UPDATE " + ShiBranch +
                " SET CLAN_NAME=CASE WHEN IFNULL(CLAN_NAME,'')='' THEN @clan ELSE CLAN_NAME END," +
                "NAMING_PROFILE=CASE WHEN IFNULL(NAMING_PROFILE,'')='' THEN @profile ELSE NAMING_PROFILE END," +
                "WESTERN_NAMING_TRADITION=CASE WHEN IFNULL(WESTERN_NAMING_TRADITION,'')='' THEN @tradition ELSE WESTERN_NAMING_TRADITION END," +
                "ORIGIN_CITY_CHINESE_NAME=CASE WHEN IFNULL(ORIGIN_CITY_CHINESE_NAME,'')='' THEN @origin_name ELSE ORIGIN_CITY_CHINESE_NAME END," +
                "DISPLAY_STEM=CASE WHEN IFNULL(DISPLAY_STEM,'')='' THEN @stem ELSE DISPLAY_STEM END" +
                " WHERE SHI_ID=@shi AND " +
                "LINEAGE_ID=@lineage";
            update.Parameters.AddWithValue("@clan", pRequest.ClanName);
            update.Parameters.AddWithValue("@profile",
                pRequest.NamingProfile);
            update.Parameters.AddWithValue("@tradition",
                pRequest.WesternNamingTradition ?? string.Empty);
            update.Parameters.AddWithValue("@origin_name",
                pRequest.OriginCityChineseName ?? string.Empty);
            update.Parameters.AddWithValue("@stem", pRequest.DisplayStem);
            update.Parameters.AddWithValue("@shi", pShiId);
            update.Parameters.AddWithValue("@lineage", pLineageId);
            RequireOne(update.ExecuteNonQuery(), "branch completion");
        }

        private static string ReadBranchProfile(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pLineageId, long pShiId)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(NAMING_PROFILE,'') FROM " +
                ShiBranch + " WHERE SHI_ID=@shi AND LINEAGE_ID=@lineage";
            command.Parameters.AddWithValue("@shi", pShiId);
            command.Parameters.AddWithValue("@lineage", pLineageId);
            object value = command.ExecuteScalar();
            if (value == null) throw new InvalidOperationException(
                "branch_not_found");
            return Convert.ToString(value) ?? string.Empty;
        }

        private static void RejectIdentityConflict(string pExisting,
            string pRequested, string pFailure)
        {
            if (!string.IsNullOrWhiteSpace(pExisting) &&
                !string.IsNullOrWhiteSpace(pRequested) &&
                !string.Equals(pExisting.Trim(), pRequested.Trim(),
                    StringComparison.Ordinal))
                throw new InvalidOperationException(pFailure);
        }

        private static bool TryResolveExistingActorBinding(
            SQLiteConnection pDb, SQLiteTransaction pTransaction,
            WesternLineageAdmissionCommitRequest pRequest,
            out long pLineageId, out long pShiId, out string pFailure)
        {
            pLineageId = -1L;
            pShiId = -1L;
            pFailure = string.Empty;
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT LINEAGE_ID,SHI_ID FROM " +
                ActorArchive + " WHERE ID=@actor";
            command.Parameters.AddWithValue("@actor", pRequest.ActorId);
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                if (!reader.Read()) return false;
                pLineageId = reader.IsDBNull(0) ? -1L : reader.GetInt64(0);
                pShiId = reader.IsDBNull(1) ? -1L : reader.GetInt64(1);
            }
            if (pLineageId < 0L || pShiId < 0L)
            {
                pFailure = "actor_binding_incomplete";
                return false;
            }
            string profile = ReadBranchProfile(pDb, pTransaction,
                pLineageId, pShiId);
            if (!string.Equals(profile, pRequest.NamingProfile,
                    StringComparison.Ordinal))
            {
                pFailure = "actor_binding_profile_mismatch";
                return false;
            }
            return true;
        }

        private static void UpsertActor(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pLineageId, long pShiId,
            WesternLineageAdmissionCommitRequest pRequest)
        {
            int affected;
            using (var update = new SQLiteCommand(pDb)
                   { Transaction = pTransaction })
            {
                update.CommandText = "UPDATE " + ActorArchive +
                    " SET GIVEN_NAME=@given,DISPLAY_NAME=@display," +
                    "FAMILY_NAME=@family,CLAN_NAME=@clan," +
                    "LINEAGE_ID=@lineage,SHI_ID=@shi,ASSET_ID=@asset," +
                    "SEX=@sex,STATUS='noble',NOBLE_DISTANCE=0," +
                    "IS_ALIVE=1 WHERE ID=@actor";
                BindActor(update, pRequest, pLineageId, pShiId);
                affected = update.ExecuteNonQuery();
            }
            if (affected == 1) return;
            if (affected != 0)
                throw new InvalidOperationException(
                    "actor update affected multiple rows");

            using var insert = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            insert.CommandText = "INSERT INTO " + ActorArchive +
                " (ID,GIVEN_NAME,DISPLAY_NAME,FAMILY_NAME,CLAN_NAME," +
                "LINEAGE_ID,SHI_ID,ASSET_ID,SEX,STATUS,NOBLE_DISTANCE," +
                "IS_ALIVE) VALUES (@actor,@given,@display,@family,@clan," +
                "@lineage,@shi,@asset,@sex,'noble',0,1)";
            BindActor(insert, pRequest, pLineageId, pShiId);
            RequireOne(insert.ExecuteNonQuery(), "actor insert");
        }

        private static void BindActor(SQLiteCommand pCommand,
            WesternLineageAdmissionCommitRequest pRequest,
            long pLineageId, long pShiId)
        {
            pCommand.Parameters.AddWithValue("@actor", pRequest.ActorId);
            pCommand.Parameters.AddWithValue("@given", pRequest.GivenName ??
                string.Empty);
            pCommand.Parameters.AddWithValue("@display",
                pRequest.DisplayName ?? string.Empty);
            pCommand.Parameters.AddWithValue("@family", pRequest.FamilyName);
            pCommand.Parameters.AddWithValue("@clan", pRequest.ClanName);
            pCommand.Parameters.AddWithValue("@lineage", pLineageId);
            pCommand.Parameters.AddWithValue("@shi", pShiId);
            pCommand.Parameters.AddWithValue("@asset", pRequest.AssetId ??
                string.Empty);
            pCommand.Parameters.AddWithValue("@sex", pRequest.Sex);
        }

        private static void Inject(
            Func<WesternLineageAdmissionCommitStage, bool> pFail,
            WesternLineageAdmissionCommitStage pStage)
        {
            if (pFail?.Invoke(pStage) == true)
                throw new InvalidOperationException(
                    "injected_failure_" + pStage);
        }

        private static void RequireOne(int pAffected, string pOperation)
        {
            if (pAffected != 1)
                throw new InvalidOperationException(pOperation +
                    " did not affect exactly one row");
        }

        private static WesternLineageAdmissionCommitResult Failed(
            string pFailure)
        {
            return new WesternLineageAdmissionCommitResult(false, -1L, -1L,
                pFailure);
        }
    }
}
