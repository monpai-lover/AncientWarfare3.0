using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.core.policy;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Main-thread dirty routing and the bounded boundary presentation stages.
    /// The native mutations are intentionally discovered by name and signature
    /// rather than guessed overload attributes: WorldBox has changed these
    /// methods between minor releases.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_HierarchicalVassalBoundaryDirtyPatch
    {
        private const int AuditIntervalFrames = 15;
        private const string HeightProperty = "WorldTile.Height";

        private static readonly BindingFlags DeclaredMethods =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;
        private static readonly HierarchicalVassalBoundaryDirtyTracker Dirty =
            new HierarchicalVassalBoundaryDirtyTracker();
        private static HierarchicalVassalBoundarySnapshotCapture _capture;
        private static HierarchicalVassalBoundaryTopologyWorker _worker;
        private static long _generation = long.MinValue;
        private static int _worldWidth;
        private static int _worldHeight;
        private static bool _meshAuthorityActive;
        private static bool _meshFallback;
        private static bool _wasActive;
        private static bool _initializing;
        private static bool _auditFallbackRequired;
        private static int _auditFrame;

        internal static bool MeshAuthorityActive
        {
            get { return _meshAuthorityActive && !_initializing; }
        }

        internal static bool RendererGenerationActive
        {
            get
            {
                return MeshAuthorityActive && _generation >= 0L &&
                       Config.game_loaded && World.world != null &&
                       MapBox.width == _worldWidth &&
                       MapBox.height == _worldHeight;
            }
        }

        internal static bool MeshFallbackActive
        {
            get { return _meshFallback && !_meshAuthorityActive; }
        }

        // Called by AW_DeferredRuntimeWorkPatch in this exact order.
        internal static void ProcessWorldRevisionEvents()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;
            bool active = HierarchicalVassalMapModeService.IsActive();
            if (!active)
            {
                _wasActive = false;
                _meshFallback = false;
                return;
            }

            if (!_wasActive)
            {
                _wasActive = true;
                if (EnsureRuntime()) Dirty.MarkAll();
            }
            HierarchicalVassalMapModeService.RefreshIfWorldChanged();
        }

        internal static void ProcessCapture()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading() ||
                !HierarchicalVassalMapModeService.IsActive()) return;
            if (!EnsureRuntime() || _capture == null || _worker == null) return;

            HierarchicalVassalMapModeSnapshot snapshot =
                HierarchicalVassalMapModeService.BuildVisibleSnapshot();
            if (snapshot == null) return;
            BoundaryDisplayLayer layer =
                HierarchicalVassalMapModeService.IsCityLayer
                    ? BoundaryDisplayLayer.Cities
                    : BoundaryDisplayLayer.Countries;
            _capture.ProcessFrame(_generation, layer, snapshot,
                SubmitCapturedSnapshot);

            // Reflected lifecycle hooks are optional. When one is absent, a
            // single round-robin chunk audit is enough to catch that mutation.
            if (!_auditFallbackRequired || ++_auditFrame < AuditIntervalFrames)
                return;
            _auditFrame = 0;
            _capture.AuditOneChunkPerSimulationCycle(layer, snapshot);
        }

        internal static void DrainWorker()
        {
            if (_worker == null) return;
            int limit = HierarchicalVassalBoundaryChunkRules.UploadBudgetPerFrame;
            int drained = 0;
            while (drained < limit &&
                   _worker.TryTakeCompletion(
                       out BoundaryWorkerCompletion completion))
            {
                drained++;
                if (!HierarchicalVassalBoundaryMeshLayer.
                        TryAcceptCompletion(completion))
                {
                    // A saturated presentation queue must not lose the fact;
                    // the next bounded capture will retry this chunk.
                    Dirty.MarkChunk(completion.ChunkKey);
                }
            }
            if (_worker.TryConsumeRescanMarker(_generation)) Dirty.MarkAll();
        }

        internal static void DrainMesh()
        {
            int uploaded = HierarchicalVassalBoundaryMeshLayer.DrainMesh();
            if (!HierarchicalVassalBoundaryMeshLayer.IsHealthy)
            {
                _meshAuthorityActive = false;
                _meshFallback = true;
            }
        }

        internal static void OnMapModeDirty()
        {
            MarkVisibleSnapshotZones();
        }

        internal static void OnLayerChanged()
        {
            // Layer switches affect only the zones currently visible in the
            // previous view; activation itself performs the one full mark.
            MarkVisibleSnapshotZones();
        }

        internal static void BoundaryHierarchyChanged(Kingdom pOldKingdom,
            Kingdom pNewKingdom)
        {
            MarkKingdom(pOldKingdom);
            MarkKingdom(pNewKingdom);
            MarkVisibleSnapshotZones();
        }

        internal static void MarkVisibleSnapshotZones()
        {
            if (_worker == null) return;
            try
            {
                HierarchicalVassalMapModeSnapshot snapshot =
                    HierarchicalVassalMapModeService.BuildVisibleSnapshot();
                IReadOnlyList<TileZone> zones = snapshot?.DrawableZones;
                if (zones == null) return;
                for (int i = 0; i < zones.Count; i++) Dirty.MarkZone(zones[i]);
            }
            catch { }
        }

        internal static void MarkTile(WorldTile pTile)
        {
            if (!RendererGenerationActive || pTile == null) return;
            try { Dirty.MarkTile(pTile); }
            catch { }
        }

        internal static void MarkZone(TileZone pZone)
        {
            if (!RendererGenerationActive || pZone == null) return;
            try { Dirty.MarkZone(pZone); }
            catch { }
        }

        internal static void MarkKingdom(Kingdom pKingdom)
        {
            if (!RendererGenerationActive || pKingdom == null) return;
            try { Dirty.MarkKingdom(pKingdom); }
            catch { }
        }

        internal static void CancelGeneration()
        {
            _meshAuthorityActive = false;
            _meshFallback = false;
            _wasActive = false;
            _initializing = false;
            _auditFrame = 0;
            unchecked { _generation++; }
            HierarchicalVassalBoundaryTopologyWorker worker = _worker;
            _worker = null;
            _capture = null;
            if (worker != null)
            {
                try { worker.Dispose(); }
                catch { }
            }
        }

        internal static void Shutdown()
        {
            CancelGeneration();
            HierarchicalVassalBoundaryMeshLayer.Reset();
            HierarchicalVassalMapModeLabelLayer.Reset();
        }

        internal static void NotifyTerrainChanged(WorldTile pTile)
        {
            // Terrain hooks must never dirty chunks during a load storm. The
            // generation gate is stricter than Config.game_loaded alone.
            if (!RendererGenerationActive) return;
            MarkTile(pTile);
        }

        private static bool EnsureRuntime()
        {
            // A failed initialization selects the legacy renderer for the
            // rest of this map-mode session. Retrying every render frame
            // would repeatedly allocate roots/materials and spam warnings.
            if (_meshFallback) return false;
            int width = MapBox.width;
            int height = MapBox.height;
            if (width <= 0 || height <= 0 || World.world?.tiles_list == null)
                return false;
            if (_worker != null && width == _worldWidth &&
                height == _worldHeight && _generation >= 0L)
                return _meshAuthorityActive;

            if (_worker != null) CancelGeneration();
            _initializing = true;
            _meshFallback = false;
            try
            {
                long count = ((long)width +
                    HierarchicalVassalBoundaryChunkRules.ChunkSize - 1L) /
                    HierarchicalVassalBoundaryChunkRules.ChunkSize;
                long rows = ((long)height +
                    HierarchicalVassalBoundaryChunkRules.ChunkSize - 1L) /
                    HierarchicalVassalBoundaryChunkRules.ChunkSize;
                long chunks = checked(count * rows);
                if (chunks <= 0L || chunks > int.MaxValue)
                    throw new InvalidOperationException("invalid boundary chunk count");

                _worldWidth = width;
                _worldHeight = height;
                _worker = new HierarchicalVassalBoundaryTopologyWorker(
                    (int)chunks);
                _generation = _worker.ResetWorld((int)chunks);
                _capture = new HierarchicalVassalBoundarySnapshotCapture(Dirty);
                _capture.ResetWorld(_generation, width, height);
                Dirty.MarkAll(); // activation/world load: exactly one full mark
                HierarchicalVassalBoundaryMeshLayer.ResetWorld(_generation);
                if (!HierarchicalVassalBoundaryMeshLayer.TryInitialize())
                    throw new InvalidOperationException("boundary mesh init failed");
                _meshAuthorityActive = true;
                _meshFallback = false;
                return true;
            }
            catch (Exception error)
            {
                _meshAuthorityActive = false;
                _meshFallback = true;
                HierarchicalVassalBoundaryTopologyWorker worker = _worker;
                _worker = null;
                _capture = null;
                if (worker != null)
                {
                    try { worker.Dispose(); }
                    catch { }
                }
                try { HierarchicalVassalBoundaryMeshLayer.Reset(); }
                catch { }
                try { ModClass.LogWarning(
                    "[AW3 hierarchical boundary] mesh fallback: " +
                    error.Message); }
                catch { }
                return false;
            }
            finally { _initializing = false; }
        }

        private static void SubmitCapturedSnapshot(
            HierarchicalVassalBoundaryChunkSnapshot pSnapshot)
        {
            if (_worker == null || pSnapshot == null) return;
            if (!_worker.Submit(pSnapshot)) Dirty.MarkChunk(pSnapshot.ChunkKey);
        }

        private static IEnumerable<MethodBase> Methods(Type pType,
            params string[] pNames)
        {
            if (pType == null || pNames == null) yield break;
            HashSet<string> names = new HashSet<string>(pNames,
                StringComparer.Ordinal);
            MethodInfo[] methods;
            try { methods = pType.GetMethods(DeclaredMethods); }
            catch { yield break; }
            for (int i = 0; i < methods.Length; i++)
                if (names.Contains(methods[i].Name)) yield return methods[i];
        }

        private static MethodInfo HeightSetter()
        {
            try
            {
                return typeof(WorldTile).GetProperty("Height",
                    DeclaredMethods)?.GetSetMethod(true);
            }
            catch { return null; }
        }

        private static IEnumerable<MethodBase> LifecycleMethods(Type pType,
            params string[] pNames)
        {
            List<MethodBase> methods = new List<MethodBase>(Methods(pType,
                pNames));
            if (methods.Count == 0) _auditFallbackRequired = true;
            return methods;
        }

        private static void CaptureZone(TileZone pZone,
            out City pCity, out Kingdom pKingdom)
        {
            pCity = null;
            pKingdom = null;
            try
            {
                pCity = pZone?.city;
                pKingdom = pCity?.kingdom;
            }
            catch { }
        }

        private static void MarkCity(City pCity)
        {
            if (pCity == null) return;
            MarkKingdom(pCity.kingdom);
            try
            {
                if (pCity.zones == null) return;
                for (int i = 0; i < pCity.zones.Count; i++) MarkZone(pCity.zones[i]);
            }
            catch { }
        }

        private static TileZone ZoneFromArgs(object[] pArgs)
        {
            if (pArgs == null) return null;
            for (int i = 0; i < pArgs.Length; i++)
            {
                if (pArgs[i] is TileZone zone) return zone;
                if (pArgs[i] is WorldTile tile) return tile.zone;
            }
            return null;
        }

        private sealed class ZoneState
        {
            internal City OldCity;
            internal Kingdom OldKingdom;
        }

        private sealed class CityState
        {
            internal Kingdom OldKingdom;
            internal int OldZoneCount;
        }

        private sealed class TileState
        {
            internal object OldType;
            internal int OldHeight;
        }

        [HarmonyPatch]
        private static class TileZoneOwnershipHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return Methods(typeof(TileZone), "setCity");
            }

            [HarmonyPrefix]
            private static void Prefix(TileZone __instance,
                out ZoneState __state)
            {
                __state = new ZoneState();
                CaptureZone(__instance, out __state.OldCity,
                    out __state.OldKingdom);
            }

            [HarmonyPostfix]
            private static void Postfix(TileZone __instance,
                ZoneState __state)
            {
                CaptureZone(__instance, out City newCity,
                    out Kingdom newKingdom);
                if (__state == null || __state.OldCity != newCity ||
                    __state.OldKingdom != newKingdom)
                {
                    MarkZone(__instance);
                    MarkCity(__state?.OldCity);
                    MarkCity(newCity);
                }
            }
        }

        [HarmonyPatch]
        private static class CityZoneHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                // City.addZone overloads are selected reflectively.
                return Methods(typeof(City), "addZone");
            }

            [HarmonyPrefix]
            private static void Prefix(City __instance,
                out CityState __state)
            {
                __state = new CityState { OldKingdom = __instance?.kingdom,
                    OldZoneCount = __instance?.zones?.Count ?? 0 };
            }

            [HarmonyPostfix]
            private static void Postfix(City __instance, object[] __args,
                CityState __state)
            {
                TileZone zone = ZoneFromArgs(__args);
                MarkZone(zone);
                MarkCity(__instance);
                if (__state != null &&
                    (__state.OldZoneCount != (__instance?.zones?.Count ?? 0) ||
                     __state.OldKingdom != __instance?.kingdom))
                    MarkKingdom(__state.OldKingdom);
            }
        }

        [HarmonyPatch]
        private static class CityTransferHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                // City.joinAnotherKingdom overloads are selected exactly by
                // reflected method name and retain whatever argument shape the
                // installed WorldBox build exposes.
                return Methods(typeof(City), "joinAnotherKingdom");
            }

            [HarmonyPrefix]
            private static void Prefix(City __instance,
                out CityState __state)
            {
                __state = new CityState { OldKingdom = __instance?.kingdom };
            }

            [HarmonyPostfix]
            private static void Postfix(City __instance,
                CityState __state)
            {
                Kingdom current = __instance?.kingdom;
                if (__state == null || __state.OldKingdom != current)
                {
                    MarkKingdom(__state?.OldKingdom);
                    MarkKingdom(current);
                    MarkCity(__instance);
                }
            }
        }

        [HarmonyPatch]
        private static class WorldTileMutationHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                List<MethodBase> methods = new List<MethodBase>();
                MethodInfo setter = HeightSetter();
                if (setter != null) methods.Add(setter);
                methods.AddRange(Methods(typeof(WorldTile), "setTileType",
                    "setTileTypes"));
                if (methods.Count == 0) _auditFallbackRequired = true;
                return methods;
            }

            [HarmonyPrefix]
            private static void Prefix(WorldTile __instance,
                out TileState __state)
            {
                __state = new TileState
                {
                    OldType = __instance?.Type,
                    OldHeight = ReadHeight(__instance)
                };
            }

            [HarmonyPostfix]
            private static void Postfix(WorldTile __instance,
                TileState __state)
            {
                if (__instance == null || __state == null ||
                    (!ReferenceEquals(__state.OldType, __instance.Type) &&
                     !SameType(__state.OldType, __instance.Type)) ||
                    __state.OldHeight != ReadHeight(__instance))
                    NotifyTerrainChanged(__instance);
            }

            private static int ReadHeight(WorldTile pTile)
            {
                try { return Convert.ToInt32(pTile?.Height); }
                catch { return 0; }
            }

            private static bool SameType(object pOldType, object pNewType)
            {
                if (pOldType == null || pNewType == null) return false;
                return pOldType.GetType() == pNewType.GetType() &&
                       string.Equals(pOldType.ToString(),
                           pNewType.ToString(), StringComparison.Ordinal);
            }
        }

        [HarmonyPatch]
        private static class ZoneLifecycleHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                // Zone removal/destruction signatures are exact reflected
                // methods when present; otherwise bounded audit covers them.
                return LifecycleMethods(typeof(TileZone), "remove", "destroy",
                    "setRekt", "kill");
            }

            [HarmonyPostfix]
            private static void Postfix(TileZone __instance)
            {
                MarkZone(__instance);
            }
        }

        [HarmonyPatch]
        private static class KingdomLifecycleHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                // Kingdom creation/destruction signatures vary by build.
                return LifecycleMethods(typeof(Kingdom), "create", "destroy",
                    "setRekt", "kill");
            }

            [HarmonyPostfix]
            private static void Postfix(Kingdom __instance)
            {
                MarkKingdom(__instance);
            }
        }

        [HarmonyPatch]
        private static class WorldResetHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodBase clear = AccessTools.Method(typeof(MapBox),
                    nameof(MapBox.clearWorld));
                return clear == null ? Array.Empty<MethodBase>() :
                    new[] { clear };
            }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static void Prefix()
            {
                CancelGeneration();
            }
        }

        [HarmonyPatch]
        private static class ShutdownHooks
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                MethodBase onDestroy = AccessTools.Method(typeof(ModClass),
                    "OnDestroy");
                MethodBase onQuit = AccessTools.Method(typeof(ModClass),
                    "OnApplicationQuit");
                if (onDestroy != null) yield return onDestroy;
                if (onQuit != null) yield return onQuit;
            }

            [HarmonyPrefix]
            private static void Prefix()
            {
                Shutdown();
            }
        }
    }
}
