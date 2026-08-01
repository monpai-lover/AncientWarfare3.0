using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class HierarchicalVassalKingdomSnapshot
    {
        private readonly List<long> _directVassalIds = new List<long>();
        private readonly List<Vector2Int> _landTiles =
            new List<Vector2Int>();
        private readonly List<TileZone> _drawableZones =
            new List<TileZone>();
        private readonly ReadOnlyCollection<long> _readOnlyDirectVassalIds;
        private readonly ReadOnlyCollection<Vector2Int> _readOnlyLandTiles;

        public HierarchicalVassalKingdomSnapshot()
        {
            _readOnlyDirectVassalIds = _directVassalIds.AsReadOnly();
            _readOnlyLandTiles = _landTiles.AsReadOnly();
        }

        public long KingdomId { get; internal set; } = -1L;
        public KingdomTitle Title { get; internal set; } = KingdomTitle.Baron;
        public long SuzerainId { get; internal set; } = -1L;
        public IReadOnlyList<long> DirectVassalIds =>
            _readOnlyDirectVassalIds;
        public IReadOnlyList<Vector2Int> LandTiles => _readOnlyLandTiles;
        internal List<TileZone> DrawableZones => _drawableZones;
        public int Area { get; internal set; } = 0;
        public Vector2 Centroid { get; internal set; } = new Vector2(0f, 0f);
        public float LabelSize { get; internal set; } =
            HierarchicalVassalMapModeRules.MinimumLabelSize;
        public float LabelAngle { get; internal set; }
        public string ColorKey { get; internal set; } = string.Empty;
        public string DisplayName { get; internal set; } = string.Empty;
        public string LabelDisplayName { get; internal set; } = string.Empty;
        public int CountryLabelGap { get; internal set; }

        internal void AddDirectVassal(long pKingdomId)
        {
            _directVassalIds.Add(pKingdomId);
        }

        internal bool AddLandTile(Vector2Int pTile)
        {
            _landTiles.Add(pTile);
            return true;
        }

        internal void AddDrawableZone(TileZone pZone)
        {
            if (pZone != null && !_drawableZones.Contains(pZone))
                _drawableZones.Add(pZone);
        }

        internal void SortLandTiles(Comparison<Vector2Int> pComparison)
        {
            _landTiles.Sort(pComparison);
        }
    }

    internal sealed class HierarchicalVassalMapModeSnapshot
    {
        private readonly List<HierarchicalVassalKingdomSnapshot> _rootEntries =
            new List<HierarchicalVassalKingdomSnapshot>();
        private readonly List<HierarchicalVassalKingdomSnapshot>
            _focusedEntries = new List<HierarchicalVassalKingdomSnapshot>();
        private readonly Dictionary<int, long> _zoneToKingdomId =
            new Dictionary<int, long>();
        private readonly List<TileZone> _drawableZones =
            new List<TileZone>();
        private readonly ReadOnlyCollection<HierarchicalVassalKingdomSnapshot>
            _readOnlyRootEntries;
        private readonly ReadOnlyCollection<HierarchicalVassalKingdomSnapshot>
            _readOnlyFocusedEntries;
        private readonly ReadOnlyDictionary<int, long>
            _readOnlyZoneToKingdomId;
        private readonly ReadOnlyCollection<TileZone> _readOnlyDrawableZones;

        public HierarchicalVassalMapModeSnapshot()
        {
            _readOnlyRootEntries = _rootEntries.AsReadOnly();
            _readOnlyFocusedEntries = _focusedEntries.AsReadOnly();
            _readOnlyZoneToKingdomId =
                new ReadOnlyDictionary<int, long>(_zoneToKingdomId);
            _readOnlyDrawableZones = _drawableZones.AsReadOnly();
        }

        public IReadOnlyList<HierarchicalVassalKingdomSnapshot> RootEntries =>
            _readOnlyRootEntries;
        public IReadOnlyList<HierarchicalVassalKingdomSnapshot>
            FocusedEntries => _readOnlyFocusedEntries;
        public IReadOnlyDictionary<int, long> ZoneToKingdomId =>
            _readOnlyZoneToKingdomId;
        public IReadOnlyList<TileZone> DrawableZones => _readOnlyDrawableZones;
        public long FocusKingdomId { get; internal set; } = -1L;
        public bool IsFocused => FocusKingdomId >= 0L;

        public IReadOnlyList<HierarchicalVassalKingdomSnapshot> Entries =>
            IsFocused ? FocusedEntries : RootEntries;

        internal void AddRootEntry(
            HierarchicalVassalKingdomSnapshot pEntry)
        {
            if (pEntry != null) _rootEntries.Add(pEntry);
        }

        internal void AddFocusedEntry(
            HierarchicalVassalKingdomSnapshot pEntry)
        {
            if (pEntry != null) _focusedEntries.Add(pEntry);
        }

        internal void MapZone(int pZoneId, long pKingdomId)
        {
            if (pZoneId >= 0) _zoneToKingdomId[pZoneId] = pKingdomId;
        }

        internal void AddDrawableZone(TileZone pZone)
        {
            if (pZone != null && !_drawableZones.Contains(pZone))
                _drawableZones.Add(pZone);
        }
    }
}
