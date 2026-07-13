using System;
using System.Collections.Generic;
using AncientWarfare3.core.policy;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;

namespace AncientWarfare3.ui
{
    internal static class AW_LineageTab
    {
        private const string TAB_ID = "AW3Lineage";
        private const string GROUP_LINEAGE = "lineage";
        private const string GROUP_CREATURE = "creature";
        private static bool _inited;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;

            PowersTab tab = TabManager.CreateTab(
                TAB_ID,
                "AW3 Lineage",
                "AW3 Lineage Description",
                SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            ApplyNativeTabSprites(tab);

            tab.SetLayout(new List<string> { GROUP_LINEAGE, GROUP_CREATURE });

            PowerButton overviewButton = PowerButtonCreator.CreateSimpleButton(
                "aw_lineage_overview_btn",
                () => OpenOverview(),
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            tab.AddPowerButton(GROUP_LINEAGE, overviewButton);

            PowerButton rosterButton = PowerButtonCreator.CreateSimpleButton(
                "aw_kingdom_roster_btn",
                () => OpenRoster(),
                SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            tab.AddPowerButton(GROUP_LINEAGE, rosterButton);

            PowerButton schoolMapButton = CreateMapModeToggleButton(
                SchoolMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconRujia")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            if (schoolMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, schoolMapButton);

            PowerButton schoolOverviewButton = PowerButtonCreator.CreateSimpleButton(
                "aw_school_overview_btn",
                () => windows.SchoolWindow.OpenSchool(),
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/traits/iconRujia"));
            if (schoolOverviewButton != null) tab.AddPowerButton(GROUP_LINEAGE, schoolOverviewButton);

            PowerButton schoolRosterButton = PowerButtonCreator.CreateSimpleButton(
                "aw_school_roster_btn",
                () => windows.SchoolRosterWindow.Open(),
                SpriteTextureLoader.getSprite("ui/icons/iconClan")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/traits/iconRujia"));
            if (schoolRosterButton != null) tab.AddPowerButton(GROUP_LINEAGE, schoolRosterButton);

            PowerButton techMapButton = CreateMapModeToggleButton(
                TechMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            if (techMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, techMapButton);

            PowerButton coreMapButton = CreateMapModeToggleButton(
                WarCoreMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconMap")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            if (coreMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, coreMapButton);

            PowerButton vassalMapButton = CreateMapModeToggleButton(
                VassalMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/wars/war_vassal")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            if (vassalMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, vassalMapButton);

            PowerButton mandateButton = PowerButtonCreator.CreateSimpleButton(
                "aw_mandate_dynasty_btn",
                () => windows.MandateDynastyWindow.Open(),
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconTianming")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconKings"));
            if (mandateButton != null) tab.AddPowerButton(GROUP_LINEAGE, mandateButton);

            PowerButton mandateMapButton = CreateMapModeToggleButton(
                MandateDynastyMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconTianming")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            if (mandateMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, mandateMapButton);

            PowerButton mandateCoreMapButton = CreateMapModeToggleButton(
                MandateCoreMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconMap")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            if (mandateCoreMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, mandateCoreMapButton);

            PowerButton vassalSetButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.VASSAL_SET,
                SpriteTextureLoader.getSprite("ui/wars/war_vassal")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            if (vassalSetButton != null) tab.AddPowerButton(GROUP_LINEAGE, vassalSetButton);

            PowerButton vassalRemoveButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.VASSAL_REMOVE,
                SpriteTextureLoader.getSprite("ui/wars/war_independent")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconPeace"));
            if (vassalRemoveButton != null) tab.AddPowerButton(GROUP_LINEAGE, vassalRemoveButton);

            PowerButton spawnButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.SPAWN_XIA,
                SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            tab.AddPowerButton(GROUP_CREATURE, spawnButton);

            PowerButton figureToggle = PowerButtonCreator.CreateToggleButton(
                content.figures.HistoricalFigureService.TOGGLE_POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/iconKings"));
            if (figureToggle != null) tab.AddPowerButton(GROUP_CREATURE, figureToggle);

            tab.UpdateLayout();
        }

        private static void ApplyNativeTabSprites(PowersTab pTab)
        {
            if (pTab == null) return;
            PowersTab native = PowerTabController.instance?.tab_main ??
                               ResourcesFinder.FindResource<PowersTab>("tab_main");
            if (native == null || ReferenceEquals(native, pTab)) return;
            pTab.image_normal = native.image_normal;
            pTab.image_selected = native.image_selected;
        }

        private static void OpenOverview()
        {
            windows.LineageOverviewWindow.Open();
        }

        private static void OpenRoster()
        {
            windows.KingdomRosterWindow.Open();
        }

        private static PowerButton CreateMapModeToggleButton(string pPowerId, Sprite pIcon)
        {
            GodPower power = AssetManager.powers.get(pPowerId);
            OptionAsset optionAsset = power?.option_asset;
            if (optionAsset == null && !string.IsNullOrEmpty(power?.toggle_name))
                optionAsset = AssetManager.options_library.get(power.toggle_name);
            int optionCount = (optionAsset?.max_value ?? 0) + 1;
            if (power?.multi_toggle == true && optionCount > 1)
                return CreateLayerMapModeToggleButton(pPowerId, pIcon, power, optionCount);

            bool suppressNmlAutoToggle = AWMapModeButtonRules.ShouldSuppressNmlAutoToggle(
                power?.map_modes_switch ?? false,
                power?.toggle_action != null);
            return PowerButtonCreator.CreateToggleButton(pPowerId, pIcon,
                pNoAutoSetToggleAction: suppressNmlAutoToggle);
        }

        private static PowerButton CreateLayerMapModeToggleButton(
            string pPowerId,
            Sprite pIcon,
            GodPower pPower,
            int pOptionCount)
        {
            string prefabId = pOptionCount <= 2 ? "kingdom_layer" : "subspecies_layer";
            PowerButton prefab = ResourcesFinder.FindResource<PowerButton>(prefabId);
            if (prefab == null)
            {
                bool suppressNmlAutoToggle = AWMapModeButtonRules.ShouldSuppressNmlAutoToggle(
                    pPower?.map_modes_switch ?? false,
                    pPower?.toggle_action != null);
                return PowerButtonCreator.CreateToggleButton(pPowerId, pIcon,
                    pNoAutoSetToggleAction: suppressNmlAutoToggle);
            }

            bool foundActive = prefab.gameObject.activeSelf;
            if (foundActive)
                prefab.gameObject.SetActive(false);

            PowerButton button = UnityEngine.Object.Instantiate(prefab);

            if (foundActive)
                prefab.gameObject.SetActive(true);

            button.name = pPowerId;
            if (pIcon != null)
            {
                button.icon.sprite = pIcon;
                button.icon.overrideSprite = pIcon;
            }

            button.open_window_id = null;
            button.type = PowerButtonType.Special;
            button.transform.localScale = Vector3.one;

            TipButton tipButton = button.GetComponent<TipButton>();
            if (tipButton != null)
            {
                tipButton.textOnClick = pPowerId;
                tipButton.textOnClickDescription = pPowerId + "_description";
                tipButton.text_description_2 = "hotkey_tip_zone_switch";
            }

            if (!PlayerConfig.dict.TryGetValue(pPower.toggle_name, out PlayerOptionData optionData))
            {
                optionData = PlayerConfig.instance.data.add(new PlayerOptionData(pPower.toggle_name)
                {
                    boolVal = false,
                    intVal = 0
                });
            }

            button.transform.Find("ToggleIcon")?.GetComponent<ToggleIcon>()?.updateIcon(optionData.boolVal);
            button.gameObject.SetActive(true);
            button.checkToggleIcon();
            return button;
        }
    }
}
