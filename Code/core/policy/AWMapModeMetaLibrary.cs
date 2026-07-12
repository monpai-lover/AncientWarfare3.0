using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal static class AWMapModeMetaLibrary
    {
        private static readonly Dictionary<string, AWMapModeMetaObject> Metas = new Dictionary<string, AWMapModeMetaObject>();
        private static readonly Dictionary<string, ColorAsset> ColorCache = new Dictionary<string, ColorAsset>();
        private static bool _hadAnyDynamicMeta;

        [System.ThreadStatic] private static Dictionary<string, string> _mandateDynastyStatusCache;
        [System.ThreadStatic] private static bool _mandateDynastyMandateResolved;
        [System.ThreadStatic] private static Kingdom _mandateDynastyMandate;

        public static MetaTypeAsset TechAsset { get; private set; }
        public static MetaTypeAsset VassalAsset { get; private set; }
        public static MetaTypeAsset WarCoreAsset { get; private set; }
        public static MetaTypeAsset WarClaimAsset { get; private set; }
        public static MetaTypeAsset MandateDynastyAsset { get; private set; }
        public static MetaTypeAsset MandateCoreAsset { get; private set; }
        public static MetaTypeAsset DevelopmentAsset { get; private set; }
        public static MetaTypeAsset SchoolAsset { get; private set; }

        private static ZoneCalculator ZoneManager => World.world?.zone_calculator;

        public static void Init()
        {
            AddAll();
            try { AssetManager.meta_type_library.linkAssets(); }
            catch { }
        }

        public static MetaTypeAsset GetAssetForPower(string pPowerId)
        {
            switch (pPowerId ?? "")
            {
                case TechMapModeService.POWER_ID:
                    return TechAsset;
                case VassalMapModeService.POWER_ID:
                    return VassalAsset;
                case WarCoreMapModeService.POWER_ID:
                    return WarCoreAsset;
                case WarClaimMapModeService.POWER_ID:
                    return WarClaimAsset;
                case MandateDynastyMapModeService.POWER_ID:
                    return MandateDynastyAsset;
                case MandateCoreMapModeService.POWER_ID:
                    return MandateCoreAsset;
                case DevelopmentMapModeService.POWER_ID:
                    return DevelopmentAsset;
                case SchoolMapModeService.POWER_ID:
                    return SchoolAsset;
                default:
                    return null;
            }
        }

        private static void AddAll()
        {
            TechAsset = AddOrGet(AWMapModeMetaTypes.TechId, AWMapModeMetaTypes.Tech, TechMapModeService.POWER_ID,
                GetTechMetaForZone);
            TechAsset.tile_get_metaobject_1 = GetDevelopmentMetaForZone;
            TechAsset.tile_get_metaobject_2 = GetDevelopmentMetaForZone;
            VassalAsset = AddOrGet(AWMapModeMetaTypes.VassalId, AWMapModeMetaTypes.Vassal, VassalMapModeService.POWER_ID,
                VassalMapModeService.GetRootMetaForZone);
            WarCoreAsset = AddOrGet(AWMapModeMetaTypes.WarCoreId, AWMapModeMetaTypes.WarCore, WarCoreMapModeService.POWER_ID,
                GetWarCoreMetaForZone);
            WarCoreAsset.tile_get_metaobject_1 = GetWarClaimMetaForZone;
            WarCoreAsset.tile_get_metaobject_2 = GetWarClaimMetaForZone;
            WarClaimAsset = AddOrGet(AWMapModeMetaTypes.WarClaimId, AWMapModeMetaTypes.WarClaim, WarClaimMapModeService.POWER_ID,
                GetWarClaimMetaForZone);
            MandateDynastyAsset = AddOrGet(AWMapModeMetaTypes.MandateDynastyId, AWMapModeMetaTypes.MandateDynasty,
                MandateDynastyMapModeService.POWER_ID, GetMandateDynastyMetaForZone);
            MandateCoreAsset = AddOrGet(AWMapModeMetaTypes.MandateCoreId, AWMapModeMetaTypes.MandateCore,
                MandateCoreMapModeService.POWER_ID, GetMandateCoreMetaForZone);
            DevelopmentAsset = AddOrGet(AWMapModeMetaTypes.DevelopmentId, AWMapModeMetaTypes.Development,
                DevelopmentMapModeService.POWER_ID, GetDevelopmentMetaForZone);
            SchoolAsset = AddOrGet(AWMapModeMetaTypes.SchoolId, AWMapModeMetaTypes.School,
                SchoolMapModeService.POWER_ID, GetSchoolMetaForZone);
            ConfigureSchoolSelectionAsset(SchoolAsset);
            SchoolAsset.tile_get_metaobject = (pZone, _) => GetSchoolIdentityMetaForZone(pZone);
            SchoolAsset.click_action_zone = SchoolMapModeService.SelectCity;
        }

        private static void ConfigureSchoolSelectionAsset(MetaTypeAsset pAsset)
        {
            if (pAsset == null) return;
            pAsset.icon_single_path = "ui/Icons/traits/iconRujia";
            pAsset.icon_list = "iconCityList";
            pAsset.window_name = "city";
            pAsset.power_tab_id = SchoolMapBottomBarController.TabId;
            pAsset.get_list = () => World.world?.cities;
            pAsset.has_any = () => World.world?.cities != null && World.world.cities.hasAny();
            pAsset.get_selected = () => SelectedMetas.selected_city;
            pAsset.set_selected = pElement =>
            {
                if (pElement is City city) SelectedMetas.selected_city = city;
            };
            pAsset.get = pIdValue => World.world?.cities?.get(pIdValue);
            pAsset.window_action_clear = () => SelectedMetas.selected_city = null;
            pAsset.window_history_action_update = delegate(ref WindowHistoryData pHistoryData)
            {
                pHistoryData.city = SelectedMetas.selected_city;
            };
            pAsset.window_history_action_restore = delegate(ref WindowHistoryData pHistoryData)
            {
                SelectedMetas.selected_city = pHistoryData.city;
            };
            pAsset.selected_tab_action_meta = _ =>
            {
                if (!SchoolMapModeService.SelectCity(SelectedMetas.selected_city))
                    PowerTabController.showMainTab();
            };
            pAsset.check_unit_has_meta = pActor => pActor?.city?.data != null;
            pAsset.set_unit_set_meta_for_meta_for_window = pActor => SelectedMetas.selected_city = pActor?.city;
        }

        private static MetaTypeAsset AddOrGet(string pId, MetaType pType, string pPowerId, MetaZoneGetMetaSimple pGetter)
        {
            MetaTypeAsset existing = AssetManager.meta_type_library.get(pId);
            if (existing != null)
            {
                ConfigureAsset(existing, pId, pType, pPowerId, pGetter);
                return existing;
            }

            var asset = new MetaTypeAsset
            {
                id = pId
            };
            ConfigureAsset(asset, pId, pType, pPowerId, pGetter);
            return AssetManager.meta_type_library.add(asset);
        }

        private static void ConfigureAsset(MetaTypeAsset pAsset, string pId, MetaType pType, string pPowerId,
            MetaZoneGetMetaSimple pGetter)
        {
            pAsset.id = pId;
            pAsset.map_mode = pType;
            pAsset.option_id = AWMapModeMetaRules.ResolveAssetOptionId(pPowerId);
            pAsset.power_option_zone_id = AWMapModeMetaRules.ResolvePowerOptionZoneId(pPowerId);
            pAsset.icon_single_path = "ui/icons/iconMap";
            pAsset.force_zone_when_selected = true;
            pAsset.set_icon_for_cancel_button = true;
            pAsset.icon_list = "iconKingdomList";
            pAsset.window_name = "kingdom";
            pAsset.power_tab_id = "selected_kingdom";
            pAsset.get_list = () => World.world?.kingdoms;
            pAsset.has_any = () => World.world?.kingdoms != null && World.world.kingdoms.hasAny();
            pAsset.get_selected = () => SelectedMetas.selected_kingdom;
            pAsset.set_selected = pElement => SelectedMetas.selected_kingdom = pElement as Kingdom;
            pAsset.get = pIdValue => World.world?.kingdoms?.get(pIdValue);
            pAsset.window_action_clear = () => SelectedMetas.selected_kingdom = null;
            pAsset.window_history_action_update = delegate(ref WindowHistoryData pHistoryData)
            {
                pHistoryData.kingdom = SelectedMetas.selected_kingdom;
            };
            pAsset.window_history_action_restore = delegate(ref WindowHistoryData pHistoryData)
            {
                SelectedMetas.selected_kingdom = pHistoryData.kingdom;
            };
            pAsset.click_action_zone = ActionLibrary.inspectKingdom;
            pAsset.selected_tab_action_meta = _ => PowerTabController.showTabSelectedMeta(MetaTypeLibrary.kingdom);
            pAsset.check_unit_has_meta = pActor => pActor != null && pActor.isKingdomCiv();
            pAsset.set_unit_set_meta_for_meta_for_window = pActor => SelectedMetas.selected_kingdom = pActor?.kingdom;
            pAsset.draw_zones = DrawZones;
            pAsset.tile_get_metaobject = (pZone, _) => GetKingdomForZone(pZone);
            pAsset.tile_get_metaobject_0 = pGetter;
            pAsset.tile_get_metaobject_1 = pGetter;
            pAsset.tile_get_metaobject_2 = pGetter;
            pAsset.check_tile_has_meta = (pZone, pAssetValue, pOption) =>
                pAssetValue?.tile_get_metaobject?.Invoke(pZone, pOption) != null;
            pAsset.check_cursor_tooltip = ShowKingdomTooltip;
            pAsset.cursor_tooltip_action = _ => { };
            pAsset.check_cursor_highlight = HighlightKingdom;
        }

        private static void DrawZones(MetaTypeAsset pAsset)
        {
            MetaZoneGetMetaSimple getter = GetZoneGetter(pAsset);
            if (World.world?.kingdoms == null || ZoneManager == null || getter == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) continue;
                if (!ShouldDrawKingdomZones(pAsset, kingdom)) continue;
                foreach (City city in kingdom.getCities())
                    DrawCityZones(pAsset, city, getter);
            }
        }

        private static bool ShouldDrawKingdomZones(MetaTypeAsset pAsset, Kingdom pKingdom)
        {
            if (!AWMapModeMetaRules.IsRuntimeMeta(pAsset?.map_mode ?? MetaType.None,
                    AWMapModeMetaTypes.MandateDynasty))
                return true;

            return MandateDynastyMapRules.ShouldDrawStatus(GetMandateDynastyStatusForKingdom(pKingdom));
        }

        private static MetaZoneGetMetaSimple GetZoneGetter(MetaTypeAsset pAsset)
        {
            if (pAsset == null) return null;
            switch (pAsset.getZoneOptionState())
            {
                case 0:
                    return pAsset.tile_get_metaobject_0;
                case 1:
                    return pAsset.tile_get_metaobject_1;
                default:
                    return pAsset.tile_get_metaobject_2 ?? pAsset.tile_get_metaobject_1 ?? pAsset.tile_get_metaobject_0;
            }
        }

        private static void DrawCityZones(MetaTypeAsset pAsset, City pCity, MetaZoneGetMetaSimple pGetter)
        {
            if (pCity?.data == null || pCity.isRekt() || pCity.zones == null) return;
            for (int i = 0; i < pCity.zones.Count; i++)
            {
                TileZone zone = pCity.zones[i];
                ZoneManager.drawBegin();
                ZoneManager.drawZoneMeta(zone, pAsset, pGetter);
                ZoneManager.drawEnd(zone);
            }
        }

        private static IMetaObject GetTechMetaForZone(TileZone pZone)
        {
            City city = GetCityForZone(pZone);
            if (city?.data == null || city.kingdom?.data == null) return null;
            if (!KingdomPolicyService.CanUsePolicySystem(city.kingdom)) return null;
            string colorKey = TechMapModeService.GetCityColorKey(city);
            return GetMeta(AWMapModeMetaTypes.Tech, "city:" + city.id + ":" + colorKey,
                city.data.name ?? "", TechMapModeService.GetColorAssetForKey(colorKey));
        }

        private static Kingdom GetKingdomForZone(TileZone pZone)
        {
            City city = GetCityForZone(pZone);
            Kingdom kingdom = city?.kingdom;
            if (city?.data == null || city.isRekt() || kingdom?.data == null || kingdom.isRekt() ||
                kingdom.isNeutral())
                return null;
            return kingdom;
        }

        private static City GetCityForZone(TileZone pZone)
        {
            City city = pZone?.city;
            if (city?.data == null || city.isRekt() || !city.isAlive()) return null;
            return city;
        }

        private static IMetaObject GetWarCoreMetaForZone(TileZone pZone)
        {
            City city = pZone?.city;
            if (city?.data == null || city.isRekt()) return null;
            string key = WarCoreMapModeService.GetColorKeyForZone(pZone);
            if (string.IsNullOrEmpty(key)) return null;
            return GetMeta(AWMapModeMetaTypes.WarCore, key, key, CoreColor(key));
        }

        private static IMetaObject GetWarClaimMetaForZone(TileZone pZone)
        {
            City city = pZone?.city;
            if (city?.data == null || city.isRekt()) return null;
            string key = WarClaimMapModeService.GetColorKeyForZone(pZone);
            if (string.IsNullOrEmpty(key)) return null;
            return GetMeta(AWMapModeMetaTypes.WarClaim, key, key, ClaimColor(key));
        }

        private static IMetaObject GetMandateDynastyMetaForZone(TileZone pZone)
        {
            Kingdom kingdom = pZone?.city?.kingdom;
            string status = GetMandateDynastyStatusForKingdom(kingdom);
            if (!MandateDynastyMapRules.ShouldDrawStatus(status)) return null;
            if (MandateDynastyMapRules.ShouldUseKingdomColor(status))
                return GetMeta(AWMapModeMetaTypes.MandateDynasty, status + ":" + (kingdom?.id ?? -1),
                    status, DirectKingdomColor(kingdom, MandateDynastyColor(status)));
            return GetMeta(AWMapModeMetaTypes.MandateDynasty, status, status, MandateDynastyColor(status));
        }

        private static string GetMandateDynastyStatusForKingdom(Kingdom pKingdom)
        {
            Kingdom kingdom = pKingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return "";
            Kingdom mandate = GetCachedMandateDynastyKingdom();
            if (mandate?.data == null) return "";

            string cacheKey = MandateDynastyMapRules.BuildStatusCacheKey(mandate.id, kingdom.id);
            if (string.IsNullOrEmpty(cacheKey)) return "";

            _mandateDynastyStatusCache ??= new Dictionary<string, string>();
            if (!_mandateDynastyStatusCache.TryGetValue(cacheKey, out string status))
            {
                bool isMandate = kingdom == mandate || kingdom.id == mandate.id;
                bool rootIsMandate = false;
                if (!isMandate)
                {
                    Kingdom root = VassalService.GetRootSuzerain(kingdom);
                    rootIsMandate = root?.data != null && root.id == mandate.id;
                }

                status = MandateDynastyMapRules.ResolveStatus(isMandate, rootIsMandate);
                _mandateDynastyStatusCache[cacheKey] = status;
            }

            return status;
        }

        private static IMetaObject GetMandateCoreMetaForZone(TileZone pZone)
        {
            City city = pZone?.city;
            if (city?.data == null || city.isRekt()) return null;
            string status = MandateCoreMapModeService.GetStatusForZone(pZone);
            if (string.IsNullOrEmpty(status) || status == "none") return null;
            return GetMeta(AWMapModeMetaTypes.MandateCore, status, status, MandateCoreColor(status));
        }

        private static IMetaObject GetDevelopmentMetaForZone(TileZone pZone)
        {
            City city = GetCityForZone(pZone);
            if (city?.data == null || city.kingdom?.data == null) return null;
            string colorKey = DevelopmentMapModeService.GetCityColorKey(city);
            return GetMeta(AWMapModeMetaTypes.Development, "city:" + city.id + ":" + colorKey,
                city.data.name ?? "", DevelopmentMapModeService.GetColorAssetForKey(colorKey));
        }

        private static IMetaObject GetSchoolMetaForZone(TileZone pZone)
        {
            City city = GetCityForZone(pZone);
            if (city?.data == null || city.kingdom?.data == null) return null;
            string colorKey = SchoolMapModeService.GetCityColorHex(city);
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
            string schoolId = snapshot?.DominantSchool ?? CourtSchoolId.None;
            return GetMeta(AWMapModeMetaTypes.School, "render:" + schoolId + ":" + colorKey,
                SchoolMapModeService.GetSchoolDisplayName(schoolId), SchoolMapModeService.GetColorAsset(city));
        }

        private static IMetaObject GetSchoolIdentityMetaForZone(TileZone pZone)
        {
            return GetSchoolIdentityMetaForCity(GetCityForZone(pZone));
        }

        internal static AWMapModeMetaObject GetSchoolIdentityMetaForCity(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return null;
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(pCity);
            CourtSchoolDefinition definition = CourtSchoolRegistry.Find(snapshot?.DominantSchool);
            if (definition == null || snapshot.TotalScore <= 0f) return null;

            string displayName = SchoolMapModeService.GetSchoolDisplayName(definition.Id);
            AWMapModeMetaObject meta = GetMeta(AWMapModeMetaTypes.School, "school:" + definition.Id,
                displayName, MakeColor(definition.ColorHex));
            if (meta.data.name != displayName) meta.data.name = displayName;
            return meta;
        }

        private static AWMapModeMetaObject GetMeta(MetaType pType, string pKey, string pName, ColorAsset pColor)
        {
            string typeKey = AWMapModeMetaTypes.TryAsString(pType, out string id) ? id : pType.ToString();
            string fullKey = typeKey + ":" + pKey;
            if (Metas.TryGetValue(fullKey, out AWMapModeMetaObject meta)) return meta;
            var obj = new AWMapModeMetaObject(StableId(pType, pKey), pName, pType, pColor);
            Metas[fullKey] = obj;
            if (MapModeMetaCacheRules.IsDynamicMetaKey(fullKey))
                _hadAnyDynamicMeta = true;
            return obj;
        }

        private static long StableId(MetaType pType, string pKey)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                string raw = ((int)pType) + ":" + (pKey ?? "");
                for (int i = 0; i < raw.Length; i++)
                {
                    hash ^= raw[i];
                    hash *= 1099511628211L;
                }
                return hash & long.MaxValue;
            }
        }

        private static ColorAsset MakeColor(string pHex)
        {
            string hex = AWMapModeMetaRules.NormalizeMapColorHex(pHex);
            if (ColorCache.TryGetValue(hex, out ColorAsset cached) && cached != null) return cached;

            ColorAsset color = ColorAsset.tryMakeNewColorAsset(hex);
            color?.initColor();
            if (color != null) ColorCache[hex] = color;
            return color;
        }

        private static ColorAsset TechColor(string pKey)
        {
            return MakeColor(CityTechMapRules.HexForColorKey(pKey));
        }

        private static ColorAsset CoreColor(string pKey)
        {
            return MakeColor(WarMapModeColorRules.CoreHexForStatus(pKey));
        }

        private static ColorAsset ClaimColor(string pKey)
        {
            return MakeColor(WarMapModeColorRules.ClaimHexForStatus(pKey));
        }

        private static ColorAsset MandateCoreColor(string pKey)
        {
            return MakeColor(MandateCoreMapRules.HexForStatus(pKey));
        }

        private static ColorAsset DirectKingdomColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            if (pKingdom?.data == null) return pFallback;
            try
            {
                int colorId = pKingdom.data.color_id;
                if (colorId >= 0)
                    return AssetManager.kingdom_colors_library.getColorByIndex(colorId) ?? pFallback;
            }
            catch
            {
            }
            return pFallback;
        }

        internal static void ClearMandateDynastyStatusCache()
        {
            _mandateDynastyStatusCache?.Clear();
            _mandateDynastyMandateResolved = false;
            _mandateDynastyMandate = null;
        }

        internal static void ClearDynamicMetaCache()
        {
            if (!MapModeMetaCacheRules.ShouldClearForWorldSwitch(_hadAnyDynamicMeta)) return;

            var keys = new List<string>();
            foreach (string key in Metas.Keys)
                if (MapModeMetaCacheRules.IsDynamicMetaKey(key))
                    keys.Add(key);

            for (int i = 0; i < keys.Count; i++)
                Metas.Remove(keys[i]);

            _hadAnyDynamicMeta = false;
        }

        internal static void ClearRuntimeCaches()
        {
            ClearDynamicMetaCache();
            ClearMandateDynastyStatusCache();
        }

        private static ColorAsset MandateDynastyColor(string pStatus)
        {
            return MakeColor(MandateDynastyMapRules.HexForStatus(pStatus));
        }

        private static Kingdom GetCachedMandateDynastyKingdom()
        {
            if (_mandateDynastyMandateResolved)
            {
                if (_mandateDynastyMandate == null) return null;
                return _mandateDynastyMandate.data != null && !_mandateDynastyMandate.isRekt()
                    ? _mandateDynastyMandate
                    : null;
            }

            _mandateDynastyMandate = MandateService.GetCurrentMandateKingdom();
            _mandateDynastyMandateResolved = true;
            return _mandateDynastyMandate;
        }

        private static bool ShowKingdomTooltip(TileZone pZone, MetaTypeAsset pAsset, int pZoneOption)
        {
            if (AWMapModeMetaRules.ShouldUseCityTooltipForMapMode(pAsset?.map_mode ?? MetaType.None))
            {
                City city = GetCityForZone(pZone);
                Kingdom cityKingdom = city?.kingdom;
                if (city?.data == null || cityKingdom?.data == null) return false;
                Tooltip.hideTooltip(city, true, "city");
                Tooltip.show(city, "city", new TooltipData
                {
                    city = city,
                    kingdom = cityKingdom,
                    tooltip_scale = 0.7f,
                    is_sim_tooltip = true
                });
                return true;
            }

            Kingdom kingdom = pZone?.city?.kingdom;
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral()) return false;
            Tooltip.hideTooltip(kingdom, true, "kingdom");
            Tooltip.show(kingdom, "kingdom", new TooltipData
            {
                kingdom = kingdom,
                tooltip_scale = 0.7f,
                is_sim_tooltip = true
            });
            return true;
        }

        private static void HighlightKingdom(MetaTypeAsset pAsset, WorldTile pTile, QuantumSpriteAsset pQAsset)
        {
            City city = pTile?.zone?.city;
            if (city?.data == null || city.kingdom?.data == null) return;
            Color color = pQAsset.color;
            if (TechMapModeService.IsActive() &&
                AWMapModeMetaRules.IsRuntimeMeta(pAsset?.map_mode ?? MetaType.None, AWMapModeMetaTypes.Tech))
            {
                QuantumSpriteLibrary.colorZones(pQAsset, city.zones, color);
                return;
            }

            if (DevelopmentMapModeService.IsActive() &&
                AWMapModeMetaRules.IsRuntimeMeta(pAsset?.map_mode ?? MetaType.None, AWMapModeMetaTypes.Development))
            {
                QuantumSpriteLibrary.colorZones(pQAsset, city.zones, color);
                return;
            }

            foreach (City item in city.kingdom.getCities())
                QuantumSpriteLibrary.colorZones(pQAsset, item.zones, color);
        }
    }
}
