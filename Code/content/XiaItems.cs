using System;
using System.Collections.Generic;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     物品注册(移植自 AW2 ItemLibrary,蓝图 1.3):戟 ji / 戈 ge / 兵法 binfa。
    ///
    ///     新版 API 适配(见记忆 aw3-item-api):
    ///     - AssetManager.items.clone(新id, 模板id);ItemAsset.material 是单个 string(旧 materials 列表已删)
    ///     - equipment_type(EquipmentType)、quality(Rarity,旧 ItemQuality)
    ///     - base_stats["key"]=val 索引器;knockback_reduction→knockback
    ///     - path_slash_animation="qing":挥击时播放青色斩击特效动画(复用 effects/qing 贴图)
    ///     - 命中施加 qing 状态:action_attack_target += qingAttack,addStatusEffect 有 string 重载
    /// </summary>
    public static class XiaItems
    {
        public static void Init()
        {
            RegisterWeapons();
            AssignPreferredWeapons();
        }

        private static void RegisterWeapons()
        {
            // ===== 戟 ji(clone 剑基础模板)=====
            // 新版剑基础模板是 "$sword"(带 $ 前缀;"sword" 只是武器池/组名,非真实 item)
            ItemAsset ji = AssetManager.items.clone("ji", "$sword");
            ji.path_icon = "ui/Icons/items/icon_ji";
            // ⚠ ji/ge clone 自 $sword,继承 is_pool_weapon=true。但 mod 在原版 ItemLibrary.post_init()
            //   跑完后才注册,post_init 不会回头补处理新物品 → path_gameplay_sprite 停留在 null,
            //   进图时 loadSprites() 拿 null 去字典 TryGetValue 直接崩(ArgumentNullException: key)。
            //   这里手动补设 post_init 漏掉的那步:地图武器贴图路径(贴图已从 AW2 继承到 items/weapons/)。
            ji.path_gameplay_sprite = "items/weapons/w_ji";
            ji.name_class = "item_class_weapon";
            ji.equipment_type = EquipmentType.Weapon;
            ji.material = "bronze";
            ji.equipment_value = 800;
            ji.base_stats["damage"] = 10f;
            ji.base_stats["attack_speed"] = -1f;
            ji.base_stats["critical_chance"] = 0.03f;
            ji.base_stats["knockback"] = 0.1f;   // 旧 knockback_reduction +0.1
            ji.base_stats["targets"] = 1f;
            ji.path_slash_animation = "qing";     // 青色斩击特效动画
            ji.action_attack_target =
                (AttackAction)Delegate.Combine(ji.action_attack_target, new AttackAction(QingAttack));

            // ===== 戈 ge(clone 剑基础模板,暴击翻倍)=====
            ItemAsset ge = AssetManager.items.clone("ge", "$sword");
            ge.path_icon = "ui/Icons/items/icon_ge";
            ge.path_gameplay_sprite = "items/weapons/w_ge"; // 同 ji:补 post_init 漏设的地图武器贴图路径,避免 null 崩
            ge.name_class = "item_class_weapon";
            ge.equipment_type = EquipmentType.Weapon;
            ge.material = "bronze";
            ge.equipment_value = 800;
            ge.base_stats["damage"] = 10f;
            ge.base_stats["attack_speed"] = -1f;
            ge.base_stats["critical_chance"] = 0.06f;  // 戟的两倍
            ge.base_stats["knockback"] = 0.1f;
            ge.base_stats["targets"] = 1f;
            ge.path_slash_animation = "qing";
            ge.action_attack_target =
                (AttackAction)Delegate.Combine(ge.action_attack_target, new AttackAction(QingAttack));

            // ===== 兵法 binfa(传奇护符)=====
            // 新版护符基础模板是 "$amulet"(带 $ 前缀,旧版 "_accessory" 已变)
            ItemAsset binfa = AssetManager.items.clone("binfa", "$amulet");
            binfa.path_icon = "ui/Icons/items/icon_binfa";
            binfa.name_class = "item_class_accessory";
            binfa.equipment_type = EquipmentType.Amulet;
            binfa.quality = Rarity.R3_Legendary;
            binfa.material = "bronze";
            binfa.equipment_value = 2000;
            binfa.base_stats["warfare"] = 20f;
            binfa.base_stats["critical_chance"] = 3f;
            binfa.name_templates = new List<string> { "amulet_name" };
        }

        /// <summary>四个原版种族偏好戟/戈/兵法(新版用 ActorAsset.default_weapons)。</summary>
        private static void AssignPreferredWeapons()
        {
            foreach (string raceId in new[] { "human", "orc", "dwarf", "elf" })
            {
                ActorAsset race = AssetManager.actor_library.get(raceId);
                if (race == null) continue;
                race.default_weapons = Append(race.default_weapons, "ji", "ge", "binfa");
            }

            // 金币上限提高(蓝图 1.3:gold 50000)
            ResourceAsset gold = AssetManager.resources.get("gold");
            if (gold != null) gold.maximum = 50000;
        }

        private static string[] Append(string[] pBase, params string[] pAdd)
        {
            var list = new List<string>(pBase ?? Array.Empty<string>());
            list.AddRange(pAdd);
            return list.ToArray();
        }

        /// <summary>命中目标时施加 qing 状态(青色清扫特效 0.5s)。</summary>
        public static bool QingAttack(BaseSimObject pSelf, BaseSimObject pTarget, WorldTile pTile = null)
        {
            if (pTarget is Actor a && a.isAlive())
            {
                a.addStatusEffect("qing", 0.5f);
            }
            return true;
        }
    }
}
