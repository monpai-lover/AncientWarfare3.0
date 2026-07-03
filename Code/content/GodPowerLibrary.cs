using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

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
            RegisterVassalMapMode();
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

        private static void RegisterTechMapMode()
        {
            if (AssetManager.powers.get(TechMapModeService.POWER_ID) != null)
            {
                TechMapModeService.EnsureLayer();
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = TechMapModeService.POWER_ID,
                name = TechMapModeService.POWER_ID,
                path_icon = "ui/icons/iconKnowledge",
                map_modes_switch = true,
                toggle_name = TechMapModeService.POWER_ID,
                force_map_mode = MetaType.Kingdom,
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = _ => TechMapModeService.DirtyMap()
            });
            TechMapModeService.EnsureLayer();
        }

        private static void RegisterVassalMapMode()
        {
            if (AssetManager.powers.get(VassalMapModeService.POWER_ID) != null)
            {
                VassalMapModeService.EnsureLayer();
                return;
            }

            AssetManager.powers.add(new GodPower
            {
                id = VassalMapModeService.POWER_ID,
                name = VassalMapModeService.POWER_ID,
                path_icon = "ui/wars/war_vassal",
                map_modes_switch = true,
                toggle_name = VassalMapModeService.POWER_ID,
                force_map_mode = MetaType.Kingdom,
                unselect_when_window = true,
                ignore_cursor_icon = true,
                allow_unit_selection = true,
                toggle_action = _ => VassalMapModeService.DirtyMap()
            });
            VassalMapModeService.EnsureLayer();
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
