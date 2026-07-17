using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class PosthumousTitleService
    {
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        private struct PosthumousKingdomContext
        {
            public Kingdom LiveKingdom;
            public long KingdomId;
            public string KingdomName;
            public string KingdomColor;
            public KingdomTitle Title;
            public bool IsMandate;

            public bool IsValid => LiveKingdom?.data != null ||
                                   (KingdomId >= 0 && !string.IsNullOrEmpty(KingdomName));
        }

        public static void OnReignEnded(Kingdom pKingdom, Actor pKing, string pEndReason,
            ReignRecordWriter.ReignInfo pReign)
        {
            if (pKingdom?.data == null) return;
            OnReignEnded(BuildLiveContext(pKingdom), pKing, pEndReason, pReign);
        }

        private static void OnReignEnded(PosthumousKingdomContext pContext, Actor pKing,
            string pEndReason, ReignRecordWriter.ReignInfo pReign)
        {
            if (!pContext.IsValid || pKing?.data == null) return;
            var manager = LineageArchiveManager.Instance;
            if (manager?.OperatingDB == null || !manager.InitializeSuccessful) return;
            if (!pReign.IsValid && pReign.ReignId != -1) return;
            if (HasExistingTitle(pKing.data.id, pReign.ReignId)) return;

            RulerTitleFacts facts = RulerTitleFactService.BuildAtReignEnd(
                pContext.LiveKingdom, pKing, pReign, pEndReason);
            if (facts.ActorId < 0 || facts.ShiId < 0) return;
            if (string.IsNullOrEmpty(facts.StateName)) facts.StateName = pContext.KingdomName ?? "";
            if (string.IsNullOrEmpty(facts.KingdomColor))
                facts.KingdomColor = HistoryColors.Normalize(pContext.KingdomColor);
            facts.HighestTitle = Math.Max(facts.HighestTitle, (int)pContext.Title);
            facts.EndReason = pEndReason ?? facts.EndReason ?? "";
            bool mandateKingdom = facts.IsMandate || pContext.IsMandate ||
                                  HasFormerMandateSnapshot(pKing, pContext.KingdomId);
            facts.IsMandate = mandateKingdom;

            RulerTitleDerivedFacts derived = RulerTitleFactRules.Derive(facts);
            int cycleNo = DynastyTitleRegistryService.ReadLatestCycle(
                facts.ShiId, "posthumous");
            HashSet<string> used = DynastyTitleRegistryService.ReadUsed(
                facts.ShiId, "posthumous", cycleNo);
            PosthumousTitleDecision posthumous = PosthumousTitleRules.Select(
                facts, derived, used, mandateKingdom, cycleNo);

            bool useMandateDeposedTitle = FormerKingTraitRules.ShouldUseMandateDeposedTitle(
                mandateKingdom, pEndReason, IsActorAlive(pKing));
            if (useMandateDeposedTitle)
            {
                posthumous = new PosthumousTitleDecision(
                    "废", "posthumous_qualification_deposed", posthumous.GradeKey,
                    posthumous.DominantKey, posthumous.Reason,
                    posthumous.Civil, posthumous.Territory, posthumous.War,
                    posthumous.Order, posthumous.Ending, posthumous.Total,
                    used.Contains("废") ? cycleNo + 1 : cycleNo);
            }

            TempleTitleDecision temple = default;
            if (facts.HighestTitle >= (int)KingdomTitle.Emperor)
            {
                int templeCycle = DynastyTitleRegistryService.ReadLatestCycle(
                    facts.ShiId, "temple");
                HashSet<string> usedTemples = DynastyTitleRegistryService.ReadUsed(
                    facts.ShiId, "temple", templeCycle);
                string previousTemple = DynastyTitleRegistryService.ReadLatestValue(
                    facts.ShiId, "temple");
                temple = TempleTitleRules.Select(facts, derived, usedTemples,
                    templeCycle, previousTemple);
            }

            string titleKind = useMandateDeposedTitle
                ? "deposed"
                : pEndReason == "abdicated" ? "abdication" : "posthumous";
            RulerTitleDecision decision = RulerTitleDecision.ForReignEnd(
                facts, posthumous, temple, titleKind);
            if (useMandateDeposedTitle)
            {
                decision.DisplayTitle = FormerKingTraitRules.BuildMandateDeposedTitle(
                    facts.StateName);
                decision.TitleSuffix = "帝";
            }
            decision.Reason = BuildReason(facts, posthumous);
            HistoryText titleEvent = BuildTitleEventText(pKing, pEndReason,
                decision.DisplayTitle, facts.KingdomColor, decision.Reason, titleKind);
            double now = World.world?.getCurWorldTime() ?? LineageService.CurTime();
            decision.HistoryPlain = titleEvent.Plain;
            decision.HistoryRich = titleEvent.Rich;
            decision.YearPrefix = HistoryWriter.BuildYearPrefix(now, pContext.LiveKingdom);
            decision.YearPrefixRich = HistoryWriter.BuildYearPrefixRich(now, pContext.LiveKingdom);

            RulerTitleCommitResult result = RulerTitleCommitService.Commit(decision);
            if (!result.Success) return;
            RulerAppellationService.ProjectCommittedTitle(
                pContext.LiveKingdom, pKing, result);
            if (FormerKingTraitRules.ShouldSnapshotLivingRulerTitle(
                    pEndReason, IsActorAlive(pKing)))
            {
                FormerKingService.StoreSnapshot(pKing,
                    pContext.KingdomId,
                    pContext.KingdomName,
                    facts.KingdomColor,
                    result.DisplayTitle,
                    mandateKingdom);
            }
        }

        public static void OnFormerRulerDied(Actor pActor)
        {
            if (pActor?.data == null) return;
            ReignRecordWriter.ReignInfo reign =
                ReignRecordWriter.ReadLatestUntitledClosedReignForActor(pActor.data.id);
            bool hasCapturedSnapshot = TryReadCapturedRulerContext(
                pActor, -1L, out PosthumousKingdomContext capturedContext);
            if (!FormerRulerPosthumousRules.ShouldTryPosthumousOnDeath(
                    pActor.isKing(), reign.IsValid, hasCapturedSnapshot))
                return;

            if (reign.IsValid)
            {
                PosthumousKingdomContext context = ResolveFormerRulerContext(pActor, reign);
                if (!context.IsValid) return;
                string reason = string.IsNullOrEmpty(reign.EndReason) ? "replaced" : reign.EndReason;
                OnReignEnded(context, pActor, reason, reign);
                return;
            }

            if (!capturedContext.IsValid) return;
            OnReignEnded(capturedContext, pActor, "captured_slave",
                BuildSyntheticCapturedRulerReign(pActor, capturedContext));
        }

        public static Kingdom ResolveCapturedRulerLiveKingdom(Actor pActor)
        {
            if (pActor?.data == null) return null;
            pActor.data.get(LineageKeys.CAPTURED_RULER_KINGDOM_ID, out long kingdomId, -1L);
            if (kingdomId < 0) return null;
            Kingdom kingdom = World.world?.kingdoms?.get(kingdomId);
            return kingdom?.data != null ? kingdom : null;
        }

        public static string BuildTooltip(long pActorId)
        {
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pActorId < 0) return "";
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText =
                    $"SELECT FULL_TITLE, GRADE, DOMINANT_DIMENSION, REASON_TEXT, TOTAL_SCORE, " +
                    $"SCORE_CIVIL, SCORE_TERRITORY, SCORE_WAR, SCORE_ORDER, SCORE_ENDING " +
                    $"FROM {PosthumousTitleTableItem.GetTableName()} " +
                    "WHERE ACTOR_ID=@actor ORDER BY DECIDED_TIME DESC LIMIT 1";
                command.Parameters.AddWithValue("@actor", pActorId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return "";

                string title = SafeString(reader, 0);
                string grade = SafeString(reader, 1);
                string dimension = SafeString(reader, 2);
                string reason = SafeString(reader, 3);
                int total = SafeInt(reader, 4);
                int civil = SafeInt(reader, 5);
                int territory = SafeInt(reader, 6);
                int war = SafeInt(reader, 7);
                int order = SafeInt(reader, 8);
                int ending = SafeInt(reader, 9);

                return T("aw_hist_posthumous_title_label") + title +
                       "\n" + T("aw_hist_posthumous_grade_label") + GradeLabel(grade) +
                       "\n" + T("aw_hist_posthumous_dimension_label") + DimensionLabel(dimension) +
                       "\n" + T("aw_hist_posthumous_total_label") + FormatSigned(total) +
                       "\n" + T("aw_hist_posthumous_civil_label") + FormatSigned(civil) +
                       " / " + T("aw_hist_posthumous_territory_label") + FormatSigned(territory) +
                       " / " + T("aw_hist_posthumous_war_label") + FormatSigned(war) +
                       " / " + T("aw_hist_posthumous_order_label") + FormatSigned(order) +
                       " / " + T("aw_hist_posthumous_ending_label") + FormatSigned(ending) +
                       (string.IsNullOrEmpty(reason) ? "" : "\n" + reason);
            }
            catch { return ""; }
        }

        private static PosthumousKingdomContext BuildLiveContext(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return default;
            return new PosthumousKingdomContext
            {
                LiveKingdom = pKingdom,
                KingdomId = pKingdom.id,
                KingdomName = pKingdom.name ?? "",
                KingdomColor = HistoryColors.FromKingdom(pKingdom),
                Title = KingdomTitleService.GetTitle(pKingdom),
                IsMandate = MandateService.IsMandateKingdom(pKingdom)
            };
        }

        private static PosthumousKingdomContext ResolveFormerRulerContext(Actor pActor,
            ReignRecordWriter.ReignInfo pReign)
        {
            Kingdom kingdom = World.world?.kingdoms?.get(pReign.KingdomId);
            if (kingdom?.data != null) return BuildLiveContext(kingdom);
            if (TryReadCapturedRulerContext(pActor, pReign.KingdomId,
                    out PosthumousKingdomContext captured))
                return captured;
            if (TryReadArchivedKingdomContext(pReign.KingdomId,
                    out PosthumousKingdomContext archived))
                return archived;
            return default;
        }

        private static bool TryReadCapturedRulerContext(Actor pActor, long pExpectedKingdomId,
            out PosthumousKingdomContext pContext)
        {
            pContext = default;
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.CAPTURED_RULER_KINGDOM_ID, out long kingdomId, -1L);
            if (kingdomId < 0 || pExpectedKingdomId >= 0 && kingdomId != pExpectedKingdomId)
                return false;

            Kingdom live = World.world?.kingdoms?.get(kingdomId);
            if (live?.data != null)
            {
                pContext = BuildLiveContext(live);
                return true;
            }

            pActor.data.get(LineageKeys.CAPTURED_RULER_KINGDOM_NAME, out string name, "");
            pActor.data.get(LineageKeys.CAPTURED_RULER_KINGDOM_COLOR, out string color, "");
            pActor.data.get(LineageKeys.CAPTURED_RULER_TITLE,
                out int title, (int)KingdomTitle.King);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(color))
            {
                if (TryReadArchivedKingdomContext(kingdomId,
                        out PosthumousKingdomContext archived))
                {
                    if (string.IsNullOrEmpty(name)) name = archived.KingdomName;
                    if (string.IsNullOrEmpty(color)) color = archived.KingdomColor;
                }
            }

            pContext = new PosthumousKingdomContext
            {
                KingdomId = kingdomId,
                KingdomName = name ?? "",
                KingdomColor = HistoryColors.Normalize(color),
                Title = (KingdomTitle)title,
                IsMandate = false
            };
            return pContext.IsValid;
        }

        private static bool TryReadArchivedKingdomContext(long pKingdomId,
            out PosthumousKingdomContext pContext)
        {
            pContext = default;
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pKingdomId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText =
                    $"SELECT IFNULL(KINGDOM_NAME, ''), IFNULL(COLOR_TEXT, '') " +
                    $"FROM {KingdomArchiveTableItem.GetTableName()} " +
                    "WHERE KINGDOM_ID=@kingdom LIMIT 1";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pContext = new PosthumousKingdomContext
                {
                    KingdomId = pKingdomId,
                    KingdomName = SafeString(reader, 0),
                    KingdomColor = HistoryColors.Normalize(SafeString(reader, 1)),
                    Title = KingdomTitle.King,
                    IsMandate = false
                };
                return pContext.IsValid;
            }
            catch { return false; }
        }

        private static ReignRecordWriter.ReignInfo BuildSyntheticCapturedRulerReign(Actor pActor,
            PosthumousKingdomContext pContext)
        {
            string deathCause = "";
            long shiId = -1;
            try
            {
                pActor?.data?.get(LineageKeys.DEATH_CAUSE, out deathCause, "");
                pActor?.data?.get(LineageKeys.SHI_ID, out shiId, -1L);
            }
            catch
            {
                deathCause = "";
                shiId = -1;
            }

            double start = 0;
            try { start = pActor?.data?.created_time ?? 0; }
            catch { start = 0; }
            return new ReignRecordWriter.ReignInfo
            {
                ReignId = -1,
                KingdomId = pContext.KingdomId,
                KingActorId = pActor?.data?.id ?? -1L,
                ShiId = shiId,
                HighestTitle = (int)pContext.Title,
                StateNameSnapshot = pContext.KingdomName ?? "",
                StartTime = start,
                EndTime = World.world?.getCurWorldTime() ?? start,
                EndReason = "captured_slave",
                DeathCause = deathCause ?? "",
                ReignIndex = 0
            };
        }

        private static HistoryText BuildTitleEventText(Actor pKing, string pEndReason,
            string pFullTitle, string pTitleColor, string pReason, string pTitleKind)
        {
            string verb = EndVerb(pEndReason);
            string label = pTitleKind == "deposed"
                ? T("aw_hist_posthumous_title_deposed")
                : pEndReason == "abdicated"
                    ? T("aw_hist_posthumous_title_abdicated")
                    : T("aw_hist_posthumous_title_normal");
            HistoryText actor = HistoryText.Actor(pKing, pKing?.getName() ?? "");
            HistoryText title = HistoryText.Colored(pFullTitle, pTitleColor);
            string template = T("aw_hist_title_awarded");
            string suffix = "（" + verb + "；" + label.Trim('，', ' ', ':') +
                            "；" + pReason + "）";
            string plain = string.Format(template,
                pKing?.getName() ?? "", pFullTitle) + suffix;
            string rich = string.Format(template, actor.Rich, title.Rich) +
                          HistoryColors.EscapeRich(suffix);
            return new HistoryText(plain, rich, actor.TargetType, actor.TargetId);
        }

        private static string BuildReason(RulerTitleFacts pFacts,
            PosthumousTitleDecision pPosthumous)
        {
            string reason = T("aw_hist_posthumous_reason_civil") + FormatSigned(pPosthumous.Civil) +
                            T("aw_hist_posthumous_reason_territory") + FormatSigned(pPosthumous.Territory) +
                            T("aw_hist_posthumous_reason_war") + FormatSigned(pPosthumous.War) +
                            T("aw_hist_posthumous_reason_order") + FormatSigned(pPosthumous.Order) +
                            T("aw_hist_posthumous_reason_ending") + FormatSigned(pPosthumous.Ending) +
                            T("aw_hist_posthumous_reason_wins") + pFacts.WarWins +
                            T("aw_hist_posthumous_reason_losses") + pFacts.WarLosses +
                            T("aw_hist_posthumous_reason_city") + FormatSigned(pFacts.CityDelta) +
                            T("aw_hist_posthumous_reason_reign") + pFacts.ReignYears +
                            T("aw_hist_posthumous_reason_year");
            if (!string.IsNullOrEmpty(pFacts.DeathCause))
                reason += T("aw_hist_posthumous_reason_death_cause") + pFacts.DeathCause;
            return reason;
        }

        private static bool HasExistingTitle(long pActorId, long pReignId)
        {
            SQLiteConnection db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = pReignId >= 0
                    ? $"SELECT 1 FROM {PosthumousTitleTableItem.GetTableName()} " +
                      "WHERE ACTOR_ID=@actor OR REIGN_ID=@reign LIMIT 1"
                    : $"SELECT 1 FROM {PosthumousTitleTableItem.GetTableName()} " +
                      "WHERE ACTOR_ID=@actor LIMIT 1";
                command.Parameters.AddWithValue("@actor", pActorId);
                command.Parameters.AddWithValue("@reign", pReignId);
                return command.ExecuteScalar() != null;
            }
            catch { return false; }
        }

        private static bool IsActorAlive(Actor pActor)
        {
            try { return pActor?.data != null && !pActor.isRekt() && pActor.isAlive(); }
            catch { return false; }
        }

        private static bool HasFormerMandateSnapshot(Actor pActor, long pKingdomId)
        {
            if (pActor?.data == null || pKingdomId < 0) return false;
            try
            {
                pActor.data.get(LineageKeys.FORMER_KINGDOM_ID,
                    out long formerKingdomId, -1L);
                pActor.data.get(LineageKeys.FORMER_KING_MANDATE,
                    out bool formerMandate, false);
                return formerMandate && formerKingdomId == pKingdomId;
            }
            catch { return false; }
        }

        private static string EndVerb(string pEndReason)
        {
            if (pEndReason == "captured_slave")
                return T("aw_hist_posthumous_end_captured_slave");
            if (pEndReason == "captured_executed")
                return T("aw_hist_posthumous_end_captured_executed");
            return pEndReason switch
            {
                "abdicated" => T("aw_hist_posthumous_end_abdicated"),
                "kingdom_fell" => T("aw_hist_posthumous_end_kingdom_fell"),
                _ => T("aw_hist_posthumous_end_died")
            };
        }

        private static string GradeLabel(string pGrade)
        {
            return pGrade switch
            {
                "praise_high" => T("aw_hist_posthumous_grade_praise_high"),
                "praise" => T("aw_hist_posthumous_grade_praise"),
                "blame" => T("aw_hist_posthumous_grade_blame"),
                "blame_high" => T("aw_hist_posthumous_grade_blame_high"),
                _ => T("aw_hist_posthumous_grade_neutral")
            };
        }

        private static string DimensionLabel(string pDimension)
        {
            return pDimension switch
            {
                "civil" => T("aw_hist_posthumous_dimension_civil"),
                "territory" => T("aw_hist_posthumous_dimension_territory"),
                "war" => T("aw_hist_posthumous_dimension_war"),
                "order" => T("aw_hist_posthumous_dimension_order"),
                "ending" => T("aw_hist_posthumous_dimension_ending"),
                _ => T("aw_hist_posthumous_dimension_balanced")
            };
        }

        private static string FormatSigned(int pValue)
        {
            return pValue > 0 ? "+" + pValue : pValue.ToString();
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
