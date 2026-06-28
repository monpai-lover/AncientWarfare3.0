using System;
using System.Collections.Generic;
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
    }
}
