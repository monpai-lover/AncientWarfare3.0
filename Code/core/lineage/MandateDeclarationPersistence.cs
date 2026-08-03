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

            using SQLiteTransaction transaction = pDb.BeginTransaction();
            try
            {
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
                   pRequest.StartMandate >= 0 &&
                   pRequest.EmperorTitle >= 0;
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
                              " WHERE STATE_ID=@state"
            };
            AddCommonParameters(update, pRequest);
            int affected = update.ExecuteNonQuery();
            if (affected == 1) return;
            if (affected != 0)
                throw new InvalidOperationException(
                    "mandate declaration updated multiple state rows");

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
        }
    }
}
