using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.presentation;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorVisualRolePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "getUnitTexturePath")]
        public static bool GetUnitTexturePathPrefix(Actor __instance,
            ref string __result)
        {
            if (TryGetBanditKingTexturePath(__instance, out __result))
                return false;
            if (TryGetBanditCivilianTexturePath(__instance, out __result))
                return false;

            ActorVisualRole role = ActorVisualRoleResolver.Resolve(__instance);
            if (role == ActorVisualRole.Default ||
                !TryGetRoleTexturePath(__instance, role, out __result))
                return true;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "checkSpriteHead")]
        public static bool CheckSpriteHeadPrefix(Actor __instance)
        {
            if (ShouldUseBanditKingHead(__instance))
                return ApplyBanditKingHead(__instance);
            if (ShouldUseBanditHead(__instance))
                return ApplyBanditHead(__instance);
            ActorVisualRole role = ActorVisualRoleResolver.Resolve(__instance);
            if (role == ActorVisualRole.Default)
                return true;
            if (__instance?.data == null || !__instance.dirty_sprite_head)
                return false;

            __instance.dirty_sprite_head = false;
            AnimationContainerUnit container = __instance.animation_container;
            if (__instance.frame_data == null ||
                !__instance.frame_data.show_head || container == null ||
                container.heads == null || container.heads.Length == 0 ||
                __instance.isEgg() ||
                (__instance.isBaby() &&
                 !container.render_heads_for_children))
                return false;

            ActorTextureSubAsset textureAsset = __instance.getTextureAsset();
            if (textureAsset == null) return false;
            if (!textureAsset.has_advanced_textures)
            {
                ApplyHeadId(__instance, container.heads);
                __instance.cached_sprite_head =
                    container.heads[__instance.data.head];
                return false;
            }

            string path;
            Sprite[] heads;
            if (__instance.isSexMale())
            {
                path = textureAsset.texture_heads_male;
                heads = container.heads_male;
            }
            else
            {
                path = textureAsset.texture_heads_female;
                heads = container.heads_female;
            }

            string specialPath = null;
            if (__instance.isSapient())
            {
                switch (role)
                {
                    case ActorVisualRole.Warrior:
                        if (!__instance.equipment.helmet.isEmpty())
                            specialPath = textureAsset.texture_head_warrior;
                        break;
                    case ActorVisualRole.King:
                        specialPath = textureAsset.texture_head_king;
                        break;
                    case ActorVisualRole.Civilian:
                    case ActorVisualRole.Leader:
                        if (textureAsset.has_old_heads &&
                            __instance.hasTrait("wise"))
                            specialPath = __instance.isSexMale()
                                ? textureAsset.texture_heads_old_male
                                : textureAsset.texture_heads_old_female;
                        break;
                }
            }
            if (!string.IsNullOrEmpty(specialPath))
            {
                __instance.cached_sprite_head =
                    ActorAnimationLoader.getHeadSpecial(specialPath);
                return false;
            }

            if (heads == null || heads.Length == 0) return false;
            ApplyHeadId(__instance, heads);
            __instance.cached_sprite_head =
                ActorAnimationLoader.getHead(path, __instance.data.head);
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ActorAvatarData), "setData", typeof(Actor))]
        public static void SetAvatarDataPostfix(ActorAvatarData __instance,
            Actor pActor)
        {
            if (__instance == null || pActor?.data == null) return;
            ActorVisualRole role = ActorVisualRoleResolver.Resolve(pActor);
            if (role == ActorVisualRole.Default) return;
            __instance.is_king = ActorVisualRoleRules.IsKing(role,
                pActor.isKing());
            bool vanillaWarrior = pActor.isWarrior() &&
                                  !pActor.equipment.helmet.isEmpty();
            __instance.is_warrior = ActorVisualRoleRules.IsWarrior(role,
                vanillaWarrior) && !pActor.equipment.helmet.isEmpty();
        }

        private static bool TryGetRoleTexturePath(Actor pActor,
            ActorVisualRole pRole, out string pPath)
        {
            pPath = null;
            if (pActor?.asset == null || pActor.isEgg() || pActor.isBaby())
                return false;
            if (pActor.hasSubspecies() &&
                pActor.subspecies.has_mutation_reskin &&
                pActor.asset.unit_zombie)
                return false;
            ActorTextureSubAsset textureAsset = pActor.getTextureAsset();
            if (textureAsset == null || !textureAsset.has_advanced_textures)
                return false;

            switch (pRole)
            {
                case ActorVisualRole.Civilian:
                    pPath = CivilianTexturePath(pActor, textureAsset);
                    return !string.IsNullOrEmpty(pPath);
                case ActorVisualRole.Warrior:
                    return TryGetWarriorTexturePath(pActor, textureAsset,
                        out pPath);
                case ActorVisualRole.Leader:
                    pPath = textureAsset.texture_path_leader;
                    return !string.IsNullOrEmpty(pPath);
                case ActorVisualRole.King:
                    pPath = textureAsset.texture_path_king;
                    return !string.IsNullOrEmpty(pPath);
                default:
                    return false;
            }
        }

        private static bool TryGetWarriorTexturePath(Actor pActor,
            ActorTextureSubAsset pTextureAsset, out string pPath)
        {
            pPath = null;

            string skin = pTextureAsset.texture_path_warrior;
            if (pActor.hasSubspecies())
                skin = pActor.subspecies.getSkinWarrior();
            if (pActor.subspecies?.has_mutation_reskin == true)
            {
                List<string> mutationSkins =
                    pActor.subspecies.mutation_skin_asset?.skin_warrior;
                if (mutationSkins == null || mutationSkins.Count == 0)
                    return false;
                int originalIndex = pActor.asset.skin_warrior.IndexOf(skin);
                int index = Toolbox.loopIndex(originalIndex,
                    mutationSkins.Count);
                skin = mutationSkins[index];
            }
            if (string.IsNullOrEmpty(skin)) return false;
            pPath = pTextureAsset.texture_path_base + skin;
            return !string.IsNullOrEmpty(pPath);
        }

        private static string CivilianTexturePath(Actor pActor,
            ActorTextureSubAsset pTextureAsset)
        {
            if (pActor.isSexFemale())
                return pActor.hasSubspecies()
                    ? pTextureAsset.texture_path_base +
                      pActor.subspecies.getSkinFemale()
                    : pTextureAsset.texture_path_base_female;
            return pActor.hasSubspecies()
                ? pTextureAsset.texture_path_base +
                  pActor.subspecies.getSkinMale()
                : pTextureAsset.texture_path_base_male;
        }

        private static bool TryGetBanditCivilianTexturePath(Actor pActor,
            out string pPath)
        {
            pPath = null;
            if (pActor?.asset == null || pActor.isEgg() || pActor.isBaby() ||
                pActor.isWarrior() || pActor.isKing() ||
                pActor.isCityLeader() ||
                !string.Equals(pActor.asset.id, XiaRace.ID,
                    System.StringComparison.Ordinal))
                return false;

            bool bandit;
            try
            {
                bandit = PeasantRebelRouteService.IsBandit(pActor.kingdom);
            }
            catch
            {
                return false;
            }
            if (!bandit) return false;

            ActorTextureSubAsset textureAsset = pActor.getTextureAsset();
            if (textureAsset == null || !textureAsset.has_advanced_textures)
                return false;

            pPath = textureAsset.texture_path_base +
                (pActor.isSexFemale() ? "bandit_female" : "bandit_male");
            return true;
        }

        private static bool TryGetBanditKingTexturePath(Actor pActor,
            out string pPath)
        {
            pPath = null;
            if (pActor?.asset == null || pActor.isEgg() || pActor.isBaby() ||
                !pActor.isKing() ||
                !string.Equals(pActor.asset.id, XiaRace.ID,
                    System.StringComparison.Ordinal))
                return false;

            try
            {
                if (!PeasantRebelRouteService.IsBandit(pActor.kingdom))
                    return false;
            }
            catch
            {
                return false;
            }

            ActorTextureSubAsset textureAsset = pActor.getTextureAsset();
            if (textureAsset == null || !textureAsset.has_advanced_textures)
                return false;
            pPath = textureAsset.texture_path_base + "bandit_general";
            return true;
        }

        private static void ApplyHeadId(Actor pActor, Sprite[] pHeads)
        {
            if (pActor?.data == null || pHeads == null || pHeads.Length == 0)
                return;
            if (pActor.data.head > pHeads.Length - 1) pActor.data.head = 0;
            if (pActor.data.head == -1)
                pActor.data.head = AnimationHelper.getSpriteIndex(
                    pActor.data.id, pHeads.Length);
        }

        private static bool ApplyBanditHead(Actor pActor)
        {
            if (pActor?.data == null || !pActor.dirty_sprite_head)
                return false;
            pActor.dirty_sprite_head = false;
            AnimationContainerUnit container = pActor.animation_container;
            if (pActor.frame_data == null || !pActor.frame_data.show_head ||
                container == null || container.heads == null ||
                container.heads.Length == 0 || pActor.isEgg() ||
                (pActor.isBaby() && !container.render_heads_for_children))
                return false;
            ActorTextureSubAsset textureAsset = pActor.getTextureAsset();
            if (textureAsset == null) return false;

            int headIndex = XiaBanditHeadRules.ResolveHeadIndex(pActor.data.id);
            pActor.data.head = headIndex;
            pActor.cached_sprite_head = SpriteTextureLoader.getSprite(
                textureAsset.texture_path_base +
                XiaBanditHeadRules.ResolveHeadPath(pActor.data.id));
            return false;
        }

        private static bool ApplyBanditKingHead(Actor pActor)
        {
            if (pActor?.data == null || !pActor.dirty_sprite_head)
                return false;
            pActor.dirty_sprite_head = false;
            AnimationContainerUnit container = pActor.animation_container;
            if (pActor.frame_data == null || !pActor.frame_data.show_head ||
                container == null || container.heads == null ||
                container.heads.Length == 0 || pActor.isEgg() ||
                (pActor.isBaby() && !container.render_heads_for_children))
                return false;
            ActorTextureSubAsset textureAsset = pActor.getTextureAsset();
            if (textureAsset == null) return false;

            pActor.cached_sprite_head = ActorAnimationLoader.getHeadSpecial(
                textureAsset.texture_path_base +
                "heads_special/head_bandit_general");
            return false;
        }

        private static bool ShouldUseBanditKingHead(Actor pActor)
        {
            if (pActor?.asset == null || !pActor.isKing() ||
                !string.Equals(pActor.asset.id, XiaRace.ID,
                    System.StringComparison.Ordinal))
                return false;
            try
            {
                return PeasantRebelRouteService.IsBandit(pActor.kingdom);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldUseBanditHead(Actor pActor)
        {
            if (pActor?.data == null || pActor.asset == null) return false;
            bool bandit = false;
            bool synthetic = false;
            try { bandit = PeasantRebelRouteService.IsBandit(pActor.kingdom); }
            catch { }
            try { synthetic = SyntheticLevyService.IsSynthetic(pActor); }
            catch { }
            return XiaBanditHeadRules.ShouldUse(pActor.asset.id, bandit,
                synthetic);
        }
    }
}
