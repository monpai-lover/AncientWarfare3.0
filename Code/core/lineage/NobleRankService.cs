using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.content;
using AncientWarfare3.core.db;
using AncientWarfare3.core.schools;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct NobleTitleSnapshot
    {
        public readonly long GrantId;
        public readonly long KingdomId;
        public readonly int Rank;
        public readonly NobleTitleStyle Style;
        public readonly string TitleName;

        public NobleTitleSnapshot(long pGrantId, long pKingdomId, int pRank,
            NobleTitleStyle pStyle, string pTitleName = "")
        {
            GrantId = pGrantId;
            KingdomId = pKingdomId;
            Rank = pRank;
            Style = pStyle;
            TitleName = pTitleName ?? "";
        }

        public bool IsActive => GrantId >= 0 &&
                                (Rank > NobleRankRules.RankNone ||
                                 Style is NobleTitleStyle.Princess or
                                     NobleTitleStyle.SeniorPrincess or
                                     NobleTitleStyle.GrandPrincess);
    }

    internal static class NobleRankService
    {
        public const int MaximumGreatGrantCandidates = 96;
        private static readonly string[] HistoricalStateTitleNames =
            XiaPreQinKingdomNameRules.All();

        private readonly struct PendingRoyalGrant
        {
            public readonly Actor Actor;
            public readonly int Rank;
            public readonly NobleTitleStyle Style;
            public readonly long GrantId;
            public readonly string TitleName;

            public PendingRoyalGrant(Actor pActor, int pRank,
                NobleTitleStyle pStyle, long pGrantId, string pTitleName)
            {
                Actor = pActor;
                Rank = pRank;
                Style = pStyle;
                GrantId = pGrantId;
                TitleName = pTitleName ?? "";
            }
        }

        private sealed class PendingDeathSuccession
        {
            public Actor Holder;
            public NobleTitleSnapshot Held;
            public string HeldDisplayTitle;
            public Actor Successor;
            public Kingdom Context;
            public NobleTitleSnapshot SuccessorCurrent;
            public int SuccessorRank;
            public bool KeepsHigherTitle;
        }

        private static readonly NobleDeathSuccessionRetryQueue<
                PendingDeathSuccession> PendingDeathSuccessions =
            new NobleDeathSuccessionRetryQueue<PendingDeathSuccession>();

        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance
                                         .InitializeSuccessful;

        public static NobleTitleSnapshot ReadHot(Actor pActor)
        {
            if (pActor?.data == null) return default;
            pActor.data.get(LineageKeys.NOBLE_GRANT_ID, out long grantId, -1L);
            pActor.data.get(LineageKeys.NOBLE_RANK_KINGDOM_ID,
                out long kingdomId, -1L);
            pActor.data.get(LineageKeys.NOBLE_RANK, out int rank,
                NobleRankRules.RankNone);
            pActor.data.get(LineageKeys.NOBLE_TITLE_STYLE,
                out string styleId, "");
            pActor.data.get(LineageKeys.NOBLE_TITLE_NAME,
                out string titleName, "");
            return new NobleTitleSnapshot(grantId, kingdomId,
                NobleRankRules.ClampRank(rank), ParseStyle(styleId),
                titleName);
        }

        public static string GetDisplayTitle(Actor pActor)
        {
            NobleTitleSnapshot title = ReadHot(pActor);
            if (!title.IsActive) return "";
            string key = NobleRankRules.TitleKey(title.Rank, title.Style);
            string fallback = NobleRankRules.TitleFallback(title.Rank,
                title.Style);
            string rankOrStyle = string.IsNullOrEmpty(key)
                ? fallback
                : AW_L10n.Text(key, fallback);
            return NobleTitleNameRules.ComposeDisplayTitle(title.TitleName,
                rankOrStyle);
        }

        internal static IReadOnlyList<long> GetActiveTitleHolderIds(
            long pKingdomId, int pLimit)
        {
            if (!Ready || pKingdomId < 0 || pLimit <= 0)
                return Array.Empty<long>();
            var result = new List<long>(pLimit);
            try
            {
                using var command = new SQLiteCommand(DB);
                command.CommandText = "SELECT ACTOR_ID FROM " +
                                      EnfeoffmentTableItem.GetTableName() +
                                      " INDEXED BY idx_Enfeoffment_kingdom_active " +
                                      "WHERE KINGDOM_ID=@kingdom AND ACTIVE=1 " +
                                      "ORDER BY NOBLE_RANK DESC,ACTOR_ID LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@limit", pLimit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(reader.GetInt64(0));
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Active noble title read failed: " +
                                    error.Message);
            }
            return result;
        }

        public static bool EnsureFeudatoryPrinceTitle(Kingdom pKingdom,
            Actor pPrince, out string pTitleName)
        {
            pTitleName = "";
            if (pKingdom?.data == null || pPrince?.data == null) return false;
            NobleTitleSnapshot current = ReadHot(pPrince);
            if (current.IsActive && current.Style == NobleTitleStyle.Male &&
                current.Rank == NobleRankRules.RankPrince &&
                !string.IsNullOrWhiteSpace(current.TitleName))
            {
                pTitleName = current.TitleName.Trim();
                return true;
            }

            if (!TryGrant(pKingdom, pKingdom.king, pPrince,
                    NobleRankRules.RankPrince, NobleTitleStyle.Male,
                    "feudatory_establishment", -1L,
                    out NobleTitleSnapshot granted))
                return false;
            pTitleName = granted.TitleName.Trim();
            return pTitleName.Length > 0;
        }

        public static bool TryGrantAdultRoyalChildTitle(Kingdom pKingdom,
            Actor pGrantor, Actor pChild)
        {
            if (pChild?.data == null || pKingdom?.data == null) return false;
            NobleTitleSnapshot current = ReadHot(pChild);
            if (!NobleRankRules.ShouldGrantFormalRoyalTitle(
                    pChild.isAdult(), current.IsActive))
                return current.IsActive;
            return TryGrant(pKingdom, pGrantor ?? pKingdom.king, pChild,
                pChild.isSexMale()
                    ? NobleRankRules.RankPrince
                    : NobleRankRules.RankNone,
                pChild.isSexMale()
                    ? NobleTitleStyle.Male
                    : NobleTitleStyle.Princess,
                "royal_child_adulthood", -1L, out _);
        }

        public static bool TryInheritFeudatoryPrinceTitle(Actor pHolder,
            Actor pSuccessor, Kingdom pKingdom, string pFeudatoryName)
        {
            if (!Ready || pHolder?.data == null || pSuccessor?.data == null ||
                pKingdom?.data == null)
                return false;
            NobleTitleSnapshot held = ReadHot(pHolder);
            if (!held.IsActive || held.Style != NobleTitleStyle.Male)
                return false;
            NobleTitleSnapshot successorCurrent = ReadHot(pSuccessor);
            int inheritedRank = NobleRankRules.ResultingInheritedRank(
                successorCurrent.Rank, held.Rank);
            string titleName = held.TitleName?.Trim() ?? "";
            if (titleName.Length == 0)
            {
                titleName = (pFeudatoryName ?? "").Trim();
                if (titleName.EndsWith("藩", StringComparison.Ordinal))
                    titleName = titleName.Substring(0,
                        titleName.Length - 1).Trim();
            }
            if (titleName.Length == 0) return false;

            long grantId;
            try
            {
                int year = SafeYear();
                double now = LineageService.CurTime();
                using SQLiteTransaction transaction = DB.BeginTransaction();
                HashSet<string> usedTitleNames =
                    ReadActiveTitleNames(transaction);
                long predecessor = CloseActiveGrant(transaction,
                    pHolder.data.id, year, now, "feudatory_inherited");
                if (predecessor < 0) return false;
                usedTitleNames.Remove(held.TitleName);
                long successorPredecessor = CloseActiveGrant(transaction,
                    pSuccessor.data.id, year, now,
                    "replaced_by_feudatory_inheritance");
                if (successorPredecessor >= 0)
                    usedTitleNames.Remove(successorCurrent.TitleName);
                grantId = NextGrantId(transaction);
                InsertActiveGrant(transaction, grantId, pKingdom, pHolder,
                    pSuccessor, inheritedRank, NobleTitleStyle.Male,
                    titleName, "feudatory_inheritance", pHolder.data.id,
                    predecessor, year, now);
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Feudatory noble title succession failed: " +
                                    exception.Message);
                return false;
            }

            ClearProjection(pHolder);
            Project(pSuccessor, grantId, pKingdom.id, inheritedRank,
                NobleTitleStyle.Male, titleName);
            try { LineageService.ArchiveActor(pSuccessor, pAlive: true); }
            catch { }
            return true;
        }

        internal static string GetArchivedDisplayTitle(int pRank,
            string pStyleId, string pTitleName)
        {
            NobleTitleStyle style = ParseStyle(pStyleId);
            string key = NobleRankRules.TitleKey(pRank, style);
            string fallback = NobleRankRules.TitleFallback(pRank, style);
            string rankOrStyle = string.IsNullOrEmpty(key)
                ? fallback
                : AW_L10n.Text(key, fallback);
            return NobleTitleNameRules.ComposeDisplayTitle(
                pTitleName, rankOrStyle);
        }

        public static bool CanExecuteGreatRoyalGrant(Kingdom pKingdom)
        {
            if (!Ready || pKingdom?.data == null || pKingdom.isRekt() ||
                !pKingdom.hasKing() ||
                MandateService.GetCurrentMandateKingdom() != pKingdom)
                return false;
            Actor emperor = pKingdom.king;
            if (emperor?.data == null) return false;
            pKingdom.data.get(LineageKeys.NOBLE_GREAT_GRANT_RULER_ID,
                out long completedRulerId, -1L);
            if (completedRulerId == emperor.data.id) return false;
            long royalClanId = pKingdom.data.royal_clan_id;
            if (royalClanId < 0) return false;
            try
            {
                var royalClan = World.world?.clans?.get(royalClanId);
                return royalClan?.units != null &&
                       royalClan.units.Count > 1 &&
                       HasGrantableRoyalCandidateCached(pKingdom, emperor);
            }
            catch { return false; }
        }

        public static int ExecuteGreatRoyalGrant(Kingdom pKingdom)
        {
            if (!CanExecuteGreatRoyalGrant(pKingdom)) return -1;
            Actor emperor = pKingdom.king;
            List<(Actor Actor, int Rank, NobleTitleStyle Style,
                string ExistingTitleName)> planned =
                BuildRoyalGrantPlan(pKingdom, emperor);
            if (planned.Count == 0)
            {
                CacheGreatGrantAvailability(pKingdom, emperor, false);
                return 0;
            }

            var committed = new List<PendingRoyalGrant>(planned.Count);
            int year = SafeYear();
            double now = LineageService.CurTime();
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                long nextGrantId = NextGrantId(transaction);
                HashSet<string> usedTitleNames =
                    ReadActiveTitleNames(transaction);
                for (int i = 0; i < planned.Count; i++)
                {
                    (Actor actor, int rank, NobleTitleStyle style,
                        string existingTitleName) =
                        planned[i];
                    long predecessor = CloseActiveGrant(transaction,
                        actor.data.id, year, now, "royal_grant_upgrade");
                    if (predecessor >= 0)
                        usedTitleNames.Remove(existingTitleName ?? "");
                    long grantId = nextGrantId++;
                    string titleName = AllocateTitleName(actor, pKingdom,
                        rank, style, grantId, existingTitleName,
                        usedTitleNames);
                    if (string.IsNullOrEmpty(titleName))
                        throw new InvalidOperationException(
                            "no unused historical noble title name");
                    usedTitleNames.Add(titleName);
                    InsertActiveGrant(transaction, grantId, pKingdom,
                        emperor, actor, rank, style, titleName,
                        "great_royal_grant", -1L, predecessor, year, now);
                    committed.Add(new PendingRoyalGrant(actor, rank, style,
                        grantId, titleName));
                }
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Great royal grant failed: " +
                                    exception.Message);
                return -1;
            }

            for (int i = 0; i < committed.Count; i++)
            {
                PendingRoyalGrant grant = committed[i];
                Project(grant.Actor, grant.GrantId, pKingdom.id, grant.Rank,
                    grant.Style, grant.TitleName);
                try
                {
                    LineageService.OnActorPromoted(grant.Actor,
                        NobleTrigger.Figure);
                    LineageService.ArchiveActor(grant.Actor, pAlive: true);
                }
                catch { }
                try
                {
                    ChronicleEvents.OnNobleRankGranted(pKingdom, emperor,
                        grant.Actor, GetDisplayTitle(grant.Actor));
                }
                catch (Exception exception)
                {
                    ModClass.LogWarning("Noble rank history failed: " +
                                        exception.Message);
                }
            }
            pKingdom.data.set(LineageKeys.NOBLE_GREAT_GRANT_RULER_ID,
                emperor.data.id);
            if (committed.Count > 0)
            {
                try
                {
                    ChronicleEvents.OnGreatRoyalGrant(pKingdom, emperor,
                        committed.Count);
                }
                catch (Exception exception)
                {
                    ModClass.LogWarning("Great royal grant history failed: " +
                                        exception.Message);
                }
            }
            return committed.Count;
        }

        private static bool HasGrantableRoyalCandidateCached(
            Kingdom pKingdom, Actor pEmperor)
        {
            int year = SafeYear();
            pKingdom.data.get(
                LineageKeys.NOBLE_GREAT_GRANT_CHECK_RULER_ID,
                out long cachedRulerId, -1L);
            pKingdom.data.get(LineageKeys.NOBLE_GREAT_GRANT_CHECK_YEAR,
                out int cachedYear, int.MinValue);
            if (NobleRankRules.ShouldReuseGreatGrantAvailability(
                    year, cachedYear, pEmperor.data.id, cachedRulerId))
            {
                pKingdom.data.get(LineageKeys.NOBLE_GREAT_GRANT_AVAILABLE,
                    out bool cachedAvailable, false);
                return cachedAvailable;
            }

            bool available =
                BuildRoyalGrantPlan(pKingdom, pEmperor).Count > 0;
            CacheGreatGrantAvailability(pKingdom, pEmperor, available);
            return available;
        }

        private static void CacheGreatGrantAvailability(Kingdom pKingdom,
            Actor pEmperor, bool pAvailable)
        {
            if (pKingdom?.data == null || pEmperor?.data == null) return;
            pKingdom.data.set(LineageKeys.NOBLE_GREAT_GRANT_CHECK_RULER_ID,
                pEmperor.data.id);
            pKingdom.data.set(LineageKeys.NOBLE_GREAT_GRANT_CHECK_YEAR,
                SafeYear());
            pKingdom.data.set(LineageKeys.NOBLE_GREAT_GRANT_AVAILABLE,
                pAvailable);
        }

        private static List<(Actor Actor, int Rank,
            NobleTitleStyle Style, string ExistingTitleName)>
            BuildRoyalGrantPlan(Kingdom pKingdom, Actor pEmperor)
        {
            List<Actor> candidates = CollectRoyalCandidates(pKingdom,
                pEmperor);
            var fatherCache = new Dictionary<long, long>();
            Dictionary<long, int> emperorPath = BuildAgnaticPath(
                pEmperor.data.id, fatherCache);
            var planned = new List<(Actor Actor, int Rank,
                NobleTitleStyle Style, string ExistingTitleName)>(
                candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                Actor candidate = candidates[i];
                if (!candidate.isAdult()) continue;
                int rank = NobleRankRules.RankNone;
                NobleTitleStyle style = NobleTitleStyle.None;
                if (candidate.isSexMale())
                {
                    int distance = AgnaticKinDistanceWithinFive(
                        candidate.data.id, emperorPath, fatherCache);
                    rank = NobleRankRules.RankForRoyalKinDistance(distance);
                    if (rank > NobleRankRules.RankNone)
                        style = NobleTitleStyle.Male;
                }
                else
                {
                    FemaleRoyalRelation relation = FemaleRelationToEmperor(
                        candidate.data.id, pEmperor.data.id, fatherCache);
                    style = NobleRankRules.FemaleStyleForRelation(relation);
                }
                if (style == NobleTitleStyle.None) continue;
                NobleTitleSnapshot current = ReadHot(candidate);
                if (!Outranks(current, rank, style)) continue;
                planned.Add((candidate, rank, style, current.TitleName));
            }
            return planned;
        }

        private static List<Actor> CollectRoyalCandidates(Kingdom pKingdom,
            Actor pEmperor)
        {
            var result = new List<Actor>(MaximumGreatGrantCandidates);
            try
            {
                var royalClan = World.world?.clans?.get(
                    pKingdom.data.royal_clan_id);
                if (royalClan?.units == null) return result;
                foreach (Actor member in royalClan.units)
                {
                    if (result.Count >= MaximumGreatGrantCandidates) break;
                    if (member?.data == null || member == pEmperor ||
                        member.isRekt() || !member.isAlive() ||
                        member.kingdom != pKingdom)
                        continue;
                    result.Add(member);
                }
            }
            catch { }
            result.Sort((left, right) =>
                left.data.id.CompareTo(right.data.id));
            return result;
        }

        private static Dictionary<long, int> BuildAgnaticPath(long pActorId,
            Dictionary<long, long> pFatherCache)
        {
            var result = new Dictionary<long, int>();
            long current = pActorId;
            for (int depth = 0; depth <= 5 && current >= 0; depth++)
            {
                if (result.ContainsKey(current)) break;
                result.Add(current, depth);
                current = CachedFatherId(current, pFatherCache);
            }
            return result;
        }

        private static int AgnaticKinDistanceWithinFive(long pCandidateId,
            IReadOnlyDictionary<long, int> pEmperorPath,
            Dictionary<long, long> pFatherCache)
        {
            int best = int.MaxValue;
            long current = pCandidateId;
            var visited = new HashSet<long>();
            for (int depth = 0; depth <= 5 && current >= 0; depth++)
            {
                if (!visited.Add(current)) break;
                if (pEmperorPath.TryGetValue(current, out int emperorDepth))
                {
                    int distance = depth + emperorDepth;
                    if (distance > 0 && distance <= 5)
                        best = Math.Min(best, distance);
                }
                current = CachedFatherId(current, pFatherCache);
            }
            return best == int.MaxValue ? -1 : best;
        }

        private static FemaleRoyalRelation FemaleRelationToEmperor(
            long pCandidateId, long pEmperorId,
            Dictionary<long, long> pFatherCache)
        {
            long candidateFather = CachedFatherId(pCandidateId,
                pFatherCache);
            if (candidateFather == pEmperorId)
                return FemaleRoyalRelation.Daughter;
            long emperorFather = CachedFatherId(pEmperorId, pFatherCache);
            if (candidateFather >= 0 && candidateFather == emperorFather)
                return FemaleRoyalRelation.Sister;
            long emperorGrandfather = CachedFatherId(emperorFather,
                pFatherCache);
            return candidateFather >= 0 &&
                   candidateFather == emperorGrandfather
                ? FemaleRoyalRelation.PaternalAunt
                : FemaleRoyalRelation.None;
        }

        private static long CachedFatherId(long pActorId,
            Dictionary<long, long> pCache)
        {
            if (pActorId < 0) return -1L;
            if (pCache.TryGetValue(pActorId, out long cached)) return cached;
            long fatherId;
            try { fatherId = LineageQuery.GetFatherId(pActorId); }
            catch { fatherId = -1L; }
            pCache[pActorId] = fatherId;
            return fatherId;
        }

        public static bool TryGrant(Kingdom pKingdom, Actor pGrantor,
            Actor pRecipient, int pRank, NobleTitleStyle pStyle,
            string pReason, long pInheritedFromActorId,
            out NobleTitleSnapshot pGranted)
        {
            pGranted = default;
            if (!Ready || pKingdom?.data == null || pRecipient?.data == null ||
                pRecipient.isRekt() || !pRecipient.isAlive())
                return false;
            int rank = NobleRankRules.ClampRank(pRank);
            if (!ValidTitleForActor(pRecipient, rank, pStyle)) return false;

            NobleTitleSnapshot current = ReadHot(pRecipient);
            if (!Outranks(current, rank, pStyle)) return false;

            int year = SafeYear();
            double now = LineageService.CurTime();
            long grantId;
            long predecessorGrantId;
            string titleName;
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                HashSet<string> usedTitleNames =
                    ReadActiveTitleNames(transaction);
                predecessorGrantId = CloseActiveGrant(transaction,
                    pRecipient.data.id, year, now, "replaced");
                if (predecessorGrantId >= 0)
                    usedTitleNames.Remove(current.TitleName);
                grantId = NextGrantId(transaction);
                titleName = AllocateTitleName(pRecipient, pKingdom, rank,
                    pStyle, grantId, current.TitleName, usedTitleNames);
                if (string.IsNullOrEmpty(titleName))
                    throw new InvalidOperationException(
                        "no unused historical noble title name");
                InsertActiveGrant(transaction, grantId, pKingdom, pGrantor,
                    pRecipient, rank, pStyle, titleName, pReason,
                    pInheritedFromActorId, predecessorGrantId, year, now);
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Noble rank grant failed: " +
                                    exception.Message);
                return false;
            }

            Project(pRecipient, grantId, pKingdom.id, rank, pStyle,
                titleName);
            try
            {
                LineageService.OnActorPromoted(pRecipient,
                    NobleTrigger.Figure);
                LineageService.ArchiveActor(pRecipient, pAlive: true);
            }
            catch { }
            pGranted = new NobleTitleSnapshot(grantId, pKingdom.id, rank,
                pStyle, titleName);
            try
            {
                ChronicleEvents.OnNobleRankGranted(pKingdom, pGrantor,
                    pRecipient, GetDisplayTitle(pRecipient));
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Noble rank history failed: " +
                                    exception.Message);
            }
            return true;
        }

        public static bool TryRevoke(Actor pActor, string pReason,
            out NobleTitleSnapshot pRevoked)
        {
            pRevoked = default;
            if (!Ready || pActor?.data == null) return false;
            NobleTitleSnapshot current = ReadHot(pActor);
            if (!current.IsActive) return false;
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                long grantId = CloseActiveGrant(transaction, pActor.data.id,
                    SafeYear(), LineageService.CurTime(),
                    pReason ?? "court_disposition");
                if (grantId != current.GrantId)
                    throw new InvalidOperationException(
                        "revoked noble grant does not match hot projection");
                transaction.Commit();
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Noble rank revocation failed: " +
                                    exception.Message);
                return false;
            }

            ClearProjection(pActor);
            try { LineageService.ArchiveActor(pActor, pAlive: true); }
            catch { }
            pRevoked = current;
            return true;
        }

        public static void OnActorDying(Actor pHolder)
        {
            if (!Ready || pHolder?.data == null) return;
            NobleTitleSnapshot held = ReadHot(pHolder);
            if (!held.IsActive) return;
            Actor successor = held.Style == NobleTitleStyle.Male
                ? FindEldestEligibleSon(pHolder)
                : null;
            Kingdom context = FindKingdom(held.KingdomId) ??
                              pHolder.kingdom ?? successor?.kingdom;
            bool canInherit = successor?.data != null && context?.data != null;
            NobleTitleSnapshot successorCurrent = canInherit
                ? ReadHot(successor)
                : default;
            int successorRank = canInherit
                ? NobleRankRules.ResultingInheritedRank(
                    successorCurrent.Rank, held.Rank)
                : NobleRankRules.RankNone;
            bool keepsHigherTitle = canInherit &&
                                    successorCurrent.IsActive &&
                                    successorCurrent.Style ==
                                    NobleTitleStyle.Male &&
                                    NobleTitleNameRules
                                        .ShouldKeepSuccessorTitle(
                                            successorCurrent.Rank,
                                            held.Rank);
            var pending = new PendingDeathSuccession
            {
                Holder = pHolder,
                Held = held,
                HeldDisplayTitle = GetDisplayTitle(pHolder),
                Successor = successor,
                Context = context,
                SuccessorCurrent = successorCurrent,
                SuccessorRank = successorRank,
                KeepsHigherTitle = keepsHigherTitle
            };
            PendingDeathSuccessions.TryUpsert(pHolder.data.id, pending);
            if (!PendingDeathSuccessions.TryProcess(pHolder.data.id,
                    TryProcessPendingDeathSuccession))
                ModClass.LogWarning("Noble rank succession queued for retry: actor=" +
                                    pHolder.data.id);
        }

        public static bool RetryPendingDeathSuccessionOne()
        {
            return PendingDeathSuccessions.TryProcessOne(
                TryProcessPendingDeathSuccession);
        }

        public static bool FlushPendingDeathSuccessionsForSave()
        {
            return PendingDeathSuccessions.TryFlushAll(
                TryProcessPendingDeathSuccession);
        }

        public static void ClearPendingDeathSuccessions()
        {
            PendingDeathSuccessions.Clear();
        }

        private static bool TryProcessPendingDeathSuccession(
            PendingDeathSuccession pPending)
        {
            if (!Ready || pPending?.Holder?.data == null) return false;
            Actor holder = pPending.Holder;
            Actor successor = pPending.Successor;
            Kingdom context = pPending.Context;
            bool canInherit = successor?.data != null && context?.data != null;
            bool shouldInherit = canInherit && !pPending.KeepsHigherTitle;
            long successorGrantId = -1L;
            long successorKingdomId = context?.id ?? pPending.Held.KingdomId;
            int successorRank = pPending.SuccessorRank;
            NobleTitleStyle successorStyle = NobleTitleStyle.Male;
            string successorTitleName = "";
            bool inherited = false;
            bool inheritedActive = false;

            try
            {
                int year = SafeYear();
                double now = LineageService.CurTime();
                using SQLiteTransaction transaction = DB.BeginTransaction();
                NobleDeathSuccessionCommittedGrant existing = null;
                if (shouldInherit)
                    NobleDeathSuccessionPersistence
                        .TryReadCommittedInheritance(DB,
                            EnfeoffmentTableItem.GetTableName(),
                            pPending.Held.GrantId, successor.data.id,
                            holder.data.id, context.id, successorRank,
                            NobleTitleStyle.Male, out existing, transaction);
                if (existing != null)
                {
                    successorGrantId = existing.GrantId;
                    successorKingdomId = existing.KingdomId;
                    successorRank = existing.Rank;
                    successorStyle = existing.Style;
                    successorTitleName = existing.TitleName;
                    inherited = true;
                    inheritedActive = existing.Active;
                    transaction.Commit();
                }
                else
                {
                    HashSet<string> usedTitleNames =
                        ReadActiveTitleNames(transaction);
                    string closeReason = !canInherit
                        ? "extinct"
                        : pPending.KeepsHigherTitle
                            ? "merged_into_higher_title"
                            : "inherited";
                    long inheritedGrantId = CloseActiveGrant(transaction,
                        holder.data.id, year, now, closeReason);
                    if (inheritedGrantId < 0)
                    {
                        if (shouldInherit)
                        {
                            transaction.Rollback();
                            return false;
                        }
                        transaction.Commit();
                    }
                    else
                    {
                        if (inheritedGrantId != pPending.Held.GrantId)
                            throw new InvalidOperationException(
                                "death succession closed an unexpected noble grant");
                        usedTitleNames.Remove(pPending.Held.TitleName);
                        if (shouldInherit)
                        {
                            if (pPending.SuccessorCurrent.IsActive)
                            {
                                long closedSuccessorGrant = CloseActiveGrant(
                                    transaction, successor.data.id, year, now,
                                    "upgraded_by_inheritance");
                                if (closedSuccessorGrant >= 0)
                                    usedTitleNames.Remove(
                                        pPending.SuccessorCurrent.TitleName);
                            }
                            successorGrantId = NextGrantId(transaction);
                            successorTitleName = AllocateTitleName(successor,
                                context, successorRank, NobleTitleStyle.Male,
                                successorGrantId, pPending.Held.TitleName,
                                usedTitleNames);
                            if (string.IsNullOrEmpty(successorTitleName))
                                throw new InvalidOperationException(
                                    "no unused inherited noble title name");
                            InsertActiveGrant(transaction, successorGrantId,
                                context, holder, successor, successorRank,
                                NobleTitleStyle.Male, successorTitleName,
                                "eldest_son_inheritance", holder.data.id,
                                inheritedGrantId, year, now);
                            inherited = true;
                            inheritedActive = true;
                        }
                        transaction.Commit();
                    }
                }
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Noble rank succession failed: " +
                                    exception.Message);
                return false;
            }

            ClearProjection(holder);
            if (inherited && inheritedActive && successorGrantId >= 0 &&
                successor?.data != null)
                Project(successor, successorGrantId, successorKingdomId,
                    successorRank, successorStyle, successorTitleName);
            try
            {
                if (successor?.data != null)
                    LineageService.ArchiveActor(successor, pAlive: true);
            }
            catch { }
            try
            {
                if (inherited && successor?.data != null)
                    ChronicleEvents.OnNobleRankInherited(context, holder,
                        successor, pPending.HeldDisplayTitle);
                else
                    ChronicleEvents.OnNobleRankExtinct(context, holder,
                        pPending.HeldDisplayTitle);
            }
            catch (Exception exception)
            {
                ModClass.LogWarning("Noble rank succession history failed: " +
                                    exception.Message);
            }
            return true;
        }

        private static Actor FindEldestEligibleSon(Actor pHolder)
        {
            var actors = new System.Collections.Generic.Dictionary<long, Actor>();
            var candidates = new System.Collections.Generic.List<NobleRankCandidate>();
            try
            {
                foreach (Actor child in pHolder.getChildren(false))
                {
                    if (child?.data == null || child == pHolder) continue;
                    bool eligible = child.isSexMale() && child.isAlive() &&
                                    !child.isRekt() &&
                                    !child.hasTrait("madness") &&
                                    !SlaveService.IsSlave(child);
                    actors[child.data.id] = child;
                    candidates.Add(new NobleRankCandidate(child.data.id,
                        eligible, child.data.created_time));
                }
            }
            catch { }
            long selectedId =
                NobleRankRules.SelectEldestEligibleId(candidates);
            return selectedId >= 0 && actors.TryGetValue(selectedId,
                out Actor selected)
                ? selected
                : null;
        }

        private static bool ValidTitleForActor(Actor pActor, int pRank,
            NobleTitleStyle pStyle)
        {
            if (pActor == null) return false;
            if (pStyle == NobleTitleStyle.Male)
                return pActor.isSexMale() && pRank > NobleRankRules.RankNone;
            if (pActor.isSexMale() || pRank != NobleRankRules.RankNone)
                return false;
            return pStyle is NobleTitleStyle.Princess or
                NobleTitleStyle.SeniorPrincess or
                NobleTitleStyle.GrandPrincess;
        }

        private static bool Outranks(NobleTitleSnapshot pCurrent, int pRank,
            NobleTitleStyle pStyle)
        {
            if (!pCurrent.IsActive) return true;
            if (pStyle == NobleTitleStyle.Male &&
                pCurrent.Style == NobleTitleStyle.Male)
                return pRank > pCurrent.Rank;
            if (pStyle != NobleTitleStyle.Male &&
                pCurrent.Style != NobleTitleStyle.Male)
                return (int)pStyle > (int)pCurrent.Style;
            return false;
        }

        private static long CloseActiveGrant(SQLiteTransaction pTransaction,
            long pActorId, int pYear, double pNow, string pReason)
        {
            long grantId = -1L;
            using (var read = new SQLiteCommand(DB)
                   { Transaction = pTransaction })
            {
                read.CommandText = "SELECT GRANT_ID FROM " +
                                   EnfeoffmentTableItem.GetTableName() +
                                   " WHERE ACTOR_ID=@actor AND ACTIVE=1 LIMIT 1";
                read.Parameters.AddWithValue("@actor", pActorId);
                object value = read.ExecuteScalar();
                if (value != null && value != DBNull.Value)
                    grantId = Convert.ToInt64(value);
            }
            if (grantId < 0) return -1L;

            using var close = new SQLiteCommand(DB)
                { Transaction = pTransaction };
            close.CommandText = "UPDATE " +
                                EnfeoffmentTableItem.GetTableName() +
                                " SET ACTIVE=0,END_YEAR=@year,END_TIME=@time," +
                                "END_REASON=@reason WHERE GRANT_ID=@id " +
                                "AND ACTOR_ID=@actor AND ACTIVE=1";
            close.Parameters.AddWithValue("@year", pYear);
            close.Parameters.AddWithValue("@time", pNow);
            close.Parameters.AddWithValue("@reason", pReason ?? "");
            close.Parameters.AddWithValue("@id", grantId);
            close.Parameters.AddWithValue("@actor", pActorId);
            if (close.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "active noble grant close failed");
            return grantId;
        }

        private static void InsertActiveGrant(SQLiteTransaction pTransaction,
            long pGrantId, Kingdom pKingdom, Actor pGrantor,
            Actor pRecipient, int pRank, NobleTitleStyle pStyle,
            string pTitleName, string pReason, long pInheritedFromActorId,
            long pPredecessorGrantId, int pYear, double pNow)
        {
            using var insert = new SQLiteCommand(DB)
                { Transaction = pTransaction };
            insert.CommandText = "INSERT INTO " +
                EnfeoffmentTableItem.GetTableName() +
                " (GRANT_ID,KINGDOM_ID,KINGDOM_NAME,GRANTOR_ACTOR_ID," +
                "GRANTOR_NAME,ACTOR_ID,ACTOR_NAME,NOBLE_RANK,TITLE_STYLE," +
                "TITLE_NAME,GRANT_REASON,INHERITED_FROM_ACTOR_ID," +
                "PREDECESSOR_GRANT_ID,GRANT_YEAR,START_TIME,END_YEAR," +
                "END_TIME,ACTIVE,END_REASON) " +
                "VALUES (@id,@kingdom,@kingdomName,@grantor,@grantorName," +
                "@actor,@actorName,@rank,@style,@titleName,@reason," +
                "@inherited,@previous,@year,@time,-1,-1,1,'')";
            insert.Parameters.AddWithValue("@id", pGrantId);
            insert.Parameters.AddWithValue("@kingdom", pKingdom.id);
            insert.Parameters.AddWithValue("@kingdomName",
                pKingdom.name ?? "");
            insert.Parameters.AddWithValue("@grantor",
                pGrantor?.data?.id ?? -1L);
            insert.Parameters.AddWithValue("@grantorName",
                pGrantor?.getName() ?? "");
            insert.Parameters.AddWithValue("@actor", pRecipient.data.id);
            insert.Parameters.AddWithValue("@actorName",
                pRecipient.getName() ?? "");
            insert.Parameters.AddWithValue("@rank", pRank);
            insert.Parameters.AddWithValue("@style", StyleId(pStyle));
            insert.Parameters.AddWithValue("@titleName", pTitleName ?? "");
            insert.Parameters.AddWithValue("@reason", pReason ?? "");
            insert.Parameters.AddWithValue("@inherited",
                pInheritedFromActorId);
            insert.Parameters.AddWithValue("@previous", pPredecessorGrantId);
            insert.Parameters.AddWithValue("@year", pYear);
            insert.Parameters.AddWithValue("@time", pNow);
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "active noble grant insert failed");
        }

        private static long NextGrantId(SQLiteTransaction pTransaction)
        {
            using var command = new SQLiteCommand(DB)
                { Transaction = pTransaction };
            command.CommandText = "SELECT IFNULL(MAX(GRANT_ID),0)+1 FROM " +
                                  EnfeoffmentTableItem.GetTableName();
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 1L
                : Convert.ToInt64(value);
        }

        private static void Project(Actor pActor, long pGrantId,
            long pKingdomId, int pRank, NobleTitleStyle pStyle,
            string pTitleName)
        {
            pActor.data.set(LineageKeys.NOBLE_GRANT_ID, pGrantId);
            pActor.data.set(LineageKeys.NOBLE_RANK_KINGDOM_ID, pKingdomId);
            pActor.data.set(LineageKeys.NOBLE_RANK, pRank);
            pActor.data.set(LineageKeys.NOBLE_TITLE_STYLE, StyleId(pStyle));
            pActor.data.set(LineageKeys.NOBLE_TITLE_NAME, pTitleName ?? "");
            HistoricalSchoolEliteEnrollmentService.MarkPriority(pActor,
                FindKingdom(pKingdomId),
                HistoricalSchoolElitePriority.TitledNoble);
            DynasticMaleLineContinuityService.OnTitleProjectionChanged(
                pActor);
        }

        private static void ClearProjection(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.NOBLE_GRANT_ID, -1L);
            pActor.data.set(LineageKeys.NOBLE_RANK_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.NOBLE_RANK,
                NobleRankRules.RankNone);
            pActor.data.set(LineageKeys.NOBLE_TITLE_STYLE, "");
            pActor.data.set(LineageKeys.NOBLE_TITLE_NAME, "");
            DynasticMaleLineContinuityService.OnTitleProjectionChanged(
                pActor);
        }

        internal static int StageRevoke(SQLiteTransaction pTransaction,
            IReadOnlyList<long> pActorIds, int pYear, double pNow,
            string pReason)
        {
            if (!Ready) throw new InvalidOperationException(
                "noble rank database is unavailable");
            return NobleRankRevocationPersistence.StageRevoke(DB,
                pTransaction, pActorIds, pYear, pNow, pReason);
        }

        internal static void ClearRevokedProjection(Actor pActor)
        {
            ClearProjection(pActor);
        }

        private static HashSet<string> ReadActiveTitleNames(
            SQLiteTransaction pTransaction)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            using var command = new SQLiteCommand(DB)
                { Transaction = pTransaction };
            command.CommandText = "SELECT TITLE_NAME FROM " +
                                  EnfeoffmentTableItem.GetTableName() +
                                  " WHERE ACTIVE=1 AND TITLE_NAME<>''";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0)) continue;
                string titleName = reader.GetString(0)?.Trim() ?? "";
                if (!string.IsNullOrEmpty(titleName))
                    result.Add(titleName);
            }
            return result;
        }

        private static string AllocateTitleName(Actor pRecipient,
            Kingdom pKingdom, int pRank, NobleTitleStyle pStyle,
            long pGrantId, string pExistingTitleName,
            HashSet<string> pUsedTitleNames)
        {
            string actualFiefName = ResolveActualFiefName(pRecipient);
            long seed = unchecked((pRecipient?.data?.id ?? 0L) * 397L ^
                                  (pKingdom?.id ?? 0L) * 17L ^ pGrantId);
            return NobleTitleNameRules.SelectUnused(actualFiefName,
                pExistingTitleName, pRank, pStyle, seed,
                pUsedTitleNames, HistoricalStateTitleNames);
        }

        private static string ResolveActualFiefName(Actor pRecipient)
        {
            if (pRecipient?.data == null) return "";
            try
            {
                if (FeudatoryService.TryGetByPrince(pRecipient.data.id,
                        out FeudatorySnapshot feudatory))
                {
                    if (!string.IsNullOrWhiteSpace(feudatory.SeatName))
                        return feudatory.SeatName;
                    if (!string.IsNullOrWhiteSpace(feudatory.FeudatoryName))
                        return feudatory.FeudatoryName;
                }
            }
            catch { }
            try
            {
                if (GeneralService.IsFiefHolder(pRecipient))
                    return FiefService.GetFiefCity(pRecipient)?.data?.name ??
                           "";
            }
            catch { }
            return "";
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static string StyleId(NobleTitleStyle pStyle)
        {
            return pStyle switch
            {
                NobleTitleStyle.Male => "male",
                NobleTitleStyle.Princess => "princess",
                NobleTitleStyle.SeniorPrincess => "senior_princess",
                NobleTitleStyle.GrandPrincess => "grand_princess",
                _ => ""
            };
        }

        private static NobleTitleStyle ParseStyle(string pStyle)
        {
            return pStyle switch
            {
                "male" => NobleTitleStyle.Male,
                "princess" => NobleTitleStyle.Princess,
                "senior_princess" => NobleTitleStyle.SeniorPrincess,
                "grand_princess" => NobleTitleStyle.GrandPrincess,
                _ => NobleTitleStyle.None
            };
        }

        private static int SafeYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return 0; }
        }
    }
}
