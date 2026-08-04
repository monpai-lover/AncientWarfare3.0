using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using AncientWarfare3.core.db;
using AncientWarfare3.core.naming;

namespace AncientWarfare3.core.lineage
{
    internal enum LineageTreeReadMode
    {
        Family,
        BigTree,
        Locate
    }

    internal sealed class LineageTreeReadSpec
    {
        private LineageTreeReadSpec(LineageTreeReadMode mode,
            long actorId, long shiId, int maximumNodes, int maximumEdges,
            int maximumStringLength)
        {
            Mode = mode;
            ActorId = actorId;
            ShiId = shiId;
            MaximumNodes = Math.Max(1, Math.Min(512, maximumNodes));
            MaximumEdges = Math.Max(1, Math.Min(2048, maximumEdges));
            MaximumStringLength = Math.Max(8,
                Math.Min(512, maximumStringLength));
            MaximumTotalStringCharacters = Math.Max(MaximumStringLength,
                Math.Min(1_048_576, MaximumNodes * MaximumStringLength * 16));
        }

        public LineageTreeReadMode Mode { get; }
        public long ActorId { get; }
        public long ShiId { get; }
        public int MaximumNodes { get; }
        public int MaximumEdges { get; }
        public int MaximumStringLength { get; }
        public int MaximumTotalStringCharacters { get; }
        public string Key => ((int)Mode) + ":" + ActorId + ":" + ShiId +
                             ":" + MaximumNodes + ":" + MaximumEdges;

        public static LineageTreeReadSpec ForFamily(long actorId,
            int maximumNodes = 512, int maximumEdges = 2048,
            int maximumStringLength = 256)
        {
            return new LineageTreeReadSpec(LineageTreeReadMode.Family,
                actorId, -1L, maximumNodes, maximumEdges,
                maximumStringLength);
        }

        public static LineageTreeReadSpec ForBigTree(long shiId,
            int maximumNodes = 512, int maximumEdges = 2048,
            int maximumStringLength = 256)
        {
            return new LineageTreeReadSpec(LineageTreeReadMode.BigTree,
                -1L, shiId, maximumNodes, maximumEdges,
                maximumStringLength);
        }

        public static LineageTreeReadSpec ForLocate(long actorId, long shiId,
            int maximumNodes = 512, int maximumEdges = 2048,
            int maximumStringLength = 256)
        {
            return new LineageTreeReadSpec(LineageTreeReadMode.Locate,
                actorId, shiId, maximumNodes, maximumEdges,
                maximumStringLength);
        }
    }

    internal sealed class LineageTreeOverflow
    {
        public LineageTreeOverflow(bool nodeLimitReached,
            bool edgeLimitReached, bool stringLimitReached)
        {
            NodeLimitReached = nodeLimitReached;
            EdgeLimitReached = edgeLimitReached;
            StringLimitReached = stringLimitReached;
        }

        public bool NodeLimitReached { get; }
        public bool EdgeLimitReached { get; }
        public bool StringLimitReached { get; }
        public bool Any => NodeLimitReached || EdgeLimitReached ||
                           StringLimitReached;
    }

    internal sealed class LineageTreeNodeSnapshot
    {
        internal LineageTreeNodeSnapshot(ActorArchiveTableItem actor,
            LineageTreeShiSnapshot shi, LineageTreeShiSnapshot foundedBranch,
            string ritualAppellation, string retrospectiveRelation,
            bool hasHeldTitle, LineageTreeStringBudget strings)
        {
            Id = actor?.id ?? -1L;
            DisplayName = strings.Take(LineageDisplayNameRules.ProjectArchive(
                actor?.display_name ?? actor?.given_name,
                actor?.given_name, actor?.family_name, actor?.clan_name,
                actor?.status, actor?.sex == 0, actor?.name_integrated != 0,
                shi?.NamingProfile, shi?.WesternNamingTradition,
                shi?.OriginCityName ?? shi?.OriginCityChineseName,
                shi?.DisplayStem));
            AssetId = strings.Take(actor?.asset_id);
            ArchiveResolution = strings.Take(
                actor?.archive_resolution);
            FamilyName = strings.Take(actor?.family_name);
            ClanName = strings.Take(actor?.clan_name);
            Status = strings.Take(actor?.status);
            Sex = actor?.sex ?? -1;
            IsAlive = actor?.is_alive == 1;
            BirthTime = actor?.birth_time ?? 0d;
            DeathTime = actor?.death_time ?? -1d;
            KingdomId = actor?.kingdom_id ?? -1L;
            KingdomName = strings.Take(actor?.kingdom_name);
            KingdomColor = strings.Take(actor?.kingdom_color);
            CityName = strings.Take(actor?.city_name);
            SocialTitle = strings.Take(actor?.social_title);
            SocialTitleColor = strings.Take(actor?.social_title_color);
            OriginalClanId = actor?.original_clan_id ?? -1L;
            ClanColorText = strings.Take(actor?.clan_color_text);
            ClanColorId = actor?.clan_color_id ?? -1;
            ClanBannerIconId = actor?.clan_banner_icon_id ?? -1;
            ClanBannerBackgroundId = actor?.clan_banner_background_id ?? -1;
            ShiId = actor?.shi_id ?? -1L;
            ParentShiId = shi?.ParentShiId ?? -1L;
            ShiFounderActorId = shi?.FounderActorId ?? -1L;
            ShiDisplay = strings.Take(shi?.Display);
            ShiNamingProfile = strings.Take(shi?.NamingProfile);
            ShiWesternNamingTradition = strings.Take(
                shi?.WesternNamingTradition);
            ShiOriginCityChineseName = strings.Take(
                shi?.OriginCityChineseName);
            ShiDisplayStem = strings.Take(shi?.DisplayStem);
            NobleDistance = actor?.noble_distance ?? 99;
            Head = actor?.head ?? 0;
            Skin = actor?.skin ?? 0;
            SkinSet = actor?.skin_set ?? 0;
            SubspeciesId = actor?.subspecies_id ?? -1L;
            AgeOvergrowth = actor?.age_overgrowth ?? 1;
            PhenotypeIndex = actor?.phenotype_index ?? 0;
            PhenotypeShade = actor?.phenotype_shade ?? 0;
            FoundedBranchShiId = actor?.founded_branch_shi_id ?? -1L;
            DeathCause = strings.Take(actor?.death_cause);
            ParentShiDisplay = strings.Take(shi?.ParentDisplay);
            RootShiDisplay = strings.Take(shi?.RootDisplay);
            OriginCityName = strings.Take(shi?.OriginCityName);
            StateName = strings.Take(shi?.StateName);
            BranchDisplay = strings.Take(foundedBranch?.Display);
            BranchNamingProfile = strings.Take(
                foundedBranch?.NamingProfile);
            BranchWesternNamingTradition = strings.Take(
                foundedBranch?.WesternNamingTradition);
            BranchOriginCityChineseName = strings.Take(
                foundedBranch?.OriginCityChineseName);
            BranchDisplayStem = strings.Take(foundedBranch?.DisplayStem);
            RitualAppellation = strings.Take(ritualAppellation);
            RetrospectiveRelation = strings.Take(retrospectiveRelation);
            HasHeldTitle = hasHeldTitle;
        }

        public long Id { get; }
        public string DisplayName { get; }
        public string AssetId { get; }
        public string ArchiveResolution { get; }
        public string FamilyName { get; }
        public string ClanName { get; }
        public string Status { get; }
        public int Sex { get; }
        public bool IsAlive { get; }
        public double BirthTime { get; }
        public double DeathTime { get; }
        public long KingdomId { get; }
        public string KingdomName { get; }
        public string KingdomColor { get; }
        public string CityName { get; }
        public string SocialTitle { get; }
        public string SocialTitleColor { get; }
        public long OriginalClanId { get; }
        public string ClanColorText { get; }
        public int ClanColorId { get; }
        public int ClanBannerIconId { get; }
        public int ClanBannerBackgroundId { get; }
        public long ShiId { get; }
        public long ParentShiId { get; }
        public long ShiFounderActorId { get; }
        public string ShiDisplay { get; }
        public string ShiNamingProfile { get; }
        public string ShiWesternNamingTradition { get; }
        public string ShiOriginCityChineseName { get; }
        public string ShiDisplayStem { get; }
        public int NobleDistance { get; }
        public int Head { get; }
        public int Skin { get; }
        public int SkinSet { get; }
        public long SubspeciesId { get; }
        public int AgeOvergrowth { get; }
        public int PhenotypeIndex { get; }
        public int PhenotypeShade { get; }
        public long FoundedBranchShiId { get; }
        public string DeathCause { get; }
        public string ParentShiDisplay { get; }
        public string RootShiDisplay { get; }
        public string OriginCityName { get; }
        public string StateName { get; }
        public string BranchDisplay { get; }
        public string BranchNamingProfile { get; }
        public string BranchWesternNamingTradition { get; }
        public string BranchOriginCityChineseName { get; }
        public string BranchDisplayStem { get; }
        public string RitualAppellation { get; }
        public string RetrospectiveRelation { get; }
        public bool HasHeldTitle { get; }
    }

    internal sealed class LineageTreeShiSnapshot
    {
        public long ShiId;
        public long ParentShiId = -1L;
        public long FounderActorId = -1L;
        public string SourceType = string.Empty;
        public string Display = string.Empty;
        public string ParentDisplay = string.Empty;
        public string RootDisplay = string.Empty;
        public string OriginCityName = string.Empty;
        public string StateName = string.Empty;
        public string NamingProfile = "xia";
        public string WesternNamingTradition = string.Empty;
        public string OriginCityChineseName = string.Empty;
        public string DisplayStem = string.Empty;
    }

    internal sealed class LineageTreeStringBudget
    {
        private readonly int _maximumStringLength;
        private int _remainingCharacters;

        public LineageTreeStringBudget(int maximumStringLength,
            int maximumTotalCharacters)
        {
            _maximumStringLength = maximumStringLength;
            _remainingCharacters = maximumTotalCharacters;
        }

        public bool Overflowed { get; private set; }

        public string Take(string value)
        {
            string normalized = value ?? string.Empty;
            int allowed = Math.Min(_maximumStringLength,
                Math.Max(0, _remainingCharacters));
            if (normalized.Length > allowed)
            {
                normalized = normalized.Substring(0, allowed);
                Overflowed = true;
            }
            _remainingCharacters -= normalized.Length;
            return normalized;
        }
    }

    internal sealed class LineageBulkSnapshot
    {
        private readonly IReadOnlyDictionary<long, ActorArchiveTableItem>
            _actors;
        private readonly Dictionary<long, IReadOnlyList<long>> _parents;
        private readonly Dictionary<long, IReadOnlyList<long>> _children;
        private readonly HashSet<long> _nodes;
        private readonly IReadOnlyDictionary<long, LineageTreeNodeSnapshot>
            _treeNodes;
        private readonly IReadOnlyList<long> _locatePath;

        internal LineageBulkSnapshot(
            Dictionary<long, ActorArchiveTableItem> pActors,
            Dictionary<long, List<long>> pParents,
            Dictionary<long, List<long>> pChildren,
            IEnumerable<long> pNodes, int pCommandCount,
            Dictionary<long, LineageTreeNodeSnapshot> pTreeNodes = null,
            long pRootActorId = -1L, long pBackShiId = -1L,
            long pLocateActorId = -1L, IReadOnlyList<long> pLocatePath = null,
            LineageTreeOverflow pOverflow = null, int pEdgeCount = 0)
        {
            _actors = new ReadOnlyDictionary<long, ActorArchiveTableItem>(
                pActors == null
                    ? new Dictionary<long, ActorArchiveTableItem>()
                    : new Dictionary<long, ActorArchiveTableItem>(pActors));
            _parents = FreezeAdjacency(pParents);
            _children = FreezeAdjacency(pChildren);
            _nodes = pNodes == null
                ? new HashSet<long>()
                : new HashSet<long>(pNodes);
            _treeNodes = new ReadOnlyDictionary<long,
                LineageTreeNodeSnapshot>(pTreeNodes == null
                ? new Dictionary<long, LineageTreeNodeSnapshot>()
                : new Dictionary<long, LineageTreeNodeSnapshot>(pTreeNodes));
            _locatePath = Array.AsReadOnly((pLocatePath ??
                Array.Empty<long>()).ToArray());
            CommandCount = Math.Max(0, pCommandCount);
            RootActorId = pRootActorId;
            BackShiId = pBackShiId;
            LocateActorId = pLocateActorId;
            Overflow = pOverflow ?? new LineageTreeOverflow(false, false,
                false);
            EdgeCount = Math.Max(0, pEdgeCount);
        }

        public IReadOnlyDictionary<long, ActorArchiveTableItem> Actors =>
            _actors;
        public int CommandCount { get; }
        public int NodeCount => _nodes.Count;
        public int EdgeCount { get; }
        public long RootActorId { get; }
        public long BackShiId { get; }
        public long LocateActorId { get; }
        public IReadOnlyList<long> LocatePath => _locatePath;
        public LineageTreeOverflow Overflow { get; }

        public NamingProfileId BigTreeProfile
        {
            get
            {
                return _treeNodes.TryGetValue(RootActorId,
                           out LineageTreeNodeSnapshot root)
                    ? AWCultureNamingTraditionRules.ParseProfile(
                        root.ShiNamingProfile)
                    : NamingProfileId.None;
            }
        }

        public bool ContainsNode(long pActorId)
        {
            return _nodes.Contains(pActorId);
        }

        public bool TryGetActor(long pActorId,
            out ActorArchiveTableItem pActor)
        {
            return _actors.TryGetValue(pActorId, out pActor);
        }

        public bool TryGetNode(long pActorId,
            out LineageTreeNodeSnapshot pNode)
        {
            return _treeNodes.TryGetValue(pActorId, out pNode);
        }

        public long FatherId(long pActorId)
        {
            IReadOnlyList<long> parents = ParentIds(pActorId);
            for (int index = 0; index < parents.Count; index++)
            {
                long parentId = parents[index];
                if (_treeNodes.TryGetValue(parentId,
                        out LineageTreeNodeSnapshot node) && node.Sex == 0)
                    return parentId;
                if (_actors.TryGetValue(parentId,
                        out ActorArchiveTableItem actor) && actor.sex == 0)
                    return parentId;
            }
            return -1L;
        }

        public long MotherId(long pActorId)
        {
            IReadOnlyList<long> parents = ParentIds(pActorId);
            for (int index = 0; index < parents.Count; index++)
            {
                long parentId = parents[index];
                if (_treeNodes.TryGetValue(parentId,
                        out LineageTreeNodeSnapshot node) && node.Sex != 0)
                    return parentId;
                if (_actors.TryGetValue(parentId,
                        out ActorArchiveTableItem actor) && actor.sex != 0)
                    return parentId;
            }
            return -1L;
        }

        public bool HasHeldTitle(long pActorId)
        {
            return _treeNodes.TryGetValue(pActorId,
                       out LineageTreeNodeSnapshot node) &&
                   node.HasHeldTitle;
        }

        public IReadOnlyList<long> ParentIds(long pActorId)
        {
            return _parents.TryGetValue(pActorId,
                       out IReadOnlyList<long> values)
                ? values
                : Array.Empty<long>();
        }

        public IReadOnlyList<long> ChildIds(long pActorId)
        {
            return _children.TryGetValue(pActorId,
                       out IReadOnlyList<long> values)
                ? values
                : Array.Empty<long>();
        }

        public IReadOnlyList<long> Descendants(long pRootActorId,
            int pMaximumNodes)
        {
            int maximum = Math.Max(1, Math.Min(512, pMaximumNodes));
            var result = new List<long>(maximum);
            var seen = new HashSet<long>();
            var queue = new Queue<long>();
            if (pRootActorId < 0L) return result;
            queue.Enqueue(pRootActorId);
            seen.Add(pRootActorId);
            while (queue.Count > 0 && result.Count < maximum)
            {
                long current = queue.Dequeue();
                result.Add(current);
                if (!_children.TryGetValue(current,
                        out IReadOnlyList<long> children)) continue;
                foreach (long child in children)
                    if (seen.Add(child)) queue.Enqueue(child);
            }
            return result;
        }

        private static Dictionary<long, IReadOnlyList<long>> FreezeAdjacency(
            Dictionary<long, List<long>> pSource)
        {
            var result = new Dictionary<long, IReadOnlyList<long>>();
            if (pSource == null) return result;
            foreach (KeyValuePair<long, List<long>> pair in pSource)
            {
                long[] values = pair.Value?.ToArray() ?? Array.Empty<long>();
                result[pair.Key] = Array.AsReadOnly(values);
            }
            return result;
        }
    }

    internal static class LineageBulkSnapshotContext
    {
        [ThreadStatic]
        private static LineageBulkSnapshot _current;

        public static LineageBulkSnapshot Current => _current;

        public static IDisposable Push(LineageBulkSnapshot pSnapshot)
        {
            LineageBulkSnapshot previous = _current;
            _current = pSnapshot;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly LineageBulkSnapshot _previous;
            private bool _disposed;

            public Scope(LineageBulkSnapshot pPrevious)
            {
                _previous = pPrevious;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _current = _previous;
            }
        }
    }

    internal sealed class LineageBulkReadExecution
    {
        private readonly long _rootActorId;
        private readonly int _maximumNodes;

        public LineageBulkReadExecution(long pRootActorId,
            int pMaximumNodes = 512)
        {
            _rootActorId = pRootActorId;
            _maximumNodes = Math.Max(1, Math.Min(512, pMaximumNodes));
        }

        public object Execute(SQLiteConnection pConnection,
            CancellationToken pToken)
        {
            pToken.ThrowIfCancellationRequested();
            LineageBulkSnapshot result = LineageBulkQuery.Load(
                pConnection, _rootActorId, _maximumNodes);
            pToken.ThrowIfCancellationRequested();
            return result;
        }
    }

    internal sealed class LineageTreeReadExecution
    {
        private readonly LineageTreeReadSpec _spec;

        public LineageTreeReadExecution(LineageTreeReadSpec spec)
        {
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        }

        public object Execute(SQLiteConnection connection,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            long rootActorId = ResolveRootActorId(connection, _spec);
            token.ThrowIfCancellationRequested();
            return LineageBulkQuery.Load(connection, rootActorId,
                _spec.MaximumNodes, _spec.MaximumEdges,
                _spec.MaximumStringLength,
                _spec.MaximumTotalStringCharacters,
                _spec.ShiId,
                _spec.Mode == LineageTreeReadMode.Locate
                    ? _spec.ActorId
                    : -1L);
        }

        private static long ResolveRootActorId(SQLiteConnection connection,
            LineageTreeReadSpec spec)
        {
            if (connection == null) return -1L;
            if (spec.Mode == LineageTreeReadMode.Family)
                return spec.ActorId;

            long treeShiId = spec.Mode == LineageTreeReadMode.Locate
                ? ResolveRootShiId(connection, spec.ShiId)
                : spec.ShiId;

            long founder = -1L;
            using (var founderCommand = new SQLiteCommand(connection))
            {
                founderCommand.CommandText =
                    "SELECT IFNULL(FOUNDER_ACTOR_ID,-1) FROM ShiBranch " +
                    "WHERE SHI_ID=@shi LIMIT 1";
                founderCommand.Parameters.AddWithValue("@shi", treeShiId);
                object value = founderCommand.ExecuteScalar();
                if (value != null && value != DBNull.Value)
                    founder = Convert.ToInt64(value);
            }
            if (founder >= 0L) return founder;

            using var fallbackCommand = new SQLiteCommand(connection);
            fallbackCommand.CommandText =
                "SELECT ID FROM ActorArchive WHERE SHI_ID=@shi AND SEX=0 " +
                "ORDER BY BIRTH_TIME,ID LIMIT 1";
            fallbackCommand.Parameters.AddWithValue("@shi", treeShiId);
            object fallback = fallbackCommand.ExecuteScalar();
            return fallback == null || fallback == DBNull.Value
                ? spec.ActorId
                : Convert.ToInt64(fallback);
        }

        private static long ResolveRootShiId(SQLiteConnection connection,
            long pShiId)
        {
            if (connection == null || pShiId < 0L) return pShiId;
            using var command = new SQLiteCommand(connection);
            command.CommandText =
                "WITH RECURSIVE chain(SHI_ID,PARENT_SHI_ID) AS (" +
                "SELECT SHI_ID,IFNULL(PARENT_SHI_ID,-1) FROM ShiBranch " +
                "WHERE SHI_ID=@shi UNION SELECT parent.SHI_ID," +
                "IFNULL(parent.PARENT_SHI_ID,-1) FROM ShiBranch parent " +
                "JOIN chain child ON parent.SHI_ID=child.PARENT_SHI_ID) " +
                "SELECT SHI_ID FROM chain WHERE PARENT_SHI_ID<0 OR " +
                "PARENT_SHI_ID=SHI_ID LIMIT 1";
            command.Parameters.AddWithValue("@shi", pShiId);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? pShiId
                : Convert.ToInt64(value);
        }
    }

    internal sealed class LineageTreeVisitProducer : IEnumerator<long>
    {
        private sealed class Frame
        {
            public long ActorId;
            public IReadOnlyList<long> Children;
            public int ChildIndex;
        }

        private readonly LineageBulkSnapshot _snapshot;
        private readonly int _maximumActors;
        private readonly HashSet<long> _visited = new HashSet<long>();
        private readonly Stack<Frame> _frames = new Stack<Frame>();
        private readonly long _rootActorId;
        private bool _started;

        public LineageTreeVisitProducer(LineageBulkSnapshot snapshot,
            long rootActorId, int maximumActors)
        {
            _snapshot = snapshot;
            _rootActorId = rootActorId;
            _maximumActors = Math.Max(1, maximumActors);
        }

        public long Current { get; private set; }
        object IEnumerator.Current => Current;
        public bool Overflowed { get; private set; }

        public bool MoveNext()
        {
            if (_snapshot == null || _rootActorId < 0L) return false;
            if (!_started)
            {
                _started = true;
                _visited.Add(_rootActorId);
                Current = _rootActorId;
                _frames.Push(CreateFrame(_rootActorId));
                return true;
            }

            while (_frames.Count > 0)
            {
                Frame frame = _frames.Peek();
                if (frame.ChildIndex >= frame.Children.Count)
                {
                    _frames.Pop();
                    continue;
                }

                long candidateId = frame.Children[frame.ChildIndex++];
                if (_visited.Contains(candidateId)) continue;
                if (_visited.Count >= _maximumActors)
                {
                    Overflowed = true;
                    _frames.Clear();
                    return false;
                }
                _visited.Add(candidateId);
                Current = candidateId;
                if (_snapshot.TryGetNode(frame.ActorId,
                        out LineageTreeNodeSnapshot parent) &&
                    _snapshot.TryGetNode(candidateId,
                        out LineageTreeNodeSnapshot child) &&
                    FamilyTreeRelationRules.ShouldIncludeBigTreeEdge(
                        frame.ActorId, _snapshot.FatherId(candidateId),
                        _snapshot.MotherId(candidateId), parent.Sex,
                        parent.HasHeldTitle, child.Sex, child.Status,
                        child.HasHeldTitle, _snapshot.BigTreeProfile))
                    _frames.Push(CreateFrame(candidateId));
                return true;
            }
            return false;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            _frames.Clear();
        }

        private Frame CreateFrame(long actorId)
        {
            return new Frame
            {
                ActorId = actorId,
                Children = _snapshot.ChildIds(actorId)
            };
        }
    }

    internal static class LineageBulkQuery
    {
        public static LineageBulkSnapshot Load(SQLiteConnection pDb,
            long rootActorId, int maximumNodes = 512)
        {
            int boundedNodes = Math.Max(1, Math.Min(512, maximumNodes));
            return Load(pDb, rootActorId, boundedNodes,
                Math.Min(2048, boundedNodes * 4), 256,
                Math.Min(1_048_576, boundedNodes * 256 * 16), -1L, -1L);
        }

        internal static LineageBulkSnapshot Load(SQLiteConnection pDb,
            long rootActorId, int maximumNodes, int maximumEdges,
            int maximumStringLength, int maximumTotalStringCharacters,
            long backShiId, long requestedLocateActorId)
        {
            if (pDb == null || rootActorId < 0L)
                return Empty();
            int maximum = Math.Max(1, Math.Min(512, maximumNodes));
            int edgeMaximum = Math.Max(1, Math.Min(2048, maximumEdges));
            var parents = new Dictionary<long, List<long>>();
            var children = new Dictionary<long, List<long>>();
            var actorIds = new HashSet<long>();
            var actorOrder = new List<long>();
            AddActorId(actorIds, actorOrder, rootActorId);
            var rawEdges = new List<LineageTreeEdge>(edgeMaximum + 1);
            var rawEdgeSet = new HashSet<LineageTreeEdge>();
            var resolvedParentSlots = new HashSet<LineageTreeParentSlot>();
            int commands = 0;
            using SQLiteTransaction transaction =
                pDb.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);

            using (var edges = new SQLiteCommand(pDb))
            {
                edges.Transaction = transaction;
                edges.CommandText =
                    "WITH RECURSIVE parent_slots(SLOT) AS (" +
                    "SELECT 1 UNION ALL SELECT 2)," +
                    "edge_sources(SOURCE) AS (" +
                    "SELECT 0 UNION ALL SELECT 1)," +
                    "ancestors(ID) AS (" +
                    "SELECT @root " +
                    "UNION SELECT CASE source.SOURCE WHEN 0 THEN " +
                    "edge.PARENT_ID ELSE CASE slot.SLOT WHEN 1 THEN " +
                    "actor.PARENT_ID_1 ELSE actor.PARENT_ID_2 END END " +
                    "FROM ancestors child CROSS JOIN parent_slots slot " +
                    "CROSS JOIN edge_sources source LEFT JOIN FamilyEdge edge " +
                    "ON edge.CHILD_ID=child.ID AND edge.PARENT_SLOT=slot.SLOT " +
                    "LEFT JOIN ActorArchive actor ON actor.ID=child.ID " +
                    "WHERE (source.SOURCE=0 AND edge.PARENT_ID>=0) OR " +
                    "(source.SOURCE=1 AND edge.PARENT_SLOT IS NULL AND " +
                    "CASE slot.SLOT WHEN 1 THEN actor.PARENT_ID_1 ELSE " +
                    "actor.PARENT_ID_2 END>=0) LIMIT @relativeLimit)," +
                    "relatives(ID) AS (" +
                    "SELECT ID FROM ancestors WHERE ID>=0 " +
                    "UNION SELECT CASE source.SOURCE WHEN 0 THEN " +
                    "edge.CHILD_ID ELSE actor.ID END FROM relatives parent " +
                    "CROSS JOIN parent_slots slot CROSS JOIN edge_sources source " +
                    "LEFT JOIN FamilyEdge edge ON edge.PARENT_ID=parent.ID " +
                    "AND edge.PARENT_SLOT=slot.SLOT LEFT JOIN ActorArchive actor " +
                    "ON (slot.SLOT=1 AND actor.PARENT_ID_1=parent.ID) OR " +
                    "(slot.SLOT=2 AND actor.PARENT_ID_2=parent.ID) " +
                    "WHERE (source.SOURCE=0 AND edge.CHILD_ID>=0) OR " +
                    "(source.SOURCE=1 AND actor.ID>=0 AND NOT EXISTS " +
                    "(SELECT 1 FROM FamilyEdge persisted WHERE " +
                    "persisted.CHILD_ID=actor.ID AND " +
                    "persisted.PARENT_SLOT=slot.SLOT)) LIMIT @relativeLimit)," +
                    "edge_rows(CHILD_ID,PARENT_ID,PARENT_SLOT," +
                    "CREATED_TIME) AS (" +
                    "SELECT CASE source.SOURCE WHEN 0 THEN edge.CHILD_ID " +
                    "ELSE actor.ID END, CASE source.SOURCE WHEN 0 THEN " +
                    "edge.PARENT_ID ELSE CASE slot.SLOT WHEN 1 THEN " +
                    "actor.PARENT_ID_1 ELSE actor.PARENT_ID_2 END END, " +
                    "slot.SLOT, CASE source.SOURCE WHEN 0 THEN " +
                    "edge.CREATED_TIME ELSE 0 END FROM relatives child " +
                    "CROSS JOIN parent_slots slot CROSS JOIN edge_sources source " +
                    "LEFT JOIN FamilyEdge edge ON edge.CHILD_ID=child.ID " +
                    "AND edge.PARENT_SLOT=slot.SLOT LEFT JOIN ActorArchive actor " +
                    "ON actor.ID=child.ID WHERE (source.SOURCE=0 AND " +
                    "edge.PARENT_ID>=0) OR (source.SOURCE=1 AND " +
                    "edge.PARENT_SLOT IS NULL AND CASE slot.SLOT WHEN 1 " +
                    "THEN actor.PARENT_ID_1 ELSE actor.PARENT_ID_2 END>=0)) " +
                    "SELECT CHILD_ID,PARENT_ID,PARENT_SLOT,CREATED_TIME " +
                    "FROM edge_rows ORDER BY CREATED_TIME," +
                    "CHILD_ID,PARENT_SLOT LIMIT @edgeLimit";
                edges.Parameters.AddWithValue("@root", rootActorId);
                edges.Parameters.AddWithValue("@relativeLimit", maximum + 1);
                edges.Parameters.AddWithValue("@edgeLimit", edgeMaximum + 1);
                commands++;
                using SQLiteDataReader reader = edges.ExecuteReader();
                while (reader.Read())
                {
                    long child = reader.GetInt64(0);
                    long parent = reader.GetInt64(1);
                    int parentSlot = reader.GetInt32(2);
                    AddRawEdge(rawEdges, rawEdgeSet, child, parent);
                    resolvedParentSlots.Add(new LineageTreeParentSlot(
                        child, parentSlot));
                    AddActorId(actorIds, actorOrder, child);
                    AddActorId(actorIds, actorOrder, parent);
                }
            }

            ReadRootFamilyEdges(pDb, transaction, rootActorId, edgeMaximum,
                rawEdges, rawEdgeSet, resolvedParentSlots, actorIds,
                actorOrder);

            var actors = new Dictionary<long, ActorArchiveTableItem>();
            long[] seedIds = BuildActorSeedIds(rootActorId, actorOrder,
                rawEdges, requestedLocateActorId, maximum + 1);
            if (seedIds.Length > 0)
            {
                using var actorCommand = new SQLiteCommand(pDb);
                actorCommand.Transaction = transaction;
                var seeds = new string[seedIds.Length];
                for (int index = 0; index < seedIds.Length; index++)
                {
                    seeds[index] = "(@id" + index + ")";
                    actorCommand.Parameters.AddWithValue("@id" + index,
                        seedIds[index]);
                }
                actorCommand.Parameters.AddWithValue("@archiveLimit",
                    seedIds.Length + maximum + 1);
                actorCommand.Parameters.AddWithValue("@root", rootActorId);
                actorCommand.Parameters.AddWithValue("@ancestorLimit",
                    maximum + 1);
                actorCommand.CommandText =
                    "WITH RECURSIVE seeds(ID) AS (VALUES " +
                    string.Join(",", seeds) + ")," +
                    "parent_slots(SLOT) AS (SELECT 1 UNION ALL SELECT 2)," +
                    "archive_ancestors(ID) AS (" +
                    "SELECT @root " +
                    "UNION SELECT CASE slot.SLOT WHEN 1 THEN " +
                    "current.PARENT_ID_1 ELSE current.PARENT_ID_2 END " +
                    "FROM archive_ancestors child CROSS JOIN parent_slots slot " +
                    "JOIN ActorArchive current ON current.ID=child.ID " +
                    "WHERE CASE slot.SLOT WHEN 1 THEN current.PARENT_ID_1 " +
                    "ELSE current.PARENT_ID_2 END>=0 LIMIT @ancestorLimit)," +
                    "archive_base(ID) AS (SELECT ID FROM seeds UNION " +
                    "SELECT ID FROM archive_ancestors)," +
                    "archive_relatives(ID) AS (" +
                    "SELECT ID FROM archive_base " +
                    "UNION SELECT actor.ID FROM archive_relatives related " +
                    "CROSS JOIN parent_slots slot JOIN ActorArchive actor ON " +
                    "(slot.SLOT=1 AND actor.PARENT_ID_1=related.ID) OR " +
                    "(slot.SLOT=2 AND actor.PARENT_ID_2=related.ID) " +
                    "WHERE actor.ID>=0 LIMIT @archiveLimit) " +
                    "SELECT actor.* FROM ActorArchive actor " +
                    "JOIN archive_relatives related ON actor.ID=related.ID " +
                    "ORDER BY actor.ID";
                commands++;
                using SQLiteDataReader reader = actorCommand.ExecuteReader();
                while (reader.Read())
                {
                    var row = new ActorArchiveTableItem();
                    row.ReadFromReader(reader);
                    actors[row.id] = row;
                    AddActorId(actorIds, actorOrder, row.id);
                }
            }

            foreach (ActorArchiveTableItem actor in actors.Values)
            {
                if (actor.parent_id_1 >= 0L &&
                    !resolvedParentSlots.Contains(
                        new LineageTreeParentSlot(actor.id, 1)) &&
                    actors.ContainsKey(actor.parent_id_1))
                    AddRawEdge(rawEdges, rawEdgeSet, actor.id,
                        actor.parent_id_1);
                if (actor.parent_id_2 >= 0L &&
                    !resolvedParentSlots.Contains(
                        new LineageTreeParentSlot(actor.id, 2)) &&
                    actors.ContainsKey(actor.parent_id_2))
                    AddRawEdge(rawEdges, rawEdgeSet, actor.id,
                        actor.parent_id_2);
            }

            bool nodeOverflow = actorOrder.Count > maximum;
            bool edgeOverflow = rawEdges.Count > edgeMaximum;
            List<long> prioritizedActorOrder = backShiId < 0L &&
                requestedLocateActorId < 0L
                ? PrioritizeFamilyRelations(rootActorId, actorOrder, actors,
                    rawEdges)
                : PrioritizeRootAncestors(rootActorId,
                    requestedLocateActorId, actorOrder, actors, rawEdges);
            var allowedIds = new HashSet<long>(
                prioritizedActorOrder.Take(maximum));
            actorIds = allowedIds;
            foreach (long actorId in actors.Keys.ToArray())
                if (!allowedIds.Contains(actorId)) actors.Remove(actorId);

            int edgeCount = 0;
            for (int index = 0;
                 index < rawEdges.Count && edgeCount < edgeMaximum; index++)
            {
                LineageTreeEdge edge = rawEdges[index];
                if (!allowedIds.Contains(edge.ChildId) ||
                    !allowedIds.Contains(edge.ParentId)) continue;
                AddUnique(parents, edge.ChildId, edge.ParentId);
                AddUnique(children, edge.ParentId, edge.ChildId);
                edgeCount++;
            }

            long[] orderedIds = actorIds.OrderBy(pId => pId).ToArray();

            Dictionary<long, LineageTreeShiSnapshot> shiById =
                ReadShiSnapshots(pDb, transaction, actors.Values,
                    maximum);
            HashSet<long> titleHolderIds = ReadTitleHolderIds(pDb,
                transaction, orderedIds);
            ReadPosthumousSnapshots(pDb, transaction, orderedIds,
                out Dictionary<long, string> appellations,
                out Dictionary<long, string> retrospectiveRelations);
            transaction.Commit();

            var stringBudget = new LineageTreeStringBudget(
                Math.Max(8, Math.Min(512, maximumStringLength)),
                Math.Max(8, maximumTotalStringCharacters));
            var treeNodes = new Dictionary<long, LineageTreeNodeSnapshot>();
            foreach (ActorArchiveTableItem actor in actors.Values)
            {
                shiById.TryGetValue(actor.shi_id,
                    out LineageTreeShiSnapshot shi);
                shiById.TryGetValue(actor.founded_branch_shi_id,
                    out LineageTreeShiSnapshot foundedBranch);
                if (foundedBranch == null ||
                    !LineageBranchRules.IsFoundedBranchForActor(
                        foundedBranch.SourceType,
                        foundedBranch.FounderActorId, actor.id))
                {
                    actor.founded_branch_shi_id = -1L;
                    foundedBranch = null;
                }
                appellations.TryGetValue(actor.id, out string appellation);
                retrospectiveRelations.TryGetValue(actor.id,
                    out string retrospectiveRelation);
                treeNodes[actor.id] = new LineageTreeNodeSnapshot(actor,
                    shi, foundedBranch, appellation,
                    retrospectiveRelation,
                    titleHolderIds.Contains(actor.id), stringBudget);
            }

            List<long> locatePath = BuildLocatePath(requestedLocateActorId,
                rootActorId, parents, treeNodes);
            long locateActorId = ResolveLocateActorId(requestedLocateActorId,
                rootActorId, locatePath, treeNodes);
            var overflow = new LineageTreeOverflow(nodeOverflow,
                edgeOverflow, stringBudget.Overflowed);
            return new LineageBulkSnapshot(actors, parents, children,
                actorIds, commands, treeNodes, rootActorId, backShiId,
                locateActorId, locatePath, overflow, edgeCount);
        }

        private static Dictionary<long, LineageTreeShiSnapshot>
            ReadShiSnapshots(SQLiteConnection connection,
                SQLiteTransaction transaction,
                IEnumerable<ActorArchiveTableItem> actorRows,
                int maximumNodes)
        {
            var result = new Dictionary<long, LineageTreeShiSnapshot>();
            var actors = new Dictionary<long, ActorArchiveTableItem>();
            var referencedShiIds = new HashSet<long>();
            foreach (ActorArchiveTableItem actor in actorRows ??
                     Enumerable.Empty<ActorArchiveTableItem>())
            {
                if (actor == null) continue;
                actors[actor.id] = actor;
            }

            RecoverFoundedBranchIds(connection, transaction, actors);
            foreach (ActorArchiveTableItem actor in actors.Values)
            {
                if (actor.shi_id >= 0L) referencedShiIds.Add(actor.shi_id);
                if (actor.founded_branch_shi_id >= 0L)
                    referencedShiIds.Add(actor.founded_branch_shi_id);
            }
            if (referencedShiIds.Count == 0) return result;

            int maximumShiRows = Math.Max(1, Math.Min(2048,
                Math.Max(maximumNodes, referencedShiIds.Count) * 2));
            string ids = string.Join(",", referencedShiIds
                .Take(maximumShiRows).OrderBy(id => id));
            try
            {
                using var command = new SQLiteCommand(connection);
                command.Transaction = transaction;
                command.CommandText =
                    "WITH RECURSIVE chain(SHI_ID,PARENT_SHI_ID," +
                    "FOUNDER_ACTOR_ID,CLAN_NAME,SOURCE_TYPE," +
                    "ORIGIN_CITY_ID,STATE_NAME,NAMING_PROFILE," +
                    "WESTERN_NAMING_TRADITION," +
                    "ORIGIN_CITY_CHINESE_NAME,DISPLAY_STEM) AS (" +
                    "SELECT SHI_ID,IFNULL(PARENT_SHI_ID,-1)," +
                    "IFNULL(FOUNDER_ACTOR_ID,-1),IFNULL(CLAN_NAME,'')," +
                    "IFNULL(SOURCE_TYPE,''),IFNULL(ORIGIN_CITY_ID,-1)," +
                    "IFNULL(STATE_NAME,''),IFNULL(NAMING_PROFILE,'xia')," +
                    "IFNULL(WESTERN_NAMING_TRADITION,'')," +
                    "IFNULL(ORIGIN_CITY_CHINESE_NAME,'')," +
                    "IFNULL(DISPLAY_STEM,'') FROM ShiBranch " +
                    "WHERE SHI_ID IN (" +
                    ids + ") UNION SELECT parent.SHI_ID," +
                    "IFNULL(parent.PARENT_SHI_ID,-1)," +
                    "IFNULL(parent.FOUNDER_ACTOR_ID,-1)," +
                    "IFNULL(parent.CLAN_NAME,'')," +
                    "IFNULL(parent.SOURCE_TYPE,'')," +
                    "IFNULL(parent.ORIGIN_CITY_ID,-1)," +
                    "IFNULL(parent.STATE_NAME,'')," +
                    "IFNULL(parent.NAMING_PROFILE,'xia')," +
                    "IFNULL(parent.WESTERN_NAMING_TRADITION,'')," +
                    "IFNULL(parent.ORIGIN_CITY_CHINESE_NAME,'')," +
                    "IFNULL(parent.DISPLAY_STEM,'') FROM ShiBranch parent " +
                    "JOIN chain child ON parent.SHI_ID=child.PARENT_SHI_ID " +
                    "LIMIT @limit) SELECT SHI_ID,PARENT_SHI_ID," +
                    "FOUNDER_ACTOR_ID,CLAN_NAME,SOURCE_TYPE,ORIGIN_CITY_ID," +
                    "STATE_NAME,NAMING_PROFILE,WESTERN_NAMING_TRADITION," +
                    "ORIGIN_CITY_CHINESE_NAME,DISPLAY_STEM," +
                    "IFNULL((SELECT archived.CITY_NAME FROM " +
                    "ActorArchive archived WHERE archived.ID=" +
                    "chain.FOUNDER_ACTOR_ID LIMIT 1),'') " +
                    "FROM chain ORDER BY SHI_ID";
                command.Parameters.AddWithValue("@limit", maximumShiRows);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long founderActorId = reader.GetInt64(2);
                    string clanName = reader.GetString(3);
                    string sourceType = reader.GetString(4);
                    string stateName = reader.GetString(6);
                    string namingProfile = reader.GetString(7);
                    string westernNamingTradition = reader.GetString(8);
                    string originCityChineseName = reader.GetString(9);
                    string displayStem = reader.GetString(10);
                    string archivedOriginCityName = reader.GetString(11);
                    string originCityName =
                        string.IsNullOrWhiteSpace(originCityChineseName)
                            ? archivedOriginCityName
                            : originCityChineseName;
                    result[reader.GetInt64(0)] = new LineageTreeShiSnapshot
                    {
                        ShiId = reader.GetInt64(0),
                        ParentShiId = reader.GetInt64(1),
                        FounderActorId = founderActorId,
                        SourceType = sourceType,
                        Display = ShiBranchRules.BuildDisplayName(
                            originCityName, clanName, sourceType, stateName),
                        OriginCityName = originCityName,
                        StateName = stateName,
                        NamingProfile = namingProfile,
                        WesternNamingTradition = westernNamingTradition,
                        OriginCityChineseName = originCityChineseName,
                        DisplayStem = displayStem
                    };
                }
            }
            catch (SQLiteException)
            {
                return result;
            }

            foreach (LineageTreeShiSnapshot shi in result.Values)
            {
                if (result.TryGetValue(shi.ParentShiId,
                        out LineageTreeShiSnapshot parent))
                    shi.ParentDisplay = parent.Display;

                var visited = new HashSet<long>();
                LineageTreeShiSnapshot root = shi;
                while (root.ParentShiId >= 0L &&
                       visited.Add(root.ShiId) &&
                       result.TryGetValue(root.ParentShiId,
                           out LineageTreeShiSnapshot next))
                    root = next;
                shi.RootDisplay = root.Display;
            }
            return result;
        }

        private static void RecoverFoundedBranchIds(SQLiteConnection connection,
            SQLiteTransaction transaction,
            IReadOnlyDictionary<long, ActorArchiveTableItem> actors)
        {
            if (connection == null || actors == null || actors.Count == 0)
                return;

            long[] founderIds = actors.Values
                .Where(actor => actor != null &&
                                actor.id >= 0L)
                .Select(actor => actor.id)
                .Distinct()
                .ToArray();
            if (founderIds.Length == 0) return;

            if (!FoundedBranchRecoveryQuery.TryRead(connection, transaction,
                    founderIds, out FoundedBranchRecoverySnapshot recovery))
                return;
            foreach (ActorArchiveTableItem actor in actors.Values)
            {
                if (actor == null || actor.id < 0L) continue;
                actor.founded_branch_shi_id = recovery.Resolve(
                    actor.id, actor.founded_branch_shi_id);
            }
        }

        private static HashSet<long> ReadTitleHolderIds(
            SQLiteConnection connection, SQLiteTransaction transaction,
            IEnumerable<long> actorIds)
        {
            var result = new HashSet<long>();
            long[] ids = (actorIds ?? Enumerable.Empty<long>())
                .Where(id => id >= 0L).Distinct().Take(512).ToArray();
            if (ids.Length == 0) return result;

            try
            {
                using var command = new SQLiteCommand(connection);
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT candidate.ID FROM ActorArchive candidate " +
                    "WHERE candidate.ID IN (" + string.Join(",", ids) +
                    ") AND (EXISTS (SELECT 1 FROM " +
                    KingdomReignTableItem.GetTableName() +
                    " reign WHERE reign.KING_ACTOR_ID=candidate.ID) OR " +
                    "EXISTS (SELECT 1 FROM " +
                    EnfeoffmentTableItem.GetTableName() +
                    " grant_row WHERE grant_row.ACTOR_ID=candidate.ID " +
                    "AND grant_row.NOBLE_RANK>0))";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read()) result.Add(reader.GetInt64(0));
            }
            catch (SQLiteException)
            {
                // Legacy saves without either title table retain the
                // agnatic fallback until normal schema initialization.
            }
            return result;
        }

        private static void ReadPosthumousSnapshots(
            SQLiteConnection connection, SQLiteTransaction transaction,
            IEnumerable<long> actorIds,
            out Dictionary<long, string> appellations,
            out Dictionary<long, string> retrospectiveRelations)
        {
            appellations = new Dictionary<long, string>();
            retrospectiveRelations = new Dictionary<long, string>();
            long[] ids = (actorIds ?? Enumerable.Empty<long>())
                .Where(id => id >= 0L).Distinct().Take(512).ToArray();
            if (ids.Length == 0) return;

            try
            {
                using var command = new SQLiteCommand(connection);
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT ACTOR_ID,IFNULL(FULL_TITLE,'')," +
                    "IFNULL(RETROSPECTIVE_RELATION,'') FROM PosthumousTitle " +
                    "WHERE ACTOR_ID IN (" + string.Join(",", ids) + ") " +
                    "ORDER BY DECIDED_TIME,RECORD_ID";
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long actorId = reader.GetInt64(0);
                    string title = reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    appellations[actorId] = title;
                    string relation = reader.GetString(2);
                    if (!string.IsNullOrWhiteSpace(relation))
                        retrospectiveRelations[actorId] = relation;
                }
            }
            catch (SQLiteException)
            {
                appellations.Clear();
                retrospectiveRelations.Clear();
            }
        }

        private static List<long> BuildLocatePath(long requestedActorId,
            long rootActorId, Dictionary<long, List<long>> parents,
            IReadOnlyDictionary<long, LineageTreeNodeSnapshot> nodes)
        {
            var result = new List<long>();
            if (requestedActorId < 0L || rootActorId < 0L ||
                !nodes.TryGetValue(rootActorId,
                    out LineageTreeNodeSnapshot root)) return result;
            NamingProfileId profile =
                AWCultureNamingTraditionRules.ParseProfile(
                    root.ShiNamingProfile);
            var visited = new HashSet<long> { requestedActorId };
            var queue = new Queue<long>();
            var nextTowardRequested = new Dictionary<long, long>();
            queue.Enqueue(requestedActorId);
            while (queue.Count > 0 && visited.Count <= 512)
            {
                long childId = queue.Dequeue();
                if (childId == rootActorId) break;
                if (!parents.TryGetValue(childId,
                        out List<long> parentIds) ||
                    !nodes.TryGetValue(childId,
                        out LineageTreeNodeSnapshot child)) continue;
                long fatherId = FindParentId(parentIds, nodes, 0);
                long motherId = FindParentId(parentIds, nodes, 1);
                foreach (long parentId in parentIds)
                {
                    if (!nodes.TryGetValue(parentId,
                            out LineageTreeNodeSnapshot parent) ||
                        !FamilyTreeRelationRules.ShouldIncludeBigTreeEdge(
                            parentId, fatherId, motherId, parent.Sex,
                            parent.HasHeldTitle, child.Sex, child.Status,
                            child.HasHeldTitle, profile) ||
                        !visited.Add(parentId)) continue;
                    nextTowardRequested[parentId] = childId;
                    queue.Enqueue(parentId);
                }
            }
            if (!visited.Contains(rootActorId)) return result;
            long current = rootActorId;
            result.Add(current);
            while (current != requestedActorId &&
                   nextTowardRequested.TryGetValue(current,
                       out long child))
            {
                current = child;
                result.Add(current);
            }
            if (current != requestedActorId) result.Clear();
            return result;
        }

        private static long ResolveLocateActorId(long requestedActorId,
            long rootActorId, IReadOnlyList<long> locatePath,
            IReadOnlyDictionary<long, LineageTreeNodeSnapshot> nodes)
        {
            if (locatePath == null || locatePath.Count == 0)
                return rootActorId;
            NamingProfileId profile = nodes.TryGetValue(rootActorId,
                    out LineageTreeNodeSnapshot root)
                ? AWCultureNamingTraditionRules.ParseProfile(
                    root.ShiNamingProfile)
                : NamingProfileId.None;
            if (nodes.TryGetValue(requestedActorId,
                    out LineageTreeNodeSnapshot requested) &&
                FamilyTreeRelationRules.ShouldShowInBigTree(requested.Sex,
                    requested.Status, profile, requested.HasHeldTitle))
                return requestedActorId;

            for (int index = locatePath.Count - 2; index >= 0; index--)
            {
                long actorId = locatePath[index];
                if (nodes.TryGetValue(actorId,
                        out LineageTreeNodeSnapshot actor) &&
                    FamilyTreeRelationRules.ShouldShowInBigTree(actor.Sex,
                        actor.Status, profile, actor.HasHeldTitle))
                    return actorId;
            }
            return rootActorId;
        }

        private static long FindFatherId(long actorId,
            Dictionary<long, List<long>> parents,
            IReadOnlyDictionary<long, ActorArchiveTableItem> actors)
        {
            if (!parents.TryGetValue(actorId, out List<long> parentIds))
                return -1L;
            for (int index = 0; index < parentIds.Count; index++)
            {
                long parentId = parentIds[index];
                if (actors.TryGetValue(parentId,
                        out ActorArchiveTableItem parent) && parent.sex == 0)
                    return parentId;
            }
            return -1L;
        }

        private static long FindParentId(IEnumerable<long> parentIds,
            IReadOnlyDictionary<long, LineageTreeNodeSnapshot> nodes,
            int sex)
        {
            foreach (long parentId in parentIds ?? Enumerable.Empty<long>())
                if (nodes.TryGetValue(parentId,
                        out LineageTreeNodeSnapshot parent) &&
                    (parent.Sex == 0 ? 0 : 1) == sex)
                    return parentId;
            return -1L;
        }

        private readonly struct LineageTreeEdge : IEquatable<LineageTreeEdge>
        {
            public LineageTreeEdge(long childId, long parentId)
            {
                ChildId = childId;
                ParentId = parentId;
            }

            public long ChildId { get; }
            public long ParentId { get; }

            public bool Equals(LineageTreeEdge other)
            {
                return ChildId == other.ChildId && ParentId == other.ParentId;
            }

            public override bool Equals(object obj)
            {
                return obj is LineageTreeEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ChildId.GetHashCode() * 397) ^
                           ParentId.GetHashCode();
                }
            }
        }

        private readonly struct LineageTreeParentSlot :
            IEquatable<LineageTreeParentSlot>
        {
            public LineageTreeParentSlot(long childId, int parentSlot)
            {
                ChildId = childId;
                ParentSlot = parentSlot;
            }

            private long ChildId { get; }
            private int ParentSlot { get; }

            public bool Equals(LineageTreeParentSlot other)
            {
                return ChildId == other.ChildId &&
                       ParentSlot == other.ParentSlot;
            }

            public override bool Equals(object obj)
            {
                return obj is LineageTreeParentSlot other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ChildId.GetHashCode() * 397) ^ ParentSlot;
                }
            }
        }

        private static void AddRawEdge(List<LineageTreeEdge> edges,
            HashSet<LineageTreeEdge> edgeSet, long childId, long parentId)
        {
            if (childId < 0L || parentId < 0L) return;
            var edge = new LineageTreeEdge(childId, parentId);
            if (edgeSet.Add(edge)) edges.Add(edge);
        }

        private static void ReadRootFamilyEdges(SQLiteConnection connection,
            SQLiteTransaction transaction, long rootActorId, int edgeLimit,
            List<LineageTreeEdge> edges,
            HashSet<LineageTreeEdge> edgeSet,
            HashSet<LineageTreeParentSlot> resolvedParentSlots,
            HashSet<long> actorIds, List<long> actorOrder)
        {
            if (connection == null || rootActorId < 0L) return;
            using (var command = new SQLiteCommand(connection))
            {
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT CHILD_ID,PARENT_ID,PARENT_SLOT FROM FamilyEdge " +
                    "WHERE CHILD_ID=@root OR PARENT_ID=@root " +
                    "ORDER BY CREATED_TIME,CHILD_ID,PARENT_SLOT LIMIT @limit";
                command.Parameters.AddWithValue("@root", rootActorId);
                command.Parameters.AddWithValue("@limit", edgeLimit + 1);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long childId = reader.GetInt64(0);
                    long parentId = reader.GetInt64(1);
                    int parentSlot = reader.GetInt32(2);
                    AddRawEdge(edges, edgeSet, childId, parentId);
                    resolvedParentSlots.Add(new LineageTreeParentSlot(
                        childId, parentSlot));
                    AddActorId(actorIds, actorOrder, childId);
                    AddActorId(actorIds, actorOrder, parentId);
                }
            }

            using (var command = new SQLiteCommand(connection))
            {
                command.Transaction = transaction;
                command.CommandText =
                    "SELECT ID,PARENT_ID_1,PARENT_ID_2 FROM ActorArchive " +
                    "WHERE ID=@root OR PARENT_ID_1=@root OR PARENT_ID_2=@root " +
                    "ORDER BY ID LIMIT @limit";
                command.Parameters.AddWithValue("@root", rootActorId);
                command.Parameters.AddWithValue("@limit", edgeLimit + 1);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long childId = reader.GetInt64(0);
                    long parent1 = reader.IsDBNull(1) ? -1L :
                        reader.GetInt64(1);
                    long parent2 = reader.IsDBNull(2) ? -1L :
                        reader.GetInt64(2);
                    if (parent1 >= 0L && !resolvedParentSlots.Contains(
                            new LineageTreeParentSlot(childId, 1)))
                        AddRawEdge(edges, edgeSet, childId, parent1);
                    if (parent2 >= 0L && !resolvedParentSlots.Contains(
                            new LineageTreeParentSlot(childId, 2)))
                        AddRawEdge(edges, edgeSet, childId, parent2);
                    AddActorId(actorIds, actorOrder, childId);
                    AddActorId(actorIds, actorOrder, parent1);
                    AddActorId(actorIds, actorOrder, parent2);
                }
            }
        }

        private static long[] BuildActorSeedIds(long rootActorId,
            IEnumerable<long> actorOrder,
            IReadOnlyList<LineageTreeEdge> edges,
            long requestedLocateActorId, int ordinarySeedLimit)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            if (rootActorId >= 0L && seen.Add(rootActorId))
                result.Add(rootActorId);
            foreach (LineageTreeEdge edge in edges)
            {
                if (result.Count >= ordinarySeedLimit) break;
                if (edge.ChildId != rootActorId || edge.ParentId < 0L ||
                    !seen.Add(edge.ParentId)) continue;
                result.Add(edge.ParentId);
            }
            foreach (LineageTreeEdge edge in edges)
            {
                if (result.Count >= ordinarySeedLimit) break;
                if (edge.ParentId != rootActorId || edge.ChildId < 0L ||
                    !seen.Add(edge.ChildId)) continue;
                result.Add(edge.ChildId);
            }
            foreach (long actorId in actorOrder.Take(ordinarySeedLimit))
            {
                if (result.Count >= ordinarySeedLimit) break;
                if (actorId >= 0L && seen.Add(actorId)) result.Add(actorId);
            }
            if (requestedLocateActorId < 0L ||
                !seen.Add(requestedLocateActorId)) return result.ToArray();

            result.Add(requestedLocateActorId);
            var pending = new Queue<long>();
            pending.Enqueue(requestedLocateActorId);
            int reservedAncestors = 0;
            int ancestorLimit = Math.Min(128,
                Math.Max(1, ordinarySeedLimit));
            while (pending.Count > 0 && reservedAncestors < ancestorLimit)
            {
                long childId = pending.Dequeue();
                for (int index = 0; index < edges.Count &&
                     reservedAncestors < ancestorLimit; index++)
                {
                    LineageTreeEdge edge = edges[index];
                    if (edge.ChildId != childId ||
                        !seen.Add(edge.ParentId)) continue;
                    result.Add(edge.ParentId);
                    pending.Enqueue(edge.ParentId);
                    reservedAncestors++;
                }
            }
            return result.ToArray();
        }

        private static List<long> PrioritizeFamilyRelations(long rootActorId,
            IEnumerable<long> actorOrder,
            IReadOnlyDictionary<long, ActorArchiveTableItem> actors,
            IReadOnlyList<LineageTreeEdge> edges)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            AppendFamilyActor(result, seen, actors, rootActorId);

            foreach (LineageTreeEdge edge in edges)
                if (edge.ChildId == rootActorId)
                    AppendFamilyActor(result, seen, actors, edge.ParentId);

            var pendingAncestors = new Queue<long>();
            var expandedAncestors = new HashSet<long>();
            pendingAncestors.Enqueue(rootActorId);
            while (pendingAncestors.Count > 0)
            {
                long childId = pendingAncestors.Dequeue();
                if (!expandedAncestors.Add(childId)) continue;
                foreach (LineageTreeEdge edge in edges)
                {
                    if (edge.ChildId != childId) continue;
                    AppendFamilyActor(result, seen, actors, edge.ParentId);
                    pendingAncestors.Enqueue(edge.ParentId);
                }
            }

            var directChildren = new List<long>();
            foreach (LineageTreeEdge edge in edges)
            {
                if (edge.ParentId != rootActorId) continue;
                directChildren.Add(edge.ChildId);
                AppendFamilyActor(result, seen, actors, edge.ChildId);
            }

            foreach (long parentId in directChildren)
                foreach (LineageTreeEdge edge in edges)
                    if (edge.ParentId == parentId)
                        AppendFamilyActor(result, seen, actors,
                            edge.ChildId);

            foreach (long actorId in actorOrder)
                AppendFamilyActor(result, seen, actors, actorId);
            return result;
        }

        private static void AppendFamilyActor(List<long> result,
            HashSet<long> seen,
            IReadOnlyDictionary<long, ActorArchiveTableItem> actors,
            long actorId)
        {
            if (actorId >= 0L && actors.ContainsKey(actorId) &&
                seen.Add(actorId)) result.Add(actorId);
        }

        private static List<long> PrioritizeRootAncestors(long rootActorId,
            long requestedLocateActorId,
            IEnumerable<long> actorOrder,
            IReadOnlyDictionary<long, ActorArchiveTableItem> actors,
            IReadOnlyList<LineageTreeEdge> edges)
        {
            var result = new List<long>();
            var seen = new HashSet<long>();
            var pending = new Queue<long>();
            pending.Enqueue(rootActorId);
            if (requestedLocateActorId >= 0L &&
                requestedLocateActorId != rootActorId)
                pending.Enqueue(requestedLocateActorId);
            while (pending.Count > 0)
            {
                long actorId = pending.Dequeue();
                if (actorId < 0L || !seen.Add(actorId)) continue;
                result.Add(actorId);

                if (actors.TryGetValue(actorId,
                        out ActorArchiveTableItem actor))
                {
                    if (actor.parent_id_1 >= 0L &&
                        actors.ContainsKey(actor.parent_id_1))
                        pending.Enqueue(actor.parent_id_1);
                    if (actor.parent_id_2 >= 0L &&
                        actors.ContainsKey(actor.parent_id_2))
                        pending.Enqueue(actor.parent_id_2);
                }

                for (int index = 0; index < edges.Count; index++)
                {
                    LineageTreeEdge edge = edges[index];
                    if (edge.ChildId == actorId &&
                        actors.ContainsKey(edge.ParentId))
                        pending.Enqueue(edge.ParentId);
                }
            }

            foreach (long actorId in actorOrder)
                if (actorId >= 0L && seen.Add(actorId)) result.Add(actorId);
            return result;
        }

        private static void AddActorId(HashSet<long> ids,
            List<long> orderedIds, long actorId)
        {
            if (actorId < 0L || !ids.Add(actorId)) return;
            orderedIds.Add(actorId);
        }

        private static void AddUnique(Dictionary<long, List<long>> pMap,
            long pKey, long pValue)
        {
            if (pKey < 0L || pValue < 0L) return;
            if (!pMap.TryGetValue(pKey, out List<long> values))
            {
                values = new List<long>();
                pMap.Add(pKey, values);
            }
            if (!values.Contains(pValue)) values.Add(pValue);
        }

        private static LineageBulkSnapshot Empty()
        {
            return new LineageBulkSnapshot(
                new Dictionary<long, ActorArchiveTableItem>(),
                new Dictionary<long, List<long>>(),
                new Dictionary<long, List<long>>(), Array.Empty<long>(), 0);
        }
    }
}
