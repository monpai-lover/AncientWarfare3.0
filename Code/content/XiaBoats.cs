namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝专属船(贸易 / 运输)。新版只有 boat_trading / boat_transport 两种船(无 battle_boat 概念)。
    ///
    ///     ⚠ **clone 自模板 `$boat_trading$`/`$boat_transport$`**(完整船模板,含 boat_type/default_attack/
    ///     decisions/base_stats),**不是** `boat_trading_human`。原因:human 船 clone 自模板后被 post_init 的
    ///     loadTexturesAndSprites 设了 texture_asset(指向 human 人物贴图 actors/species/heads_male),
    ///     若从 human 船 clone 会继承这套脏 texture 状态 → 报 "texture_path_main doesn't exist for
    ///     boat_trading_Xia at actors/species/heads_male"。从干净模板 clone 避开此警告(Cultiway EasternHuman 船同法)。
    ///
    ///     船身贴图:`Boat.getAnimationDataBoat` → `loadAnimationBoat(boat_texture_id=id)` 懒加载
    ///     `actors/boats/{id}/`(ActorAnimationLoader.cs:52,运行时首次用才加载+缓存,不依赖 post_init 预加载)。
    ///     故 boat_trading_Xia 自动用 actors/boats/boat_trading_Xia/(各 18 帧,已备)。
    ///
    ///     时机:必须在 XiaArchitecture.Init **之前**(架构 actor_asset_id_trading/transport 要引用这俩 id)。
    ///     **不预读 getSpriteList 校验贴图**(见记忆 aw3-architecture-buildings 缓存毒化坑)。
    /// </summary>
    public static class XiaBoats
    {
        public const string TRADING_ID   = "boat_trading_Xia";
        public const string TRANSPORT_ID = "boat_transport_Xia";

        // 从干净模板 clone(非 *_human,避免继承 human 的 texture_asset 脏状态)。
        private const string TRADING_FROM   = "$boat_trading$";
        private const string TRANSPORT_FROM = "$boat_transport$";

        public static void Init()
        {
            CloneBoat(TRADING_ID, TRADING_FROM);
            CloneBoat(TRANSPORT_ID, TRANSPORT_FROM);
        }

        private static void CloneBoat(string pNewId, string pFromId)
        {
            if (AssetManager.actor_library.get(pNewId) != null) return; // 幂等
            if (AssetManager.actor_library.get(pFromId) == null)
            {
                ModClass.LogWarning("XiaBoats: 源船 '" + pFromId + "' 缺失,跳过 " + pNewId + "。");
                return;
            }
            // clone 继承 is_boat / boat_type / default_attack / decisions 等;船身贴图按新 id 懒加载 actors/boats/{pNewId}/。
            ActorAsset boat = AssetManager.actor_library.clone(pNewId, pFromId);

            // ⚠ 关闭 has_sprite_renderer(默认 true)——船**不走普通单位 sprite 渲染**,而走 Boat 组件
            //   (Actor.cs:687 is_boat→addChildSimple(new Boat()),用 boat_texture_id 懒加载 actors/boats/{id}/)。
            //   保持 true 会让 preloadMainUnitSprites(:1323-1327)对船调 texture_asset.preloadSprites():
            //     - mod 晚于 post_init clone → texture_asset 为 null → 加载期 NullReferenceException;
            //     - 或借 human texture_asset 占位 → 其 texture_path_main 指向 actors/species/heads_male →
    //       校验"对 boat_*_Xia 不存在"刷 logAssetError 警告。
            //   设 false 后 :1323 直接 continue 跳过、BatchActors:105 也跳过普通 sprite 批渲染,船照常用 Boat 渲染。
            //   既不崩也无警告,船身贴图不受影响。
            boat.has_sprite_renderer = false;
        }
    }
}
