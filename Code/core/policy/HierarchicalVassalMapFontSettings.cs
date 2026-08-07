using System;
using System.Collections.Generic;
using System.Reflection;
using NeoModLoader.General;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    public static class HierarchicalVassalMapFontSettings
    {
        public const string OptionId = "AW3_USE_BUNDLED_HIERARCHICAL_VASSAL_MAP_FONT";
        private const string ConfigGroupId = "AWMapModeSettings";
        private const string ConfigCallback =
            "HierarchicalVassalMapFontSettings:SelectFont";
        private const string BundledFontDisplayName = "AW3 Beiwei";
        private static readonly string[] LocaleLanguages = { "cz", "en", "ch" };

        private static readonly List<string> FontNames = new List<string>();
        private static bool _catalogReady;
        private static int _selectedIndex;
        private static Font _selectedFont;
        private static int _selectedFontSize;

        public static bool UseBundledFont
        {
            get { return SelectedIndex == 0; }
        }

        public static int FontCount
        {
            get
            {
                EnsureCatalog();
                return FontNames.Count;
            }
        }

        public static int SelectedIndex
        {
            get
            {
                EnsureCatalog();
                return _selectedIndex;
            }
        }

        public static string SelectedFontName
        {
            get
            {
                EnsureCatalog();
                return GetFontName(_selectedIndex);
            }
        }

        public static string GetFontName(int pIndex)
        {
            EnsureCatalog();
            if (FontNames.Count == 0)
                return Localized("aw3_map_font_bundled_name",
                    BundledFontDisplayName);
            int index = HierarchicalVassalMapFontRules.ClampIndex(
                pIndex, FontNames.Count);
            if (index == 0)
                return Localized("aw3_map_font_bundled_name",
                    BundledFontDisplayName);
            string format = Localized("aw3_map_font_system_name",
                "System font: {0}");
            try { return string.Format(format, FontNames[index]); }
            catch { return FontNames[index]; }
        }

        internal static string GetFontFamilyName(int pIndex)
        {
            EnsureCatalog();
            if (FontNames.Count == 0) return "";
            int index = HierarchicalVassalMapFontRules.ClampIndex(pIndex,
                FontNames.Count);
            return FontNames[index];
        }

        public static void InitializeConfig()
        {
            EnsureCatalog();
            RegisterOptionLocales();

            ModConfig config = ModClass.Instance?.GetConfig();
            if (config == null) return;

            Dictionary<string, ModConfigItem> group;
            try
            {
                group = config[ConfigGroupId];
            }
            catch
            {
                return;
            }
            if (!group.TryGetValue(OptionId, out ModConfigItem item)) return;

            int selected = HierarchicalVassalMapFontRules.ClampIndex(
                item.IntVal, FontNames.Count);
            if (selected < 0) selected = 0;
            SetMaxOptionCount(item, FontNames.Count);
            item.SetValue(selected, true);
            SelectIndex(selected);
        }

        public static void SelectFont(int pIndex)
        {
            EnsureCatalog();
            SelectIndex(pIndex);
        }

        internal static void PersistSelectedFont()
        {
            EnsureCatalog();
            SyncConfigSelection();
        }

        internal static Font TryCreateSelectedFont(int pSize)
        {
            EnsureCatalog();
            if (UseBundledFont || FontNames.Count == 0) return null;
            int requestedSize = Math.Max(1, pSize);
            if (_selectedFont != null && _selectedFontSize == requestedSize)
                return _selectedFont;
            try
            {
                _selectedFont = Font.CreateDynamicFontFromOSFont(
                    new[] { GetFontFamilyName(_selectedIndex) },
                    requestedSize);
                _selectedFontSize = requestedSize;
                return _selectedFont;
            }
            catch
            {
                _selectedFont = null;
                _selectedFontSize = 0;
                return null;
            }
        }

        private static void EnsureCatalog()
        {
            if (_catalogReady) return;
            _catalogReady = true;
            FontNames.Clear();
            FontNames.Add(BundledFontDisplayName);
            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                if (installed == null) return;
                Array.Sort(installed, StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < installed.Length; index++)
                {
                    string name = installed[index];
                    if (string.IsNullOrWhiteSpace(name) || ContainsFontName(name))
                        continue;
                    FontNames.Add(name);
                }
            }
            catch
            {
                // The bundled font remains available if Unity cannot enumerate OS fonts.
            }
        }

        private static bool ContainsFontName(string pName)
        {
            for (int index = 0; index < FontNames.Count; index++)
                if (string.Equals(FontNames[index], pName,
                        StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void SelectIndex(int pIndex)
        {
            if (FontNames.Count == 0) return;
            _selectedIndex = Math.Max(0, Math.Min(pIndex, FontNames.Count - 1));
            _selectedFont = null;
            _selectedFontSize = 0;
            HierarchicalVassalMapModeLabelLayer.RefreshMapFont();
        }

        private static void RegisterOptionLocales()
        {
            string currentLanguage = string.Empty;
            try
            {
                currentLanguage = LocalizedTextManager.instance?.language ??
                    string.Empty;
            }
            catch
            {
                // The current language may not be available during early load.
            }

            for (int index = 0; index < FontNames.Count; index++)
            {
                string key = OptionId + " Option " + index;
                string value = GetFontName(index);
                for (int languageIndex = 0;
                     languageIndex < LocaleLanguages.Length;
                     languageIndex++)
                {
                    string language = LocaleLanguages[languageIndex];
                    if (string.Equals(language, currentLanguage,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            LM.AddToCurrentLocale(key, value);
                            continue;
                        }
                        catch
                        {
                            // Fall through to the persistent locale map.
                        }
                    }
                    LM.Add(language, key, value);
                }
            }
        }

        private static void SyncConfigSelection()
        {
            try
            {
                ModConfig config = ModClass.Instance?.GetConfig();
                if (config == null) return;

                Dictionary<string, ModConfigItem> group;
                try
                {
                    group = config[ConfigGroupId];
                }
                catch
                {
                    return;
                }
                if (!group.TryGetValue(OptionId, out ModConfigItem item))
                    return;

                item.SetValue(_selectedIndex, true);
                config.Save();
            }
            catch
            {
                // The map button must remain usable if config persistence is unavailable.
            }
        }

        private static string Localized(string pKey, string pFallback)
        {
            try
            {
                string value = LM.Get(pKey);
                return string.IsNullOrWhiteSpace(value) || value == pKey
                    ? pFallback
                    : value;
            }
            catch { return pFallback; }
        }

        private static void SetMaxOptionCount(ModConfigItem pItem,
            int pOptionCount)
        {
            try
            {
                PropertyInfo property = typeof(ModConfigItem).GetProperty(
                    "MaxIntVal", BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
                MethodInfo setter = property?.GetSetMethod(true);
                setter?.Invoke(pItem, new object[] { Math.Max(1, pOptionCount) });
            }
            catch
            {
                // Older NML builds keep the static schema range.
            }
        }
    }
}
