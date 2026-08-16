using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.performance;
using AncientWarfare3.ui;
using AncientWarfare3.ui.windows;
using UnityEngine;

namespace AncientWarfare3.content
{
    public static class GodPowerLibrary
    {
        public const string SPAWN_XIA = XiaRace.ID;
        public const string VASSAL_SET = "aw_vassal_set";
        public const string VASSAL_REMOVE = "aw_vassal_remove";
        public const string ROYAL_ENFEOFFMENT = "aw_royal_enfeoffment";
        public const string MILITARY_GOVERNORATE =
            "aw_military_governorate";
        public const string GRANT_MANDATE = "aw_grant_mandate";
        public const string SPAWN_BANDIT_STRONGHOLD =
            "aw_spawn_bandit_stronghold";

        private static Kingdom _pendingVassal;
        private static readonly List<City> SchoolNameplateCandidates = new List<City>();
        private static readonly HashSet<long> SchoolNameplateCandidateIds = new HashSet<long>();
        private static readonly Comparison<City> SchoolNameplateCityOrder = CompareSchoolNameplateCities;
        private static readonly Dictionary<long, City> FeudatoryNameplateAnchors =
            new Dictionary<long, City>();
        private static readonly Dictionary<long, TileZone> FeudatoryNameplateZones =
            new Dictionary<long, TileZone>();
        private static readonly Dictionary<long, FeudatorySnapshot> FeudatoryNameplateSnapshots =
            new Dictionary<long, FeudatorySnapshot>();
        private static readonly List<long> FeudatoryNameplateIds = new List<long>();
        private const float NameplateCandidateRefreshSeconds = 0.25f;
        private static bool _schoolNameplateCandidatesReady;
        private static ulong _schoolNameplateCandidateSignature;
        private static float _schoolNameplateCandidateRefreshAt;
        private static bool _feudatoryNameplateCandidatesReady;
        private static ulong _feudatoryNameplateCandidateSignature;
        private static float _feudatoryNameplateCandidateRefreshAt;

        public static void Init()
        {
            RegisterSpawnXia();
            RegisterTechMapMode();
            RegisterWarMapModes();
            RegisterVassalMapMode();
            RegisterHierarchicalVassalMapMode();
            RegisterMandateMapModes();
            RegisterFeudatoryMapMode();
            RegisterSchoolMapMode();
            RegisterDiplomacyAiToggle();
            LinkMapModeAssets();
            AWMapModeMetaLibrary.Init();
            RegisterMapModeNameplates();
            RegisterVassalPowers();
            RegisterRoyalEnfeoffmentPower();
            RegisterMilitaryGovernoratePower();
            RegisterMandateGrantPower();
            RegisterBanditStrongholdPower();
        }

        public static void ClearRuntime()
        {
            _pendingVassal = null;
            SchoolNameplateCandidates.Clear();
            SchoolNameplateCandidateIds.Clear();
            FeudatoryNameplateAnchors.Clear();
            FeudatoryNameplateZones.Clear();
            FeudatoryNameplateSnapshots.Clear();
            FeudatoryNameplateIds.Clear();
            _schoolNameplateCandidatesReady = false;
            _schoolNameplateCandidateSignature = 0UL;
            _schoolNameplateCandidateRefreshAt = 0f;
            _feudatoryNameplateCandidatesReady = false;
            _feudatoryNameplateCandidateSignature = 0UL;
            _feudatoryNameplateCandidateRefreshAt = 0f;
        }

        private static void RegisterSpawnXia()
        {
            if (AssetManager.powers.get(SPAWN_XIA) != null) return;

            if (AssetManager.powers.get("human") == null)
            {
                ModClass.LogWarning("spawn_xia: vanilla human power is missing, skip Xia spawn power.");
                return;
            }

            GodPower xia = AssetManager.powers.clone(SPAWN_XIA, "human");
            xia.name = SPAWN_XIA;
            xia.actor_asset_id = XiaRace.ID;
            xia.path_icon = "ui/Icons/iconXias";
        }

        private static void RegisterDiplomacyAiToggle()
        {
            const string optionId = DiplomacyAiRules.TogglePowerId;
            OptionAsset option = AssetManager.options_library.get(optionId);
            if (option == null)
            {
                option = AssetManager.options_library.add(new OptionAsset
                {
                    id = optionId,
                    default_bool = true,
                    type = OptionType.Bool
                });
            }
            else
            {
                option.default_bool = true;
                option.type = OptionType.Bool;
            }

            if (!PlayerConfig.dict.ContainsKey(optionId))
                PlayerConfig.instance.data.add(new PlayerOptionData(optionId)
                {
                    boolVal = true
                });

            GodPower power = AssetManager.powers.get(optionId);
            if (power == null)
            {
                power = AssetManager.powers.add(new GodPower
                {
                    id = optionId,
                    name = optionId,
                    path_icon = "ui/icons/iconDiplomacy",
                    toggle_name = optionId,
                    toggle_action = BuildBooleanToggleAction(SyncDiplomacyAiSetting)
                });
            }
            else
            {
                power.toggle_name = optionId;
                power.toggle_action = BuildBooleanToggleAction(SyncDiplomacyAiSetting);
            }

            SyncDiplomacyAiSetting();
        }

        private static void SyncDiplomacyAiSetting()
        {
            bool enabled = PlayerConfig.dict.TryGetValue(
                DiplomacyAiRules.TogglePowerId,
                out PlayerOptionData value) && value.boolVal;
            AWPerformanceSettings.SwitchDiplomacyAi(enabled);
        }

        private static void RegisterSchoolMapMode()
        {
            RegisterMapModeOption(SchoolMapModeService.POWER_ID);
            GodPower existing = AssetManager.powers.get(SchoolMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing, SchoolMapModeService.POWER_ID, RefreshSchoolMapMode);
                existing.click_special_action = new PowerActionWithID(SchoolMapClick);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = SchoolMapModeService.POWER_ID,
                name = SchoolMapModeService.POWER_ID,
                path_icon = "ui/Icons/traits/iconRujia",
                map_modes_switch = true,
                multi_toggle = false,
                toggle_name = AWMapModeMetaRules.ResolveOptionId(SchoolMapModeService.POWER_ID),
                force_map_mode = AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(RefreshSchoolMapMode),
                click_special_action = new PowerActionWithID(SchoolMapClick)
            });
        }

        private static void RefreshSchoolMapMode()
        {
            string optionId = AWMapModeMetaRules.ResolveOptionId(SchoolMapModeService.POWER_ID);
            if (!PlayerConfig.dict.TryGetValue(optionId, out PlayerOptionData data) || !data.boolVal)
            {
                SchoolMapModeService.DirtyMap();
                return;
            }
            SchoolMapModeService.Prepare();
        }

        private static bool SchoolMapClick(WorldTile pTile, string pPowerId)
        {
            return SchoolMapModeService.SelectCity(pTile, pPowerId);
        }

        private static void RegisterTechMapMode()
        {
            RegisterMapModeOption(TechMapModeService.POWER_ID, AWMapModeNameplateRules.GetTechZoneOptionLocaleIds());
            DisableLegacyDevelopmentMapMode();
            GodPower existing = AssetManager.powers.get(TechMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing, TechMapModeService.POWER_ID, TechMapModeService.DirtyMap);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = TechMapModeService.POWER_ID,
                name = TechMapModeService.POWER_ID,
                path_icon = "ui/icons/iconKnowledge",
                map_modes_switch = true,
                multi_toggle = AWMapModePowerRules.ShouldUseGodPowerMultiToggle(
                    AWMapModeNameplateRules.GetTechZoneOptionLocaleIds().Length),
                toggle_name = AWMapModeMetaRules.ResolveOptionId(TechMapModeService.POWER_ID),
                force_map_mode = AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(TechMapModeService.DirtyMap)
            });
        }

        private static void RegisterVassalMapMode()
        {
            RegisterMapModeOption(VassalMapModeService.POWER_ID);
            GodPower existing = AssetManager.powers.get(VassalMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing, VassalMapModeService.POWER_ID, VassalMapModeService.DirtyMap);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = VassalMapModeService.POWER_ID,
                name = VassalMapModeService.POWER_ID,
                path_icon = "ui/wars/war_vassal",
                map_modes_switch = true,
                multi_toggle = AWMapModePowerRules.ShouldUseGodPowerMultiToggle(
                    AWMapModeNameplateRules.GetDefaultZoneOptionLocaleIds().Length),
                toggle_name = AWMapModeMetaRules.ResolveOptionId(VassalMapModeService.POWER_ID),
                force_map_mode = AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(VassalMapModeService.DirtyMap)
            });
        }

        private static void RegisterHierarchicalVassalMapMode()
        {
            // Keep the default registration path for compatibility with
            // older saved option data before installing the two-layer list.
            RegisterMapModeOption(HierarchicalVassalMapModeService.POWER_ID);
            RegisterMapModeOption(
                HierarchicalVassalMapModeService.POWER_ID,
                AWMapModeNameplateRules.GetHierarchicalVassalZoneOptionLocaleIds());
            SyncHierarchicalVassalLayerOption();
            GodPower existing = AssetManager.powers.get(
                HierarchicalVassalMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing,
                    HierarchicalVassalMapModeService.POWER_ID,
                    RefreshHierarchicalVassalMapMode);
                existing.path_icon = "ui/icons/iconMap";
                existing.multi_toggle = true;
                existing.click_special_action = new PowerActionWithID(
                    HierarchicalVassalMapClick);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = HierarchicalVassalMapModeService.POWER_ID,
                name = HierarchicalVassalMapModeService.POWER_ID,
                path_icon = "ui/icons/iconMap",
                map_modes_switch = true,
                multi_toggle = true,
                toggle_name = AWMapModeMetaRules.ResolveOptionId(
                    HierarchicalVassalMapModeService.POWER_ID),
                force_map_mode =
                    AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(
                    RefreshHierarchicalVassalMapMode),
                click_special_action = new PowerActionWithID(
                    HierarchicalVassalMapClick)
            });
        }

        private static void RefreshHierarchicalVassalMapMode()
        {
            SyncHierarchicalVassalLayerOption();
            string optionId = AWMapModeMetaRules.ResolveOptionId(
                HierarchicalVassalMapModeService.POWER_ID);
            if (!PlayerConfig.dict.TryGetValue(optionId,
                    out PlayerOptionData data) || !data.boolVal)
            {
                HierarchicalVassalMapModeLabelLayer.
                    ObserveMapModeActive(false);
                return;
            }
            HierarchicalVassalMapModeLabelLayer.RequestRefresh();
        }

        private static void SyncHierarchicalVassalLayerOption()
        {
            try
            {
                int layer = AWMapModeMetaLibrary.HierarchicalVassalAsset?
                    .getZoneOptionState() ?? 0;
                HierarchicalVassalMapModeService.SetSelectedLayerFromOption(layer);
            }
            catch { }
        }

        private static bool HierarchicalVassalMapClick(WorldTile pTile,
            string pPowerId)
        {
            return HierarchicalVassalMapModeService.HandleZoneClick(pTile,
                pPowerId);
        }

        private static void RegisterWarMapModes()
        {
            RegisterCoreMapMode();
            DisableLegacyClaimMapMode();
        }

        private static void RegisterCoreMapMode()
        {
            RegisterMapModeOption(WarCoreMapModeService.POWER_ID, AWMapModeNameplateRules.GetWarZoneOptionLocaleIds());
            GodPower existing = AssetManager.powers.get(WarCoreMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing, WarCoreMapModeService.POWER_ID, WarCoreMapModeService.DirtyMap);
                existing.click_special_action = new PowerActionWithID(CoreMapClick);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = WarCoreMapModeService.POWER_ID,
                name = WarCoreMapModeService.POWER_ID,
                path_icon = "ui/icons/iconMap",
                map_modes_switch = true,
                multi_toggle = AWMapModePowerRules.ShouldUseGodPowerMultiToggle(
                    AWMapModeNameplateRules.GetWarZoneOptionLocaleIds().Length),
                toggle_name = AWMapModeMetaRules.ResolveOptionId(WarCoreMapModeService.POWER_ID),
                force_map_mode = AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(WarCoreMapModeService.DirtyMap),
                click_special_action = new PowerActionWithID(CoreMapClick)
            });
        }

        private static void DisableLegacyClaimMapMode()
        {
            string optionId = AWMapModeMetaRules.ResolveOptionId(WarClaimMapModeService.POWER_ID);
            if (PlayerConfig.dict.TryGetValue(optionId, out var data))
                data.boolVal = false;
        }

        private static bool CoreMapClick(WorldTile pTile, string pPowerID)
        {
            Kingdom clicked = GetTileKingdom(pTile);
            if (clicked?.data == null) return false;
            WarCoreMapModeService.SetFocus(clicked);
            return true;
        }

        private static void RegisterMandateMapModes()
        {
            RegisterMandateDynastyMapMode();
            RegisterMandateCoreMapMode();
        }

        private static void RegisterMandateDynastyMapMode()
        {
            RegisterMapModeOption(MandateDynastyMapModeService.POWER_ID);
            GodPower existing = AssetManager.powers.get(MandateDynastyMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing, MandateDynastyMapModeService.POWER_ID,
                    MandateDynastyMapModeService.DirtyMap);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = MandateDynastyMapModeService.POWER_ID,
                name = MandateDynastyMapModeService.POWER_ID,
                path_icon = "ui/Icons/traits/iconTianming",
                map_modes_switch = true,
                multi_toggle = AWMapModePowerRules.ShouldUseGodPowerMultiToggle(
                    AWMapModeNameplateRules.GetDefaultZoneOptionLocaleIds().Length),
                toggle_name = AWMapModeMetaRules.ResolveOptionId(MandateDynastyMapModeService.POWER_ID),
                force_map_mode = AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(MandateDynastyMapModeService.DirtyMap)
            });
        }

        private static void RegisterMandateCoreMapMode()
        {
            RegisterMapModeOption(MandateCoreMapModeService.POWER_ID);
            GodPower existing = AssetManager.powers.get(MandateCoreMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing, MandateCoreMapModeService.POWER_ID,
                    MandateCoreMapModeService.DirtyMap);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = MandateCoreMapModeService.POWER_ID,
                name = MandateCoreMapModeService.POWER_ID,
                path_icon = "ui/icons/iconMap",
                map_modes_switch = true,
                multi_toggle = AWMapModePowerRules.ShouldUseGodPowerMultiToggle(
                    AWMapModeNameplateRules.GetDefaultZoneOptionLocaleIds().Length),
                toggle_name = AWMapModeMetaRules.ResolveOptionId(MandateCoreMapModeService.POWER_ID),
                force_map_mode = AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(MandateCoreMapModeService.DirtyMap)
            });
        }

        private static void RegisterFeudatoryMapMode()
        {
            RegisterMapModeOption(FeudatoryMapModeService.POWER_ID);
            GodPower existing = AssetManager.powers.get(
                FeudatoryMapModeService.POWER_ID);
            if (existing != null)
            {
                ConfigureMapModePower(existing,
                    FeudatoryMapModeService.POWER_ID,
                    FeudatoryMapModeService.DirtyMap);
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = FeudatoryMapModeService.POWER_ID,
                name = FeudatoryMapModeService.POWER_ID,
                path_icon = "ui/Icons/traits/iconzhuhou",
                map_modes_switch = true,
                multi_toggle = false,
                toggle_name = AWMapModeMetaRules.ResolveOptionId(
                    FeudatoryMapModeService.POWER_ID),
                force_map_mode =
                    AWMapModePowerRules.ResolveForcedMapModeForLayerPower(),
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = BuildMapModeToggleAction(
                    FeudatoryMapModeService.DirtyMap)
            });
        }

        private static void RegisterMapModeOption(string pPowerId)
        {
            RegisterMapModeOption(pPowerId, AWMapModeNameplateRules.GetDefaultZoneOptionLocaleIds());
        }

        private static void RegisterMapModeOption(string pPowerId, string[] pLocaleOptionIds)
        {
            string optionId = AWMapModeMetaRules.ResolveOptionId(pPowerId);
            string[] localeOptionIds = pLocaleOptionIds == null || pLocaleOptionIds.Length == 0
                ? AWMapModeNameplateRules.GetDefaultZoneOptionLocaleIds()
                : pLocaleOptionIds;
            OptionAsset option = AssetManager.options_library.get(optionId);
            if (option == null)
            {
                option = AssetManager.options_library.add(new OptionAsset
                {
                    id = optionId,
                    default_bool = false,
                    default_int = 0,
                    max_value = localeOptionIds.Length - 1,
                    multi_toggle = localeOptionIds.Length > 1,
                    type = OptionType.Bool,
                    locale_options_ids = localeOptionIds
                });
            }
            else
            {
                option.default_int = 0;
                option.max_value = localeOptionIds.Length - 1;
                option.multi_toggle = localeOptionIds.Length > 1;
                option.locale_options_ids = localeOptionIds;
            }

            if (!PlayerConfig.dict.ContainsKey(optionId))
                PlayerConfig.instance.data.add(new PlayerOptionData(optionId)
                {
                    boolVal = false,
                    intVal = option.default_int
                });
            else if (PlayerConfig.dict.TryGetValue(optionId, out var data))
            {
                if (data.intVal < 0 || data.intVal > option.max_value)
                    data.intVal = option.default_int;
            }

            if (PlayerConfig.dict.TryGetValue(pPowerId, out var legacy) &&
                PlayerConfig.dict.TryGetValue(optionId, out var current))
            {
                if (legacy.boolVal) current.boolVal = true;
                legacy.boolVal = false;
            }

            LinkMapModeAssets();
        }

        private static void DisableLegacyDevelopmentMapMode()
        {
            string optionId = AWMapModeMetaRules.ResolveOptionId(DevelopmentMapModeService.POWER_ID);
            if (PlayerConfig.dict.TryGetValue(optionId, out var data))
                data.boolVal = false;
        }

        private static void RegisterMapModeNameplates()
        {
            var library = AssetManager.nameplates_library;
            if (library == null) return;
            NameplateAsset kingdomPlate = library.get("plate_kingdom");
            if (kingdomPlate == null) return;

            NameplateAsset schoolPlate = library.get("plate_aw_school");
            if (schoolPlate == null)
            {
                schoolPlate = library.add(new NameplateAsset
                {
                    id = "plate_aw_school",
                    path_sprite = "ui/nameplates/nameplate_religion",
                    padding_left = 11,
                    padding_right = 13,
                    map_mode = AWMapModeMetaTypes.School,
                    max_nameplate_count = 100,
                    action_main = DrawSchoolNameplates
                });
            }
            else
            {
                schoolPlate.map_mode = AWMapModeMetaTypes.School;
                schoolPlate.action_main = DrawSchoolNameplates;
            }

            NameplateAsset feudatoryPlate = library.get(
                "plate_aw_feudatory");
            if (feudatoryPlate == null)
            {
                library.map_modes_nameplates.Remove(
                    AWMapModeMetaTypes.Feudatory);
                feudatoryPlate = library.add(new NameplateAsset
                {
                    id = "plate_aw_feudatory",
                    path_sprite = "ui/nameplates/nameplate_kingdom",
                    padding_left = 26,
                    padding_right = 26,
                    padding_top = -2,
                    banner_only_mode_scale = 2.5f,
                    map_mode = AWMapModeMetaTypes.Feudatory,
                    max_nameplate_count = 100,
                    action_main = DrawFeudatoryNameplates
                });
            }
            else
            {
                feudatoryPlate.map_mode = AWMapModeMetaTypes.Feudatory;
                feudatoryPlate.action_main = DrawFeudatoryNameplates;
            }

            NameplateAsset hierarchicalVassalPlate = library.get(
                "plate_aw_hierarchical_vassal");
            if (hierarchicalVassalPlate == null)
            {
                hierarchicalVassalPlate = library.add(new NameplateAsset
                {
                    id = "plate_aw_hierarchical_vassal",
                    path_sprite = "ui/nameplates/nameplate_kingdom",
                    map_mode = AWMapModeMetaTypes.HierarchicalVassal,
                    max_nameplate_count = 0,
                    action_main = DrawNoNameplates
                });
            }
            else
            {
                hierarchicalVassalPlate.map_mode =
                    AWMapModeMetaTypes.HierarchicalVassal;
                hierarchicalVassalPlate.max_nameplate_count = 0;
                hierarchicalVassalPlate.action_main = DrawNoNameplates;
            }

            foreach (MetaType metaType in AWMapModeNameplateRules.GetRequiredNameplateMetaTypes())
            {
                if (metaType == AWMapModeMetaTypes.School ||
                    metaType == AWMapModeMetaTypes.Feudatory ||
                    metaType == AWMapModeMetaTypes.HierarchicalVassal)
                    continue;
                library.map_modes_nameplates[metaType] = kingdomPlate;
            }
            library.map_modes_nameplates[AWMapModeMetaTypes.School] = schoolPlate;
            library.map_modes_nameplates[AWMapModeMetaTypes.Feudatory] =
                feudatoryPlate;
            library.map_modes_nameplates[AWMapModeMetaTypes.HierarchicalVassal] =
                hierarchicalVassalPlate;
        }

        private static void DrawNoNameplates(NameplateManager pManager,
            NameplateAsset pAsset)
        {
        }

        private static void DrawFeudatoryNameplates(NameplateManager pManager,
            NameplateAsset pAsset)
        {
            if (pManager == null || pAsset == null ||
                World.world?.zone_camera == null) return;

            if (ShouldRefreshNameplateCandidates(
                    ref _feudatoryNameplateCandidatesReady,
                    ref _feudatoryNameplateCandidateSignature,
                    ref _feudatoryNameplateCandidateRefreshAt))
            {
                FeudatoryNameplateAnchors.Clear();
                FeudatoryNameplateZones.Clear();
                FeudatoryNameplateSnapshots.Clear();
                FeudatoryNameplateIds.Clear();
                List<TileZone> visibleZones =
                    World.world.zone_camera.getVisibleZones();
                for (int i = 0; i < visibleZones.Count; i++)
                {
                    TileZone zone = visibleZones[i];
                    City city = zone?.city;
                    if (!FeudatoryMapModeService.TryGetSnapshot(city,
                            out FeudatorySnapshot snapshot)) continue;

                    bool centerVisible = World.world.move_camera != null &&
                        World.world.move_camera.isWithinCameraViewNotPowerBar(
                            city.city_center);
                    bool candidateIsSeat = city.id == snapshot.SeatCityId;
                    if (FeudatoryNameplateAnchors.TryGetValue(
                            snapshot.FeudatoryId, out City current))
                    {
                        TileZone currentZone =
                            FeudatoryNameplateZones[snapshot.FeudatoryId];
                        FeudatorySnapshot currentSnapshot =
                            FeudatoryNameplateSnapshots[snapshot.FeudatoryId];
                        bool currentCenterVisible =
                            World.world.move_camera != null &&
                            World.world.move_camera.isWithinCameraViewNotPowerBar(
                                current.city_center);
                        if (!FeudatoryMapModeRules.ShouldReplaceNameplateAnchor(
                                current.id,
                                current.id == currentSnapshot.SeatCityId,
                                currentCenterVisible,
                                currentZone?.id ?? int.MaxValue,
                                city.id, candidateIsSeat, centerVisible,
                                zone.id)) continue;
                    }
                    else
                    {
                        FeudatoryNameplateIds.Add(snapshot.FeudatoryId);
                    }

                    FeudatoryNameplateAnchors[snapshot.FeudatoryId] = city;
                    FeudatoryNameplateZones[snapshot.FeudatoryId] = zone;
                    FeudatoryNameplateSnapshots[snapshot.FeudatoryId] = snapshot;
                }

                FeudatoryNameplateIds.Sort();
            }

            int count = 0;
            for (int i = 0; i < FeudatoryNameplateIds.Count; i++)
            {
                if (count >= pAsset.max_nameplate_count) break;
                long feudatoryId = FeudatoryNameplateIds[i];
                City anchor = FeudatoryNameplateAnchors[feudatoryId];
                TileZone zone = FeudatoryNameplateZones[feudatoryId];
                FeudatorySnapshot snapshot =
                    FeudatoryNameplateSnapshots[feudatoryId];

                Actor prince;
                try
                {
                    prince = World.world?.units?.get(
                        snapshot.PrinceActorId);
                }
                catch
                {
                    prince = null;
                }
                if (prince?.data == null || prince.isRekt() ||
                    !prince.isAlive()) continue;

                AWMapModeMetaObject meta = AWMapModeMetaLibrary.
                    GetFeudatoryIdentityMeta(snapshot, anchor.kingdom);
                if (meta == null) continue;
                bool centerVisible = World.world.move_camera != null &&
                    World.world.move_camera.isWithinCameraViewNotPowerBar(
                        anchor.city_center);
                Vector2 position = centerVisible || zone?.centerTile == null
                    ? anchor.city_center
                    : zone.centerTile.posV3;
                NameplateText text = pManager.prepareNext(pAsset, prince);
                text.setupMeta(meta.data, meta.getColor());
                text.setText(meta.data.name, position);
                text.setPriority(anchor.getPopulationPeople());
                count++;
            }
        }

        private static void DrawSchoolNameplates(NameplateManager pManager, NameplateAsset pAsset)
        {
            if (pManager == null || pAsset == null || World.world?.zone_camera == null) return;

            if (ShouldRefreshNameplateCandidates(
                    ref _schoolNameplateCandidatesReady,
                    ref _schoolNameplateCandidateSignature,
                    ref _schoolNameplateCandidateRefreshAt))
            {
                SchoolNameplateCandidates.Clear();
                SchoolNameplateCandidateIds.Clear();
                List<TileZone> visibleZones = World.world.zone_camera.getVisibleZones();
                for (int i = 0; i < visibleZones.Count; i++)
                {
                    City city = visibleZones[i]?.city;
                    if (city?.data == null || city.isRekt() || !city.isAlive()) continue;
                    if (SchoolNameplateCandidateIds.Add(city.data.id))
                        SchoolNameplateCandidates.Add(city);
                }
                SchoolNameplateCandidates.Sort(SchoolNameplateCityOrder);
            }

            int count = 0;
            for (int i = 0; i < SchoolNameplateCandidates.Count; i++)
            {
                if (count >= pAsset.max_nameplate_count) break;
                City city = SchoolNameplateCandidates[i];
                if (!World.world.move_camera.isWithinCameraViewNotPowerBar(city.city_center)) continue;

                CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
                AWMapModeMetaObject meta =
                    AWMapModeMetaLibrary.GetSchoolIdentityMetaForCity(city, snapshot);
                if (meta == null) continue;

                NameplateText text = pManager.prepareNext(pAsset, city);
                text.setupMeta(meta.data, meta.getColor());
                text.setText(meta.data.name, city.city_center);
                text.setPriority(city.getPopulationPeople());

                CourtSchoolDefinition definition = CourtSchoolRegistry.Find(snapshot.DominantSchool);
                if (definition != null) text.showSpecial(definition.IconPath);
                count++;
            }
        }

        private static bool ShouldRefreshNameplateCandidates(
            ref bool pReady, ref ulong pPreviousSignature,
            ref float pRefreshAt)
        {
            ulong signature = AWPresentationVisibility.GetSignature(
                renderGameplay: false);
            float now = Time.unscaledTime;
            if (pReady && pPreviousSignature == signature &&
                now < pRefreshAt)
                return false;

            pReady = true;
            pPreviousSignature = signature;
            pRefreshAt = now + NameplateCandidateRefreshSeconds;
            return true;
        }

        private static int CompareSchoolNameplateCities(City pLeft, City pRight)
        {
            if (ReferenceEquals(pLeft, pRight)) return 0;
            long leftId = pLeft?.data?.id ?? long.MaxValue;
            long rightId = pRight?.data?.id ?? long.MaxValue;
            return leftId.CompareTo(rightId);
        }

        private static void ConfigureMapModePower(GodPower pPower, string pPowerId, Action pDirtyAction)
        {
            if (pPower == null) return;
            pPower.map_modes_switch = true;
            pPower.toggle_name = AWMapModeMetaRules.ResolveOptionId(pPowerId);
            pPower.force_map_mode = AWMapModePowerRules.ResolveForcedMapModeForLayerPower();
            int optionCount = (pPower.option_asset?.max_value ?? 0) + 1;
            pPower.multi_toggle = AWMapModePowerRules.ShouldUseGodPowerMultiToggle(optionCount);
            pPower.unselect_when_window = true;
            pPower.ignore_cursor_icon = true;
            pPower.allow_unit_selection = true;
            pPower.toggle_action = BuildMapModeToggleAction(pDirtyAction);
        }

        private static PowerToggleAction BuildMapModeToggleAction(Action pDirtyAction)
        {
            return (PowerToggleAction)Delegate.Combine(
                new PowerToggleAction(AssetManager.powers.toggleOptionZone),
                new PowerToggleAction(_ =>
                {
                    try { pDirtyAction?.Invoke(); }
                    catch { }
                }));
        }

        private static PowerToggleAction BuildBooleanToggleAction(Action pChangedAction)
        {
            return new PowerToggleAction(pPowerId =>
            {
                GodPower power = AssetManager.powers.get(pPowerId);
                if (power == null || string.IsNullOrEmpty(power.toggle_name)) return;
                if (!PlayerConfig.dict.TryGetValue(power.toggle_name,
                        out PlayerOptionData optionData)) return;

                optionData.boolVal = !optionData.boolVal;
                PlayerConfig.saveData();
                pChangedAction?.Invoke();
            });
        }

        private static void LinkMapModeAssets()
        {
            try { AssetManager.powers.linkAssets(); } catch { }
            try { AssetManager.options_library.linkAssets(); } catch { }
        }

        private static void RegisterVassalPowers()
        {
            if (AssetManager.powers.get(VASSAL_SET) == null)
            {
                AssetManager.powers.add(new GodPower
                {
                    id = VASSAL_SET,
                    name = VASSAL_SET,
                    path_icon = "ui/wars/war_vassal",
                    force_map_mode = MetaType.Kingdom,
                    unselect_when_window = true,
                    allow_unit_selection = false,
                    click_special_action = new PowerActionWithID(VassalSetClick)
                });
            }

            if (AssetManager.powers.get(VASSAL_REMOVE) == null)
            {
                AssetManager.powers.add(new GodPower
                {
                    id = VASSAL_REMOVE,
                    name = VASSAL_REMOVE,
                    path_icon = "ui/wars/war_independent",
                    force_map_mode = MetaType.Kingdom,
                    unselect_when_window = true,
                    allow_unit_selection = false,
                    click_special_action = new PowerActionWithID(VassalRemoveClick)
                });
            }
        }

        private static void RegisterRoyalEnfeoffmentPower()
        {
            if (AssetManager.powers.get(ROYAL_ENFEOFFMENT) != null) return;
            AssetManager.powers.add(new GodPower
            {
                id = ROYAL_ENFEOFFMENT,
                name = ROYAL_ENFEOFFMENT,
                path_icon = "ui/wars/war_vassal",
                force_map_mode = MetaType.City,
                unselect_when_window = true,
                allow_unit_selection = false,
                click_special_action = new PowerActionWithID(
                    RoyalEnfeoffmentClick)
            });
        }

        private static void RegisterMilitaryGovernoratePower()
        {
            if (AssetManager.powers.get(MILITARY_GOVERNORATE) != null)
                return;
            AssetManager.powers.add(new GodPower
            {
                id = MILITARY_GOVERNORATE,
                name = MILITARY_GOVERNORATE,
                path_icon = "ui/wars/war_vassal",
                force_map_mode = MetaType.City,
                unselect_when_window = true,
                allow_unit_selection = false,
                click_special_action = new PowerActionWithID(
                    MilitaryGovernorateClick)
            });
        }

        private static void RegisterMandateGrantPower()
        {
            if (AssetManager.powers.get(GRANT_MANDATE) != null) return;

            AssetManager.powers.add(new GodPower
            {
                id = GRANT_MANDATE,
                name = GRANT_MANDATE,
                path_icon = "ui/Icons/traits/iconTianming",
                force_map_mode = MetaType.Kingdom,
                unselect_when_window = true,
                allow_unit_selection = false,
                click_special_action = new PowerActionWithID(GrantMandateClick)
            });
        }

        private static void RegisterBanditStrongholdPower()
        {
            if (AssetManager.powers.get(SPAWN_BANDIT_STRONGHOLD) != null)
                return;
            AssetManager.powers.add(new GodPower
            {
                id = SPAWN_BANDIT_STRONGHOLD,
                name = SPAWN_BANDIT_STRONGHOLD,
                path_icon = "ui/wars/war_rebellion",
                force_map_mode = MetaType.City,
                unselect_when_window = true,
                allow_unit_selection = false,
                click_special_action = new PowerActionWithID(
                    BanditStrongholdClick)
            });
        }

        private static bool BanditStrongholdClick(WorldTile pTile,
            string pPowerId)
        {
            City city = pTile?.zone?.city;
            if (city?.data == null)
            {
                Tip(AW_L10n.Text("aw_bandit_stronghold_invalid_city",
                    "Select an occupied city zone"));
                return false;
            }
            if (!PeasantRebelBanditStrongholdService.TryCreateDirect(city,
                    out Kingdom bandit, out City stronghold,
                    out string failureKey))
            {
                Tip(AW_L10n.Text(failureKey,
                    "Bandit stronghold creation failed"));
                return false;
            }
            Tip(AW_L10n.Text("aw_bandit_stronghold_success",
                "Bandit stronghold created") + ": " + stronghold.name);
            return true;
        }

        private static bool GrantMandateClick(WorldTile pTile, string pPowerID)
        {
            Kingdom target = GetTileKingdom(pTile);
            if (!MandateService.TryGrantMandateByPlayer(target, out string reason))
            {
                Tip(AW_L10n.Text("aw_grant_mandate_error_" + reason, reason));
                return false;
            }

            Tip(AW_L10n.Text("aw_grant_mandate_success", "Mandate granted") +
                ": " + target.name);
            return true;
        }

        private static bool VassalSetClick(WorldTile pTile, string pPowerID)
        {
            Kingdom clicked = GetTileKingdom(pTile);
            if (clicked?.data == null)
            {
                Tip("\u8BF7\u70B9\u51FB\u4E00\u4E2A\u6587\u660E\u56FD\u5BB6");
                return false;
            }

            if (_pendingVassal == null || _pendingVassal.data == null || _pendingVassal.isRekt())
            {
                _pendingVassal = clicked;
                Tip("\u5DF2\u9009\u62E9 " + clicked.name + " \u4E3A\u9644\u5EB8\u5019\u9009\uFF0C\u518D\u70B9\u51FB\u5B97\u4E3B\u56FD");
                return true;
            }

            Kingdom vassal = _pendingVassal;
            _pendingVassal = null;
            if (vassal == clicked)
            {
                Tip("\u9644\u5EB8\u56FD\u548C\u5B97\u4E3B\u56FD\u4E0D\u80FD\u662F\u540C\u4E00\u4E2A\u56FD\u5BB6");
                return false;
            }

            if (!VassalService.SetVassal(vassal, clicked, "manual"))
            {
                Tip("\u65E0\u6CD5\u5EFA\u7ACB\u9644\u5EB8\u5173\u7CFB");
                return false;
            }

            Tip(vassal.name + " \u5DF2\u81E3\u5C5E\u4E8E " + clicked.name);
            return true;
        }

        private static bool VassalRemoveClick(WorldTile pTile, string pPowerID)
        {
            Kingdom clicked = GetTileKingdom(pTile);
            if (clicked?.data == null)
            {
                Tip("\u8BF7\u70B9\u51FB\u4E00\u4E2A\u6587\u660E\u56FD\u5BB6");
                return false;
            }

            if (!VassalService.IsVassalKingdom(clicked))
            {
                Tip(clicked.name + " \u4E0D\u662F\u9644\u5EB8\u56FD");
                return false;
            }

            if (!VassalService.EndVassal(clicked, "manual"))
            {
                Tip("\u89E3\u9664\u9644\u5EB8\u5173\u7CFB\u5931\u8D25");
                return false;
            }

            Tip(clicked.name + " \u5DF2\u8131\u79BB\u9644\u5EB8\u5173\u7CFB");
            return true;
        }

        private static bool RoyalEnfeoffmentClick(WorldTile pTile,
            string pPowerID)
        {
            City city = pTile?.zone?.city;
            if (city?.data == null || city.isRekt() || !city.isAlive())
            {
                Tip(AW_L10n.Text("aw_royal_enfeoffment_failure",
                    "Select a living civilized city."));
                return false;
            }

            if (!RoyalEnfeoffmentService.TryCreate(city,
                    out string reason))
            {
                Tip(AW_L10n.Text("aw_royal_enfeoffment_failure_" + reason,
                    AW_L10n.Text("aw_royal_enfeoffment_failure",
                        "Royal enfeoffment failed.")));
                return false;
            }

            Tip(AW_L10n.Text("aw_royal_enfeoffment_success",
                "A royal clansman now rules the new vassal kingdom."));
            return true;
        }

        private static bool MilitaryGovernorateClick(WorldTile pTile,
            string pPowerID)
        {
            City city = pTile?.zone?.city;
            if (!MilitaryGovernorateCreationService.CanSelectSeat(city,
                    out string reason))
            {
                Tip(AW_L10n.Text(
                    "aw_military_governorate_failure_" + reason,
                    AW_L10n.Text("aw_military_governorate_failure",
                        "This city cannot become a military command.")));
                return false;
            }
            MilitaryGovernorateWindow.OpenCreation(city);
            return true;
        }

        private static Kingdom GetTileKingdom(WorldTile pTile)
        {
            Kingdom kingdom = pTile?.zone?.city?.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv() || kingdom.isNeutral()) return null;
            return kingdom;
        }

        private static void Tip(string pText)
        {
            WorldTip.showNow(pText, false, "top", 4f);
        }
    }
}
