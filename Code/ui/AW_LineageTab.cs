using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.ui;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui
{
    internal static class AW_LineageTab
    {
        private const string TAB_ID = "AW3Lineage";
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

            tab.SetLayout(new List<string> { AWLineageTabLayoutRules.Manual });
            Dictionary<string, List<PowerButton>> groups = CreateButtonGroups();

            PowerButton spawnButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.SPAWN_XIA,
                SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            Register(groups, AWLineageTabLayoutRules.XiaSpawn, spawnButton);

            PowerButton overviewButton = PowerButtonCreator.CreateSimpleButton(
                "aw_lineage_overview_btn",
                () => OpenOverview(),
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            Register(groups, AWLineageTabLayoutRules.Archives, overviewButton);

            PowerButton historicalCardsButton = PowerButtonCreator.CreateSimpleButton(
                "aw_historical_figure_cards_btn",
                () => windows.HistoricalFigureDrawWindow.Open(),
                SpriteTextureLoader.getSprite("ui/Icons/iconKings")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            Register(groups, AWLineageTabLayoutRules.Archives, historicalCardsButton);

            PowerButton rosterButton = PowerButtonCreator.CreateSimpleButton(
                "aw_kingdom_roster_btn",
                () => OpenRoster(),
                SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            Register(groups, AWLineageTabLayoutRules.Archives, rosterButton);

            PowerButton schoolMapButton = CreateMapModeToggleButton(
                SchoolMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconRujia")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            Register(groups, AWLineageTabLayoutRules.Schools, schoolMapButton);

            PowerButton shiMapButton = CreateMapModeToggleButton(
                ShiLineageMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            Register(groups, AWLineageTabLayoutRules.Archives, shiMapButton);

            PowerButton schoolOverviewButton = PowerButtonCreator.CreateSimpleButton(
                "aw_school_overview_btn",
                () => windows.SchoolWindow.OpenSchool(),
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/traits/iconRujia"));
            Register(groups, AWLineageTabLayoutRules.Schools, schoolOverviewButton);

            PowerButton schoolRosterButton = PowerButtonCreator.CreateSimpleButton(
                "aw_school_roster_btn",
                () => windows.SchoolRosterWindow.Open(),
                SpriteTextureLoader.getSprite("ui/icons/iconClan")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/traits/iconRujia"));
            Register(groups, AWLineageTabLayoutRules.Schools, schoolRosterButton);

            PowerButton techMapButton = CreateMapModeToggleButton(
                TechMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            Register(groups, AWLineageTabLayoutRules.Schools, techMapButton);

            PowerButton coreMapButton = CreateMapModeToggleButton(
                WarCoreMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconMap")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            Register(groups, AWLineageTabLayoutRules.Territory, coreMapButton);

            PowerButton vassalMapButton = CreateMapModeToggleButton(
                VassalMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/wars/war_vassal")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            Register(groups, AWLineageTabLayoutRules.Territory, vassalMapButton);

            PowerButton hierarchicalVassalMapButton =
                CreateMapModeToggleButton(
                    HierarchicalVassalMapModeService.POWER_ID,
                    SpriteTextureLoader.getSprite("ui/icons/iconMap")
                    ?? SpriteTextureLoader.getSprite(
                        "ui/icons/iconDiplomacy"));
            Register(groups, AWLineageTabLayoutRules.Territory,
                hierarchicalVassalMapButton);

            PowerButton mandateButton = PowerButtonCreator.CreateSimpleButton(
                "aw_mandate_dynasty_btn",
                () => windows.MandateDynastyWindow.Open(),
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconTianming")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconKings"));
            Register(groups, AWLineageTabLayoutRules.Mandate, mandateButton);

            PowerButton mandateMapButton = CreateMapModeToggleButton(
                MandateDynastyMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconTianming")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            Register(groups, AWLineageTabLayoutRules.Mandate, mandateMapButton);

            PowerButton feudatoryMapButton = CreateMapModeToggleButton(
                FeudatoryMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconzhuhou")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconMap"));
            Register(groups, AWLineageTabLayoutRules.Territory, feudatoryMapButton);

            PowerButton grantMandateButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.GRANT_MANDATE,
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconTianming")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconKings"));
            Register(groups, AWLineageTabLayoutRules.Mandate, grantMandateButton);

            PowerButton spawnBanditButton =
                PowerButtonCreator.CreateGodPowerButton(
                    content.GodPowerLibrary.SPAWN_BANDIT_STRONGHOLD,
                    SpriteTextureLoader.getSprite(
                        "ui/wars/war_rebellion") ??
                    SpriteTextureLoader.getSprite(
                        "ui/Icons/traits/iconrebel"));
            Register(groups, AWLineageTabLayoutRules.Mandate,
                spawnBanditButton);

            PowerButton amnestyBanditButton =
                PowerButtonCreator.CreateGodPowerButton(
                    content.GodPowerLibrary.AMNESTY_BANDIT,
                    SpriteTextureLoader.getSprite(
                        content.GodPowerLibrary.BanditAmnestyIconPath)
                    ?? SpriteTextureLoader.getSprite("ui/icons/iconPeace")
                    ?? SpriteTextureLoader.getSprite("ui/wars/war_rebellion"));
            Register(groups, AWLineageTabLayoutRules.Mandate,
                amnestyBanditButton);

            PowerButton deJureRegionCreateButton =
                PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.DE_JURE_REGION_CREATE,
                SpriteTextureLoader.getSprite("ui/Icons/aw_de_jure_region")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconMap"));
            Register(groups, AWLineageTabLayoutRules.Administration,
                deJureRegionCreateButton);

            PowerButton deJureRegionAssignButton =
                PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.DE_JURE_REGION_ASSIGN,
                SpriteTextureLoader.getSprite("ui/Icons/aw_de_jure_region")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconMap"));
            Register(groups, AWLineageTabLayoutRules.Administration,
                deJureRegionAssignButton);

            PowerButton deJureRegionRetireButton =
                PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.DE_JURE_REGION_RETIRE,
                SpriteTextureLoader.getSprite("ui/icons/iconDeleteWorld")
                ?? SpriteTextureLoader.getSprite("ui/Icons/aw_de_jure_region")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconMap"));
            Register(groups, AWLineageTabLayoutRules.Administration,
                deJureRegionRetireButton);

            PowerButton mandateCoreMapButton = CreateMapModeToggleButton(
                MandateCoreMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconMap")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            Register(groups, AWLineageTabLayoutRules.Mandate, mandateCoreMapButton);

            PowerButton vassalSetButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.VASSAL_SET,
                SpriteTextureLoader.getSprite("ui/wars/war_vassal")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            Register(groups, AWLineageTabLayoutRules.Administration, vassalSetButton);

            PowerButton vassalRemoveButton = PowerButtonCreator.CreateGodPowerButton(
                content.GodPowerLibrary.VASSAL_REMOVE,
                SpriteTextureLoader.getSprite("ui/wars/war_independent")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconPeace"));
            Register(groups, AWLineageTabLayoutRules.Administration, vassalRemoveButton);

            PowerButton royalEnfeoffmentButton =
                PowerButtonCreator.CreateGodPowerButton(
                    content.GodPowerLibrary.ROYAL_ENFEOFFMENT,
                    SpriteTextureLoader.getSprite("ui/wars/war_vassal")
                    ?? SpriteTextureLoader.getSprite("ui/icons/iconKings"));
            Register(groups, AWLineageTabLayoutRules.Administration,
                royalEnfeoffmentButton);

            PowerButton militaryGovernorateButton =
                PowerButtonCreator.CreateGodPowerButton(
                    content.GodPowerLibrary.MILITARY_GOVERNORATE,
                    SpriteTextureLoader.getSprite("ui/wars/war_vassal")
                    ?? SpriteTextureLoader.getSprite("ui/icons/iconKings"));
            Register(groups, AWLineageTabLayoutRules.Administration,
                militaryGovernorateButton);

            PowerButton settingsButton = PowerButtonCreator.CreateSimpleButton(
                "aw_settings_btn",
                () => OpenSettings(),
                SpriteTextureLoader.getSprite("ui/icons/iconOptions")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            Register(groups, AWLineageTabLayoutRules.Settings, settingsButton);

            PowerButton figureToggle = PowerButtonCreator.CreateToggleButton(
                content.figures.HistoricalFigureService.TOGGLE_POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/iconKings"));
            Register(groups, AWLineageTabLayoutRules.Settings, figureToggle);

            PowerButton diplomacyAiToggle =
                PowerButtonCreator.CreateToggleButton(
                    DiplomacyAiRules.TogglePowerId,
                    SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"),
                    pNoAutoSetToggleAction: true);
            Register(groups, AWLineageTabLayoutRules.Settings,
                diplomacyAiToggle);

            PowerButton supportersButton = PowerButtonCreator.CreateSimpleButton(
                "aw_supporter_leaderboard_btn",
                () => windows.SupporterLeaderboardWindow.Open(),
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconClan"));
            Register(groups, AWLineageTabLayoutRules.Settings,
                supportersButton);

            ApplyNativeLayout(tab, groups);
            CountyMapRenameOverlay.Attach(tab.transform.root);
        }

        private static Dictionary<string, List<PowerButton>> CreateButtonGroups()
        {
            var groups = new Dictionary<string, List<PowerButton>>();
            foreach (string groupId in AWLineageTabLayoutRules.OrderedGroups)
                groups[groupId] = new List<PowerButton>();
            return groups;
        }

        private static void Register(Dictionary<string, List<PowerButton>> pGroups,
            string pGroupId, PowerButton pButton)
        {
            if (pButton == null || pGroups == null ||
                !pGroups.TryGetValue(pGroupId, out List<PowerButton> buttons)) return;
            buttons.Add(pButton);
        }

        private static void ApplyNativeLayout(PowersTab pTab,
            Dictionary<string, List<PowerButton>> pGroups)
        {
            GameObject linePrefab = ResourcesFinder.FindResource<GameObject>("_line");
            bool hasPreviousGroup = false;
            foreach (string groupId in AWLineageTabLayoutRules.OrderedGroups)
            {
                if (!pGroups.TryGetValue(groupId,
                        out List<PowerButton> buttons) || buttons.Count == 0)
                    continue;

                if (hasPreviousGroup && linePrefab != null)
                    AddNativeDivider(pTab, linePrefab, groupId);

                foreach (PowerButton button in buttons)
                    pTab.AddPowerButton(AWLineageTabLayoutRules.Manual, button);

                hasPreviousGroup = true;
            }

            // UpdateLayout refreshes NML's button/navigation cache. The final
            // positions deliberately come from the vanilla sibling-order
            // layout, which recognizes every _line-prefixed child as a group
            // boundary.
            pTab.UpdateLayout();
            pTab.sortButtons();
        }

        private static void AddNativeDivider(PowersTab pTab,
            GameObject pLinePrefab, string pFollowingGroupId)
        {
            GameObject divider = UnityEngine.Object.Instantiate(
                pLinePrefab, pTab.transform);
            divider.name = "_line_aw3_" + pFollowingGroupId;
            Image image = divider.GetComponent<Image>();
            if (image != null) image.enabled = true;
            divider.transform.localScale = new Vector3(1f, 48.3f, 1f);
            divider.SetActive(true);
        }

        private static void OpenOverview()
        {
            windows.LineageOverviewWindow.Open();
        }

        private static void OpenRoster()
        {
            windows.KingdomRosterWindow.Open();
        }

        private static void OpenSettings()
        {
            var config = global::AncientWarfare3.ModClass.Instance?.GetConfig();
            if (config == null) return;
            ModConfigureWindow.ShowWindow(config);
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
