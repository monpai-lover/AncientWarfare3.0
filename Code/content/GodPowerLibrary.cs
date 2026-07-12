using System;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui.windows;

namespace AncientWarfare3.content
{
    public static class GodPowerLibrary
    {
        public const string SPAWN_XIA = XiaRace.ID;
        public const string VASSAL_SET = "aw_vassal_set";
        public const string VASSAL_REMOVE = "aw_vassal_remove";

        private static Kingdom _pendingVassal;

        public static void Init()
        {
            RegisterSpawnXia();
            RegisterTechMapMode();
            RegisterWarMapModes();
            RegisterVassalMapMode();
            RegisterMandateMapModes();
            RegisterSchoolMapMode();
            LinkMapModeAssets();
            AWMapModeMetaLibrary.Init();
            RegisterMapModeNameplates();
            RegisterVassalPowers();
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
            City city = pTile?.zone?.city;
            if (city?.data == null) return false;
            SchoolWindow.OpenCity(city.data.id);
            return true;
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

            foreach (MetaType metaType in AWMapModeNameplateRules.GetRequiredNameplateMetaTypes())
            {
                if (metaType == AWMapModeMetaTypes.School) continue;
                library.map_modes_nameplates[metaType] = kingdomPlate;
            }
            library.map_modes_nameplates[AWMapModeMetaTypes.School] = schoolPlate;
        }

        private static void DrawSchoolNameplates(NameplateManager pManager, NameplateAsset pAsset)
        {
            if (pManager == null || pAsset == null || World.world?.cities == null) return;

            int count = 0;
            foreach (City city in World.world.cities)
            {
                if (count >= pAsset.max_nameplate_count) break;
                if (city?.data == null || city.isRekt() || !city.isAlive() ||
                    !World.world.move_camera.isWithinCameraViewNotPowerBar(city.city_center)) continue;

                AWMapModeMetaObject meta = AWMapModeMetaLibrary.GetSchoolIdentityMetaForCity(city);
                if (meta == null) continue;

                NameplateText text = pManager.prepareNext(pAsset, meta);
                text.setupMeta(meta.data, meta.getColor());
                text.setText(meta.data.name, city.city_center);
                text.setPriority(city.getPopulationPeople());

                CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
                CourtSchoolDefinition definition = CourtSchoolRegistry.Find(snapshot?.DominantSchool);
                if (definition != null) text.showSpecial(definition.IconPath);
                count++;
            }
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
