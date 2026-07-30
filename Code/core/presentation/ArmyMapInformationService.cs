using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.performance;
using UnityEngine;

namespace AncientWarfare3.core.presentation
{
    internal static class ArmyMapInformationService
    {
        private const float SelectionRefreshSeconds = 2f;
        private const int MaximumCandidatesReadPerFrame = 32;
        private const float LabelHeight = 0.85f;
        private const int LabelSortingOrder = 18;
        private const float NativeFlagCharacterSize = 1f;

        private sealed class PoolEntry
        {
            internal long ArmyId = -1L;
            internal GameObject Root;
            internal TextMesh Text;

            internal void Hide()
            {
                ArmyId = -1L;
                if (Root != null && Root.activeSelf)
                    Root.SetActive(false);
            }
        }

        private static readonly List<PoolEntry> Pool =
            new List<PoolEntry>(ArmyMapInformationRules.MaximumVisibleArmies);
        private static readonly List<ArmyRtsVisualizationCandidate>
            CandidateScratch = new List<ArmyRtsVisualizationCandidate>(
                ArmyMapInformationRules.MaximumVisibleArmies);
        private static readonly List<long> ArmyIdScratch = new List<long>();
        private static readonly long[] VisibleArmyIds =
            new long[ArmyMapInformationRules.MaximumVisibleArmies];

        private static GameObject _root;
        private static bool _reportedFailure;
        private static bool _initializationFailed;
        private static long _selectedKingdomId = -1L;
        private static int _visibleCount;
        private static int _refreshCursor;
        private static float _nextSelectionRefresh;
        private static long _selectionKingdomId = -1L;
        private static long _selectionAfterArmyId = -1L;
        private static bool _selectionInProgress;

        static ArmyMapInformationService()
        {
            AWPerformanceSettings.ArmyMapInformationDisabled +=
                ClearDisplayState;
        }

        public static void ProcessFrame()
        {
            try
            {
                Kingdom selected = SelectedMetas.selected_kingdom;
                long selectedId = selected?.data?.id ?? -1L;
                bool worldReady = Config.game_loaded &&
                                  !SmoothLoader.isLoading();
                if (!worldReady || _initializationFailed ||
                    !ArmyMapInformationRules.ShouldDisplay(
                        ArmyRtsRuntimeMode.Current,
                        AWPerformanceSettings.ShowArmyMapInformation,
                        selectedId) || selected == null || selected.isRekt())
                {
                    ClearDisplayState();
                    return;
                }

                EnsurePool();
                float now = Time.unscaledTime;
                bool selectingCurrent = _selectionInProgress &&
                                        _selectionKingdomId == selectedId;
                if (selectedId != _selectedKingdomId && !selectingCurrent)
                    BeginSelection(selected, pClearVisible: true);
                else if (!_selectionInProgress &&
                         now >= _nextSelectionRefresh)
                    BeginSelection(selected, pClearVisible: false);
                if (_selectionInProgress)
                    ProcessSelectionBatch(selected, now);
                PruneInvalidEntries(selected);
                RefreshNextEntries(selected);
            }
            catch (Exception error)
            {
                ClearDisplayState();
                DestroyPresentationObjects();
                _initializationFailed = true;
                if (_reportedFailure) return;
                _reportedFailure = true;
                ModClass.LogWarning(
                    "Army map information failed: " + error.Message);
            }
        }

        public static void ClearRuntime()
        {
            ClearDisplayState();
            _reportedFailure = false;
            _initializationFailed = false;
        }

        public static void Shutdown()
        {
            ClearRuntime();
            DestroyPresentationObjects();
            for (int index = 0; index < VisibleArmyIds.Length; index++)
                VisibleArmyIds[index] = -1L;
        }

        internal static void CopyVisibleArmyIdsForMinimap(long pKingdomId,
            List<long> pDestination)
        {
            if (pDestination == null) return;
            pDestination.Clear();
            if (pKingdomId < 0L || pKingdomId != _selectedKingdomId) return;
            int count = Math.Min(_visibleCount,
                ArmyMapInformationRules.MaximumMinimapMarkers);
            for (int index = 0; index < count; index++)
            {
                long armyId = VisibleArmyIds[index];
                if (armyId >= 0L) pDestination.Add(armyId);
            }
        }

        private static void BeginSelection(Kingdom pKingdom,
            bool pClearVisible)
        {
            if (pClearVisible)
            {
                for (int index = 0; index < Pool.Count; index++)
                    Pool[index].Hide();
                for (int index = 0; index < _visibleCount; index++)
                    VisibleArmyIds[index] = -1L;
                _selectedKingdomId = -1L;
                _visibleCount = 0;
                _refreshCursor = 0;
            }
            CandidateScratch.Clear();
            ArmyIdScratch.Clear();
            _selectionKingdomId = pKingdom.id;
            _selectionAfterArmyId = -1L;
            _selectionInProgress = true;
        }

        private static void ProcessSelectionBatch(Kingdom pKingdom,
            float pNow)
        {
            if (!_selectionInProgress ||
                _selectionKingdomId != pKingdom.id) return;
            ArmyStrategicIndexService.CopyArmyIdsAfter(pKingdom,
                _selectionAfterArmyId, MaximumCandidatesReadPerFrame,
                ArmyIdScratch, out bool complete);
            for (int index = 0; index < ArmyIdScratch.Count; index++)
            {
                Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                    ArmyIdScratch[index], pKingdom.id);
                if (army?.data == null) continue;
                bool hasProjection = ArmyRtsControllerService.
                    TryGetProjection(army,
                        out ArmyRtsStrategicProjection projection);
                bool hasMission = ArmyRtsControllerService.TryGetMission(
                    army, out ArmyRtsMission mission);
                int memberCount = 0;
                try { memberCount = army.countUnits(); }
                catch { }
                ArmyRtsState state = ArmyMapInformationRules.
                    ResolvePendingState(hasProjection,
                        hasProjection && projection != null
                            ? projection.State
                            : ArmyRtsState.Idle,
                        memberCount,
                        ArmyLogisticsRules.MinimumOperationalForce);
                ArmyRtsVisualizationRules.TryAddVisibleCandidate(
                    CandidateScratch,
                    new ArmyRtsVisualizationCandidate(army.id,
                        pKingdom.id, state,
                        hasMission && mission != null
                            ? mission.Role
                            : ArmyRtsRole.Reserve,
                        hasProjection && projection != null &&
                        projection.PlayerOrder), pKingdom.id);
            }
            if (ArmyIdScratch.Count > 0)
                _selectionAfterArmyId =
                    ArmyIdScratch[ArmyIdScratch.Count - 1];
            if (complete) CommitSelection(pKingdom, pNow);
        }

        private static void CommitSelection(Kingdom pKingdom, float pNow)
        {
            bool changed = CandidateScratch.Count != _visibleCount;
            for (int index = 0; index < CandidateScratch.Count; index++)
            {
                long armyId = CandidateScratch[index].ArmyId;
                if (VisibleArmyIds[index] != armyId) changed = true;
                VisibleArmyIds[index] = armyId;
            }
            for (int index = CandidateScratch.Count; index < _visibleCount;
                 index++)
                VisibleArmyIds[index] = -1L;

            _selectedKingdomId = pKingdom.id;
            _visibleCount = CandidateScratch.Count;
            _nextSelectionRefresh = pNow + SelectionRefreshSeconds;
            _selectionKingdomId = -1L;
            _selectionAfterArmyId = -1L;
            _selectionInProgress = false;
            if (!changed) return;

            _refreshCursor = 0;
            for (int index = 0; index < Pool.Count; index++)
                Pool[index].Hide();
            for (int index = 0; index < _visibleCount; index++)
                Pool[index].ArmyId = VisibleArmyIds[index];
        }

        private static void PruneInvalidEntries(Kingdom pKingdom)
        {
            for (int index = 0; index < _visibleCount; index++)
            {
                PoolEntry entry = Pool[index];
                if (entry.ArmyId < 0L || !entry.Root.activeSelf) continue;
                if (!TryResolveLiveCaptain(entry.ArmyId, pKingdom,
                        out _, out _)) entry.Hide();
            }
        }

        private static void RefreshNextEntries(Kingdom pKingdom)
        {
            if (_visibleCount <= 0) return;
            int budget = Math.Min(
                ArmyMapInformationRules.MaximumEntriesRefreshedPerFrame,
                _visibleCount);
            for (int offset = 0; offset < budget; offset++)
            {
                int index = (_refreshCursor + offset) % _visibleCount;
                PoolEntry entry = Pool[index];
                entry.ArmyId = VisibleArmyIds[index];
                RefreshEntry(entry, pKingdom);
            }
            _refreshCursor = (_refreshCursor + budget) % _visibleCount;
        }

        private static void RefreshEntry(PoolEntry pEntry,
            Kingdom pKingdom)
        {
            if (!TryResolveLiveCaptain(pEntry.ArmyId, pKingdom,
                    out Army army, out Actor captain))
            {
                pEntry.Hide();
                return;
            }
            if (!TryComposeText(army, captain, out string text))
            {
                pEntry.Hide();
                return;
            }
            pEntry.Text.text = text;
            RefreshTextFont(pEntry.Text);
            Vector3 position = captain.getHeadOffsetPositionForFunRendering();
            position.y += LabelHeight * captain.current_scale.y;
            position.z = -0.13f;
            pEntry.Root.transform.position = position;
            pEntry.Root.SetActive(true);
        }

        internal static bool TryPopulateNativeFlagText(Army pArmy,
            Actor pCaptain, QuantumSpriteWithText pFlag)
        {
            if (pFlag?.text == null ||
                !TryComposeText(pArmy, pCaptain, out string text)) return false;

            pFlag.text.text = text;
            pFlag.text.gameObject.SetActive(true);
            pFlag.text.characterSize = NativeFlagCharacterSize;
            pFlag.text.fontSize = 14;
            pFlag.text.lineSpacing = 0.7f;
            RefreshTextFont(pFlag.text);
            Renderer renderer = pFlag.text.GetComponent<Renderer>();
            if (renderer != null && pFlag.sprite_renderer != null)
            {
                renderer.sortingLayerID = pFlag.sprite_renderer.sortingLayerID;
                renderer.sortingOrder = pFlag.sprite_renderer.sortingOrder + 1;
            }
            return true;
        }

        private static bool TryComposeText(Army pArmy, Actor pCaptain,
            out string pText)
        {
            pText = string.Empty;
            try
            {
                if (pArmy?.data == null || pCaptain?.data == null ||
                    !pCaptain.isAlive() || pCaptain.isRekt()) return false;

                string nativeName = pArmy.data.name;
                if (!ArmyMapInformationRules.ShouldDisplayEntry(
                        mapInformationEnabled: true, armyAlive: true,
                        captainAlive: true, nativeName)) return false;

                int memberCount = 0;
                try { memberCount = pArmy.countUnits(); }
                catch { }
                bool hasProjection = ArmyRtsControllerService.
                    TryGetProjection(pArmy,
                        out ArmyRtsStrategicProjection projection);
                bool hasMission = ArmyRtsControllerService.TryGetMission(
                    pArmy, out ArmyRtsMission mission);
                ResolveManpowerValues(pArmy, memberCount, hasProjection,
                    hasMission, mission, out int replenishmentShortage,
                    out int reserveSupply);
                string manpowerText = ArmyMapInformationRules.
                    ComposeManpowerText(
                        Localize("aw_army_replenishment_shortage",
                            "Replenishment shortage"),
                        Localize("aw_army_reserve_supply", "Reserve supply"),
                        replenishmentShortage, reserveSupply);
                if (!hasProjection || projection == null || !hasMission ||
                    mission == null)
                {
                    ArmyRtsState pendingState = ArmyMapInformationRules.
                        ResolvePendingState(hasProjection,
                            hasProjection && projection != null
                                ? projection.State
                                : ArmyRtsState.Idle,
                            memberCount,
                            ArmyLogisticsRules.MinimumOperationalForce);
                    string pendingOperation = Localize(
                        ArmyMapInformationRules.
                            PendingOperationLocalizationKey(pendingState),
                        pendingState == ArmyRtsState.Replenish
                            ? "Replenishing"
                            : "Awaiting orders");
                    pText = ArmyMapInformationRules.ComposeText(nativeName,
                        memberCount, SafeName(pCaptain), pendingOperation,
                        manpowerText: manpowerText);
                    return true;
                }
                pText = ArmyMapInformationRules.ComposeText(nativeName,
                    memberCount, SafeName(pCaptain), ResolveOperation(projection,
                        mission, pArmy), manpowerText: manpowerText);
                return true;
            }
            catch
            {
                pText = string.Empty;
                return false;
            }
        }

        private static void ResolveManpowerValues(Army pArmy,
            int pMemberCount, bool pHasProjection, bool pHasMission,
            ArmyRtsMission pMission, out int pShortage,
            out int pReserveSupply)
        {
            pShortage = 0;
            pReserveSupply = 0;
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                if (!pHasProjection || pArmy?.data == null) return;
                pArmy.data.get(
                    LineageKeys.AW_ARMY_PROJECTED_REPLENISHMENT_SHORTAGE,
                    out pShortage, 0);
                pArmy.data.get(
                    LineageKeys.AW_ARMY_PROJECTED_KINGDOM_RESERVE_AVAILABLE,
                    out pReserveSupply, 0);
                pShortage = Math.Max(0, pShortage);
                pReserveSupply = Math.Max(0, pReserveSupply);
                return;
            }

            if (pHasMission && pMission != null)
                pShortage = Math.Max(0,
                    Math.Max(0, pMission.TargetStrength) -
                    Math.Max(0, pMemberCount));
            else
                pShortage = Math.Max(0,
                    ArmyLogisticsRules.MinimumOperationalForce -
                    Math.Max(0, pMemberCount));

            Kingdom kingdom = null;
            try { kingdom = pArmy?.getKingdom(); }
            catch { }
            pReserveSupply = CityReservePoolService.CountAvailable(kingdom);
        }

        private static bool TryResolveLiveCaptain(long pArmyId,
            Kingdom pKingdom, out Army pArmy, out Actor pCaptain)
        {
            pArmy = ArmyStrategicIndexService.ResolveIndexedArmy(pArmyId,
                pKingdom.id);
            pCaptain = null;
            if (pArmy?.data == null) return false;
            try { pCaptain = pArmy.getCaptain(); }
            catch { }
            try
            {
                return pCaptain?.data != null && pCaptain.isAlive() &&
                       !pCaptain.isRekt();
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveOperation(
            ArmyRtsStrategicProjection pProjection, ArmyRtsMission pMission,
            Army pArmy)
        {
            ArmyRtsTransportPhase transportPhase =
                ArmyRtsTransportService.GetPhase(pArmy);
            string state = Localize(
                ArmyRtsPresentationRules.OperationLocalizationKey(
                    pProjection.State, transportPhase),
                ArmyRtsPresentationRules.OperationFallback(pProjection.State,
                    transportPhase));
            string role = Localize(
                ArmyRtsPresentationRules.RoleLocalizationKey(pMission.Role),
                ArmyRtsPresentationRules.RoleFallback(pMission.Role));
            City target = null;
            try { target = World.world?.cities?.get(pMission.TargetCityId); }
            catch { }
            string targetName = target?.data?.name;
            if (string.IsNullOrWhiteSpace(targetName))
                targetName = Localize("aw_army_rts_target_unknown",
                    "Unknown target");
            return ArmyRtsPresentationRules.ComposeOperation(state, role,
                targetName, pProjection.PlayerOrder,
                Localize("aw_army_rts_player_order", "Player order"));
        }

        private static string Localize(string pKey, string pFallback)
        {
            try
            {
                string text = LocalizedTextManager.getText(pKey);
                if (!string.IsNullOrWhiteSpace(text) && text != pKey)
                    return text;
            }
            catch { }
            return pFallback;
        }

        private static string SafeName(Actor pCaptain)
        {
            try { return pCaptain?.getName() ?? string.Empty; }
            catch { return pCaptain?.data?.name ?? string.Empty; }
        }

        private static void EnsurePool()
        {
            if (_root != null && Pool.Count ==
                ArmyMapInformationRules.MaximumVisibleArmies) return;

            GameObject root = new GameObject("AW3_ArmyMapInformation")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(root);
            string sortingLayer = ResolveSortingLayer();
            var entries = new List<PoolEntry>(
                ArmyMapInformationRules.MaximumVisibleArmies);
            try
            {
                for (int index = 0;
                     index < ArmyMapInformationRules.MaximumVisibleArmies;
                     index++)
                {
                    var entry = new PoolEntry
                    {
                        Root = new GameObject("ArmyMapInfo_" + index)
                    };
                    entry.Root.transform.SetParent(root.transform, false);
                    entry.Text = entry.Root.AddComponent<TextMesh>();
                    entry.Text.anchor = TextAnchor.LowerLeft;
                    entry.Text.alignment = TextAlignment.Left;
                    entry.Text.characterSize = 0.12f;
                    entry.Text.fontSize = 32;
                    entry.Text.lineSpacing = 0.8f;
                    entry.Text.richText = false;
                    entry.Text.color = Color.white;
                    RefreshTextFont(entry.Text);
                    Renderer renderer = entry.Text.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sortingLayerName = sortingLayer;
                        renderer.sortingOrder = LabelSortingOrder;
                    }
                    entry.Hide();
                    entries.Add(entry);
                }
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }

            DestroyPresentationObjects();
            _root = root;
            Pool.AddRange(entries);
        }

        private static void RefreshTextFont(TextMesh pText)
        {
            if (pText == null || LocalizedTextManager.current_font == null)
                return;
            pText.font = LocalizedTextManager.current_font;
            Renderer renderer = pText.GetComponent<Renderer>();
            if (renderer != null && pText.font.material != null)
                renderer.sharedMaterial = pText.font.material;
        }

        private static string ResolveSortingLayer()
        {
            string layer = "Default";
            try
            {
                SpriteRenderer renderer =
                    World.world?.GetComponent<SpriteRenderer>();
                if (renderer != null) layer = renderer.sortingLayerName;
            }
            catch { }
            return layer;
        }

        private static void ClearDisplayState()
        {
            for (int index = 0; index < Pool.Count; index++)
                Pool[index].Hide();
            for (int index = 0; index < _visibleCount; index++)
                VisibleArmyIds[index] = -1L;
            CandidateScratch.Clear();
            ArmyIdScratch.Clear();
            _selectedKingdomId = -1L;
            _visibleCount = 0;
            _refreshCursor = 0;
            _nextSelectionRefresh = 0f;
            _selectionKingdomId = -1L;
            _selectionAfterArmyId = -1L;
            _selectionInProgress = false;
        }

        private static void DestroyPresentationObjects()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            Pool.Clear();
            _root = null;
        }
    }
}
