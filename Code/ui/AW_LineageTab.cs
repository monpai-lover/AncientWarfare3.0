using System.Collections.Generic;
using AncientWarfare3.core.policy;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;

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

            PowerButton techMapButton = PowerButtonCreator.CreateToggleButton(
                TechMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconXias"));
            if (techMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, techMapButton);

            PowerButton coreMapButton = PowerButtonCreator.CreateToggleButton(
                WarCoreMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/icons/iconMap")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge"));
            if (coreMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, coreMapButton);

            PowerButton claimMapButton = PowerButtonCreator.CreateToggleButton(
                WarClaimMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/wars/war_reclaim")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            if (claimMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, claimMapButton);

            PowerButton vassalMapButton = PowerButtonCreator.CreateToggleButton(
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

            PowerButton mandateMapButton = PowerButtonCreator.CreateToggleButton(
                MandateDynastyMapModeService.POWER_ID,
                SpriteTextureLoader.getSprite("ui/Icons/traits/iconTianming")
                ?? SpriteTextureLoader.getSprite("ui/icons/iconDiplomacy"));
            if (mandateMapButton != null) tab.AddPowerButton(GROUP_LINEAGE, mandateMapButton);

            PowerButton mandateCoreMapButton = PowerButtonCreator.CreateToggleButton(
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

        private static void OpenOverview()
        {
            windows.LineageOverviewWindow.Open();
        }

        private static void OpenRoster()
        {
            windows.KingdomRosterWindow.Open();
        }
    }
}
