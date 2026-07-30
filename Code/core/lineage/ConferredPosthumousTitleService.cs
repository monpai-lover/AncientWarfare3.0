using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.court;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    public sealed class ConferredPosthumousPreview
    {
        public ConferredPosthumousResult Result;
        public long KingdomId = -1;
        public long ActorId = -1;
        public string ActorName = "";
        public ConferredPosthumousRole Roles;
        public int CooldownRemaining;
        public string ExistingTitle = "";
        public string ProposedTitle = "";
        public string DisplayTitle = "";
        public string TitleMeaning = "";
        public string RelationshipLabel = "";
        public string HighestOfficeLabel = "";
        public string NobleTitleLabel = "";
        public string MajorDeeds = "";
        public string PreviewToken = "";
        public int CivilScore;
        public int TerritoryScore;
        public int WarScore;
        public int OrderScore;
        public int EndingScore;
        public int TotalScore;
        public RulerTitleFacts Facts;
        public ConferredPosthumousFactContext Context;
        internal RulerTitleDecision Decision;

        public bool CanCommit => Result == ConferredPosthumousResult.Success &&
                                 Decision != null &&
                                 !string.IsNullOrEmpty(PreviewToken);
    }

    public readonly struct ConferredPosthumousCommitResult
    {
        public readonly ConferredPosthumousResult Result;
        public readonly long RecordId;
        public readonly string DisplayTitle;

        public ConferredPosthumousCommitResult(
            ConferredPosthumousResult pResult, long pRecordId,
            string pDisplayTitle)
        {
            Result = pResult;
            RecordId = pRecordId;
            DisplayTitle = pDisplayTitle ?? "";
        }

        public bool Success => Result == ConferredPosthumousResult.Success;
    }

    public static class ConferredPosthumousTitleService
    {
        private static readonly Dictionary<long, int> LastConferredYear =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int> LastQueuedYear =
            new Dictionary<long, int>();
        private static bool CooldownCacheLoaded;

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static ConferredPosthumousPreview Prepare(
            long pKingdomId, long pActorId)
        {
            return PrepareInternal(pKingdomId, pActorId, null);
        }

        private static ConferredPosthumousPreview PrepareInternal(
            long pKingdomId, long pActorId,
            ConferredPosthumousCooldownRecord? pKnownCooldown)
        {
            var preview = new ConferredPosthumousPreview
            {
                KingdomId = pKingdomId,
                ActorId = pActorId
            };
            if (pKingdomId < 0)
            {
                preview.Result = ConferredPosthumousResult.MissingContext;
                return preview;
            }
            Kingdom kingdom = FindKingdom(pKingdomId);
            if (!Ready || kingdom?.data == null || kingdom.isRekt() ||
                !kingdom.hasKing())
            {
                preview.Result = ConferredPosthumousResult.InvalidKingdom;
                return preview;
            }

            try
            {
                var query = new ConferredPosthumousTitleQuery(DB);
                long royalLineageId = RoyalLineageId(kingdom);
                if (!query.TryReadTarget(pKingdomId, royalLineageId,
                        pActorId, out ConferredPosthumousTargetRecord target))
                {
                    preview.Result = ConferredPosthumousResult.MissingArchive;
                    return preview;
                }

                preview.ActorName = target.ActorName;
                preview.Roles = target.Roles;
                bool titled = query.TryReadFormalTitle(pActorId,
                    out ConferredPosthumousExistingTitle existing);
                preview.ExistingTitle = existing?.DisplayTitle ?? "";
                ConferredPosthumousCooldownRecord cooldown =
                    pKnownCooldown ?? query.ReadLastConferred(pKingdomId);
                int currentYear = SafeYear();
                int lastYear = cooldown.DecidedTime < 0d
                    ? -1
                    : Date.getYear(cooldown.DecidedTime);
                preview.CooldownRemaining =
                    ConferredPosthumousTitleRules.CooldownRemaining(
                        currentYear, lastYear);
                preview.Result =
                    ConferredPosthumousTitleRules.ValidatePreview(
                        !target.IsAlive, pHasKingdomContext: true,
                        ConferredPosthumousTitleRules.IsEligibleRole(
                            target.Roles), titled,
                        preview.CooldownRemaining);
                if (preview.Result != ConferredPosthumousResult.Success)
                    return preview;

                var candidate = new ConferredPosthumousCandidateRecord
                {
                    ActorId = target.ActorId,
                    ActorName = target.ActorName,
                    LineageId = target.LineageId,
                    ShiId = target.ShiId,
                    BirthTime = target.BirthTime,
                    Roles = target.Roles
                };
                var factService = new ConferredPosthumousTitleFactService(
                    DB, Date.getYear);
                if (!factService.TryBuild(pKingdomId, kingdom.name,
                        HistoryColors.FromKingdom(kingdom), candidate,
                        out RulerTitleFacts facts,
                        out ConferredPosthumousFactContext context))
                {
                    preview.Result =
                        ConferredPosthumousResult.PersistenceFailed;
                    return preview;
                }

                int cycle = facts.ShiId < 0
                    ? 0
                    : DynastyTitleRegistryService.ReadLatestCycle(
                        facts.ShiId, "posthumous");
                HashSet<string> used = facts.ShiId < 0
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : DynastyTitleRegistryService.ReadUsed(facts.ShiId,
                        "posthumous", cycle);
                PosthumousTitleDecision selected =
                    PosthumousTitleRules.Select(facts,
                        RulerTitleFactRules.Derive(facts), used,
                        pMandateDouble: false, cycle);
                if (string.IsNullOrWhiteSpace(selected.Name))
                {
                    preview.Result =
                        ConferredPosthumousResult.NoTitleAvailable;
                    return preview;
                }

                string nobleTitle = NobleRankService.GetArchivedDisplayTitle(
                    context.NobleRank, context.NobleTitleStyle,
                    context.NobleTitleName);
                string displayTitle =
                    (target.Roles &
                     ConferredPosthumousRole.FormerRuler) != 0
                        ? RulerTitleDecision.ForPosthumous(facts, selected)
                            .DisplayTitle
                        : ConferredPosthumousTitleRules.ComposeDisplayTitle(
                            target.ActorName, nobleTitle, selected.Name);
                BuildEdict(kingdom, facts, context, selected, nobleTitle,
                    out string historyPlain, out string historyRich);
                double now = LineageService.CurTime();
                RulerTitleDecision decision =
                    RulerTitleDecision.ForConferred(facts, selected,
                        displayTitle, historyPlain, historyRich,
                        HistoryWriter.BuildYearPrefix(now, kingdom),
                        HistoryWriter.BuildYearPrefixRich(now, kingdom),
                        RoleKey(target.Roles), RoleLabel(target.Roles));

                preview.ProposedTitle = selected.Name;
                preview.DisplayTitle = displayTitle;
                preview.TitleMeaning = BuildMeaning(selected.Name);
                preview.RelationshipLabel = RoleLabel(target.Roles);
                preview.HighestOfficeLabel = OfficeLabel(context);
                preview.NobleTitleLabel = nobleTitle;
                preview.MajorDeeds = BuildMajorDeeds(context, selected);
                preview.CivilScore = selected.Civil;
                preview.TerritoryScore = selected.Territory;
                preview.WarScore = selected.War;
                preview.OrderScore = selected.Order;
                preview.EndingScore = selected.Ending;
                preview.TotalScore = selected.Total;
                preview.Facts = facts;
                preview.Context = context;
                preview.Decision = decision;
                preview.PreviewToken =
                    ConferredPosthumousTitleRules.BuildPreviewToken(
                        pKingdomId, pActorId, selected.Name,
                        currentYear, cooldown.RecordId);
                return preview;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Conferred title preview failed: " +
                                    error.Message);
                preview.Result = ConferredPosthumousResult.PersistenceFailed;
                return preview;
            }
        }

        public static ConferredPosthumousCommitResult TryCommit(
            long pKingdomId, long pActorId, string pPreviewToken,
            ConferredPosthumousSource pSource)
        {
            ConferredPosthumousPreview current = Prepare(
                pKingdomId, pActorId);
            if (current.Result != ConferredPosthumousResult.Success)
                return new ConferredPosthumousCommitResult(
                    current.Result, -1L, current.ExistingTitle);
            if (!string.Equals(current.PreviewToken,
                    pPreviewToken ?? "", StringComparison.Ordinal))
                return new ConferredPosthumousCommitResult(
                    ConferredPosthumousResult.StalePreview,
                    -1L, current.DisplayTitle);
            return CommitPrepared(current, pSource);
        }

        private static ConferredPosthumousCommitResult CommitPrepared(
            ConferredPosthumousPreview pPreview,
            ConferredPosthumousSource pSource)
        {
            if (pPreview == null || !pPreview.CanCommit)
                return new ConferredPosthumousCommitResult(
                    ConferredPosthumousResult.StalePreview, -1L,
                    pPreview?.DisplayTitle ?? "");
            RulerTitleDecision decision = pPreview.Decision;
            RulerTitleCommitResult committed =
                RulerTitleCommitService.Commit(decision);
            if (!committed.Success)
                return new ConferredPosthumousCommitResult(
                    ConferredPosthumousResult.PersistenceFailed,
                    -1L, pPreview.DisplayTitle);

            LastConferredYear[pPreview.KingdomId] = SafeYear();
            return new ConferredPosthumousCommitResult(
                ConferredPosthumousResult.Success, committed.RecordId,
                committed.DisplayTitle);
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                if (!Ready || pKingdom?.data == null ||
                    pKingdom.isRekt() || !pKingdom.hasKing()) return;
                int year = SafeYear();
                EnsureCooldownCacheLoaded();
                int lastConferred = GetLastConferredYear(pKingdom.id);
                bool hasLastQueued = LastQueuedYear.TryGetValue(
                    pKingdom.id, out int lastQueued);
                if (!ConferredPosthumousTitleRules.ShouldQueueAi(
                        year, lastConferred,
                        hasLastQueued ? lastQueued : -1,
                        pKingdom.id))
                    return;
                LastQueuedYear[pKingdom.id] = year;
                long kingdomId = pKingdom.id;
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    DeferredRuntimeWorkRules.CoalescingKey(
                        "conferred_posthumous", kingdomId),
                    DeferredWorkClass.Persistent,
                    () => ProcessAiKingdom(kingdomId, year));
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.ConferredPosthumousIndex,
                    benchmark);
            }
        }

        public static void ClearRuntime()
        {
            LastConferredYear.Clear();
            LastQueuedYear.Clear();
            CooldownCacheLoaded = false;
        }

        private static void ProcessAiKingdom(long pKingdomId,
            int pQueuedYear)
        {
            long benchmark = RecentFeatureBenchmark.Begin();
            try
            {
                Kingdom kingdom = FindKingdom(pKingdomId);
                if (!Ready || kingdom?.data == null || kingdom.isRekt() ||
                    !kingdom.hasKing() || SafeYear() != pQueuedYear)
                    return;
                var query = new ConferredPosthumousTitleQuery(DB);
                ConferredPosthumousCooldownRecord cooldown =
                    query.ReadLastConferred(pKingdomId);
                List<ConferredPosthumousCandidateRecord> candidates =
                    query.ReadCandidates(pKingdomId,
                        RoyalLineageId(kingdom),
                        ConferredPosthumousTitleRules.MaximumCandidates);
                candidates.Sort((left, right) =>
                    ConferredPosthumousTitleRules.CompareCandidate(
                        Score(left), Score(right)));
                int count =
                    ConferredPosthumousTitleRules.FullEvaluationCount(
                        candidates.Count);
                ConferredPosthumousPreview best = null;
                int bestValue = int.MinValue;
                for (int i = 0; i < count; i++)
                {
                    ConferredPosthumousCandidateRecord candidate =
                        candidates[i];
                    ConferredPosthumousPreview preview = PrepareInternal(
                        pKingdomId, candidate.ActorId, cooldown);
                    if (!preview.CanCommit) continue;
                    int value =
                        ConferredPosthumousTitleRules.FinalCandidateValue(
                            Score(candidate), preview.TotalScore);
                    if (best != null && value <= bestValue) continue;
                    best = preview;
                    bestValue = value;
                }
                if (best == null) return;
                CommitPrepared(best, ConferredPosthumousSource.Ai);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Conferred title AI failed for kingdom " +
                                    pKingdomId + ": " + error.Message);
            }
            finally
            {
                RecentFeatureBenchmark.End(
                    RecentFeatureBenchmarkRules.ConferredPosthumousIndex,
                    benchmark);
            }
        }

        private static ConferredPosthumousCandidateScore Score(
            ConferredPosthumousCandidateRecord pCandidate)
        {
            return new ConferredPosthumousCandidateScore(
                pCandidate.ActorId,
                ConferredPosthumousTitleRules.RoleWeight(pCandidate.Roles),
                pCandidate.NobleRank, pCandidate.HighestOfficeRank,
                pCandidate.TenureYears, pCandidate.CivilMerit,
                pCandidate.GeneralMerit);
        }

        private static int GetLastConferredYear(long pKingdomId)
        {
            EnsureCooldownCacheLoaded();
            if (LastConferredYear.TryGetValue(pKingdomId, out int year))
                return year;
            return -1;
        }

        private static void EnsureCooldownCacheLoaded()
        {
            if (CooldownCacheLoaded || !Ready) return;
            IReadOnlyDictionary<long, double> times =
                new ConferredPosthumousTitleQuery(DB)
                    .ReadLastConferredTimes();
            foreach (KeyValuePair<long, double> pair in times)
                LastConferredYear[pair.Key] = pair.Value < 0d
                    ? -1
                    : Date.getYear(pair.Value);
            CooldownCacheLoaded = true;
        }

        private static long RoyalLineageId(Kingdom pKingdom)
        {
            return pKingdom?.king?.data == null
                ? -1L
                : LineageQuery.GetActorLineageId(pKingdom.king.data.id);
        }

        private static void BuildEdict(Kingdom pKingdom,
            RulerTitleFacts pFacts, ConferredPosthumousFactContext pContext,
            PosthumousTitleDecision pTitle, string pNobleTitle,
            out string pPlain, out string pRich)
        {
            string office = OfficeLabel(pContext);
            string identityPrefix = !string.IsNullOrEmpty(pNobleTitle)
                ? pNobleTitle
                : office;
            string identity = identityPrefix + pFacts.ActorName;
            string life = AW_L10n.Text(
                CeremonialHistoryRules.LifeSummaryKey(
                    pTitle.DominantKey, pTitle.GradeKey),
                "功业具载，行谊可考");
            string template = AW_L10n.Text(
                "aw_hist_conferred_posthumous_edict",
                "故{0}，{1}，赠谥{2}。");
            pPlain = string.Format(template, identity, life, pTitle.Name);
            HistoryText coloredIdentity = HistoryText.Colored(identity,
                pFacts.KingdomColor);
            HistoryText coloredTitle = HistoryText.Colored(pTitle.Name,
                pFacts.KingdomColor);
            pRich = string.Format(template, coloredIdentity.Rich,
                HistoryColors.EscapeRich(life), coloredTitle.Rich);
        }

        private static string BuildMeaning(string pPosthumousName)
        {
            IReadOnlyList<string> keys =
                CeremonialHistoryRules.MeaningKeys(pPosthumousName);
            var parts = new List<string>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                string value = AW_L10n.Text(keys[i], "");
                if (!string.IsNullOrEmpty(value) && value != keys[i])
                    parts.Add(value);
            }
            return parts.Count == 0
                ? pPosthumousName ?? ""
                : string.Join("；", parts.ToArray());
        }

        private static string BuildMajorDeeds(
            ConferredPosthumousFactContext pContext,
            PosthumousTitleDecision pTitle)
        {
            return AW_L10n.Text("aw_conferred_deeds_civil", "Civil merit") +
                   " " + pContext.CivilMerit + "  " +
                   AW_L10n.Text("aw_conferred_deeds_military", "Military merit") +
                   " " + pContext.GeneralMerit + "  " +
                   AW_L10n.Text("aw_conferred_deeds_tenure", "Years served") +
                   " " + pContext.ServiceYears + "  " +
                   AW_L10n.Text("aw_hist_posthumous_total_label", "Total") +
                   " " + pTitle.Total;
        }

        private static string OfficeLabel(
            ConferredPosthumousFactContext pContext)
        {
            if (string.IsNullOrEmpty(pContext?.HighestOfficeId)) return "";
            string fallback = pContext.HighestOfficeId.Replace('_', ' ');
            return AW_L10n.Text(
                CourtInstitutionRules.OfficeLocalizationKey(
                    "", pContext.HighestOfficeId), fallback);
        }

        private static string RoleKey(ConferredPosthumousRole pRoles)
        {
            if ((pRoles & ConferredPosthumousRole.FormerRuler) != 0)
                return "former_king";
            if ((pRoles & ConferredPosthumousRole.Royal) != 0)
                return "royal_clan";
            if ((pRoles & ConferredPosthumousRole.General) != 0)
                return "general";
            return "official";
        }

        private static string RoleLabel(ConferredPosthumousRole pRoles)
        {
            string role = RoleKey(pRoles);
            string key = role switch
            {
                "former_king" => "aw_conferred_role_former_king",
                "royal_clan" => "aw_conferred_role_royal_clan",
                "general" => "aw_conferred_role_general",
                _ => "aw_conferred_role_official"
            };
            return AW_L10n.Text(key, role.Replace('_', ' '));
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }
    }
}
