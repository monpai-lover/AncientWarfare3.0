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
        private readonly List<Vector2Int> _landTiles = new List<Vector2Int>();
        private readonly List<TileZone> _drawableZones = new List<TileZone>();

        public long KingdomId { get; internal set; } = -1L;
        public KingdomTitle Title { get; internal set; } = KingdomTitle.Baron;
        public long SuzerainId { get; internal set; } = -1L;
        public IReadOnlyList<long> DirectVassalIds =>
            _directVassalIds.AsReadOnly();
        public IReadOnlyList<Vector2Int> LandTiles => _landTiles.AsReadOnly();
        internal List<TileZone> DrawableZones => _drawableZones;
        public int Area { get; internal set; }
        public Vector2 Centroid { get; internal set; }
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

    internal readonly struct HierarchicalVassalBoundaryZoneFacts
    {
        public HierarchicalVassalBoundaryZoneFacts(
            long pSystemId, long pRealmId, long pCityId, uint pRootRgba)
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

    internal sealed class HierarchicalVassalMapModeSnapshot
    {
        private readonly List<HierarchicalVassalKingdomSnapshot> _rootEntries =
            new List<HierarchicalVassalKingdomSnapshot>();
        private readonly List<HierarchicalVassalKingdomSnapshot> _focusedEntries =
            new List<HierarchicalVassalKingdomSnapshot>();
        private readonly Dictionary<int, long> _zoneToKingdomId =
            new Dictionary<int, long>();
        private readonly Dictionary<int, HierarchicalVassalBoundaryZoneFacts>
            _boundaryFactsByZone =
                new Dictionary<int, HierarchicalVassalBoundaryZoneFacts>();
        private readonly List<TileZone> _drawableZones = new List<TileZone>();
        private HierarchyColorIdentity[] _colorIdentities =
            Array.Empty<HierarchyColorIdentity>();
        private HierarchyColorEdge[] _colorEdges =
            Array.Empty<HierarchyColorEdge>();
        private HierarchyColorAssignment _colorAssignment =
            HierarchicalVassalBoundaryColorRules.BuildCanonicalAssignment(
                Array.Empty<HierarchyColorIdentity>(),
                Array.Empty<HierarchyColorEdge>());

        public IReadOnlyList<HierarchicalVassalKingdomSnapshot> RootEntries =>
            _rootEntries.AsReadOnly();
        public IReadOnlyList<HierarchicalVassalKingdomSnapshot> FocusedEntries =>
            _focusedEntries.AsReadOnly();
        public IReadOnlyDictionary<int, long> ZoneToKingdomId =>
            new ReadOnlyDictionary<int, long>(_zoneToKingdomId);
        public IReadOnlyList<TileZone> DrawableZones => _drawableZones.AsReadOnly();
        public IReadOnlyList<HierarchyColorIdentity> BoundaryColorIdentities =>
            Array.AsReadOnly(_colorIdentities);
        public IReadOnlyList<HierarchyColorEdge> BoundaryColorEdges =>
            Array.AsReadOnly(_colorEdges);
        internal HierarchyColorAssignment BoundaryColorAssignment =>
            _colorAssignment;
        public long FocusKingdomId { get; internal set; } = -1L;
        public bool IsFocused => FocusKingdomId >= 0L;
        public IReadOnlyList<HierarchicalVassalKingdomSnapshot> Entries =>
            IsFocused ? FocusedEntries : RootEntries;

        internal void AddRootEntry(HierarchicalVassalKingdomSnapshot pEntry)
        {
            if (pEntry != null) _rootEntries.Add(pEntry);
        }

        internal void AddFocusedEntry(HierarchicalVassalKingdomSnapshot pEntry)
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

        internal void SetBoundaryColorInputs(
            IReadOnlyList<HierarchyColorIdentity> pIdentities,
            IReadOnlyList<HierarchyColorEdge> pEdges)
        {
            _colorIdentities = Copy(pIdentities);
            _colorEdges = Copy(pEdges);
            _colorAssignment =
                HierarchicalVassalBoundaryColorRules.BuildCanonicalAssignment(
                    _colorIdentities, _colorEdges);
        }

        internal bool TryGetBoundaryCellFacts(int pZoneId,
            BoundaryDisplayLayer pLayer, out long pSystemId,
            out long pRealmId, out long pCityId, out uint pRgba)
        {
            pSystemId = -1L;
            pRealmId = -1L;
            pCityId = -1L;
            pRgba = 0u;
            if (pZoneId < 0 ||
                !_boundaryFactsByZone.TryGetValue(pZoneId,
                    out HierarchicalVassalBoundaryZoneFacts facts))
                return false;

            pSystemId = facts.SystemId;
            pRealmId = facts.RealmId;
            pCityId = facts.CityId;
            BoundaryTier tier;
            long displayedOwnerId;
            if (pLayer == BoundaryDisplayLayer.Cities)
            {
                tier = BoundaryTier.City;
                displayedOwnerId = facts.CityId;
            }
            else
            {
                displayedOwnerId = facts.RealmId >= 0L
                    ? facts.RealmId
                    : facts.SystemId;
                tier = displayedOwnerId == facts.SystemId
                    ? BoundaryTier.SuzerainSystem
                    : BoundaryTier.VassalRealm;
            }
            if (displayedOwnerId < 0L) return true;
            if (_colorAssignment != null && _colorAssignment.IsValid &&
                _colorAssignment.TryGetColor(tier, displayedOwnerId,
                    out pRgba)) return true;

            pRgba = HierarchicalVassalBoundaryColorRules.CandidateColor(
                new HierarchyColorIdentity(tier, displayedOwnerId,
                    facts.SystemId, facts.SystemId, facts.RealmId,
                    facts.CityId, facts.RootRgba), 0);
            return true;
        }

        internal void AddDrawableZone(TileZone pZone)
        {
            if (pZone != null && !_drawableZones.Contains(pZone))
                _drawableZones.Add(pZone);
        }

        private static HierarchyColorIdentity[] Copy(
            IReadOnlyList<HierarchyColorIdentity> pValues)
        {
            if (pValues == null) return Array.Empty<HierarchyColorIdentity>();
            var result = new HierarchyColorIdentity[pValues.Count];
            for (int i = 0; i < result.Length; i++) result[i] = pValues[i];
            return result;
        }

        private static HierarchyColorEdge[] Copy(
            IReadOnlyList<HierarchyColorEdge> pValues)
        {
            if (pValues == null) return Array.Empty<HierarchyColorEdge>();
            var result = new HierarchyColorEdge[pValues.Count];
            for (int i = 0; i < result.Length; i++) result[i] = pValues[i];
            return result;
        }
    }
}
