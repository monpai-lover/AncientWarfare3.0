using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     君主世系写入。OnKingChanged→OpenReign(新)+CloseOpenReign(旧)；
    ///     OnKingDied/OnAbdicate→CloseOpenReign(已在各 On 方法调过)。
    ///     亡国→CloseOpenReign("kingdom_fell")。谥号只写入 PosthumousTitle。
    /// </summary>
    internal static class ReignRecordWriter
    {
        public struct ReignInfo
        {
            public long ReignId;
            public long KingdomId;
            public long KingActorId;
            public long ShiId;
            public long DynastyId;
            public long MandatePeriodId;
            public int HighestTitle;
            public string StateNameSnapshot;
            public int StartPopulation;
            public int StartCityCount;
            public int StartArmyCount;
            public int EndPopulation;
            public int EndCityCount;
            public int EndArmyCount;
            public int IsFounder;
            public int WarWins;
            public int WarLosses;
            public int LostCapital;
            public string DeathCause;
            public string EndReason;
            public double StartTime;
            public double EndTime;
            public int ReignIndex;

            public bool IsValid => ReignId >= 0;

            public static ReignInfo Empty => new ReignInfo
            {
                ReignId = -1,
                KingdomId = -1,
                KingActorId = -1
            };
        }

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;
        private static string TABLE => KingdomReignTableItem.GetTableName();

        // ── 外部接口 ──

        public static bool OpenReign(Kingdom pKingdom, Actor pNewKing)
        {
            if (!Ready || pKingdom?.data == null || pNewKing?.data == null)
                return false;
            if (!LineageService.IsXiaKingdom(pKingdom) ||
                !LineageService.IsXia(pNewKing))
                return false;
            long reignId = TableIdAllocator.Next(DB, TABLE, "REIGN_ID");
            int idx = CountReigns(pKingdom.id) + 1;
            double now = World.world.getCurWorldTime();
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string stem, "");
            int pop = SafePopulation(pKingdom);
            int cities = SafeCityCount(pKingdom);
            int armies = SafeArmyCount(pKingdom);
            int isFounder = idx == 1 ? 1 : 0;
            string kingdomColor = HistoryColors.FromKingdom(pKingdom);
            string kingColor = HistoryColors.FromActor(pNewKing);
            pNewKing.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            long dynastyId = DynastyRecordWriter.GetCurrentDynastyId(pKingdom.id);
            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID, out long mandatePeriodId, -1L);
            int highestTitle = (int)KingdomTitleService.GetTitle(pKingdom);
            string stateName = StateNameService.GetBoundOrCurrentName(pKingdom, shiId);
            try
            {
                DB.Insert(TABLE,
                    ColumnVal.Create("REIGN_ID",           reignId),
                    ColumnVal.Create("KINGDOM_ID",         pKingdom.id),
                    ColumnVal.Create("KINGDOM_COLOR",      kingdomColor),
                    ColumnVal.Create("KING_ACTOR_ID",      pNewKing.data.id),
                    ColumnVal.Create("SHI_ID",             shiId),
                    ColumnVal.Create("DYNASTY_ID",         dynastyId),
                    ColumnVal.Create("MANDATE_PERIOD_ID",  mandatePeriodId),
                    ColumnVal.Create("HIGHEST_TITLE",      highestTitle),
                    ColumnVal.Create("STATE_NAME_SNAPSHOT", stateName),
                    ColumnVal.Create("KING_NAME",          pNewKing.getName()),
                    ColumnVal.Create("KING_COLOR",         string.IsNullOrEmpty(kingColor) ? kingdomColor : kingColor),
                    ColumnVal.Create("REIGN_INDEX",        idx),
                    ColumnVal.Create("START_TIME",         now),
                    ColumnVal.Create("END_TIME",           -1.0),
                    ColumnVal.Create("YEAR_NAME_STEM",     stem ?? ""),
                    ColumnVal.Create("YEAR_NAME_COLOR",    kingdomColor),
                    ColumnVal.Create("END_REASON",         ""),
                    ColumnVal.Create("START_POPULATION",   pop),
                    ColumnVal.Create("START_CITY_COUNT",   cities),
                    ColumnVal.Create("START_ARMY_COUNT",   armies),
                    ColumnVal.Create("END_POPULATION",     0),
                    ColumnVal.Create("END_CITY_COUNT",     0),
                    ColumnVal.Create("END_ARMY_COUNT",     0),
                    ColumnVal.Create("IS_FOUNDER",         isFounder),
                    ColumnVal.Create("WAR_WINS",           0),
                    ColumnVal.Create("WAR_LOSSES",         0),
                    ColumnVal.Create("LOST_CAPITAL",       0),
                    ColumnVal.Create("DEATH_CAUSE",        ""));
                pKingdom.data.set(LineageKeys.KINGDOM_REIGN_START,
                    (float)now);
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("ReignRecordWriter.OpenReign: " +
                                    e.Message);
                return false;
            }
        }

        public static bool EnsureOpenReign(Kingdom pKingdom, Actor pKing)
        {
            if (!Ready || pKingdom?.data == null || pKing?.data == null)
                return false;
            if (!TryReadUniqueOpenRuler(pKingdom.id, out long rulerActorId))
                return false;
            if (rulerActorId == pKing.data.id) return true;
            if (rulerActorId >= 0L || !OpenReign(pKingdom, pKing))
                return false;
            return TryReadUniqueOpenRuler(pKingdom.id, out rulerActorId) &&
                   rulerActorId == pKing.data.id;
        }

        public static bool TryTransitionReign(Kingdom pKingdom,
            Actor pNewKing, double pStartTime,
            out double pPersistedStartTime, out string pError)
        {
            pPersistedStartTime = -1d;
            pError = "";
            if (!Ready || pKingdom?.data == null || pNewKing?.data == null ||
                pStartTime < 0d || !LineageService.IsXiaKingdom(pKingdom) ||
                !LineageService.IsXia(pNewKing))
                return false;

            ReignInfo open = ReadOpenReignInfo(pKingdom.id);
            int endPop = SafePopulation(pKingdom);
            int endCities = SafeCityCount(pKingdom);
            int endArmies = SafeArmyCount(pKingdom);
            var (wins, losses) = open.IsValid
                ? WarRecordWriter.GetWarRecord(
                    pKingdom.id, open.StartTime, pStartTime)
                : (0, 0);
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME,
                out string stem, "");
            pNewKing.data.get(LineageKeys.SHI_ID, out long shiId, -1L);
            pKingdom.data.get(LineageKeys.MANDATE_PERIOD_ID,
                out long mandatePeriodId, -1L);
            int reignIndex = CountReigns(pKingdom.id) + 1;
            string kingdomColor = HistoryColors.FromKingdom(pKingdom);
            string rulerColor = HistoryColors.FromActor(pNewKing);
            int highestTitle = (int)KingdomTitleService.GetTitle(pKingdom);
            var request = new ReignAccessionPersistence.Request
            {
                KingdomId = pKingdom.id,
                NewReignId = TableIdAllocator.Next(DB, TABLE, "REIGN_ID"),
                NewRulerActorId = pNewKing.data.id,
                NewKingdomColor = kingdomColor,
                NewShiId = shiId,
                NewDynastyId = DynastyRecordWriter.GetCurrentDynastyId(
                    pKingdom.id),
                NewMandatePeriodId = mandatePeriodId,
                NewHighestTitle = highestTitle,
                NewStateName = StateNameService.GetBoundOrCurrentName(
                    pKingdom, shiId),
                NewRulerName = pNewKing.getName(),
                NewRulerColor = string.IsNullOrEmpty(rulerColor)
                    ? kingdomColor
                    : rulerColor,
                NewReignIndex = reignIndex,
                NewStartTime = pStartTime,
                NewYearNameStem = stem ?? "",
                NewYearNameColor = kingdomColor,
                NewStartPopulation = endPop,
                NewStartCityCount = endCities,
                NewStartArmyCount = endArmies,
                NewIsFounder = reignIndex == 1 ? 1 : 0,
                OldEndTime = pStartTime,
                OldEndReason = "replaced",
                OldEndPopulation = endPop,
                OldEndCityCount = endCities,
                OldEndArmyCount = endArmies,
                OldWarWins = wins,
                OldWarLosses = losses,
                OldLostCapital = 0,
                OldHighestTitle = Math.Max(open.HighestTitle, highestTitle),
                OldDeathCause = ""
            };
            return ReignAccessionPersistence.TryTransition(DB, TABLE,
                request, out pPersistedStartTime, out pError);
        }

        public static void ProjectCurrentReignStart(Kingdom pKingdom,
            Actor pKing, double pStartTime)
        {
            if (pKingdom?.data == null || pKing?.data == null ||
                pStartTime < 0d) return;
            pKingdom.data.set(LineageKeys.CHRONICLE_LAST_KING_ID,
                pKing.data.id);
            pKingdom.data.set(LineageKeys.KINGDOM_REIGN_START,
                (float)pStartTime);
        }

        public static bool ProjectMandateContext(Kingdom pKingdom,
            Actor pKing, long pMandatePeriodId)
        {
            if (!Ready || pKingdom?.data == null || pKing?.data == null ||
                pMandatePeriodId < 0L)
                return false;
            bool projected = ReignMandateProjectionPersistence.TryProject(
                DB, TABLE, pKingdom.id, pKing.data.id, pMandatePeriodId,
                (int)KingdomTitle.Emperor, pKingdom.name,
                out string error);
            if (!projected)
            {
                ModClass.LogWarning("Project mandate reign context failed: " +
                                    error);
            }
            return projected;
        }

        public static bool TryRecoverCurrentProjection(Kingdom pKingdom,
            Actor pKing)
        {
            if (!Ready || pKingdom?.data == null || pKing?.data == null)
                return false;
            try
            {
                double startTime = -1d;
                using (var history = new SQLiteCommand(DB))
                {
                    history.CommandText =
                        "SELECT WORLD_TIME FROM " +
                        KingdomHistoryTableItem.GetTableName() +
                        " WHERE KINGDOM_ID=@kingdom AND EVENT_TYPE=@event" +
                        " AND TARGET_TYPE='actor' AND TARGET_ID=@actor" +
                        " ORDER BY WORLD_TIME DESC,EVENT_ID DESC LIMIT 1";
                    history.Parameters.AddWithValue("@kingdom", pKingdom.id);
                    history.Parameters.AddWithValue("@event",
                        KingdomEvent.RULE_CHANGE);
                    history.Parameters.AddWithValue("@actor", pKing.data.id);
                    object value = history.ExecuteScalar();
                    if (value != null && value != DBNull.Value)
                        startTime = Convert.ToDouble(value);
                }

                if (startTime < 0d)
                {
                    using var reign = new SQLiteCommand(DB);
                    reign.CommandText =
                        "SELECT START_TIME FROM " + TABLE +
                        " WHERE KINGDOM_ID=@kingdom AND KING_ACTOR_ID=@actor" +
                        " ORDER BY START_TIME DESC LIMIT 1";
                    reign.Parameters.AddWithValue("@kingdom", pKingdom.id);
                    reign.Parameters.AddWithValue("@actor", pKing.data.id);
                    object value = reign.ExecuteScalar();
                    if (value != null && value != DBNull.Value)
                        startTime = Convert.ToDouble(value);
                }

                if (startTime < 0d) return false;
                ProjectCurrentReignStart(pKingdom, pKing, startTime);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Reign projection recovery failed: " +
                                    error.Message);
                return false;
            }
        }

        private static bool TryReadUniqueOpenRuler(long pKingdomId,
            out long pRulerActorId)
        {
            pRulerActorId = -1L;
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT COUNT(*),COALESCE(MAX(KING_ACTOR_ID),-1) FROM " +
                    TABLE + " WHERE KINGDOM_ID=@kingdom AND END_TIME=-1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                int count = Convert.ToInt32(reader.GetValue(0));
                if (count == 0) return true;
                if (count != 1) return false;
                pRulerActorId = Convert.ToInt64(reader.GetValue(1));
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Read open reign identity failed: " +
                                    error.Message);
                return false;
            }
        }

        /// <summary>关闭该国当前 end_time=-1 的 reign,并写入结束快照。</summary>
        public static ReignInfo CloseOpenReign(Kingdom pKingdom, string pReason, Actor pKing = null)
        {
            if (!Ready || pKingdom?.data == null) return ReignInfo.Empty;
            ReignInfo open = ReadOpenReignInfo(pKingdom.id);
            if (!open.IsValid) return ReignInfo.Empty;

            int endPop = SafePopulation(pKingdom);
            int endCities = SafeCityCount(pKingdom);
            int endArmy = SafeArmyCount(pKingdom);
            double endTime = World.world.getCurWorldTime();
            var (wins, losses) = WarRecordWriter.GetWarRecord(pKingdom.id, open.StartTime, endTime);
            string deathCause = ReadDeathCause(pKing);
            int lostCapital = pReason == "kingdom_fell" ? 1 : 0;
            int highestTitle = Math.Max(open.HighestTitle,
                (int)KingdomTitleService.GetTitle(pKingdom));

            try
            {
                DB.UpdateValue(TABLE,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("REIGN_ID", open.ReignId) },
                    ColumnVal.Create("END_TIME", endTime),
                    ColumnVal.Create("END_REASON", pReason ?? ""),
                    ColumnVal.Create("END_POPULATION", endPop),
                    ColumnVal.Create("END_CITY_COUNT", endCities),
                    ColumnVal.Create("END_ARMY_COUNT", endArmy),
                    ColumnVal.Create("WAR_WINS", wins),
                    ColumnVal.Create("WAR_LOSSES", losses),
                    ColumnVal.Create("LOST_CAPITAL", lostCapital),
                    ColumnVal.Create("HIGHEST_TITLE", highestTitle),
                    ColumnVal.Create("DEATH_CAUSE", deathCause));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("ReignRecordWriter.CloseOpenReign rich: " + e.Message);
                return ReignInfo.Empty;
            }

            open.EndPopulation = endPop;
            open.EndCityCount = endCities;
            open.EndArmyCount = endArmy;
            open.WarWins = wins;
            open.WarLosses = losses;
            open.LostCapital = lostCapital;
            open.HighestTitle = highestTitle;
            open.DeathCause = deathCause;
            open.EndTime = endTime;
            pKingdom.data.set(LineageKeys.KINGDOM_REIGN_START, -1f);
            return open;
        }

        /// <summary>关闭该国当前 end_time=-1 的 reign。</summary>
        public static ReignInfo CloseOpenReign(long pKingdomId, string pReason)
        {
            if (!Ready) return ReignInfo.Empty;
            ReignInfo open = ReadOpenReignInfo(pKingdomId);
            if (!open.IsValid) return ReignInfo.Empty;
            try
            {
                DB.UpdateValue(TABLE,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("REIGN_ID", open.ReignId) },
                    ColumnVal.Create("END_TIME",   World.world.getCurWorldTime()),
                    ColumnVal.Create("END_REASON", pReason ?? ""));
                return open;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("ReignRecordWriter.CloseOpenReign: " + e.Message);
                return ReignInfo.Empty;
            }
        }

        /// <summary>读当前开着的 reign 行（end=-1），供谥号评定读取起始国力。</summary>
        public static (long reignId, int startPop, int startCities, double startTime)
            ReadOpenReign(long pKingdomId)
        {
            ReignInfo info = ReadOpenReignInfo(pKingdomId);
            if (!info.IsValid) return (-1, 0, 0, 0);
            return (info.ReignId, info.StartPopulation, info.StartCityCount, info.StartTime);
        }

        public static ReignInfo ReadOpenReignInfo(long pKingdomId)
        {
            if (!Ready) return ReignInfo.Empty;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT REIGN_ID, KINGDOM_ID, KING_ACTOR_ID, START_POPULATION, START_CITY_COUNT, START_TIME, " +
                    $"START_ARMY_COUNT, IS_FOUNDER, REIGN_INDEX, END_TIME, " +
                    $"IFNULL(SHI_ID, -1), IFNULL(DYNASTY_ID, -1), IFNULL(MANDATE_PERIOD_ID, -1), " +
                    $"IFNULL(HIGHEST_TITLE, 0), IFNULL(STATE_NAME_SNAPSHOT, '') " +
                    $"FROM {TABLE} WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return ReignInfo.Empty;
                return new ReignInfo
                {
                    ReignId = r.GetInt64(0),
                    KingdomId = r.GetInt64(1),
                    KingActorId = r.GetInt64(2),
                    StartPopulation = (int)r.GetInt64(3),
                    StartCityCount = (int)r.GetInt64(4),
                    StartTime = r.GetDouble(5),
                    StartArmyCount = SafeInt64(r, 6),
                    IsFounder = SafeInt64(r, 7),
                    ReignIndex = SafeInt64(r, 8),
                    EndTime = SafeDouble(r, 9),
                    ShiId = SafeLong(r, 10, -1),
                    DynastyId = SafeLong(r, 11, -1),
                    MandatePeriodId = SafeLong(r, 12, -1),
                    HighestTitle = SafeInt64(r, 13),
                    StateNameSnapshot = SafeString(r, 14)
                };
            }
            catch { return ReignInfo.Empty; }
        }

        public static ReignInfo ReadLatestUntitledClosedReignForActor(long pActorId)
        {
            if (!Ready || pActorId < 0) return ReignInfo.Empty;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText =
                    $"SELECT REIGN_ID, KINGDOM_ID, KING_ACTOR_ID, START_POPULATION, START_CITY_COUNT, START_TIME, " +
                    $"START_ARMY_COUNT, IS_FOUNDER, REIGN_INDEX, END_TIME, IFNULL(END_REASON, ''), " +
                    $"END_POPULATION, END_CITY_COUNT, END_ARMY_COUNT, WAR_WINS, WAR_LOSSES, LOST_CAPITAL, IFNULL(DEATH_CAUSE, ''), " +
                    $"IFNULL(SHI_ID, -1), IFNULL(DYNASTY_ID, -1), IFNULL(MANDATE_PERIOD_ID, -1), " +
                    $"IFNULL(HIGHEST_TITLE, 0), IFNULL(STATE_NAME_SNAPSHOT, '') " +
                    $"FROM {TABLE} reign WHERE reign.KING_ACTOR_ID=@aid AND reign.END_TIME>=0 " +
                    $"AND NOT EXISTS (SELECT 1 FROM {PosthumousTitleTableItem.GetTableName()} title " +
                    $"WHERE title.REIGN_ID=reign.REIGN_ID) " +
                    $"ORDER BY reign.END_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@aid", pActorId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return ReignInfo.Empty;
                return new ReignInfo
                {
                    ReignId = r.GetInt64(0),
                    KingdomId = r.GetInt64(1),
                    KingActorId = r.GetInt64(2),
                    StartPopulation = (int)r.GetInt64(3),
                    StartCityCount = (int)r.GetInt64(4),
                    StartTime = r.GetDouble(5),
                    StartArmyCount = SafeInt64(r, 6),
                    IsFounder = SafeInt64(r, 7),
                    ReignIndex = SafeInt64(r, 8),
                    EndTime = SafeDouble(r, 9),
                    EndReason = SafeString(r, 10),
                    EndPopulation = SafeInt64(r, 11),
                    EndCityCount = SafeInt64(r, 12),
                    EndArmyCount = SafeInt64(r, 13),
                    WarWins = SafeInt64(r, 14),
                    WarLosses = SafeInt64(r, 15),
                    LostCapital = SafeInt64(r, 16),
                    DeathCause = SafeString(r, 17),
                    ShiId = SafeLong(r, 18, -1),
                    DynastyId = SafeLong(r, 19, -1),
                    MandatePeriodId = SafeLong(r, 20, -1),
                    HighestTitle = SafeInt64(r, 21),
                    StateNameSnapshot = SafeString(r, 22)
                };
            }
            catch { return ReignInfo.Empty; }
        }

        // ── 内部辅助 ──

        public static long FindOpenReignId(long pKingdomId)
        {
            if (!Ready) return -1;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT REIGN_ID FROM {TABLE} " +
                                  $"WHERE KINGDOM_ID=@kid AND END_TIME=-1 ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? -1L : Convert.ToInt64(v);
            }
            catch { return -1; }
        }

        private static int CountReigns(long pKingdomId)
        {
            if (!Ready) return 0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = $"SELECT COUNT(*) FROM {TABLE} WHERE KINGDOM_ID=@kid";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                object v = cmd.ExecuteScalar();
                return (v == null || v == DBNull.Value) ? 0 : Convert.ToInt32(v);
            }
            catch { return 0; }
        }

        private static int SafePopulation(Kingdom k)
        {
            try { return k.getPopulationTotal(); } catch { return 0; }
        }

        private static int SafeCityCount(Kingdom k)
        {
            try { return k.cities?.Count ?? 0; } catch { return 0; }
        }

        private static int SafeArmyCount(Kingdom k)
        {
            try
            {
                if (k?.data == null) return 0;
                int cityContribution = Math.Max(0, k.cities?.Count ?? 0) * 5 + 1;
                return Math.Max(0, (k.power - cityContribution) / 2);
            }
            catch { return 0; }
        }

        private static string ReadDeathCause(Actor pActor)
        {
            if (pActor?.data == null) return "";
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string cause, "");
            return cause ?? "";
        }

        private static int SafeInt64(SQLiteDataReader pReader, int pIndex)
        {
            try { return pReader.IsDBNull(pIndex) ? 0 : (int)pReader.GetInt64(pIndex); }
            catch { return 0; }
        }

        private static long SafeLong(SQLiteDataReader pReader, int pIndex, long pDefault = 0)
        {
            try { return pReader.IsDBNull(pIndex) ? pDefault : Convert.ToInt64(pReader.GetValue(pIndex)); }
            catch { return pDefault; }
        }

        private static double SafeDouble(SQLiteDataReader pReader, int pIndex)
        {
            try { return pReader.IsDBNull(pIndex) ? -1.0 : Convert.ToDouble(pReader.GetValue(pIndex)); }
            catch { return -1.0; }
        }

        private static string SafeString(SQLiteDataReader pReader, int pIndex)
        {
            try { return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex)) ?? ""; }
            catch { return ""; }
        }
    }
}
