namespace AncientWarfare3.content
{
    /// <summary>
    ///     神力(god power)注册。生成夏人单位的 god power。
    ///
    ///     ⚠ power id **必须 = 种族 id "Xia"**(对齐原版:human 种族 ↔ id="human" 的 god power,
    ///     PowerLibrary.cs:2301)。因为 ActorAsset.getGodPower() 用 `power_id ?? base_asset_id ?? id`
    ///     反查 god power(ActorAsset.cs:1347):Xia 没设 power_id → 回退 id="Xia" → 必须有同名 power,
    ///     否则 getGodPower() 返 null → getDescriptionID()/getLocalizedDescription() 喂 null 给
    ///     LocalizedTextManager.getText → ArgumentNullException(key) 崩(hover/点 spawn 按钮 tooltip 时)。
    ///
    ///     做法:clone 原版 "human" 生成 power(已绑 type=PowerSpawnActor + click_action=spawnUnit),
    ///     改 actor_asset_id 指向 Xia。按钮 id 也必须 = "Xia"(AW_LineageTab,经 powers.get(name) 绑定)。
    /// </summary>
    public static class GodPowerLibrary
    {
        /// <summary>生成夏人 god power 的 id = 种族 id(原版范式:power id ↔ actor id 同名)。</summary>
        public const string SPAWN_XIA = XiaRace.ID; // "Xia"

        public static void Init()
        {
            // 防御:理论上 OnModLoad 在 AssetManager.init 之后,human power 已存在(与 actor/item 同批)。
            if (AssetManager.powers.get("human") == null)
            {
                ModClass.LogWarning("spawn_xia: 原版 human power 缺失,跳过神力注册。");
                return;
            }

            GodPower xia = AssetManager.powers.clone(SPAWN_XIA, "human");
            xia.name = SPAWN_XIA;                  // 本地化键(others.csv 注册 Xia=夏)
            xia.actor_asset_id = XiaRace.ID;       // "Xia" → 生成夏人(决定种族的字段)
            xia.path_icon = "ui/Icons/iconXias";   // 神力自身图标(顶栏选中显示)
            // type / click_action / actor_spawn_height 等已从 human clone 带过来,无需改。
        }
    }
}
