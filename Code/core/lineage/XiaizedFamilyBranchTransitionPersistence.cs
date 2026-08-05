using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal enum XiaizedFamilyBranchTransitionStage
    {
        AfterBranchWrite,
        AfterActorWrite
    }

    internal sealed class XiaizedFamilyBranchTransitionRequest
    {
        internal long FounderActorId = -1L;
        internal long LineageId = -1L;
        internal long OldShiId = -1L;
        internal string OldNamingProfile = string.Empty;
        internal string FamilyName = string.Empty;
        internal string ClanName = string.Empty;
        internal string DisplayStem = string.Empty;
        internal long OriginKingdomId = -1L;
        internal long OriginCityId = -1L;
        internal string OriginCityChineseName = string.Empty;
        internal double CreatedTime;
    }

    internal sealed class XiaizedFamilyBranchTransitionResult
    {
        internal XiaizedFamilyBranchTransitionResult(bool pSuccess,
            long pNewShiId, IReadOnlyList<long> pMovedActorIds,
            string pFailure)
        {
            Success = pSuccess;
            NewShiId = pNewShiId;
            MovedActorIds = pMovedActorIds ?? Array.Empty<long>();
            Failure = pFailure ?? string.Empty;
        }

        internal bool Success { get; }
        internal long NewShiId { get; }
        internal IReadOnlyList<long> MovedActorIds { get; }
        internal string Failure { get; }
    }

    internal static class XiaizedFamilyBranchTransitionPersistence
    {
        private const string TransitionSource = "xiaization_transition";

        internal static XiaizedFamilyBranchTransitionResult TryCommit(
            SQLiteConnection pDb,
            XiaizedFamilyBranchTransitionRequest pRequest,
            Func<XiaizedFamilyBranchTransitionStage, bool> pFail = null)
        {
            string validation = Validate(pDb, pRequest);
            if (validation.Length > 0) return Failed(validation);

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction(IsolationLevel.Serializable);
                RequireOldBranch(pDb, transaction, pRequest);
                long newShiId = FindExistingChild(pDb, transaction, pRequest);
                if (newShiId < 0L)
                {
                    newShiId = NextShiId(pDb, transaction);
                    InsertChild(pDb, transaction, pRequest, newShiId);
                }
                Inject(pFail,
                    XiaizedFamilyBranchTransitionStage.AfterBranchWrite);

                List<long> movedActorIds = ReadLivingMemberIds(pDb,
                    transaction, pRequest.OldShiId, newShiId);
                RebindLivingMembers(pDb, transaction, pRequest, newShiId);
                Inject(pFail,
                    XiaizedFamilyBranchTransitionStage.AfterActorWrite);
                transaction.Commit();
                return new XiaizedFamilyBranchTransitionResult(true,
                    newShiId, movedActorIds, string.Empty);
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
            XiaizedFamilyBranchTransitionRequest pRequest)
        {
            if (pDb == null) return "database_unavailable";
            if (pRequest == null) return "request_missing";
            if (pRequest.FounderActorId < 0L || pRequest.LineageId < 0L ||
                pRequest.OldShiId < 0L) return "identity_missing";
            if (pRequest.OldNamingProfile != "western" &&
                pRequest.OldNamingProfile != "orc_nomadic")
                return "source_profile_invalid";
            if (string.IsNullOrWhiteSpace(pRequest.FamilyName) ||
                string.IsNullOrWhiteSpace(pRequest.ClanName))
                return "xia_identity_missing";
            return string.Empty;
        }

        private static void RequireOldBranch(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            XiaizedFamilyBranchTransitionRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(NAMING_PROFILE,'') FROM " +
                "ShiBranch WHERE SHI_ID=@shi AND LINEAGE_ID=@lineage";
            command.Parameters.AddWithValue("@shi", pRequest.OldShiId);
            command.Parameters.AddWithValue("@lineage", pRequest.LineageId);
            object value = command.ExecuteScalar();
            if (value == null || value == DBNull.Value)
                throw new InvalidOperationException("source_branch_missing");
            if (!string.Equals(Convert.ToString(value),
                    pRequest.OldNamingProfile, StringComparison.Ordinal))
                throw new InvalidOperationException("source_profile_mismatch");
        }

        private static long FindExistingChild(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            XiaizedFamilyBranchTransitionRequest pRequest)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT SHI_ID FROM ShiBranch WHERE " +
                "LINEAGE_ID=@lineage AND PARENT_SHI_ID=@parent AND " +
                "NAMING_PROFILE='xia' AND SOURCE_TYPE=@source " +
                "ORDER BY SHI_ID LIMIT 1";
            command.Parameters.AddWithValue("@lineage", pRequest.LineageId);
            command.Parameters.AddWithValue("@parent", pRequest.OldShiId);
            command.Parameters.AddWithValue("@source", TransitionSource);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? -1L
                : Convert.ToInt64(value);
        }

        private static long NextShiId(SQLiteConnection pDb,
            SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(
                "SELECT IFNULL(MAX(SHI_ID),0)+1 FROM ShiBranch", pDb,
                pTransaction);
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private static void InsertChild(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            XiaizedFamilyBranchTransitionRequest pRequest, long pNewShiId)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "INSERT INTO ShiBranch " +
                "(SHI_ID,LINEAGE_ID,CLAN_NAME,PARENT_SHI_ID,NAMING_PROFILE," +
                "WESTERN_NAMING_TRADITION,ORIGIN_CITY_CHINESE_NAME," +
                "DISPLAY_STEM,FOUNDER_ACTOR_ID,SOURCE_TYPE," +
                "ORIGIN_KINGDOM_ID,ORIGIN_CITY_ID,ORIGIN_ORIGINAL_CLAN_ID," +
                "CREATED_TIME,IS_EXTINCT) VALUES " +
                "(@shi,@lineage,@clan,@parent,'xia','',@origin,@stem," +
                "@founder,@source,@kingdom,@city,-1,@time,0)";
            command.Parameters.AddWithValue("@shi", pNewShiId);
            command.Parameters.AddWithValue("@lineage", pRequest.LineageId);
            command.Parameters.AddWithValue("@clan", pRequest.ClanName);
            command.Parameters.AddWithValue("@parent", pRequest.OldShiId);
            command.Parameters.AddWithValue("@origin",
                pRequest.OriginCityChineseName ?? string.Empty);
            command.Parameters.AddWithValue("@stem",
                string.IsNullOrWhiteSpace(pRequest.DisplayStem)
                    ? pRequest.ClanName
                    : pRequest.DisplayStem.Trim());
            command.Parameters.AddWithValue("@founder",
                pRequest.FounderActorId);
            command.Parameters.AddWithValue("@source", TransitionSource);
            command.Parameters.AddWithValue("@kingdom",
                pRequest.OriginKingdomId);
            command.Parameters.AddWithValue("@city", pRequest.OriginCityId);
            command.Parameters.AddWithValue("@time", pRequest.CreatedTime);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("child_branch_insert_failed");
        }

        private static List<long> ReadLivingMemberIds(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pOldShiId, long pNewShiId)
        {
            var result = new List<long>();
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT ID FROM ActorArchive WHERE " +
                "IS_ALIVE=1 AND (SHI_ID=@old OR SHI_ID=@new) ORDER BY ID";
            command.Parameters.AddWithValue("@old", pOldShiId);
            command.Parameters.AddWithValue("@new", pNewShiId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read()) result.Add(reader.GetInt64(0));
            return result;
        }

        private static void RebindLivingMembers(SQLiteConnection pDb,
            SQLiteTransaction pTransaction,
            XiaizedFamilyBranchTransitionRequest pRequest, long pNewShiId)
        {
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "UPDATE ActorArchive SET SHI_ID=@new," +
                "FAMILY_NAME=@family,CLAN_NAME=@clan,NAME_INTEGRATED=1 " +
                "WHERE IS_ALIVE=1 AND (SHI_ID=@old OR SHI_ID=@new)";
            command.Parameters.AddWithValue("@new", pNewShiId);
            command.Parameters.AddWithValue("@family", pRequest.FamilyName);
            command.Parameters.AddWithValue("@clan", pRequest.ClanName);
            command.Parameters.AddWithValue("@old", pRequest.OldShiId);
            command.ExecuteNonQuery();
        }

        private static void Inject(
            Func<XiaizedFamilyBranchTransitionStage, bool> pFail,
            XiaizedFamilyBranchTransitionStage pStage)
        {
            if (pFail?.Invoke(pStage) == true)
                throw new InvalidOperationException(
                    "injected_failure_" + pStage);
        }

        private static XiaizedFamilyBranchTransitionResult Failed(
            string pFailure)
        {
            return new XiaizedFamilyBranchTransitionResult(false, -1L,
                Array.Empty<long>(), pFailure);
        }
    }
}
