using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_AgePatch
    {
        internal const float XIA_OLD_HEAD_AGE = 75f;
        private const string XIA_OLD_HEAD_ACTIVE = "aw_xia_old_head_active";
        private const string XIA_OLD_HEAD_SEEN = "aw_xia_old_head_seen";
        private const float XIA_AGE_PRESSURE_START_RATIO = 1.25f;
        private const float XIA_AGE_HIGH_PRESSURE_RATIO = 1.65f;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "updateAge")]
        public static void UpdateAge_Postfix(Actor __instance)
        {
            if (__instance?.data == null) return;
            if (!LineageService.IsXia(__instance)) return;
            if (__instance.isRekt() || !__instance.isAlive()) return;

            bool shouldUseOldHead = ShouldUseXiaOldHead(__instance);
            __instance.data.get(XIA_OLD_HEAD_ACTIVE, out bool wasOldHead, false);
            __instance.data.get(XIA_OLD_HEAD_SEEN, out bool seen, false);
            if (seen && wasOldHead == shouldUseOldHead) return;

            __instance.data.set(XIA_OLD_HEAD_SEEN, true);
            __instance.data.set(XIA_OLD_HEAD_ACTIVE, shouldUseOldHead);
            __instance.clearGraphicsFully();
        }

        internal static bool ShouldUseXiaOldHead(Actor pActor)
        {
            if (pActor?.data == null) return false;
            if (!LineageService.IsXia(pActor)) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            return pActor.getAge() >= XIA_OLD_HEAD_AGE;
        }

        internal static bool ShouldUseXiaOldHead(long pActorId)
        {
            if (pActorId < 0) return false;
            Actor actor = World.world?.units?.get(pActorId);
            return ShouldUseXiaOldHead(actor);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "checkSpriteHead")]
        public static bool CheckSpriteHead_Prefix(Actor __instance)
        {
            if (__instance?.data == null || !LineageService.IsXia(__instance)) return true;
            if (!__instance.dirty_sprite_head) return false;

            __instance.dirty_sprite_head = false;
            if (__instance.frame_data == null || !__instance.frame_data.show_head) return false;
            if (__instance.animation_container == null || __instance.animation_container.heads == null ||
                __instance.animation_container.heads.Length == 0) return false;
            if (__instance.isEgg()) return false;
            if (__instance.isBaby() && !__instance.animation_container.render_heads_for_children) return false;

            ActorTextureSubAsset textureAsset = __instance.getTextureAsset();
            if (textureAsset == null) return false;

            if (!textureAsset.has_advanced_textures)
            {
                ApplyHeadId(__instance, __instance.animation_container.heads);
                __instance.cached_sprite_head = __instance.animation_container.heads[__instance.data.head];
                return false;
            }

            bool specialHead = false;
            string path;
            Sprite[] listHeads;
            if (__instance.isSexMale())
            {
                path = textureAsset.texture_heads_male;
                listHeads = __instance.animation_container.heads_male;
            }
            else
            {
                path = textureAsset.texture_heads_female;
                listHeads = __instance.animation_container.heads_female;
            }

            if (__instance.isSapient())
            {
                if (__instance.is_profession_warrior && !__instance.equipment.helmet.isEmpty())
                {
                    path = textureAsset.texture_head_warrior;
                    specialHead = true;
                }
                else if (__instance.is_profession_king)
                {
                    path = textureAsset.texture_head_king;
                    specialHead = true;
                }
                else if (ShouldUseXiaOldHead(__instance))
                {
                    path = __instance.isSexMale()
                        ? textureAsset.texture_heads_old_male
                        : textureAsset.texture_heads_old_female;
                    specialHead = true;
                }
            }

            if (specialHead && !string.IsNullOrEmpty(path))
            {
                __instance.cached_sprite_head = ActorAnimationLoader.getHeadSpecial(path);
                return false;
            }

            if (listHeads == null || listHeads.Length == 0) return false;
            ApplyHeadId(__instance, listHeads);
            __instance.cached_sprite_head = ActorAnimationLoader.getHead(path, __instance.data.head);
            return false;
        }

        private static void ApplyHeadId(Actor pActor, Sprite[] pHeads)
        {
            if (pActor == null || pHeads == null || pHeads.Length == 0) return;
            if (pActor.data.head > pHeads.Length - 1) pActor.data.head = 0;
            if (pActor.data.head == -1)
                pActor.data.head = AnimationHelper.getSpriteIndex(pActor.data.id, pHeads.Length);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.checkNaturalDeath))]
        public static void CheckNaturalDeath_Postfix(Actor __instance, ref bool __result)
        {
            if (__result) return;
            if (__instance?.data == null) return;
            if (!LineageService.IsXia(__instance)) return;
            if (!WorldLawLibrary.world_law_old_age.isEnabled()) return;
            if (__instance.hasTrait("immortal")) return;

            float lifespan = __instance.stats["lifespan"];
            if (lifespan <= 0f) return;

            float ratio = __instance.getAge() / lifespan;
            if (ratio <= XIA_AGE_PRESSURE_START_RATIO) return;

            float chance = ratio >= XIA_AGE_HIGH_PRESSURE_RATIO
                ? 0.85f
                : Mathf.Clamp((ratio - XIA_AGE_PRESSURE_START_RATIO) * 0.5f, 0.02f, 0.35f);
            if (!Randy.randomChance(chance)) return;

            __instance.data.set(LineageKeys.DEATH_CAUSE, "\u81EA\u7136\u8001\u6B7B");
            __instance.getHitFullHealth(AttackType.Age);
            __result = true;
        }
    }
}
