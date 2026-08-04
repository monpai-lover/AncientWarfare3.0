using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    public static class ReignAccessionPersistence
    {
        public sealed class Request
        {
            public long KingdomId;
            public long NewReignId;
            public long NewRulerActorId;
            public string NewKingdomColor = "";
            public long NewShiId = -1L;
            public long NewDynastyId = -1L;
            public long NewMandatePeriodId = -1L;
            public int NewHighestTitle;
            public string NewStateName = "";
            public string NewRulerName = "";
            public string NewRulerColor = "";
            public int NewReignIndex;
            public double NewStartTime;
            public string NewYearNameStem = "";
            public string NewYearNameColor = "";
            public int NewStartPopulation;
            public int NewStartCityCount;
            public int NewStartArmyCount;
            public int NewIsFounder;
            public double OldEndTime;
            public string OldEndReason = "replaced";
            public int OldEndPopulation;
            public int OldEndCityCount;
            public int OldEndArmyCount;
            public int OldWarWins;
            public int OldWarLosses;
            public int OldLostCapital;
            public int OldHighestTitle;
            public string OldDeathCause = "";
        }

        public static bool TryTransition(SQLiteConnection pDb,
            string pTable, Request pRequest, out string pError)
        {
            return TryTransition(pDb, pTable, pRequest,
                out _, out pError);
        }

        public static bool TryTransition(SQLiteConnection pDb,
            string pTable, Request pRequest, out double pOpenStartTime,
            out string pError)
        {
            pOpenStartTime = -1d;
            pError = "";
            if (pDb == null || pRequest == null ||
                !string.Equals(pTable, "KingdomReign",
                    StringComparison.Ordinal) ||
                pRequest.KingdomId < 0L || pRequest.NewReignId < 0L ||
                pRequest.NewRulerActorId < 0L ||
                pRequest.NewStartTime < 0d || pRequest.OldEndTime < 0d)
            {
                pError = "invalid reign accession persistence input";
                return false;
            }

            SQLiteTransaction transaction = null;
            try
            {
                transaction = pDb.BeginTransaction();
                if (!TryReadOpen(pDb, transaction, pTable,
                        pRequest.KingdomId, out long openReignId,
                        out long openRulerActorId, out int openCount,
                        out double openStartTime, out pError))
                {
                    transaction.Rollback();
                    return false;
                }
                if (openCount == 1 &&
                    openRulerActorId == pRequest.NewRulerActorId)
                {
                    pOpenStartTime = openStartTime;
                    transaction.Commit();
                    return true;
                }
                if (openCount == 1)
                    CloseOld(pDb, transaction, pTable, pRequest,
                        openReignId, openRulerActorId);
                InsertNew(pDb, transaction, pTable, pRequest);
                pOpenStartTime = pRequest.NewStartTime;
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                pError = error.Message;
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static bool TryReadOpen(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, long pKingdomId,
            out long pReignId, out long pRulerActorId, out int pCount,
            out double pStartTime, out string pError)
        {
            pReignId = -1L;
            pRulerActorId = -1L;
            pCount = 0;
            pStartTime = -1d;
            pError = "";
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT REIGN_ID,KING_ACTOR_ID,START_TIME FROM " +
                    pTable + " WHERE KINGDOM_ID=@kingdom AND END_TIME=-1 " +
                    "ORDER BY START_TIME DESC,REIGN_ID DESC"
            };
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                pCount++;
                if (pCount == 1)
                {
                    pReignId = Convert.ToInt64(reader.GetValue(0));
                    pRulerActorId = Convert.ToInt64(reader.GetValue(1));
                    pStartTime = Convert.ToDouble(reader.GetValue(2));
                }
            }
            if (pCount <= 1) return true;
            pError = "reign accession expected at most one open reign, got " +
                     pCount;
            return false;
        }

        private static void CloseOld(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, Request pRequest,
            long pOpenReignId, long pOpenRulerActorId)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "UPDATE " + pTable +
                    " SET END_TIME=@endTime,END_REASON=@reason," +
                    "END_POPULATION=@population,END_CITY_COUNT=@cities," +
                    "END_ARMY_COUNT=@armies,WAR_WINS=@wins," +
                    "WAR_LOSSES=@losses,LOST_CAPITAL=@lostCapital," +
                    "HIGHEST_TITLE=CASE WHEN HIGHEST_TITLE<@highest " +
                    "THEN @highest ELSE HIGHEST_TITLE END," +
                    "DEATH_CAUSE=@deathCause WHERE REIGN_ID=@reign " +
                    "AND KINGDOM_ID=@kingdom AND KING_ACTOR_ID=@actor " +
                    "AND END_TIME=-1"
            };
            command.Parameters.AddWithValue("@endTime", pRequest.OldEndTime);
            command.Parameters.AddWithValue("@reason",
                pRequest.OldEndReason ?? "replaced");
            command.Parameters.AddWithValue("@population",
                pRequest.OldEndPopulation);
            command.Parameters.AddWithValue("@cities",
                pRequest.OldEndCityCount);
            command.Parameters.AddWithValue("@armies",
                pRequest.OldEndArmyCount);
            command.Parameters.AddWithValue("@wins", pRequest.OldWarWins);
            command.Parameters.AddWithValue("@losses", pRequest.OldWarLosses);
            command.Parameters.AddWithValue("@lostCapital",
                pRequest.OldLostCapital);
            command.Parameters.AddWithValue("@highest",
                pRequest.OldHighestTitle);
            command.Parameters.AddWithValue("@deathCause",
                pRequest.OldDeathCause ?? "");
            command.Parameters.AddWithValue("@reign", pOpenReignId);
            command.Parameters.AddWithValue("@kingdom", pRequest.KingdomId);
            command.Parameters.AddWithValue("@actor", pOpenRulerActorId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "reign accession failed to close exactly one old reign");
        }

        private static void InsertNew(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, string pTable, Request pRequest)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " + pTable +
                    "(REIGN_ID,KINGDOM_ID,KINGDOM_COLOR,KING_ACTOR_ID," +
                    "SHI_ID,DYNASTY_ID,MANDATE_PERIOD_ID,HIGHEST_TITLE," +
                    "STATE_NAME_SNAPSHOT,KING_NAME,KING_COLOR,REIGN_INDEX," +
                    "START_TIME,END_TIME,YEAR_NAME_STEM,YEAR_NAME_COLOR," +
                    "END_REASON,START_POPULATION,START_CITY_COUNT," +
                    "START_ARMY_COUNT,END_POPULATION,END_CITY_COUNT," +
                    "END_ARMY_COUNT,IS_FOUNDER,WAR_WINS,WAR_LOSSES," +
                    "LOST_CAPITAL,DEATH_CAUSE) VALUES(@reign,@kingdom," +
                    "@kingdomColor,@actor,@shi,@dynasty,@mandate,@highest," +
                    "@state,@kingName,@kingColor,@index,@start,-1,@stem," +
                    "@yearColor,'',@population,@cities,@armies,0,0,0," +
                    "@founder,0,0,0,'')"
            };
            command.Parameters.AddWithValue("@reign", pRequest.NewReignId);
            command.Parameters.AddWithValue("@kingdom", pRequest.KingdomId);
            command.Parameters.AddWithValue("@kingdomColor",
                pRequest.NewKingdomColor ?? "");
            command.Parameters.AddWithValue("@actor",
                pRequest.NewRulerActorId);
            command.Parameters.AddWithValue("@shi", pRequest.NewShiId);
            command.Parameters.AddWithValue("@dynasty",
                pRequest.NewDynastyId);
            command.Parameters.AddWithValue("@mandate",
                pRequest.NewMandatePeriodId);
            command.Parameters.AddWithValue("@highest",
                pRequest.NewHighestTitle);
            command.Parameters.AddWithValue("@state",
                pRequest.NewStateName ?? "");
            command.Parameters.AddWithValue("@kingName",
                pRequest.NewRulerName ?? "");
            command.Parameters.AddWithValue("@kingColor",
                pRequest.NewRulerColor ?? "");
            command.Parameters.AddWithValue("@index",
                pRequest.NewReignIndex);
            command.Parameters.AddWithValue("@start", pRequest.NewStartTime);
            command.Parameters.AddWithValue("@stem",
                pRequest.NewYearNameStem ?? "");
            command.Parameters.AddWithValue("@yearColor",
                pRequest.NewYearNameColor ?? "");
            command.Parameters.AddWithValue("@population",
                pRequest.NewStartPopulation);
            command.Parameters.AddWithValue("@cities",
                pRequest.NewStartCityCount);
            command.Parameters.AddWithValue("@armies",
                pRequest.NewStartArmyCount);
            command.Parameters.AddWithValue("@founder",
                pRequest.NewIsFounder);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "reign accession failed to insert exactly one new reign");
        }
    }
}
