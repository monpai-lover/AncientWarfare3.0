using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapModeBoundaryLayer
    {
        private const float BoundaryZ = -0.18f;
        private const float BoundaryWidth = 0.22f;
        // Keep the presentation boundary aligned with the actual zone edge.
        // The former outward bow visibly crossed rivers and city interiors.
        private const float CurveOffset = 0f;
        private const int CurveSamples = 2;
        private const int MaximumSegments = 5000;

        private static readonly List<LineRenderer> Lines =
            new List<LineRenderer>();
        private static GameObject _root;
        private static Material _material;
        private static HierarchicalVassalMapModeSnapshot _snapshot;
        private static bool _minimapHidden;

        internal static void ProcessFrame()
        {
            try
            {
                if (!Config.game_loaded ||
                    !HierarchicalVassalMapModeService.IsActive())
                {
                    SetRootActive(false);
                    _snapshot = null;
                    return;
                }

                if (_minimapHidden) return;

                // City borders belong to the city layer only.  Keeping this
                // root disabled at the country layer also keeps them out of
                // the minimap's world-space capture.
                if (!HierarchicalVassalMapModeService.IsCityLayer)
                {
                    SetRootActive(false);
                    return;
                }

                EnsureRoot();
                SetRootActive(true);
                HierarchicalVassalMapModeSnapshot snapshot =
                    HierarchicalVassalMapModeService.BuildVisibleSnapshot();
                if (!ReferenceEquals(_snapshot, snapshot))
                {
                    _snapshot = snapshot;
                    Rebuild(snapshot);
                }
            }
            catch
            {
                SetRootActive(false);
            }
        }

        internal static void Reset()
        {
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
                else UnityEngine.Object.DestroyImmediate(_root);
            }
            _root = null;
            _snapshot = null;
            _minimapHidden = false;
            Lines.Clear();
            if (_material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_material);
                else UnityEngine.Object.DestroyImmediate(_material);
            }
            _material = null;
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("AW3_HierarchicalVassalCityBoundaries");
            if (World.world != null)
                _root.transform.SetParent(World.world.transform, false);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) _material = new Material(shader);
        }

        private static void Rebuild(
            HierarchicalVassalMapModeSnapshot pSnapshot)
        {
            int lineIndex = 0;
            var seen = new HashSet<long>();
            if (pSnapshot?.DrawableZones != null)
            {
                for (int index = 0;
                     index < pSnapshot.DrawableZones.Count &&
                     lineIndex < MaximumSegments; index++)
                {
                    TileZone zone = pSnapshot.DrawableZones[index];
                    City city = zone?.city;
                    if (city?.data == null || city.isRekt() ||
                        zone.neighbours == null) continue;

                    for (int neighbourIndex = 0;
                         neighbourIndex < zone.neighbours.Length &&
                         neighbourIndex < 4 &&
                         lineIndex < MaximumSegments; neighbourIndex++)
                    {
                        TileZone neighbour = zone.neighbours[neighbourIndex];
                        // A null or all-water neighbour is a coastline/gap,
                        // not a city division. Drawing it creates stray
                        // rectangles over the sea in the city layer.
                        if (neighbour == null ||
                            !HierarchicalVassalMapModeService.ContainsVisibleLand(
                                neighbour)) continue;
                        if (neighbour?.city == city) continue;
                        if (neighbour?.city != null && neighbour.id < zone.id)
                            continue;

                        int direction = ResolveDirection(zone, neighbour,
                            neighbourIndex);
                        if (direction < 0) continue;
                        // A zone can contain both land and water.  Drawing a
                        // full eight-tile edge for such a zone leaks a city
                        // boundary across the water, producing the long
                        // stray lines visible in the city map mode.
                        if (!HasContinuousLandEdge(zone, direction) ||
                            !HasContinuousLandEdge(neighbour,
                                OppositeDirection(direction))) continue;
                        long edgeKey = EncodeEdge(zone, direction);
                        if (!seen.Add(edgeKey)) continue;

                        Vector3 start;
                        Vector3 end;
                        ResolveEdge(zone, direction, out start, out end);
                        UseLine(ref lineIndex, start, end,
                            ResolveColor(city), direction);
                    }
                }
            }

            for (int index = lineIndex; index < Lines.Count; index++)
                Lines[index].enabled = false;
        }

        private static bool HasContinuousLandEdge(TileZone pZone,
            int pDirection)
        {
            if (pZone?.tiles == null || pZone.tiles.Length == 0 ||
                pDirection < 0 || pDirection > 3) return false;

            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            for (int index = 0; index < pZone.tiles.Length; index++)
            {
                WorldTile tile = pZone.tiles[index];
                if (tile == null) continue;
                if (tile.x < minX) minX = tile.x;
                if (tile.x > maxX) maxX = tile.x;
                if (tile.y < minY) minY = tile.y;
                if (tile.y > maxY) maxY = tile.y;
            }
            if (minX == int.MaxValue) return false;

            int edgeCoordinate = pDirection == 0 ? minX :
                pDirection == 1 ? maxX :
                pDirection == 2 ? minY : maxY;
            int edgeTiles = 0;
            for (int index = 0; index < pZone.tiles.Length; index++)
            {
                WorldTile tile = pZone.tiles[index];
                if (tile == null) continue;
                bool onEdge = pDirection == 0 || pDirection == 1
                    ? tile.x == edgeCoordinate
                    : tile.y == edgeCoordinate;
                if (!onEdge) continue;
                edgeTiles++;
                if (!HierarchicalVassalMapModeService.IsVisibleLand(tile))
                    return false;
            }
            return edgeTiles > 0;
        }

        private static int OppositeDirection(int pDirection)
        {
            return pDirection == 0 ? 1 :
                pDirection == 1 ? 0 :
                pDirection == 2 ? 3 : 2;
        }

        private static void UseLine(ref int pIndex, Vector3 pStart,
            Vector3 pEnd, Color pColor, int pDirection)
        {
            LineRenderer line;
            if (pIndex >= Lines.Count)
            {
                GameObject objectForLine = new GameObject(
                    "aw3_city_boundary_" + pIndex);
                objectForLine.transform.SetParent(_root.transform, false);
                line = objectForLine.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.sharedMaterial = _material;
                line.sortingLayerName = ResolveSortingLayer();
                line.sortingOrder = 902;
                line.startWidth = BoundaryWidth;
                line.endWidth = BoundaryWidth;
                line.numCapVertices = 4;
                line.numCornerVertices = 4;
                Lines.Add(line);
            }
            else
            {
                line = Lines[pIndex];
            }

            Vector2 direction = new Vector2(pEnd.x - pStart.x,
                pEnd.y - pStart.y);
            float length = direction.magnitude;
            Vector2 normal = length < 0.001f
                ? Vector2.zero
                : new Vector2(-direction.y / length,
                    direction.x / length);
            float side = ResolveOutwardCurveSide(pDirection);
            Vector3 control = (pStart + pEnd) * 0.5f +
                new Vector3(normal.x, normal.y, 0f) *
                (CurveOffset * side);
            line.positionCount = CurveSamples;
            for (int sample = 0; sample < CurveSamples; sample++)
            {
                float t = sample / (float)(CurveSamples - 1);
                float inverse = 1f - t;
                line.SetPosition(sample,
                    inverse * inverse * pStart +
                    2f * inverse * t * control +
                    t * t * pEnd);
            }
            line.startColor = pColor;
            line.endColor = pColor;
            line.enabled = true;
            pIndex++;
        }

        private static float ResolveOutwardCurveSide(int pDirection)
        {
            // ResolveEdge emits left/right edges bottom-to-top and bottom/top
            // edges left-to-right.  The sign maps the normal to the outside
            // of the city tile for each neighbour direction.
            switch (pDirection)
            {
                case 0: return 1f;  // neighbour on the left
                case 1: return -1f; // neighbour on the right
                case 2: return -1f; // neighbour below
                default: return 1f; // neighbour above
            }
        }

        private static int ResolveDirection(TileZone pZone,
            TileZone pNeighbour, int pFallbackDirection)
        {
            if (pZone == null) return -1;
            if (pNeighbour == null)
                return pFallbackDirection >= 0 && pFallbackDirection < 4
                    ? pFallbackDirection
                    : -1;
            if (pNeighbour.x < pZone.x) return 0;
            if (pNeighbour.x > pZone.x) return 1;
            if (pNeighbour.y < pZone.y) return 2;
            if (pNeighbour.y > pZone.y) return 3;
            return -1;
        }

        private static void ResolveEdge(TileZone pZone, int pDirection,
            out Vector3 pStart, out Vector3 pEnd)
        {
            float left = pZone.x * 8f;
            float bottom = pZone.y * 8f;
            switch (pDirection)
            {
                case 0:
                    pStart = new Vector3(left, bottom, BoundaryZ);
                    pEnd = new Vector3(left, bottom + 8f, BoundaryZ);
                    return;
                case 1:
                    pStart = new Vector3(left + 8f, bottom, BoundaryZ);
                    pEnd = new Vector3(left + 8f, bottom + 8f, BoundaryZ);
                    return;
                case 2:
                    pStart = new Vector3(left, bottom, BoundaryZ);
                    pEnd = new Vector3(left + 8f, bottom, BoundaryZ);
                    return;
                default:
                    pStart = new Vector3(left, bottom + 8f, BoundaryZ);
                    pEnd = new Vector3(left + 8f, bottom + 8f, BoundaryZ);
                    return;
            }
        }

        private static long EncodeEdge(TileZone pZone, int pDirection)
        {
            return ((long)(uint)pZone.id << 3) | (uint)pDirection;
        }

        private static Color ResolveColor(City pCity)
        {
            try
            {
                Color color = pCity.getColor().getColorMainSecond();
                color.a = 0.8f;
                return color;
            }
            catch
            {
                return new Color(0.05f, 0.05f, 0.05f, 0.72f);
            }
        }

        private static string ResolveSortingLayer()
        {
            try
            {
                SpriteRenderer renderer = World.world?.GetComponent<SpriteRenderer>();
                return renderer?.sortingLayerName ?? "Default";
            }
            catch { return "Default"; }
        }

        private static void SetRootActive(bool pActive)
        {
            if (_root != null && _root.activeSelf != pActive)
                _root.SetActive(pActive);
        }

        internal static void SetMinimapHidden(bool pHidden)
        {
            _minimapHidden = pHidden;
            if (pHidden)
            {
                SetRootActive(false);
                return;
            }
            if (Config.game_loaded &&
                HierarchicalVassalMapModeService.IsActive() &&
                HierarchicalVassalMapModeService.IsCityLayer)
                SetRootActive(true);
        }
    }
}
