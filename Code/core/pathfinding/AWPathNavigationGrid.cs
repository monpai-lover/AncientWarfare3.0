using System;
using System.Collections.Generic;
using System.Threading;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.pathfinding;

[Flags]
internal enum AWNavigationTileFlags : ushort
{
    None = 0,
    Exists = 1 << 0,
    HasType = 1 << 1,
    Block = 1 << 2,
    Lava = 1 << 3,
    Ocean = 1 << 4,
    Liquid = 1 << 5,
    DamageUnits = 1 << 6,
    Fire = 1 << 7
}

/// <summary>
/// 宸ヤ綔绾跨▼浣跨敤鐨勫崟鏍煎鑸暟鎹紝涓嶆寔鏈?Unity 鎴?WorldBox 杩愯鏃跺璞°€?
/// </summary>
internal readonly struct AWNavigationTileSnapshot
{
    internal AWNavigationTileSnapshot(AWNavigationTileFlags flags, float damage, float walkMultiplier, int regionId)
    {
        Flags = flags;
        Damage = damage;
        WalkMultiplier = walkMultiplier;
        RegionId = regionId;
    }

    internal AWNavigationTileFlags Flags { get; }
    internal float Damage { get; }
    internal float WalkMultiplier { get; }
    internal int RegionId { get; }
    internal bool Exists => (Flags & AWNavigationTileFlags.Exists) != 0;
    internal bool HasType => (Flags & AWNavigationTileFlags.HasType) != 0;
    internal bool Block => (Flags & AWNavigationTileFlags.Block) != 0;
    internal bool Lava => (Flags & AWNavigationTileFlags.Lava) != 0;
    internal bool Ocean => (Flags & AWNavigationTileFlags.Ocean) != 0;
    internal bool Liquid => (Flags & AWNavigationTileFlags.Liquid) != 0;
    internal bool DamageUnits => (Flags & AWNavigationTileFlags.DamageUnits) != 0;
    internal bool IsOnFire => (Flags & AWNavigationTileFlags.Fire) != 0;

    /// <summary>浠庡師鐗?terrain 璧勪骇鎻愬彇涓嶄緷璧?WorldTile 鐨勫璺涔夈€?/summary>
    internal static AWNavigationTileSnapshot Capture(TileTypeBase type)
    {
        if (type == null)
        {
            return default;
        }

        AWNavigationTileFlags flags = AWNavigationTileFlags.Exists | AWNavigationTileFlags.HasType;
        if (type.block) flags |= AWNavigationTileFlags.Block;
        if (type.lava) flags |= AWNavigationTileFlags.Lava;
        if (type.ocean) flags |= AWNavigationTileFlags.Ocean;
        if (type.liquid) flags |= AWNavigationTileFlags.Liquid;
        if (type.damage_units) flags |= AWNavigationTileFlags.DamageUnits;
        return new AWNavigationTileSnapshot(flags, type.damage, type.walk_multiplier, -1);
    }

    /// <summary>鍦ㄦā鎷熺嚎绋嬩笂鎻愬彇涓€涓湴鍧楃殑瀵昏矾璇箟銆?/summary>
    internal static AWNavigationTileSnapshot Capture(WorldTile tile)
    {
        if (tile?.data == null)
        {
            return default;
        }

        AWNavigationTileFlags flags = AWNavigationTileFlags.Exists;
        TileTypeBase type = tile.Type;
        if (type != null)
        {
            flags |= AWNavigationTileFlags.HasType;
            if (type.block) flags |= AWNavigationTileFlags.Block;
            if (type.lava) flags |= AWNavigationTileFlags.Lava;
            if (type.ocean) flags |= AWNavigationTileFlags.Ocean;
            if (type.liquid) flags |= AWNavigationTileFlags.Liquid;
            if (type.damage_units) flags |= AWNavigationTileFlags.DamageUnits;
        }

        try
        {
            if (tile.isOnFire()) flags |= AWNavigationTileFlags.Fire;
        }
        catch
        {
            // 涓栫晫鐢熸垚鍜屾竻鐞嗚竟鐣屼笂鐏劙鏁扮粍鍙兘灏氭湭灏辩华锛屾鏃舵寜鏃犵伀澶勭悊銆?
        }

        return new AWNavigationTileSnapshot(
            flags,
            type?.damage ?? 0f,
            type?.walk_multiplier ?? 1f,
            tile.region?.id ?? -1);
    }

    internal static AWNavigationTileSnapshot Capture(AWTileTraversalSnapshot tile)
    {
        if (!tile.Exists) return default;
        AWNavigationTileFlags flags = AWNavigationTileFlags.Exists;
        if (tile.HasType) flags |= AWNavigationTileFlags.HasType;
        if (tile.Block) flags |= AWNavigationTileFlags.Block;
        if (tile.Lava) flags |= AWNavigationTileFlags.Lava;
        if (tile.Ocean) flags |= AWNavigationTileFlags.Ocean;
        if (tile.Liquid) flags |= AWNavigationTileFlags.Liquid;
        if (tile.DamageUnits) flags |= AWNavigationTileFlags.DamageUnits;
        if (tile.Fire) flags |= AWNavigationTileFlags.Fire;
        return new AWNavigationTileSnapshot(flags, tile.TerrainDamage,
            tile.WalkMultiplier, tile.RegionId);
    }
}

internal sealed class AWNavigationRegionSnapshot
{
    internal AWNavigationRegionSnapshot(int id, int centerTileId, int[] neighbours)
    {
        Id = id;
        CenterTileId = centerTileId;
        Neighbours = neighbours ?? Array.Empty<int>();
    }

    internal int Id { get; }
    internal int CenterTileId { get; }
    internal int[] Neighbours { get; }
}

/// <summary>
/// 鍖哄煙绾ц矾寰勬嫇鎵戙€傚師鐗堥噸绠楀尯鍩熷悗鏁翠綋鏇挎崲锛屽伐浣滅嚎绋嬫寔鏈夌殑鏃х増鏈缁堝彧璇汇€?
/// </summary>
internal sealed class AWNavigationRegionTopology
{
    private readonly Dictionary<int, AWNavigationRegionSnapshot> regions;

    private AWNavigationRegionTopology(long generation, int revision, Dictionary<int, AWNavigationRegionSnapshot> regions)
    {
        Generation = generation;
        Revision = revision;
        this.regions = regions ?? new Dictionary<int, AWNavigationRegionSnapshot>();
    }

    internal long Generation { get; }
    internal int Revision { get; }

    internal bool TryGetRegion(int regionId, out AWNavigationRegionSnapshot region)
    {
        return regions.TryGetValue(regionId, out region);
    }

    internal static AWNavigationRegionTopology Capture(MapBox world, WorldTile[] tiles, int generation, int revision)
    {
        MapChunk[] chunks = world?.map_chunk_manager?.chunks;
        if (chunks != null && chunks.Length > 0)
        {
            var chunkRegions = new Dictionary<int, AWNavigationRegionSnapshot>();
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                MapChunk chunk = chunks[chunkIndex];
                if (chunk?.regions == null) continue;
                for (int regionIndex = 0; regionIndex < chunk.regions.Count; regionIndex++)
                {
                    MapRegion region = chunk.regions[regionIndex];
                    if (region == null || chunkRegions.ContainsKey(region.id)) continue;
                    chunkRegions.Add(region.id, CaptureRegion(region));
                }
            }

            if (chunkRegions.Count > 0)
            {
                return new AWNavigationRegionTopology(generation, revision, chunkRegions);
            }
        }

        var liveRegions = new Dictionary<int, MapRegion>();
        if (tiles != null)
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                MapRegion region = tiles[i]?.region;
                if (region != null && !liveRegions.ContainsKey(region.id))
                {
                    liveRegions.Add(region.id, region);
                }
            }
        }

        var result = new Dictionary<int, AWNavigationRegionSnapshot>(liveRegions.Count);
        foreach (KeyValuePair<int, MapRegion> pair in liveRegions)
        {
            result.Add(pair.Key, CaptureRegion(pair.Value));
        }

        return new AWNavigationRegionTopology(generation, revision, result);
    }

    internal static AWNavigationRegionTopology Capture(AWTraversalGeneration generation,
        int revision)
    {
        var result = new Dictionary<int, AWNavigationRegionSnapshot>();
        if (generation?.RegionTopology == null)
            return new AWNavigationRegionTopology(generation?.WorldGeneration ?? 0L,
                revision, result);

        // The traversal topology is already immutable and worker-safe. Copy
        // only the small region adjacency graph instead of touching live map
        // chunks during bootstrap.
        for (int tileId = 0; tileId < generation.TileCount; tileId++)
        {
            if (!generation.TryGet(tileId, out AWTileTraversalSnapshot tile) ||
                tile.RegionId < 0 || result.ContainsKey(tile.RegionId)) continue;
            if (!generation.RegionTopology.TryGetRegion(tile.RegionId,
                    out AWRegionNode region)) continue;
            result.Add(region.Id, new AWNavigationRegionSnapshot(region.Id,
                region.CenterTileId, region.Neighbours));
        }

        return new AWNavigationRegionTopology(generation.WorldGeneration,
            revision, result);
    }

    private static AWNavigationRegionSnapshot CaptureRegion(MapRegion region)
    {
        var neighbours = new List<int>(region.neighbours?.Count ?? 0);
        if (region.neighbours != null)
        {
            for (int i = 0; i < region.neighbours.Count; i++)
            {
                MapRegion neighbour = region.neighbours[i];
                if (neighbour != null) neighbours.Add(neighbour.id);
            }
        }

        return new AWNavigationRegionSnapshot(region.id, ResolveRegionCenterTile(region), neighbours.ToArray());
    }

    private static int ResolveRegionCenterTile(MapRegion region)
    {
        if (region?.tiles == null || region.tiles.Count == 0)
        {
            return -1;
        }

        for (int i = 0; i < region.tiles.Count; i++)
        {
            WorldTile tile = region.tiles[i];
            if (tile?.data != null) return tile.data.tile_id;
        }

        return -1;
    }
}

/// <summary>
/// 涓栫晫绾х揣鍑戝鑸紦瀛樸€傚湴鍧楀瓧娈电敱妯℃嫙绾跨▼鍘熷瓙鍙戝竷锛屽璺嚎绋嬪彧璇诲彇鏍囬噺鏁扮粍銆?
/// </summary>
internal sealed class AWPathNavigationGrid
{
    internal const float DiagonalDistance = 1.41421356f;

    private static int nextIdentity;
    private readonly int[] flags;
    private readonly float[] damage;
    private readonly float[] walkMultipliers;
    private readonly int[] regionIds;
    private AWNavigationRegionTopology topology;
    private int topologyRevision;
    private long revision;

    private AWPathNavigationGrid(AWPathWorldKey worldKey, int width, int height, int tileCount)
    {
        Identity = Interlocked.Increment(ref nextIdentity);
        WorldKey = worldKey;
        Generation = worldKey.Generation;
        Width = width;
        Height = height;
        TileCount = tileCount;
        MaxWalkMultiplier = 1f;
        flags = new int[tileCount];
        damage = new float[tileCount];
        walkMultipliers = new float[tileCount];
        regionIds = new int[tileCount];
        revision = 1;
    }

    internal int Identity { get; }
    internal AWPathWorldKey WorldKey { get; }
    internal long Generation { get; }
    internal long Revision => Volatile.Read(ref revision);
    internal int Width { get; }
    internal int Height { get; }
    internal int TileCount { get; }
    internal float MaxWalkMultiplier { get; private set; }
    internal AWNavigationRegionTopology Topology => Volatile.Read(ref topology);

    internal bool MatchesCurrentWorld(WorldTile[] tiles)
    {
        return WorldKey.Kind == AWPathWorldKind.MainWorld &&
               tiles != null && tiles.Length == TileCount && Width == MapBox.width && Height == MapBox.height &&
               Generation == AWSimulationTime.Generation;
    }

    internal bool TryGetTile(int tileId, out AWNavigationTileSnapshot tile)
    {
        if ((uint)tileId >= (uint)TileCount)
        {
            tile = default;
            return false;
        }

        AWNavigationTileFlags currentFlags = (AWNavigationTileFlags)Volatile.Read(ref flags[tileId]);
        if ((currentFlags & AWNavigationTileFlags.Exists) == 0)
        {
            tile = default;
            return false;
        }

        tile = new AWNavigationTileSnapshot(
            currentFlags,
            Volatile.Read(ref damage[tileId]),
            Volatile.Read(ref walkMultipliers[tileId]),
            Volatile.Read(ref regionIds[tileId]));
        return true;
    }

    internal bool TryGetTileAt(int x, int y, out int tileId, out AWNavigationTileSnapshot tile)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            tileId = -1;
            tile = default;
            return false;
        }

        tileId = x + y * Width;
        return TryGetTile(tileId, out tile);
    }

    internal int XOf(int tileId)
    {
        return tileId % Width;
    }

    internal int YOf(int tileId)
    {
        return tileId / Width;
    }

    internal int ManhattanDistance(int firstTileId, int secondTileId)
    {
        return Math.Abs(XOf(firstTileId) - XOf(secondTileId)) +
               Math.Abs(YOf(firstTileId) - YOf(secondTileId));
    }

    internal float OctileDistance(int firstTileId, int secondTileId)
    {
        int dx = Math.Abs(XOf(firstTileId) - XOf(secondTileId));
        int dy = Math.Abs(YOf(firstTileId) - YOf(secondTileId));
        int diagonal = Math.Min(dx, dy);
        return Math.Max(dx, dy) + (DiagonalDistance - 1f) * diagonal;
    }

    /// <summary>浠庣函鏍囬噺 tile 蹇収鍒涘缓闈炰富涓栫晫瀵艰埅缃戞牸銆?/summary>
    internal static AWPathNavigationGrid Create(AWPathWorldKey worldKey, int width, int height,
        IReadOnlyList<AWNavigationTileSnapshot> tiles, long revision = 1)
    {
        int tileCount = checked(width * height);
        if (width <= 0 || height <= 0 || tiles == null || tiles.Count != tileCount)
        {
            throw new ArgumentException("瀵昏矾缃戞牸灏哄鎴?tile 鏁伴噺鏃犳晥", nameof(tiles));
        }

        var grid = new AWPathNavigationGrid(worldKey, width, height, tileCount);
        for (int i = 0; i < tileCount; i++)
        {
            AWNavigationTileSnapshot tile = tiles[i];
            if (tile.WalkMultiplier > grid.MaxWalkMultiplier) grid.MaxWalkMultiplier = tile.WalkMultiplier;
            grid.WriteTile(i, tile);
        }
        grid.revision = Math.Max(1L, revision);
        return grid;
    }

    internal static AWPathNavigationGrid Capture(MapBox world, int generation)
    {
        WorldTile[] tiles = world?.tiles_list;
        int width = MapBox.width;
        int height = MapBox.height;
        if (tiles == null || tiles.Length == 0 || width <= 0 || height <= 0)
        {
            return null;
        }

        var grid = new AWPathNavigationGrid(AWPathWorldKey.MainWorld(generation), width, height, tiles.Length);
        for (int i = 0; i < tiles.Length; i++)
        {
            grid.WriteTile(i, AWNavigationTileSnapshot.Capture(tiles[i]));
        }

        grid.topologyRevision = 1;
        grid.topology = AWNavigationRegionTopology.Capture(world, tiles, generation, grid.topologyRevision);
        return grid;
    }

    internal static AWPathNavigationGrid Capture(AWTraversalGeneration generation)
    {
        if (generation == null || generation.TileCount <= 0 ||
            generation.Width <= 0 || generation.Height <= 0) return null;

        var tiles = new AWNavigationTileSnapshot[generation.TileCount];
        for (int i = 0; i < tiles.Length; i++)
        {
            generation.TryGet(i, out AWTileTraversalSnapshot tile);
            tiles[i] = AWNavigationTileSnapshot.Capture(tile);
        }

        AWPathNavigationGrid grid = Create(
            AWPathWorldKey.MainWorld(generation.WorldGeneration),
            generation.Width, generation.Height, tiles, generation.Id);
        grid.topologyRevision = Math.Max(1, generation.RegionTopology?.Revision ?? 1);
        grid.topology = AWNavigationRegionTopology.Capture(generation,
            grid.topologyRevision);
        return grid;
    }

    /// <summary>
    /// Creates a worker-safe traversal snapshot for a non-main-world grid.
    /// The snapshot deliberately contains only navigation data; it does not
    /// create or attach any WorldBox/SubWorld gameplay objects.
    /// </summary>
    internal AWTraversalGeneration CreateTraversalGeneration(int pGenerationId = 0)
    {
        var snapshots = new AWTileTraversalSnapshot[TileCount];
        for (int tileId = 0; tileId < TileCount; tileId++)
        {
            if (!TryGetTile(tileId, out AWNavigationTileSnapshot tile))
                continue;

            int x = XOf(tileId);
            int y = YOf(tileId);
            snapshots[tileId] = new AWTileTraversalSnapshot(
                tile.Exists ? tileId : -1,
                x,
                y,
                ground: !tile.Liquid && !tile.Ocean,
                block: tile.Block,
                liquid: tile.Liquid,
                ocean: tile.Ocean,
                lava: tile.Lava,
                fire: tile.IsOnFire,
                damageUnits: tile.DamageUnits,
                terrainDamage: tile.Damage,
                walkMultiplier: tile.WalkMultiplier,
                goodForBoat: tile.Ocean,
                regionId: tile.RegionId);
        }

        return AWTraversalGeneration.FromTiles(
            pGenerationId,
            Width,
            Height,
            snapshots,
            AWTraversalGeneration.DefaultChunkSize,
            WorldKey.Generation);
    }

    internal void UpdateTiles(WorldTile[] worldTiles, IReadOnlyList<int> dirtyTileIds)
    {
        if (worldTiles == null || worldTiles.Length != TileCount || dirtyTileIds == null || dirtyTileIds.Count == 0)
        {
            return;
        }

        for (int i = 0; i < dirtyTileIds.Count; i++)
        {
            int tileId = dirtyTileIds[i];
            if ((uint)tileId >= (uint)TileCount) continue;
            WriteTile(tileId, AWNavigationTileSnapshot.Capture(worldTiles[tileId]));
        }

        Interlocked.Increment(ref revision);
    }

    internal void ReplaceTopology(MapBox world)
    {
        int nextTopologyRevision = Interlocked.Increment(ref topologyRevision);
        AWNavigationRegionTopology next = AWNavigationRegionTopology.Capture(world, world?.tiles_list, checked((int)Generation),
            nextTopologyRevision);
        Volatile.Write(ref topology, next);
        Interlocked.Increment(ref revision);
    }

    private void WriteTile(int tileId, AWNavigationTileSnapshot tile)
    {
        // flags 鏈€鍚庡彂甯冿紱璇诲埌鏂?flags 鐨勭嚎绋嬩篃鑳界湅鍒版鍓嶅啓鍏ョ殑鎴愭湰鍜屽尯鍩熸暟鎹€?
        Volatile.Write(ref damage[tileId], tile.Damage);
        Volatile.Write(ref walkMultipliers[tileId], tile.WalkMultiplier);
        Volatile.Write(ref regionIds[tileId], tile.RegionId);
        Volatile.Write(ref flags[tileId], (int)tile.Flags);
    }
}

/// <summary>
/// 鍦ㄦā鎷熺嚎绋嬬淮鎶ゅ鑸紦瀛橈紱宸ヤ綔绾跨▼涓嶄細鎺ヨЕ瀹炴椂 WorldTile銆丮apRegion 鎴栫伀鐒版暟缁勩€?
/// </summary>
internal static class AWPathNavigationGridService
{
    private static readonly object DirtySync = new();
    private static readonly HashSet<int> DirtyTiles = new();
    private static readonly Dictionary<AWPathWorldKey, AWPathNavigationGrid>
        Grids = new();
    private static AWPathNavigationGrid current;
    private static bool topologyDirty;

    internal static AWPathNavigationGrid Current => Volatile.Read(ref current);

    internal static AWPathNavigationGrid Get(AWPathWorldKey pWorldKey)
    {
        if (pWorldKey.Kind == AWPathWorldKind.MainWorld)
        {
            AWPathNavigationGrid main = Current;
            return main != null && main.WorldKey == pWorldKey ? main : null;
        }
        lock (DirtySync)
        {
            return Grids.TryGetValue(pWorldKey,
                out AWPathNavigationGrid grid) ? grid : null;
        }
    }

    internal static void Register(AWPathNavigationGrid pGrid)
    {
        if (pGrid == null) throw new ArgumentNullException(nameof(pGrid));
        if (pGrid.WorldKey.Kind == AWPathWorldKind.MainWorld)
        {
            Volatile.Write(ref current, pGrid);
            return;
        }

        lock (DirtySync) Grids[pGrid.WorldKey] = pGrid;
    }

    internal static bool Remove(AWPathWorldKey pWorldKey)
    {
        if (pWorldKey.Kind == AWPathWorldKind.MainWorld) return false;
        lock (DirtySync) return Grids.Remove(pWorldKey);
    }

    internal static void BuildForCurrentWorld()
    {
        MapBox world = World.world;
        if (world?.tiles_list == null || world.tiles_list.Length == 0)
        {
            Clear();
            return;
        }

        AWPathNavigationGrid grid = AWPathNavigationGrid.Capture(world, AWSimulationTime.Generation);
        Register(grid);
        lock (DirtySync)
        {
            DirtyTiles.Clear();
            topologyDirty = false;
        }
    }

    internal static void BuildFromTraversal(AWTraversalGeneration generation)
    {
        AWPathNavigationGrid grid = AWPathNavigationGrid.Capture(generation);
        if (grid == null) return;
        Register(grid);
        lock (DirtySync)
        {
            DirtyTiles.Clear();
            topologyDirty = false;
        }
    }

    internal static void MarkDirty(WorldTile tile)
    {
        int tileId = tile?.data?.tile_id ?? -1;
        if (tileId < 0) return;
        lock (DirtySync)
        {
            DirtyTiles.Add(tileId);
        }
    }

    internal static void MarkTopologyDirty(IEnumerable<MapChunk> chunks)
    {
        if (chunks == null) return;
        lock (DirtySync)
        {
            foreach (MapChunk chunk in chunks)
            {
                if (chunk?.tiles == null) continue;
                for (int i = 0; i < chunk.tiles.Length; i++)
                {
                    int tileId = chunk.tiles[i]?.data?.tile_id ?? -1;
                    if (tileId >= 0) DirtyTiles.Add(tileId);
                }
            }

            topologyDirty = true;
        }
    }

    internal static void FlushDirty()
    {
        AWPathNavigationGrid grid = Current;
        MapBox world = World.world;
        WorldTile[] worldTiles = world?.tiles_list;
        if (grid == null || !grid.MatchesCurrentWorld(worldTiles))
        {
            BuildForCurrentWorld();
            return;
        }

        List<int> dirty;
        bool refreshTopology;
        lock (DirtySync)
        {
            if (DirtyTiles.Count == 0 && !topologyDirty) return;
            dirty = new List<int>(DirtyTiles);
            DirtyTiles.Clear();
            refreshTopology = topologyDirty;
            topologyDirty = false;
        }

        grid.UpdateTiles(worldTiles, dirty);
        if (refreshTopology)
        {
            grid.ReplaceTopology(world);
        }
    }

    internal static void Clear()
    {
        Volatile.Write(ref current, null);
        lock (DirtySync)
        {
            Grids.Clear();
            DirtyTiles.Clear();
            topologyDirty = false;
        }
    }
}
