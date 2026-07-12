using AncientWarfare3.core.court;
using AncientWarfare3.ui.items;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class SchoolMapBottomBarController
    {
        public const string TabId = "selected_aw_school_city";

        private static PowersTab _tab;
        private static PowersTab _tabBeforeInitialization;
        private static SchoolCompositionElement _element;
        private static City _pendingCity;
        private static long _cityId = -1L;
        private static int _generation = -1;
        private static bool _showRequested;

        public static void Show(City pCity)
        {
            if (!IsValidCity(pCity))
            {
                Hide();
                return;
            }

            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(pCity, pEnsureFresh: true);
            if (snapshot == null)
            {
                Hide();
                return;
            }

            if (!EnsureTab()) return;
            _pendingCity = pCity;
            _cityId = pCity.data.id;
            _generation = snapshot.Generation;
            _showRequested = true;
            TryPresent(snapshot);
        }

        public static void ProcessFrame()
        {
            if (!SchoolMapModeService.IsActive())
            {
                Hide();
                return;
            }

            City city = SelectedMetas.selected_city;
            if (!IsValidCity(city) || SelectedObjects.getSelectedNanoObject() != city)
            {
                Hide();
                return;
            }

            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
            if (snapshot == null)
            {
                Hide();
                return;
            }

            if (_cityId != city.data.id || _generation != snapshot.Generation)
            {
                Show(city);
                return;
            }

            if (_showRequested) TryPresent(snapshot);
        }

        public static void Hide()
        {
            _showRequested = false;
            _pendingCity = null;
            _cityId = -1L;
            _generation = -1;
            if (_element != null) _element.gameObject.SetActive(false);
            if (_tab != null && _tab.getAsset() != null && _tab.isCurrentPowerTabSelected())
                PowersTab.unselect();
        }

        private static bool EnsureTab()
        {
            if (_tab != null) return true;
            PowersTab template = PowerTabController.instance?.tab_selected_city;
            if (template == null || template.transform.parent == null) return false;

            PowerTabAsset asset = AssetManager.power_tab_library.get(TabId);
            if (asset == null)
            {
                asset = AssetManager.power_tab_library.add(new PowerTabAsset
                {
                    id = TabId
                });
            }
            ConfigureAsset(asset);

            _tabBeforeInitialization = PowersTab.getActiveTab();
            var tabObject = new GameObject(TabId, typeof(RectTransform));
            tabObject.SetActive(false);
            tabObject.layer = template.gameObject.layer;
            tabObject.transform.SetParent(template.transform.parent, false);
            CopyRect(template.GetComponent<RectTransform>(), tabObject.GetComponent<RectTransform>());
            tabObject.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            _tab = tabObject.AddComponent<PowersTab>();
            _element = SchoolCompositionElement.Create(_tab.transform);
            _element.gameObject.SetActive(false);

            // PowersTab resolves its PowerTabAsset in Start, so let it initialize before TryPresent opens it.
            tabObject.SetActive(true);
            return true;
        }

        private static void ConfigureAsset(PowerTabAsset pAsset)
        {
            pAsset.id = TabId;
            pAsset.tab_type_selected = true;
            pAsset.meta_type = MetaType.City;
            pAsset.window_id = "city";
            pAsset.get_power_tab = () => _tab;
            pAsset.on_update_check_active = _ => SchoolMapModeService.IsActive() && HasSelectedCity();
        }

        private static void TryPresent(CitySchoolSnapshot pSnapshot)
        {
            if (!_showRequested || _tab == null || _tab.getAsset() == null ||
                _element == null || !IsValidCity(_pendingCity) || pSnapshot == null)
                return;

            _element.Bind(_pendingCity, pSnapshot);
            _tab.sortButtons();
            _tab.recalc();
            if (!_tab.isCurrentPowerTabSelected())
            {
                if (_tabBeforeInitialization != null && _tabBeforeInitialization != _tab &&
                    _tabBeforeInitialization.gameObject.activeSelf)
                    _tabBeforeInitialization.hideTab();
                _tabBeforeInitialization = null;
                _tab.showTab(null);
            }
            _showRequested = false;
        }

        private static bool HasSelectedCity()
        {
            City city = SelectedMetas.selected_city;
            return IsValidCity(city) && SelectedObjects.getSelectedNanoObject() == city;
        }

        private static bool IsValidCity(City pCity)
        {
            return pCity?.data != null && !pCity.isRekt() && pCity.isAlive();
        }

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
