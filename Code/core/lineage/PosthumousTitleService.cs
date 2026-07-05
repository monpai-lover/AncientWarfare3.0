using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     普通谥号系统:非天命君主按民生、疆域、战功、秩序、结局分项评定。
    ///     太祖/高祖/世祖/烈祖等庙号只留给未来天命王朝系统。
    /// </summary>
    internal static class PosthumousTitleService
    {
        private static readonly System.Random Rng = new System.Random();

        /// <summary>在位结束时评谥。由 ChronicleEvents 在 CloseOpenReign 之后调用。</summary>
        public static void OnReignEnded(Kingdom pKingdom, Actor pKing, string pEndReason,
            ReignRecordWriter.ReignInfo pReign)
        {
            if (pKingdom?.data == null || pKing?.data == null) return;
            var manager = LineageArchiveManager.Instance;
            var db = manager?.OperatingDB;
            if (db == null || !manager.InitializeSuccessful) return;
            if (!pReign.IsValid) return;
            if (HasExistingTitle(pKing.data.id, pReign.ReignId)) return;

            double now = World.world.getCurWorldTime();
            PosthumousEvaluation eval = Evaluate(pKingdom, pEndReason, pReign);
            string titleChar = SelectTitleChar(eval, pEndReason, pKingdom.id);
            string suffix = KingdomTitleService.GetTitleChar(KingdomTitleService.GetTitle(pKingdom));
            if (string.IsNullOrEmpty(suffix)) suffix = "君";

            string fullTitle = FirstChar(pKingdom.name) + titleChar + suffix;
            string titleColor = HistoryColors.FromKingdom(pKingdom);
            string evalKey = EvalKey(eval.Grade);
            string titleKind = pEndReason == "abdicated" ? "abdication" : "posthumous";

            long recordId = TableIdAllocator.Next(db, PosthumousTitleTableItem.GetTableName(), "RECORD_ID");
            try
            {
                db.Insert(PosthumousTitleTableItem.GetTableName(),
                    ColumnVal.Create("RECORD_ID", recordId),
                    ColumnVal.Create("ACTOR_ID", pKing.data.id),
                    ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("REIGN_ID", pReign.ReignId),
                    ColumnVal.Create("KING_NAME", pKing.getName()),
                    ColumnVal.Create("KING_COLOR", HistoryColors.FromActor(pKing)),
                    ColumnVal.Create("TITLE_CHAR", titleChar),
                    ColumnVal.Create("TITLE_SUFFIX", suffix),
                    ColumnVal.Create("FULL_TITLE", fullTitle),
                    ColumnVal.Create("FULL_TITLE_COLOR", titleColor),
                    ColumnVal.Create("TITLE_KIND", titleKind),
                    ColumnVal.Create("EVAL", evalKey),
                    ColumnVal.Create("SCORE_DETAIL", eval.Reason),
                    ColumnVal.Create("GRADE", GradeKey(eval.Grade)),
                    ColumnVal.Create("DOMINANT_DIMENSION", DimensionKey(eval.Dominant)),
                    ColumnVal.Create("SCORE_CIVIL", eval.Civil),
                    ColumnVal.Create("SCORE_TERRITORY", eval.Territory),
                    ColumnVal.Create("SCORE_WAR", eval.War),
                    ColumnVal.Create("SCORE_ORDER", eval.Order),
                    ColumnVal.Create("SCORE_ENDING", eval.Ending),
                    ColumnVal.Create("TOTAL_SCORE", eval.Total),
                    ColumnVal.Create("REASON_TEXT", eval.Reason),
                    ColumnVal.Create("DECIDED_TIME", now));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("PosthumousTitleService.Insert: " + e.Message);
                return;
            }

            ReignRecordWriter.SetPosthumous(pReign.ReignId, fullTitle, titleColor);
            if (MandateService.IsMandateKingdom(pKingdom))
                MandateRulerTitleService.OnMandateReignEnded(pKingdom, pKing, pReign, pEndReason);

            HistoryText posthumousText = BuildTitleEventText(pKing, pEndReason, fullTitle, titleColor, eval.Reason);
            /*
                EndVerb(pEndReason) + "，谥为：" + HistoryText.Colored(fullTitle, titleColor) +
                HistoryText.PlainText("（" + eval.Reason + "）");

            */
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.POSTHUMOUS, posthumousText,
                HistoryTarget.Actor(pKing));

            if (ChronicleGate.IsNobleActor(pKing))
                HistoryWriter.RecordPerson(pKing.data.id, pKingdom, fullTitle,
                    PersonEvent.POSTHUMOUS,
                    posthumousText,
                    ChronicleCategory.HONOR,
                    HistoryTarget.Actor(pKing));
        }

        private static HistoryText BuildTitleEventText(Actor pKing, string pEndReason, string pFullTitle,
            string pTitleColor, string pReason)
        {
            string verb = EndVerb(pEndReason);
            string label = pEndReason == "abdicated" ? "，退号为：" : "，谥为：";
            return HistoryText.Actor(pKing, pKing.getName()) +
                   HistoryText.PlainText(verb + label) +
                   HistoryText.Colored(pFullTitle, pTitleColor) +
                   HistoryText.PlainText("（" + pReason + "）");
        }

        public static string BuildTooltip(long pActorId)
        {
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pActorId < 0) return "";
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT FULL_TITLE, GRADE, DOMINANT_DIMENSION, REASON_TEXT, TOTAL_SCORE, " +
                    $"SCORE_CIVIL, SCORE_TERRITORY, SCORE_WAR, SCORE_ORDER, SCORE_ENDING " +
                    $"FROM {PosthumousTitleTableItem.GetTableName()} " +
                    "WHERE ACTOR_ID=@actor ORDER BY DECIDED_TIME DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@actor", pActorId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                if (!r.Read()) return "";

                string title = SafeString(r, 0);
                string grade = SafeString(r, 1);
                string dim = SafeString(r, 2);
                string reason = SafeString(r, 3);
                int total = SafeInt(r, 4);
                int civil = SafeInt(r, 5);
                int territory = SafeInt(r, 6);
                int war = SafeInt(r, 7);
                int order = SafeInt(r, 8);
                int ending = SafeInt(r, 9);

                return "谥号：" + title +
                       "\n评等：" + GradeLabel(grade) +
                       "\n主因：" + DimensionLabel(dim) +
                       "\n总分：" + FormatSigned(total) +
                       "\n民生 " + FormatSigned(civil) +
                       " / 疆域 " + FormatSigned(territory) +
                       " / 战功 " + FormatSigned(war) +
                       " / 秩序 " + FormatSigned(order) +
                       " / 结局 " + FormatSigned(ending) +
                       (string.IsNullOrEmpty(reason) ? "" : "\n" + reason);
            }
            catch { return ""; }
        }

        private struct PosthumousEvaluation
        {
            public int Civil;
            public int Territory;
            public int War;
            public int Order;
            public int Ending;
            public int Total;
            public int Wins;
            public int Losses;
            public int CityDelta;
            public int ArmyDelta;
            public int Years;
            public bool Founder;
            public string DeathCause;
            public PosthumousGrade Grade;
            public PosthumousDimension Dominant;
            public string Reason;
        }

        private static PosthumousEvaluation Evaluate(Kingdom pKingdom, string pEndReason,
            ReignRecordWriter.ReignInfo pReign)
        {
            int endPop = pReign.EndPopulation > 0 ? pReign.EndPopulation : SafePopulation(pKingdom);
            int endCities = pReign.EndCityCount > 0 ? pReign.EndCityCount : SafeCityCount(pKingdom);
            int endArmy = pReign.EndArmyCount > 0 ? pReign.EndArmyCount : SafeArmyCount(pKingdom);
            int cityDelta = endCities - pReign.StartCityCount;
            int armyDelta = endArmy - pReign.StartArmyCount;
            int years = Math.Max(1, Date.getYear(World.world.getCurWorldTime()) - Date.getYear(pReign.StartTime) + 1);

            int civil = ScorePopulation(pReign.StartPopulation, endPop);
            int territory = ScoreTerritory(cityDelta, pEndReason);
            int war = ScoreWar(pReign.WarWins, pReign.WarLosses);
            int order = ScoreOrder(years, pEndReason, pReign.LostCapital != 0);
            int ending = ScoreEnding(pEndReason, pReign.DeathCause);
            int total = civil + territory + war + order + ending;

            PosthumousGrade grade = total >= 6 ? PosthumousGrade.PraiseHigh :
                total >= 2 ? PosthumousGrade.Praise :
                total >= -1 ? PosthumousGrade.Neutral :
                total >= -5 ? PosthumousGrade.Blame :
                PosthumousGrade.BlameHigh;

            PosthumousDimension dominant = DominantDimension(civil, territory, war, order, ending);
            string reason = "民生" + FormatSigned(civil) +
                            " 疆域" + FormatSigned(territory) +
                            " 战功" + FormatSigned(war) +
                            " 秩序" + FormatSigned(order) +
                            " 结局" + FormatSigned(ending) +
                            "；胜" + pReign.WarWins +
                            " 负" + pReign.WarLosses +
                            " 城" + FormatSigned(cityDelta) +
                            " 军" + FormatSigned(armyDelta) +
                            " 在位" + years + "年";
            if (!string.IsNullOrEmpty(pReign.DeathCause))
                reason += "；死因：" + pReign.DeathCause;

            return new PosthumousEvaluation
            {
                Civil = civil,
                Territory = territory,
                War = war,
                Order = order,
                Ending = ending,
                Total = total,
                Wins = pReign.WarWins,
                Losses = pReign.WarLosses,
                CityDelta = cityDelta,
                ArmyDelta = armyDelta,
                Years = years,
                Founder = pReign.IsFounder != 0,
                DeathCause = pReign.DeathCause ?? "",
                Grade = grade,
                Dominant = dominant,
                Reason = reason
            };
        }

        private static int ScorePopulation(int pStart, int pEnd)
        {
            if (pStart <= 0) return pEnd > 0 ? 1 : 0;
            float rate = (pEnd - pStart) / (float)pStart;
            if (rate >= 0.50f) return 3;
            if (rate >= 0.25f) return 2;
            if (rate >= 0.05f) return 1;
            if (rate >= -0.05f) return 0;
            if (rate >= -0.25f) return -1;
            if (rate >= -0.50f) return -2;
            return -3;
        }

        private static int ScoreTerritory(int pCityDelta, string pEndReason)
        {
            if (pEndReason == "kingdom_fell") return -3;
            if (pCityDelta >= 5) return 3;
            if (pCityDelta >= 2) return 2;
            if (pCityDelta >= 1) return 1;
            if (pCityDelta == 0) return 0;
            if (pCityDelta >= -2) return -1;
            if (pCityDelta >= -5) return -2;
            return -3;
        }

        private static int ScoreWar(int pWins, int pLosses)
        {
            int total = pWins + pLosses;
            if (total <= 1) return 0;
            float rate = pWins / (float)total;
            if (rate >= 0.80f && total >= 3) return 3;
            if (rate >= 0.60f) return 2;
            if (rate >= 0.50f) return 1;
            if (rate < 0.25f && total >= 3) return -3;
            if (rate < 0.40f) return -2;
            return -1;
        }

        private static int ScoreOrder(int pYears, string pEndReason, bool pLostCapital)
        {
            int score = 0;
            if (pYears >= 60) score += 2;
            else if (pYears >= 20) score += 1;
            else if (pYears < 3) score -= 1;

            if (pEndReason == "abdicated") score += 1;
            if (pEndReason == "replaced") score -= 1;
            if (pLostCapital) score -= 1;
            return ClampInt(score, -3, 3);
        }

        private static int ScoreEnding(string pEndReason, string pDeathCause)
        {
            if (pEndReason == "kingdom_fell") return -3;
            if (pEndReason == "abdicated") return 1;
            if (string.IsNullOrEmpty(pDeathCause)) return 0;
            if (pDeathCause.Contains("自然老死")) return 1;
            if (pDeathCause.Contains("战斗") || pDeathCause.Contains("击杀") || pDeathCause.Contains("身亡")) return 1;
            if (pDeathCause.Contains("饥饿") || pDeathCause.Contains("疾病") || pDeathCause.Contains("中毒")) return -1;
            if (pDeathCause.Contains("神力") || pDeathCause.Contains("神秘")) return -1;
            return 0;
        }

        private static PosthumousDimension DominantDimension(int pCivil, int pTerritory, int pWar, int pOrder, int pEnding)
        {
            int max = Math.Max(Math.Abs(pCivil),
                Math.Max(Math.Abs(pTerritory), Math.Max(Math.Abs(pWar), Math.Max(Math.Abs(pOrder), Math.Abs(pEnding)))));
            int near = 0;
            if (max - Math.Abs(pCivil) <= 1) near++;
            if (max - Math.Abs(pTerritory) <= 1) near++;
            if (max - Math.Abs(pWar) <= 1) near++;
            if (max - Math.Abs(pOrder) <= 1) near++;
            if (max - Math.Abs(pEnding) <= 1) near++;
            if (near >= 3) return PosthumousDimension.Balanced;
            if (Math.Abs(pEnding) == max) return PosthumousDimension.Ending;
            if (Math.Abs(pWar) == max) return PosthumousDimension.War;
            if (Math.Abs(pTerritory) == max) return PosthumousDimension.Territory;
            if (Math.Abs(pCivil) == max) return PosthumousDimension.Civil;
            return PosthumousDimension.Order;
        }

        private static string SelectTitleChar(PosthumousEvaluation pEval, string pEndReason, long pKingdomId)
        {
            if (pEndReason == "abdicated")
                return PickAbdicationTitle(pEval, pKingdomId);

            if (pEndReason == "kingdom_fell")
                return PickByPriority(pEval.Total <= -5
                    ? new[] { "厉", "幽", "荒", "废" }
                    : new[] { "哀", "闵", "悼" }, pKingdomId);

            if (pEval.Years < 3 && !pEval.Founder)
                return PickByPriority(new[] { "殇", "悼", "怀", "少" }, pKingdomId);

            if (pEndReason == "abdicated")
                return PickByPriority(new[] { "顺", "恭", "安", "让" }, pKingdomId);

            if (pEval.Founder && pEval.Total >= 0)
                return PickByPriority(new[] { "武", "文", "成", "桓", "烈" }, pKingdomId);

            return PickFromPool(pEval, pKingdomId);
        }

        private static string PickFromPool(PosthumousEvaluation pEval, long pKingdomId)
        {
            var candidates = new List<PosthumousTitleChar>();
            foreach (PosthumousTitleChar item in PosthumousTitleDefs.Pool)
            {
                if (!GradeAllows(pEval.Grade, item.MinGrade)) continue;
                if (item.Dimension == pEval.Dominant || item.Dimension == PosthumousDimension.Balanced)
                    candidates.Add(item);
            }

            if (candidates.Count == 0)
            {
                foreach (PosthumousTitleChar item in PosthumousTitleDefs.Pool)
                    if (GradeAllows(pEval.Grade, item.MinGrade)) candidates.Add(item);
            }

            if (candidates.Count == 0) return "平";

            HashSet<string> used = GetUsedTitleChars(pKingdomId);
            var unused = new List<PosthumousTitleChar>();
            foreach (PosthumousTitleChar item in candidates)
                if (!used.Contains(item.Char)) unused.Add(item);
            if (unused.Count > 0) candidates = unused;

            return candidates[Rng.Next(candidates.Count)].Char;
        }

        private static string PickAbdicationTitle(PosthumousEvaluation pEval, long pKingdomId)
        {
            if (pEval.Total >= 4)
                return PickByPriority(new[] { "让", "顺", "恭", "靖", "安" }, pKingdomId);
            if (pEval.Total >= 0)
                return PickByPriority(new[] { "顺", "恭", "安", "简", "静" }, pKingdomId);
            if (pEval.Total >= -4)
                return PickByPriority(new[] { "怀", "哀", "闵", "隐", "幽" }, pKingdomId);
            return PickByPriority(new[] { "废", "荒", "厉", "幽", "戾" }, pKingdomId);
        }

        private static bool GradeAllows(PosthumousGrade pActual, PosthumousGrade pRequired)
        {
            switch (pActual)
            {
                case PosthumousGrade.PraiseHigh:
                    return pRequired == PosthumousGrade.PraiseHigh ||
                           pRequired == PosthumousGrade.Praise ||
                           pRequired == PosthumousGrade.Neutral;
                case PosthumousGrade.Praise:
                    return pRequired == PosthumousGrade.Praise ||
                           pRequired == PosthumousGrade.Neutral;
                case PosthumousGrade.Neutral:
                    return pRequired == PosthumousGrade.Neutral;
                case PosthumousGrade.Blame:
                    return pRequired == PosthumousGrade.Blame ||
                           pRequired == PosthumousGrade.Neutral;
                case PosthumousGrade.BlameHigh:
                    return pRequired == PosthumousGrade.BlameHigh ||
                           pRequired == PosthumousGrade.Blame;
                default:
                    return pRequired == PosthumousGrade.Neutral;
            }
        }

        private static string PickByPriority(string[] pPool, long pKingdomId)
        {
            if (pPool == null || pPool.Length == 0) return "平";
            HashSet<string> used = GetUsedTitleChars(pKingdomId);
            foreach (string item in pPool)
                if (!used.Contains(item)) return item;
            return pPool[Rng.Next(pPool.Length)];
        }

        private static bool HasExistingTitle(long pActorId, long pReignId)
        {
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return false;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT 1 FROM {PosthumousTitleTableItem.GetTableName()} " +
                    "WHERE ACTOR_ID=@actor OR REIGN_ID=@reign LIMIT 1";
                cmd.Parameters.AddWithValue("@actor", pActorId);
                cmd.Parameters.AddWithValue("@reign", pReignId);
                return cmd.ExecuteScalar() != null;
            }
            catch { return false; }
        }

        private static HashSet<string> GetUsedTitleChars(long pKingdomId)
        {
            var result = new HashSet<string>();
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return result;
            try
            {
                using var cmd = new SQLiteCommand(db);
                cmd.CommandText =
                    $"SELECT TITLE_CHAR FROM {PosthumousTitleTableItem.GetTableName()} " +
                    "WHERE KINGDOM_ID=@kid AND IFNULL(TITLE_CHAR, '')<>''";
                cmd.Parameters.AddWithValue("@kid", pKingdomId);
                using var r = (SQLiteDataReader)cmd.ExecuteReader();
                while (r.Read())
                {
                    string value = r.IsDBNull(0) ? "" : r.GetString(0);
                    if (!string.IsNullOrEmpty(value)) result.Add(value);
                }
            }
            catch { }
            return result;
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

        private static string EvalKey(PosthumousGrade pGrade)
        {
            return pGrade switch
            {
                PosthumousGrade.PraiseHigh => "good",
                PosthumousGrade.Praise => "good",
                PosthumousGrade.Blame => "bad",
                PosthumousGrade.BlameHigh => "bad",
                _ => "neutral"
            };
        }

        private static string GradeKey(PosthumousGrade pGrade)
        {
            return pGrade switch
            {
                PosthumousGrade.PraiseHigh => "praise_high",
                PosthumousGrade.Praise => "praise",
                PosthumousGrade.Neutral => "neutral",
                PosthumousGrade.Blame => "blame",
                PosthumousGrade.BlameHigh => "blame_high",
                _ => "neutral"
            };
        }

        private static string DimensionKey(PosthumousDimension pDimension)
        {
            return pDimension switch
            {
                PosthumousDimension.Civil => "civil",
                PosthumousDimension.Territory => "territory",
                PosthumousDimension.War => "war",
                PosthumousDimension.Order => "order",
                PosthumousDimension.Ending => "ending",
                _ => "balanced"
            };
        }

        private static string GradeLabel(string pGrade)
        {
            return pGrade switch
            {
                "praise_high" => "上谥",
                "praise" => "美谥",
                "neutral" => "平谥",
                "blame" => "下谥",
                "blame_high" => "恶谥",
                _ => string.IsNullOrEmpty(pGrade) ? "未知" : pGrade
            };
        }

        private static string DimensionLabel(string pDimension)
        {
            return pDimension switch
            {
                "civil" => "民生",
                "territory" => "疆域",
                "war" => "战功",
                "order" => "秩序",
                "ending" => "结局",
                "balanced" => "综合",
                _ => string.IsNullOrEmpty(pDimension) ? "综合" : pDimension
            };
        }

        private static int SafePopulation(Kingdom k)
        {
            try { return k?.getPopulationTotal() ?? 0; } catch { return 0; }
        }

        private static int SafeCityCount(Kingdom k)
        {
            try { return k?.cities?.Count ?? 0; } catch { return 0; }
        }

        private static int SafeArmyCount(Kingdom k)
        {
            try
            {
                if (k?.data == null) return 0;
                int count = 0;
                foreach (Actor unit in k.getUnits())
                {
                    if (unit?.data == null || unit.isRekt()) continue;
                    if (unit.isWarrior()) count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private static string FirstChar(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Substring(0, 1);
        }

        private static int ClampInt(int pValue, int pMin, int pMax)
        {
            if (pValue < pMin) return pMin;
            return pValue > pMax ? pMax : pValue;
        }

        private static string FormatSigned(int pValue)
        {
            if (pValue > 0) return "+" + pValue;
            return pValue.ToString();
        }

        private static string SafeString(SQLiteDataReader pReader, int pIndex)
        {
            try { return pReader.IsDBNull(pIndex) ? "" : pReader.GetString(pIndex); }
            catch { return ""; }
        }

        private static int SafeInt(SQLiteDataReader pReader, int pIndex)
        {
            try { return pReader.IsDBNull(pIndex) ? 0 : Convert.ToInt32(pReader.GetValue(pIndex)); }
            catch { return 0; }
        }
    }
}
