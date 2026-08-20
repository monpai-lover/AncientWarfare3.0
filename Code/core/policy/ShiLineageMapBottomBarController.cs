using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class ShiLineageMapBottomBarController
    {
        public const string TabId = "selected_aw_shi_city";
        private static PowersTab _tab;
        private static PowersTab _tabBeforeInitialization;
        private static ShiLineageCompositionElement _element;
        private static City _pendingCity;
        private static long _cityId = -1L;
        private static int _generation = -1;
        private static bool _showRequested;
        private static bool _visibleOrPending;

        public static void Show(City pCity)
        {
            if (!IsValidCity(pCity)) { Hide(); return; }
            _pendingCity = pCity;
            _cityId = pCity.data.id;
            _generation = -1;
            _showRequested = true;
            _visibleOrPending = true;
            HideNativeSelectedCityTab();
            CityShiInfluenceSnapshotService.Demand(pCity);
            CityShiInfluenceSnapshot snapshot =
                CityShiInfluenceSnapshotService.GetSnapshot(pCity);
            if (snapshot == null) return;
            _generation = snapshot.Generation;
            if (EnsureTab()) TryPresent(snapshot);
        }

        public static void ProcessFrame()
        {
            if (ScrollWindow.getCurrentWindow() != null)
            {
                if (_visibleOrPending) Hide();
                return;
            }
            if (!ShiLineageMapModeService.IsActive())
            {
                if (_visibleOrPending) Hide();
                return;
            }
            HideNativeSelectedCityTab();
            City city = ShiLineageMapModeService.SelectedCity;
            if (!IsValidCity(city))
            {
                Hide();
                return;
            }
            if (_cityId != city.data.id) { Show(city); return; }
            CityShiInfluenceSnapshotService.Demand(city);
            CityShiInfluenceSnapshot snapshot =
                CityShiInfluenceSnapshotService.GetSnapshot(city);
            if (snapshot == null) { _pendingCity = city; _showRequested = true; return; }
            if (_generation != snapshot.Generation)
            {
                _pendingCity = city;
                _generation = snapshot.Generation;
                _showRequested = true;
            }
            if (_showRequested && EnsureTab()) TryPresent(snapshot);
        }

        public static void Hide()
        {
            if (!_visibleOrPending && _tabBeforeInitialization == null) return;
            CancelPendingInitialization();
            _showRequested = false;
            _visibleOrPending = false;
            _pendingCity = null;
            _cityId = -1L;
            _generation = -1;
            if (_element != null) _element.gameObject.SetActive(false);
            if (_tab != null && _tab.getAsset() != null &&
                _tab.isCurrentPowerTabSelected()) PowersTab.unselect();
            RestoreNativeSelectedCityTab();
        }

        internal static void ResetRuntime() => Hide();

        private static bool EnsureTab()
        {
            if (_tab != null)
            {
                if (_tab.getAsset() == null && !_tab.gameObject.activeSelf)
                {
                    CaptureTabBeforeInitialization();
                    _tab.gameObject.SetActive(true);
                }
                return _element != null;
            }
            PowersTab template = PowerTabController.instance?.tab_selected_city;
            if (template == null || template.transform.parent == null) return false;
            PowerTabAsset asset = AssetManager.power_tab_library.get(TabId) ??
                AssetManager.power_tab_library.add(new PowerTabAsset { id = TabId });
            asset.id = TabId;
            asset.tab_type_selected = true;
            asset.meta_type = MetaType.City;
            asset.window_id = "city";
            asset.get_power_tab = () => _tab;
            asset.on_update_check_active = _ =>
                ShiLineageMapModeService.IsActive() && HasSelectedCity();
            CaptureTabBeforeInitialization();
            GameObject obj = new GameObject(TabId, typeof(RectTransform));
            obj.SetActive(false);
            obj.layer = template.gameObject.layer;
            obj.transform.SetParent(template.transform.parent, false);
            CopyRect(template.GetComponent<RectTransform>(),
                obj.GetComponent<RectTransform>());
            obj.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            _tab = obj.AddComponent<PowersTab>();
            _element = ShiLineageCompositionElement.Create(_tab.transform);
            _element.gameObject.SetActive(false);
            obj.SetActive(true);
            return true;
        }

        private static void TryPresent(CityShiInfluenceSnapshot pSnapshot)
        {
            if (!_showRequested || _tab == null || _tab.getAsset() == null ||
                _element == null || !IsValidCity(_pendingCity) || pSnapshot == null)
                return;
            _element.Bind(_pendingCity, pSnapshot);
            _tab.sortButtons();
            _tab.recalc();
            if (!_tab.isCurrentPowerTabSelected())
            {
                if (_tabBeforeInitialization != null &&
                    _tabBeforeInitialization != _tab &&
                    _tabBeforeInitialization.gameObject.activeSelf)
                    _tabBeforeInitialization.hideTab();
                _tabBeforeInitialization = null;
                _tab.showTab(null);
            }
            _showRequested = false;
        }

        private static void CaptureTabBeforeInitialization()
        {
            PowersTab active = PowersTab.getActiveTab();
            _tabBeforeInitialization = active != _tab ? active : null;
        }

        private static void HideNativeSelectedCityTab()
        {
            PowersTab nativeTab = PowerTabController.instance?.tab_selected_city;
            if (nativeTab == null || nativeTab == _tab) return;
            if (nativeTab.isCurrentPowerTabSelected()) nativeTab.hideTab();
            nativeTab.gameObject.SetActive(false);
        }

        private static void RestoreNativeSelectedCityTab()
        {
            PowersTab nativeTab = PowerTabController.instance?.tab_selected_city;
            if (nativeTab == null || ShiLineageMapModeService.IsActive()) return;
            nativeTab.gameObject.SetActive(true);
        }

        private static void CancelPendingInitialization()
        {
            if (_tabBeforeInitialization == null) return;
            if (_tab != null && _tab.getAsset() == null && _tab.gameObject.activeSelf)
                _tab.gameObject.SetActive(false);
            RestoreTabBeforeInitialization();
            _tabBeforeInitialization = null;
        }

        private static void RestoreTabBeforeInitialization()
        {
            PowersTab previous = _tabBeforeInitialization;
            if (previous == null || previous == _tab || previous.isCurrentPowerTabSelected()) return;
            if (PowerTabController.instance?.tab_main == previous)
            {
                PowersTab.unselect();
                return;
            }
            previous.showTab(null);
        }

        private static bool HasSelectedCity()
        {
            return IsValidCity(ShiLineageMapModeService.SelectedCity);
        }

        private static bool IsValidCity(City pCity) =>
            pCity?.data != null && !pCity.isRekt() && pCity.isAlive();

        private static void CopyRect(RectTransform pSource, RectTransform pTarget)
        {
            if (pSource == null || pTarget == null) return;
            pTarget.anchorMin = pSource.anchorMin;
            pTarget.anchorMax = pSource.anchorMax;
            pTarget.pivot = pSource.pivot;
            pTarget.anchoredPosition = pSource.anchoredPosition;
            pTarget.sizeDelta = pSource.sizeDelta;
            pTarget.localRotation = pSource.localRotation;
            pTarget.localScale = pSource.localScale;
        }
    }
}
