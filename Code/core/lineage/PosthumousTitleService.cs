using System;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     谥号系统:君主死亡/退位时按治绩、武功、衰败、结局分项评定谥号。
    ///     后缀按当前爵位取伯/侯/公/王/帝,避免所有非帝国都被谥为"王"。
    /// </summary>
    internal static class PosthumousTitleService
    {
        // 谥字池(System.Random,禁 UnityEngine.Random — 见 aw3-random-seed-pitfall)
        private static readonly System.Random Rng = new System.Random();

        private static readonly string[] GOOD_WAR = { "武", "烈", "桓", "宣" };
        private static readonly string[] GOOD_RULE = { "文", "成", "康", "昭", "穆", "景" };
        private static readonly string[] MID = { "安", "顺", "平", "和", "靖", "恭", "简", "怀" };
        private static readonly string[] BAD = { "幽", "厉", "灵", "炀", "戾", "暴", "荒" };
        private static readonly string[] FALL = { "哀", "闵" };

        /// <summary>在位结束时评谥。由 ChronicleEvents.OnKingDied / OnAbdicate 在 CloseOpenReign 之后调用。</summary>
        public static void OnReignEnded(Kingdom pKingdom, Actor pKing, string pEndReason,
            ReignRecordWriter.ReignInfo pReign)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || !LineageArchiveManager.Instance.InitializeSuccessful) return;

            // 读刚关闭的 reign 的起始国力基准（CloseOpenReign 已记 end_time）
            if (!pReign.IsValid) return;

            long reignId = pReign.ReignId;
            int startPop = pReign.StartPopulation;
            int startCities = pReign.StartCityCount;
            double startTime = pReign.StartTime;

            double now = World.world.getCurWorldTime();
            int curPop = SafePopulation(pKingdom);
            int curCities = SafeCityCount(pKingdom);

            TitleScore score = BuildScore(pKingdom, startPop, curPop, startCities, curCities, startTime, now, pEndReason);

            string eval = score.Total >= 1.4f ? "good" : score.Total <= -1.2f ? "bad" : "neutral";
            string titleChar = PickTitleChar(eval, score, pEndReason);
            string suffix = KingdomTitleService.GetTitleChar(KingdomTitleService.GetTitle(pKingdom));
            if (string.IsNullOrEmpty(suffix)) suffix = "君";

            // 普通王国名前缀用国名首字 + 谥字
            string kingdomPrefix = FirstChar(pKingdom.name);
            string fullTitle = kingdomPrefix + titleChar + suffix;
            string titleColor = HistoryColors.FromKingdom(pKingdom);

            // 写 PosthumousTitle 表
            long recordId = TableIdAllocator.Next(db, PosthumousTitleTableItem.GetTableName(), "RECORD_ID");
            try
            {
                db.Insert(PosthumousTitleTableItem.GetTableName(),
                    ColumnVal.Create("RECORD_ID",    recordId),
                    ColumnVal.Create("ACTOR_ID",     pKing.data.id),
                    ColumnVal.Create("KINGDOM_ID",   pKingdom.id),
                    ColumnVal.Create("REIGN_ID",     reignId),
                    ColumnVal.Create("KING_NAME",    pKing.getName()),
                    ColumnVal.Create("KING_COLOR",   HistoryColors.FromActor(pKing)),
                    ColumnVal.Create("TITLE_CHAR",   titleChar),
                    ColumnVal.Create("TITLE_SUFFIX", suffix),
                    ColumnVal.Create("FULL_TITLE",   fullTitle),
                    ColumnVal.Create("FULL_TITLE_COLOR", titleColor),
                    ColumnVal.Create("EVAL",         eval),
                    ColumnVal.Create("SCORE_DETAIL", score.Detail),
                    ColumnVal.Create("DECIDED_TIME", now));
            }
            catch (Exception e) { ModClass.LogWarning("PosthumousTitleService.Insert: " + e.Message); return; }

            // 回填到 KingdomReign
            ReignRecordWriter.SetPosthumous(reignId, fullTitle, titleColor);

            HistoryText posthumousText = HistoryText.Actor(pKing, pKing.getName()) +
                EndVerb(pEndReason) + "，谥为：" + HistoryText.Colored(fullTitle, titleColor) +
                HistoryText.PlainText("（" + score.Detail + "）");

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.POSTHUMOUS, posthumousText);

            // 人物传记追加谥号事件（贵族门槛）
            if (ChronicleGate.IsNobleActor(pKing))
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, fullTitle,
                    PersonEvent.POSTHUMOUS,
                    posthumousText,
                    ChronicleCategory.HONOR);
        }

        // ── 辅助 ──

        private struct TitleScore
        {
            public float Rule;
            public float War;
            public float Decline;
            public float Ending;
            public float Total;
            public int Wins;
            public int Losses;
            public int CityDelta;
            public string Detail;
        }

        private static TitleScore BuildScore(Kingdom pKingdom, int pStartPop, int pCurPop,
            int pStartCities, int pCurCities, double pStartTime, double pEndTime, string pEndReason)
        {
            var (wins, losses) = WarRecordWriter.GetWarRecord(pKingdom.id, pStartTime, pEndTime);
            int cityDelta = pCurCities - pStartCities;
            float popDelta = ScorePopulation(pStartPop, pCurPop);
            int years = Math.Max(1, Date.getYear(pEndTime) - Date.getYear(pStartTime) + 1);

            float rule = Clamp(popDelta * 1.2f, -1.8f, 1.8f)
                         + Clamp(cityDelta * 0.35f, -1.4f, 1.4f)
                         + Clamp(years / 80f, 0f, 0.8f);

            float war = Clamp((wins - losses) * 0.65f, -2.0f, 2.0f);

            float decline = 0f;
            if (cityDelta < 0) decline += cityDelta * 0.45f;
            if (popDelta <= -0.5f) decline -= 1.0f;
            else if (popDelta <= -0.25f) decline -= 0.45f;
            if (pEndReason == "kingdom_fell") decline -= 2.0f;

            float ending = pEndReason switch
            {
                "died" => 0.15f,
                "abdicated" => -0.15f,
                "kingdom_fell" => -1.0f,
                _ => 0f
            };

            float total = rule + war + decline + ending;
            string detail = $"治绩{rule:+0.0;-0.0;0.0} 武功{war:+0.0;-0.0;0.0} " +
                            $"衰败{decline:+0.0;-0.0;0.0} 结局{ending:+0.0;-0.0;0.0} " +
                            $"胜{wins}败{losses} 城{cityDelta:+0;-0;0}";

            return new TitleScore
            {
                Rule = rule,
                War = war,
                Decline = decline,
                Ending = ending,
                Total = total,
                Wins = wins,
                Losses = losses,
                CityDelta = cityDelta,
                Detail = detail
            };
        }

        private static string PickTitleChar(string pEval, TitleScore pScore, string pEndReason)
        {
            if (pEval == "good")
            {
                if (pScore.War >= pScore.Rule && pScore.Wins > pScore.Losses) return Pick(GOOD_WAR);
                return Pick(GOOD_RULE);
            }

            if (pEval == "bad")
            {
                if (pEndReason == "kingdom_fell" || pScore.CityDelta <= -2 || pScore.Decline <= -2.0f)
                    return Pick(FALL);
                return Pick(BAD);
            }

            if (pEndReason == "abdicated") return Pick(new[] { "顺", "恭", "安" });
            if (pScore.War > 0.8f && pScore.Wins > pScore.Losses) return "桓";
            if (pScore.Rule > 0.8f) return Pick(new[] { "平", "靖", "简" });
            return Pick(MID);
        }

        private static string Pick(string[] pPool)
        {
            return pPool[Rng.Next(pPool.Length)];
        }

        private static string EndVerb(string pEndReason)
        {
            return pEndReason switch
            {
                "abdicated" => "退位",
                "kingdom_fell" => "国亡",
                _ => "驾崩"
            };
        }

        private static float ScorePopulation(int pStart, int pCur)
        {
            if (pStart <= 0) return 0;
            return (pCur - pStart) / (float)pStart; // +0.5 = 增长50% → 褒谥
        }

        private static int SafePopulation(Kingdom k)
        { try { return k.getPopulationTotal(); } catch { return 0; } }

        private static int SafeCityCount(Kingdom k)
        { try { return k.cities?.Count ?? 0; } catch { return 0; } }

        private static string FirstChar(string s)
        { return string.IsNullOrEmpty(s) ? "" : s.Substring(0, 1); }

        private static float Clamp(float pValue, float pMin, float pMax)
        {
            if (pValue < pMin) return pMin;
            return pValue > pMax ? pMax : pValue;
        }

        private static long FindLastReignId(long pKingdomId, System.Data.SQLite.SQLiteConnection db)
        {
            try
            {
                using var cmd = new System.Data.SQLite.SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT REIGN_ID FROM {KingdomReignTableItem.GetTableName()} " +
                    $"WHERE KINGDOM_ID=@kid ORDER BY START_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                var v = cmd.ExecuteScalar();
                return (v == null || v == System.DBNull.Value) ? -1L : Convert.ToInt64(v);
            }
            catch { return -1; }
        }
    }
}
