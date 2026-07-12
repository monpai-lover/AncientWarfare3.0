using System;
using AncientWarfare3.core.court;
using UnityEngine;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     状态效果注册(移植自 AW2 StatusEffectLibrary 的 qing)。
    ///     qing:戟/戈命中施加的青色清扫特效(0.5s),配合武器的 path_slash_animation="qing" 斩击动画。
    ///
    ///     新版 API:AssetManager.status(StatusLibrary),StatusAsset(旧 StatusEffect)。
    ///     字段:texture/locale_id/locale_description/duration/path_icon/animated/animation_speed。
    /// </summary>
    public static class XiaStatus
    {
        public static void Init()
        {
            var qing = new StatusAsset
            {
                id = "qing",
                texture = "qing",
                path_icon = "effects/qing/tile002",
                duration = 0.5f,
                animated = true,
                animation_speed = 0.1f,
                locale_id = "status_title_qing",
                locale_description = "status_description_qing"
            };
            Sprite[] sprites = SpriteTextureLoader.getSpriteList("effects/qing");
            qing.sprite_list = sprites ?? Array.Empty<Sprite>();
            qing.get_sprites_count = (_, effect) => effect?.sprite_list?.Length ?? 0;
            AssetManager.status.add(qing);

            var royalMedicalCare = new StatusAsset
            {
                id = CourtStatusId.RoyalMedicalCare,
                duration = 120f,
                allow_timer_reset = true,
                path_icon = "ui/Icons/traits/icondanyao",
                locale_id = "status_title_aw_royal_medical_care",
                locale_description = "status_description_aw_royal_medical_care"
            };
            royalMedicalCare.base_stats["multiplier_health"] = 0.5f;
            royalMedicalCare.base_stats["lifespan"] = 15f;
            AssetManager.status.add(royalMedicalCare);
        }
    }
}
