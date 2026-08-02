using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal readonly struct HierarchicalVassalBoundaryZoneFacts
    {
        public HierarchicalVassalBoundaryZoneFacts(long pSystemId,
            long pRealmId, long pCityId, uint pRootRgba)
        {
            SystemId = pSystemId;
            RealmId = pRealmId;
            CityId = pCityId;
            RootRgba = pRootRgba;
        }

        public long SystemId { get; }
        public long RealmId { get; }
        public long CityId { get; }
        public uint RootRgba { get; }
    }

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
        private readonly Dictionary<int, HierarchicalVassalBoundaryZoneFacts>
            _boundaryFactsByZone =
                new Dictionary<int, HierarchicalVassalBoundaryZoneFacts>();
        private readonly List<HierarchyColorIdentity> _boundaryColorIdentities =
            new List<HierarchyColorIdentity>();
        private readonly List<HierarchyColorEdge> _boundaryColorEdges =
            new List<HierarchyColorEdge>();
        private readonly ReadOnlyCollection<HierarchicalVassalKingdomSnapshot>
            _readOnlyRootEntries;
        private readonly ReadOnlyCollection<HierarchicalVassalKingdomSnapshot>
            _readOnlyFocusedEntries;
        private readonly ReadOnlyDictionary<int, long>
            _readOnlyZoneToKingdomId;
        private readonly ReadOnlyCollection<TileZone> _readOnlyDrawableZones;
        private readonly ReadOnlyDictionary<int, HierarchicalVassalBoundaryZoneFacts>
            _readOnlyBoundaryFactsByZone;
        private readonly ReadOnlyCollection<HierarchyColorIdentity>
            _readOnlyBoundaryColorIdentities;
        private readonly ReadOnlyCollection<HierarchyColorEdge>
            _readOnlyBoundaryColorEdges;
        private HierarchyColorAssignment _boundaryColorAssignment;

        public HierarchicalVassalMapModeSnapshot()
        {
            _readOnlyRootEntries = _rootEntries.AsReadOnly();
            _readOnlyFocusedEntries = _focusedEntries.AsReadOnly();
            _readOnlyZoneToKingdomId =
                new ReadOnlyDictionary<int, long>(_zoneToKingdomId);
            _readOnlyDrawableZones = _drawableZones.AsReadOnly();
            _readOnlyBoundaryFactsByZone =
                new ReadOnlyDictionary<int, HierarchicalVassalBoundaryZoneFacts>(
                    _boundaryFactsByZone);
            _readOnlyBoundaryColorIdentities =
                _boundaryColorIdentities.AsReadOnly();
            _readOnlyBoundaryColorEdges = _boundaryColorEdges.AsReadOnly();
            _boundaryColorAssignment =
                HierarchicalVassalBoundaryColorRules.BuildCanonicalAssignment(
                    _readOnlyBoundaryColorIdentities,
                    _readOnlyBoundaryColorEdges);
        }

        public IReadOnlyList<HierarchicalVassalKingdomSnapshot> RootEntries =>
            _readOnlyRootEntries;
        public IReadOnlyList<HierarchicalVassalKingdomSnapshot>
            FocusedEntries => _readOnlyFocusedEntries;
        public IReadOnlyDictionary<int, long> ZoneToKingdomId =>
            _readOnlyZoneToKingdomId;
        public IReadOnlyList<TileZone> DrawableZones => _readOnlyDrawableZones;
        internal IReadOnlyDictionary<int, HierarchicalVassalBoundaryZoneFacts>
            BoundaryFactsByZone => _readOnlyBoundaryFactsByZone;
        internal IReadOnlyList<HierarchyColorIdentity>
            BoundaryColorIdentities => _readOnlyBoundaryColorIdentities;
        internal IReadOnlyList<HierarchyColorEdge> BoundaryColorEdges =>
            _readOnlyBoundaryColorEdges;
        internal HierarchyColorAssignment BoundaryColorAssignment =>
            _boundaryColorAssignment;
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

        internal void MapBoundaryZone(int pZoneId, long pSystemId,
            long pRealmId, long pCityId, uint pRootRgba)
        {
            if (pZoneId < 0) return;
            _boundaryFactsByZone[pZoneId] =
                new HierarchicalVassalBoundaryZoneFacts(
                    pSystemId, pRealmId, pCityId, pRootRgba);
        }

        internal bool TryGetBoundaryZoneFacts(int pZoneId,
            out HierarchicalVassalBoundaryZoneFacts pFacts)
        {
            if (pZoneId >= 0 &&
                _boundaryFactsByZone.TryGetValue(pZoneId, out pFacts))
                return true;
            pFacts = default(HierarchicalVassalBoundaryZoneFacts);
            return false;
        }

        internal bool TryGetBoundaryCellFacts(int pZoneId,
            BoundaryDisplayLayer pLayer, out long pSystemId,
            out long pRealmId, out long pCityId, out uint pRgba)
        {
            pSystemId = -1L;
            pRealmId = -1L;
            pCityId = -1L;
            pRgba = 0u;
            if (!TryGetBoundaryZoneFacts(pZoneId,
                    out HierarchicalVassalBoundaryZoneFacts facts))
                return false;
            pSystemId = facts.SystemId;
            pRealmId = facts.RealmId;
            pCityId = facts.CityId;
            BoundaryTier tier;
            long ownerId;
            if (pLayer == BoundaryDisplayLayer.Cities)
            {
                tier = BoundaryTier.City;
                ownerId = facts.CityId;
            }
            else
            {
                ownerId = facts.RealmId >= 0L
                    ? facts.RealmId
                    : facts.SystemId;
                tier = ownerId == facts.SystemId
                    ? BoundaryTier.SuzerainSystem
                    : BoundaryTier.VassalRealm;
            }
            if (ownerId >= 0L && _boundaryColorAssignment.IsValid)
                _boundaryColorAssignment.TryGetColor(tier, ownerId,
                    out pRgba);
            if (pRgba == 0u) pRgba = facts.RootRgba;
            return true;
        }

        internal void SetBoundaryColorInputs(
            IReadOnlyList<HierarchyColorIdentity> pIdentities,
            IReadOnlyList<HierarchyColorEdge> pEdges)
        {
            _boundaryColorIdentities.Clear();
            _boundaryColorEdges.Clear();
            if (pIdentities != null)
                for (int i = 0; i < pIdentities.Count; i++)
                    _boundaryColorIdentities.Add(pIdentities[i]);
            if (pEdges != null)
                for (int i = 0; i < pEdges.Count; i++)
                    _boundaryColorEdges.Add(pEdges[i]);
            _boundaryColorAssignment =
                HierarchicalVassalBoundaryColorRules.BuildCanonicalAssignment(
                    _readOnlyBoundaryColorIdentities,
                    _readOnlyBoundaryColorEdges);
        }

        internal void AddDrawableZone(TileZone pZone)
        {
            if (pZone != null && !_drawableZones.Contains(pZone))
                _drawableZones.Add(pZone);
        }
    }
}
