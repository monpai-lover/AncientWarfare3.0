using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝种族贴图接入。
    ///     新版 WorldBox(0.51.0+)自带种族(human 等)用单图集 sprite sheet,但 mod 仍可走
    ///     "逐帧 png 目录 + sprites.json" 路径(参考同版本 PVZ mod 的 loadTexturesAndSprites)。
    ///     AW2 的 races/Xia 贴图正是逐帧 png 格式,目录:actors/races/Xia/。
    ///
    ///     与 PVZ 不同:夏人是人形种族,有完整头型(heads_male/heads_female/heads_special),
    ///     因此这里保留头型路径,而非像植物/僵尸那样置空。
    /// </summary>
    public static class XiaTextures
    {
        /// <summary>动态贴图委托表,key = ActorAsset.id。由 getUnitTexturePath patch 消费。</summary>
        public static readonly Dictionary<string, Func<Actor, string>> AnimationTextures =
            new Dictionary<string, Func<Actor, string>>();

        /// <summary>
        ///     把一个 ActorAsset 的贴图绑定到给定的逐帧 png 根目录(末尾不带斜杠的目录前缀)。
        ///     pBasePath 例:"actors/races/Xia/"
        /// </summary>
        public static void BindRaceTextures(ActorAsset pAsset, string pBasePath)
        {
            // has_advanced_textures=true 才会按职业(战士/国王/领袖)区分贴图,人形种族需要。
            pAsset.has_advanced_textures = true;
            pAsset.render_heads_for_babies = false;

            // ⚠ 核心修复(PixelBag 越界根因):新版种族靠 skin_citizen_* 数组(子目录名)定位多套皮肤,
            //   单位 spawn 时按同一随机索引取 male/female/warrior 三数组对应项,拼 base+skinName 加载贴图。
            //   clone($civ_advanced_unit$) 继承的默认值是 ["male_1"…"male_10"](无 unit_ 前缀),
            //   与 AW2 实际目录名 unit_male_1 对不上 → 拼出不存在路径 → 拿空 sprite 数组 → 下游按帧索引越界崩。
            //   照搬 Cultiway 范式:动态扫 mod 磁盘目录得到真实皮肤名,天然对齐、天然等长。
            BindSkinArrays(pAsset, pBasePath);

            var tex = new ActorTextureSubAsset(pBasePath, pAsset.has_advanced_textures);
            pAsset.texture_asset = tex;

            // 主体/平民:base + 皮肤名(unit_male_1 等),由 subspecies.getSkinMale/Female 拼接。
            tex.texture_path_base = pBasePath;
            tex.texture_path_main = pBasePath + "unit_male_1";
            tex.texture_path_base_male = pBasePath + "unit_male_1";
            tex.texture_path_base_female = pBasePath + "unit_female_1";

            // 特殊职业(AW2 贴图目录名)。
            tex.texture_path_warrior = pBasePath + "unit_warrior_1";
            tex.texture_path_king = pBasePath + "unit_king";
            tex.texture_path_leader = pBasePath + "unit_leader";
            tex.texture_path_baby = pBasePath + "unit_child";

            // 头型:夏人有完整头型贴图,保留。
            tex.texture_heads_male = pBasePath + "heads_male";
            tex.texture_heads_female = pBasePath + "heads_female";
            tex.texture_head_warrior = pBasePath + "heads_special/head_warrior";
            tex.texture_head_king = pBasePath + "heads_special/head_king";
            tex.texture_heads_old_male = pBasePath + "heads_special/head_old_male";
            tex.texture_heads_old_female = pBasePath + "heads_special/head_old_female";

            tex.prevent_unconscious_rotation = pAsset.prevent_unconscious_rotation;
            tex.render_heads_for_children = false;

            if (pAsset.shadow)
            {
                tex.shadow = true;
                tex.shadow_texture = pAsset.shadow_texture;
                tex.shadow_texture_egg = pAsset.shadow_texture_egg;
                tex.shadow_texture_baby = pAsset.shadow_texture_baby;
                tex.loadShadow();
            }
        }

        /// <summary>
        ///     设 skin_citizen_male/female/warrior 三数组(平民/战士多套皮肤的子目录名)。
        ///     动态扫 mod 磁盘 GameResources/&lt;pBasePath&gt; 下 unit_male_*/unit_female_*/unit_warrior_* 子目录,
        ///     得到与磁盘一致的真实皮肤名。三数组**必须等长**(Subspecies 用同一随机索引取三者,长度不一会越界),
        ///     故按三者最小数量截齐。任一类扫不到则整体回退到单皮肤,绝不留空数组(空数组同样会越界)。
        /// </summary>
        private static void BindSkinArrays(ActorAsset pAsset, string pBasePath)
        {
            string root = ModClass.Instance.GetDeclaration().FolderPath + "/GameResources/" + pBasePath;

            string[] males = ScanSkins(root, "unit_male_");
            string[] females = ScanSkins(root, "unit_female_");
            string[] warriors = ScanSkins(root, "unit_warrior_");

            // 三者等长截齐(同一 skin_id 索引三数组)。任一为空则全部回退单皮肤。
            int count = Math.Min(males.Length, Math.Min(females.Length, warriors.Length));
            if (count <= 0)
            {
                pAsset.skin_citizen_male = new[] { "unit_male_1" };
                pAsset.skin_citizen_female = new[] { "unit_female_1" };
                pAsset.skin_warrior = new[] { "unit_warrior_1" };
                ModClass.LogWarning("Xia skin 目录扫描为空,回退单皮肤 unit_*_1。检查 " + root);
                return;
            }

            pAsset.skin_citizen_male = males.Take(count).ToArray();
            pAsset.skin_citizen_female = females.Take(count).ToArray();
            pAsset.skin_warrior = warriors.Take(count).ToArray();
        }

        /// <summary>扫给定根目录下以 pPrefix 开头的子目录名(仅目录名,不含路径),按编号自然排序。</summary>
        private static string[] ScanSkins(string pRoot, string pPrefix)
        {
            if (!Directory.Exists(pRoot)) return Array.Empty<string>();
            return Directory.GetDirectories(pRoot)
                .Select(d => new DirectoryInfo(d).Name)
                .Where(n => n.StartsWith(pPrefix, StringComparison.Ordinal))
                .OrderBy(n => ParseTrailingNumber(n, pPrefix))
                .ToArray();
        }

        /// <summary>取目录名末尾编号(unit_male_10→10),用于自然排序(避免 _10 排到 _2 前)。</summary>
        private static int ParseTrailingNumber(string pName, string pPrefix)
        {
            string tail = pName.Substring(pPrefix.Length);
            return int.TryParse(tail, out int n) ? n : int.MaxValue;
        }
    }
}
