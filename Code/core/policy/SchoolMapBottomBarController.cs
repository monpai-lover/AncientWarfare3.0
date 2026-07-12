using AncientWarfare3.core.court;
using AncientWarfare3.ui.items;

namespace AncientWarfare3.core.policy
{
    internal static class SchoolMapBottomBarController
    {
        private static SchoolCompositionElement _element;
        private static long _cityId = -1L;
        private static int _generation = -1;

        public static void Show(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt() || !pCity.isAlive())
            {
                Hide();
                return;
            }

            SchoolCompositionElement element = EnsureElement();
            if (element == null) return;
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(pCity, pEnsureFresh: true);
            if (snapshot == null)
            {
                Hide();
                return;
            }

            element.Bind(pCity, snapshot);
            _cityId = pCity.data.id;
            _generation = snapshot.Generation;
            PowersTab tab = PowerTabController.instance?.tab_selected_city;
            tab?.sortButtons();
            tab?.recalc();
        }

        public static void ProcessFrame()
        {
            if (!SchoolMapModeService.IsActive())
            {
                Hide();
                return;
            }

            City city = SelectedMetas.selected_city;
            if (city?.data == null || city.isRekt() || !city.isAlive() ||
                SelectedObjects.getSelectedNanoObject() != city)
            {
                Hide();
                return;
            }

            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
            if (_element == null || !_element.gameObject.activeSelf || _cityId != city.data.id ||
                snapshot != null && _generation != snapshot.Generation)
                Show(city);
        }

        public static void Hide()
        {
            if (_element != null && _element.gameObject.activeSelf)
            {
                _element.gameObject.SetActive(false);
                PowersTab tab = PowerTabController.instance?.tab_selected_city;
                tab?.sortButtons();
                tab?.recalc();
            }
            _cityId = -1L;
            _generation = -1;
        }

        private static SchoolCompositionElement EnsureElement()
        {
            if (_element != null) return _element;
            PowersTab tab = PowerTabController.instance?.tab_selected_city;
            if (tab == null) return null;
            _element = SchoolCompositionElement.Create(tab.transform);
            return _element;
        }
    }
}
