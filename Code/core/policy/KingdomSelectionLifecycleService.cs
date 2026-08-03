using System;

namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// Clears UI and map-mode references before a kingdom is disposed.
    /// Vanilla only clears the relations-tool selection, so a visible
    /// kingdom window can otherwise retain a dead object across a frame.
    /// </summary>
    internal static class KingdomSelectionLifecycleService
    {
        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom == null) return;

            try
            {
                if (ReferenceEquals(SelectedMetas.selected_kingdom, pKingdom))
                    SelectedMetas.selected_kingdom = null;
            }
            catch { }

            try
            {
                City selectedCity = SelectedMetas.selected_city;
                if (selectedCity != null &&
                    ReferenceEquals(selectedCity.kingdom, pKingdom))
                    SelectedMetas.selected_city = null;
            }
            catch { }

            ClearWindowHistory(pKingdom);

            try
            {
                HierarchicalVassalMapModeService.OnKingdomDestroying(pKingdom);
            }
            catch { }

            try { AWMapModeMetaLibrary.ClearDynamicMetaCache(); }
            catch { }
        }

        private static void ClearWindowHistory(Kingdom pKingdom)
        {
            try
            {
                if (WindowHistory.list == null) return;
                for (int index = 0; index < WindowHistory.list.Count; index++)
                {
                    WindowHistoryData item = WindowHistory.list[index];
                    if (!ReferenceEquals(item.kingdom, pKingdom)) continue;
                    item.kingdom = null;
                    WindowHistory.list[index] = item;
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Kingdom window history cleanup failed: " +
                    error.Message);
            }
        }
    }
}
