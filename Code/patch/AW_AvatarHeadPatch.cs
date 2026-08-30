using HarmonyLib;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.presentation;
using UnityEngine;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Keeps Xia inspect/family-tree heads aligned with map heads.
    /// World map loads head_N by file name, while the UI helper uses array index order.
    /// This patch also makes Xia old heads age-based instead of wise-trait-based.
    /// </summary>
    [HarmonyPatch]
    public static class AW_AvatarHeadPatch
    {
        private const string XIA = "Xia";

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DynamicActorSpriteCreatorUI), nameof(DynamicActorSpriteCreatorUI.getSpriteHeadForUI))]
        public static void GetSpriteHeadForUI_Postfix(ref Sprite __result, ActorAsset pAsset, ActorSex pSex,
            AnimationContainerUnit pContainer, long pActorId, int pHeadId, bool pAdult, bool pEgg,
            bool pKing, bool pWarrior, bool pWise, bool pRandom)
        {
            if (pAsset == null || pAsset.id != XIA) return;
            if (pEgg || pAsset.is_boat) return;
            if (pRandom) return;
            if (!pAdult && (pContainer == null || !pContainer.render_heads_for_children)) return;

            ActorTextureSubAsset tex = pAsset.texture_asset;
            if (tex == null || !tex.has_advanced_textures) return;

            Actor actor = null;
            try { actor = World.world?.units?.get(pActorId); }
            catch { }
            bool bandit = false;
            bool synthetic = false;
            try { bandit = PeasantRebelRouteService.IsBandit(actor?.kingdom); }
            catch { }
            try { synthetic = SyntheticLevyService.IsSynthetic(actor); }
            catch { }
            bool banditKing = actor != null && actor.isKing() && bandit;
            if (banditKing)
            {
                Sprite banditKingHead = ActorAnimationLoader.getHeadSpecial(
                    tex.texture_path_base +
                    "heads_special/head_bandit_general");
                if (banditKingHead != null) __result = banditKingHead;
                return;
            }
            if (XiaBanditHeadRules.ShouldUse(pAsset.id, bandit, synthetic))
            {
                string banditHeadPath = tex.texture_path_base +
                    XiaBanditHeadRules.ResolveHeadPath(pActorId);
                Sprite banditHead = SpriteTextureLoader.getSprite(
                    banditHeadPath);
                if (banditHead != null) __result = banditHead;
                return;
            }

            if (pKing) return;
            if (pWarrior)
            {
                if (actor?.data != null && pAsset.id == XIA)
                {
                    Sprite warrior = SpriteTextureLoader.getSprite(
                        tex.texture_path_base +
                        XiaActorTextureRules.ResolveWarriorHeadPath(
                            actor.data.id));
                    if (warrior != null) __result = warrior;
                }
                return;
            }

            if (actor?.data != null)
            {
                actor.data.get(LineageKeys.IS_HEIR, out bool isHeir, false);
                bool activePrince = false;
                try { activePrince = FeudatoryService.IsActivePrince(actor); }
                catch { }
                if (isHeir || activePrince)
                {
                    Sprite heir = SpriteTextureLoader.getSprite(
                        tex.texture_path_base + "heads_heir/head_0");
                    if (heir != null) __result = heir;
                    return;
                }
            }

            if (actor?.data != null)
            {
                actor.data.get(LineageKeys.OFFICER_RANK, out int rank,
                    OfficialCareerRankRules.Unranked);
                actor.data.get(LineageKeys.COURT_OFFICE_ID,
                    out string officeId, "");
                actor.data.get(LineageKeys.COURT_LAYER,
                    out string layer, "");
                int officeGrade = OfficialCareerStateService.OfficeGradeForOffice(
                    actor.kingdom, layer, officeId, actor.city);
                string officialHead =
                    XiaActorTextureRules.ResolveOfficialHeadPath(rank,
                        officeGrade);
                if (!string.IsNullOrEmpty(officialHead))
                {
                    Sprite official = SpriteTextureLoader.getSprite(
                        tex.texture_path_base + officialHead);
                    if (official != null) __result = official;
                    return;
                }
                if (actor.isCityLeader())
                {
                    Sprite leader = SpriteTextureLoader.getSprite(
                        tex.texture_path_base +
                        XiaActorTextureRules.ResolveOfficialHeadPath(
                            OfficialCareerRankRules.MinimumRank));
                    if (leader != null) __result = leader;
                    return;
                }
            }

            if (AW_AgePatch.ShouldUseXiaOldHead(pActorId))
            {
                string oldPath = (pSex == ActorSex.Male) ? tex.texture_heads_old_male : tex.texture_heads_old_female;
                if (!string.IsNullOrEmpty(oldPath))
                {
                    Sprite oldHead = ActorAnimationLoader.getHeadSpecial(oldPath);
                    if (oldHead != null) __result = oldHead;
                    return;
                }
            }

            string path = (pSex == ActorSex.Male) ? tex.texture_heads_male : tex.texture_heads_female;
            if (string.IsNullOrEmpty(path)) return;
            if (pHeadId < 0) return;

            Sprite byName = ActorAnimationLoader.getHead(path, pHeadId);
            if (byName != null) __result = byName;
        }
    }
}
