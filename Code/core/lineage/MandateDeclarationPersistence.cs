using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class MandateDeclarationPersistence
    {
        public sealed class Request
        {
            public long StateId;
            public long PeriodId;
            public long KingdomId;
            public string KingdomName = "";
            public string KingdomColor = "";
            public string DynastyName = "";
            public long RulerActorId;
            public string RulerName = "";
            public double StartTime;
            public int CurrentYear;
            public int StartMandate;
            public int ImperialAuthority;
            public int DynastyPrestige;
            public double CoreControl;
            public double VassalLoyalty;
            public string CrisisLevel = "";
            public string OriginType = "native";
            public long RebelOriginKingdomId = -1L;
            public string RebelOriginKingdomName = "";
            public string ClaimantKind = "orthodox";
            public string MapMarkerKind = "moh";
            public int EmperorTitle;
            public bool ExpectedPreviousActive;
            public long PreviousPeriodId = -1L;
            public long PreviousKingdomId = -1L;
            public string PreviousKingdomName = "";
            public string PreviousKingdomColor = "";
            public long PreviousRulerActorId = -1L;
            public string PreviousRulerName = "";
            public int PreviousMandateValue;
            public string PreviousEndReason = "replaced";
            public string NewYearPrefix = "";
            public string NewYearPrefixRich = "";
            public string PreviousYearPrefix = "";
            public string PreviousYearPrefixRich = "";
            public string OperationKey = "";
            public bool WasAlreadyEmperor;
        }

        public static bool TryCommit(SQLiteConnection pDb,
            string pPeriodTable, string pStateTable, string pReignTable,
            Request pRequest, out string pError)
        {
            pError = "";
            if (!Valid(pDb, pPeriodTable, pStateTable, pReignTable,
                    pRequest))
            {
                pError = "invalid mandate declaration persistence input";
                return false;
            }

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                if (pRequest.ExpectedPreviousActive)
                    EndPreviousPeriod(pDb, transaction, pPeriodTable,
                        pRequest);
                InsertPeriod(pDb, transaction, pPeriodTable, pRequest);
                UpsertState(pDb, transaction, pStateTable, pRequest);
                if (!ReignMandateProjectionPersistence.TryProject(
                        pDb, transaction, pReignTable, pRequest.KingdomId,
                        pRequest.RulerActorId, pRequest.PeriodId,
                        pRequest.EmperorTitle, pRequest.KingdomName,
                        out pError))
                {
                    transaction.Rollback();
                    return false;
                }
                var pending = new MandateProjectionOutboxPersistence.
                    PendingProjection
                {
                    OperationKey = pRequest.OperationKey,
                    PeriodId = pRequest.PeriodId,
                    KingdomId = pRequest.KingdomId,
                    KingdomName = pRequest.KingdomName,
                    KingdomColor = pRequest.KingdomColor,
                    DynastyName = pRequest.DynastyName,
                    RulerActorId = pRequest.RulerActorId,
                    RulerName = pRequest.RulerName,
                    PreviousPeriodId = pRequest.PreviousPeriodId,
                    PreviousKingdomId = pRequest.PreviousKingdomId,
                    PreviousKingdomName = pRequest.PreviousKingdomName,
                    PreviousKingdomColor = pRequest.PreviousKingdomColor,
                    PreviousRulerActorId = pRequest.PreviousRulerActorId,
                    PreviousRulerName = pRequest.PreviousRulerName,
                    PreviousMandateValue = pRequest.PreviousMandateValue,
                    PreviousEndReason = pRequest.PreviousEndReason,
                    OldEndRequired = pRequest.ExpectedPreviousActive,
                    CurrentYear = pRequest.CurrentYear,
                    WasAlreadyEmperor = pRequest.WasAlreadyEmperor,
                    OriginType = pRequest.OriginType,
                    ClaimantKind = pRequest.ClaimantKind,
                    MapMarkerKind = pRequest.MapMarkerKind,
                    NewYearPrefix = pRequest.NewYearPrefix,
                    NewYearPrefixRich = pRequest.NewYearPrefixRich,
                    PreviousYearPrefix = pRequest.PreviousYearPrefix,
                    PreviousYearPrefixRich =
                        pRequest.PreviousYearPrefixRich,
                    CreatedTime = pRequest.StartTime
                };
                if (!MandateProjectionOutboxPersistence.TryEnqueue(
                        pDb, transaction, pending, out pError))
                {
                    transaction.Rollback();
                    return false;
                }
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction.Rollback(); }
                catch { }
                pError = error.Message;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static bool Valid(SQLiteConnection pDb,
            string pPeriodTable, string pStateTable, string pReignTable,
            Request pRequest)
        {
            return pDb != null && pRequest != null &&
                   string.Equals(pPeriodTable, "MandatePeriod",
                       StringComparison.Ordinal) &&
                   string.Equals(pStateTable, "MandateState",
                       StringComparison.Ordinal) &&
                   string.Equals(pReignTable, "KingdomReign",
                       StringComparison.Ordinal) &&
                   pRequest.StateId >= 0L && pRequest.PeriodId >= 0L &&
                   pRequest.KingdomId >= 0L &&
                   pRequest.RulerActorId >= 0L &&
                   !string.IsNullOrWhiteSpace(pRequest.OperationKey) &&
                   pRequest.StartMandate >= 0 &&
                   pRequest.EmperorTitle >= 0 &&
                   (!pRequest.ExpectedPreviousActive ||
                    (pRequest.PreviousPeriodId >= 0L &&
                     pRequest.PreviousKingdomId >= 0L &&
                     pRequest.PreviousKingdomId != pRequest.KingdomId));
        }

        private static void EndPreviousPeriod(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, Request pRequest)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "UPDATE " + pTable +
                    " SET END_TIME=@time,END_REASON=@previousEndReason," +
                    "END_MANDATE=@previousMandate WHERE PERIOD_ID=@previousPeriod" +
                    " AND KINGDOM_ID=@previousKingdom AND END_TIME=-1"
            };
            AddCommonParameters(command, pRequest);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "mandate replacement expected one previous period");
        }

        private static void InsertPeriod(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, Request pRequest)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " + pTable +
                    "(PERIOD_ID,KINGDOM_ID,KINGDOM_NAME,KINGDOM_COLOR," +
                    "DYNASTY_NAME,FOUNDER_ACTOR_ID,FOUNDER_NAME,START_TIME," +
                    "END_TIME,END_REASON,START_MANDATE,END_MANDATE," +
                    "LEGAL_CORE_COUNT,ORIGIN_TYPE,REBEL_ORIGIN_KINGDOM_ID," +
                    "REBEL_ORIGIN_KINGDOM_NAME,CLAIMANT_KIND) VALUES(" +
                    "@period,@kingdom,@kingdomName,@kingdomColor,@dynasty," +
                    "@actor,@actorName,@time,-1,'',@mandate,@mandate,0," +
                    "@origin,@rebelId,@rebelName,@claimant)"
            };
            AddCommonParameters(command, pRequest);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "mandate declaration expected one period row");
        }

        private static void UpsertState(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, Request pRequest)
        {
            string assignments =
                "ACTIVE=1,KINGDOM_ID=@kingdom,KINGDOM_NAME=@kingdomName," +
                "KINGDOM_COLOR=@kingdomColor,DYNASTY_NAME=@dynasty," +
                "EMPEROR_ACTOR_ID=@actor,EMPEROR_NAME=@actorName," +
                "PERIOD_ID=@period,MANDATE_VALUE=@mandate," +
                "IMPERIAL_AUTHORITY=@authority," +
                "DYNASTY_PRESTIGE=@prestige,CORE_CONTROL=@coreControl," +
                "VASSAL_LOYALTY=@vassalLoyalty,CRISIS_LEVEL=@crisis," +
                "START_TIME=@time,UPDATED_TIME=@time,LAST_YEAR=@year," +
                "ORIGIN_TYPE=@origin,ORIGINAL_CORE_COUNT=0," +
                "REBEL_ORIGIN_KINGDOM_ID=@rebelId," +
                "REBEL_ORIGIN_KINGDOM_NAME=@rebelName," +
                "CLAIMANT_KIND=@claimant,MAP_MARKER_KIND=@marker";
            using var update = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "UPDATE " + pTable + " SET " + assignments +
                    " WHERE STATE_ID=@state AND " +
                    (pRequest.ExpectedPreviousActive
                        ? "ACTIVE=1 AND KINGDOM_ID=@previousKingdom " +
                          "AND PERIOD_ID=@previousPeriod"
                        : "ACTIVE=0")
            };
            AddCommonParameters(update, pRequest);
            int affected = update.ExecuteNonQuery();
            if (affected == 1) return;
            if (affected != 0)
                throw new InvalidOperationException(
                    "mandate declaration updated multiple state rows");
            if (pRequest.ExpectedPreviousActive)
                throw new InvalidOperationException(
                    "mandate replacement expected one previous state row");

            using var insert = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " + pTable + "(STATE_ID," +
                    "ACTIVE,KINGDOM_ID,KINGDOM_NAME,KINGDOM_COLOR," +
                    "DYNASTY_NAME,EMPEROR_ACTOR_ID,EMPEROR_NAME,PERIOD_ID," +
                    "MANDATE_VALUE,IMPERIAL_AUTHORITY,DYNASTY_PRESTIGE," +
                    "CORE_CONTROL,VASSAL_LOYALTY,CRISIS_LEVEL,START_TIME," +
                    "UPDATED_TIME,LAST_YEAR,ORIGIN_TYPE,ORIGINAL_CORE_COUNT," +
                    "REBEL_ORIGIN_KINGDOM_ID,REBEL_ORIGIN_KINGDOM_NAME," +
                    "CLAIMANT_KIND,MAP_MARKER_KIND) VALUES(@state,1," +
                    "@kingdom,@kingdomName,@kingdomColor,@dynasty,@actor," +
                    "@actorName,@period,@mandate,@authority,@prestige," +
                    "@coreControl,@vassalLoyalty,@crisis,@time,@time,@year," +
                    "@origin,0,@rebelId,@rebelName,@claimant,@marker)"
            };
            AddCommonParameters(insert, pRequest);
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "mandate declaration expected one state row");
        }

        private static void AddCommonParameters(SQLiteCommand pCommand,
            Request pRequest)
        {
            pCommand.Parameters.AddWithValue("@state", pRequest.StateId);
            pCommand.Parameters.AddWithValue("@period", pRequest.PeriodId);
            pCommand.Parameters.AddWithValue("@kingdom", pRequest.KingdomId);
            pCommand.Parameters.AddWithValue("@kingdomName",
                pRequest.KingdomName ?? "");
            pCommand.Parameters.AddWithValue("@kingdomColor",
                pRequest.KingdomColor ?? "");
            pCommand.Parameters.AddWithValue("@dynasty",
                pRequest.DynastyName ?? "");
            pCommand.Parameters.AddWithValue("@actor", pRequest.RulerActorId);
            pCommand.Parameters.AddWithValue("@actorName",
                pRequest.RulerName ?? "");
            pCommand.Parameters.AddWithValue("@time", pRequest.StartTime);
            pCommand.Parameters.AddWithValue("@year", pRequest.CurrentYear);
            pCommand.Parameters.AddWithValue("@mandate",
                pRequest.StartMandate);
            pCommand.Parameters.AddWithValue("@authority",
                pRequest.ImperialAuthority);
            pCommand.Parameters.AddWithValue("@prestige",
                pRequest.DynastyPrestige);
            pCommand.Parameters.AddWithValue("@coreControl",
                pRequest.CoreControl);
            pCommand.Parameters.AddWithValue("@vassalLoyalty",
                pRequest.VassalLoyalty);
            pCommand.Parameters.AddWithValue("@crisis",
                pRequest.CrisisLevel ?? "");
            pCommand.Parameters.AddWithValue("@origin",
                pRequest.OriginType ?? "native");
            pCommand.Parameters.AddWithValue("@rebelId",
                pRequest.RebelOriginKingdomId);
            pCommand.Parameters.AddWithValue("@rebelName",
                pRequest.RebelOriginKingdomName ?? "");
            pCommand.Parameters.AddWithValue("@claimant",
                pRequest.ClaimantKind ?? "orthodox");
            pCommand.Parameters.AddWithValue("@marker",
                pRequest.MapMarkerKind ?? "moh");
            pCommand.Parameters.AddWithValue("@previousPeriod",
                pRequest.PreviousPeriodId);
            pCommand.Parameters.AddWithValue("@previousKingdom",
                pRequest.PreviousKingdomId);
            pCommand.Parameters.AddWithValue("@previousMandate",
                pRequest.PreviousMandateValue);
            pCommand.Parameters.AddWithValue("@previousEndReason",
                pRequest.PreviousEndReason ?? "replaced");
        }
    }
}
