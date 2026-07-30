using System;
using System.Collections.Generic;
using System.Text;
using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorMainSpritePatch
    {
        private const int MaxDiagnosticLogs = 80;
        private static readonly object DiagnosticLock = new object();
        private static readonly HashSet<string> LoggedDiagnostics = new HashSet<string>();
        private static int _diagnosticCount;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.calculateMainSprite))]
        public static bool CalculateMainSprite_Prefix(Actor __instance, ref Sprite __result)
        {
            if (__instance?.data == null ||
                !LineageService.IsXia(__instance))
                return true;
            try
            {
                if (TryCalculateSafe(__instance, out Sprite sprite))
                {
                    __result = sprite;
                    return false;
                }
            }
            catch (Exception exception)
            {
                LogSuspiciousContainer(__instance,
                    "safe_calculation_exception:" +
                    exception.GetType().Name, null, null);
                if (TryGetActorFallback(__instance, out Sprite fallback))
                {
                    __result = fallback;
                    return false;
                }
            }

            return true;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Actor), nameof(Actor.calculateMainSprite))]
        private static Exception CalculateMainSprite_Finalizer(
            Actor __instance, ref Sprite __result, Exception __exception)
        {
            if (__exception == null) return null;
            if (__instance?.data == null ||
                !LineageService.IsXia(__instance))
                return __exception;
            LogSuspiciousContainer(__instance,
                "render_exception:" + __exception.GetType().Name,
                __instance.animation_container, null);
            if (!TryGetActorFallback(__instance, out Sprite fallback))
                return __exception;
            __result = fallback;
            return null;
        }

        private static bool TryCalculateSafe(Actor pActor, out Sprite pSprite)
        {
            pSprite = null;
            if (pActor.asset == null)
            {
                LogSuspiciousContainer(pActor, "asset_null", null, null);
                return false;
            }

            if (pActor.asset.has_override_sprite)
            {
                pSprite = pActor.asset.get_override_sprite(pActor);
                return pSprite != null;
            }

            try
            {
                pActor.checkAnimationContainer();
            }
            catch (Exception e)
            {
                LogSuspiciousContainer(pActor, "check_container_exception:" + e.GetType().Name, null, null);
                return TryGetActorFallback(pActor, out pSprite);
            }

            AnimationContainerUnit container = pActor.animation_container;
            if (container == null)
            {
                LogSuspiciousContainer(pActor, "container_null", null, null);
                return TryGetActorFallback(pActor, out pSprite);
            }

            if (pActor.ai?.action != null && pActor.ai.action.force_animation)
            {
                if (TryGetForced(container, pActor.ai.action.force_animation_id, out pSprite))
                    return true;
                LogSuspiciousContainer(pActor, "forced_missing:" + pActor.ai.action.force_animation_id, container,
                    null);
            }

            if (!pActor.isAlive() || pActor._has_stop_idle_animation)
            {
                if (container.has_swimming && pActor._has_status_drowning &&
                    TryGetFirst(container.swimming, out pSprite))
                    return true;
                if (TryGetFallback(container, out pSprite))
                {
                    if (!HasFrames(container.idle))
                        LogSuspiciousContainer(pActor, "dead_idle_missing_used_fallback", container, null);
                    return true;
                }
                LogSuspiciousContainer(pActor, "dead_no_fallback", container, null);
                return TryGetActorFallback(pActor, out pSprite);
            }

            ActorAnimation selected = SelectAnimation(pActor, container);
            float speed = SelectSpeed(pActor, container, selected);
            if (TryGetAnimated(pActor, selected, speed, out pSprite))
                return true;

            LogSuspiciousContainer(pActor, "selected_missing:" + AnimationName(container, selected), container,
                selected);
            if (TryGetFallback(container, out pSprite)) return true;
            if (TryGetAnySprite(container, out pSprite)) return true;

            LogSuspiciousContainer(pActor, "no_sprite_in_container", container, selected);
            return TryGetActorFallback(pActor, out pSprite);
        }

        private static ActorAnimation SelectAnimation(Actor pActor, AnimationContainerUnit pContainer)
        {
            if (pActor.is_moving || pActor.timer_jump_animation > 0f || pActor.move_jump_offset.y > 0f ||
                pActor.is_in_magnet)
            {
                if (pContainer.has_swimming && pActor.isAffectedByLiquid())
                    return pContainer.swimming;
                return pContainer.walking;
            }

            if (pActor.position_height > 0f)
                return pContainer.idle;

            if (pContainer.has_swimming && pActor.isAffectedByLiquid())
                return pContainer.swimming;

            return pContainer.idle;
        }

        private static float SelectSpeed(Actor pActor, AnimationContainerUnit pContainer, ActorAnimation pSelected)
        {
            if (pSelected == pContainer.swimming)
                return pActor.asset.animation_swim_speed;
            if (pSelected == pContainer.walking)
            {
                float speed = pActor.asset.animation_walk_speed;
                if (pActor.asset.animation_speed_based_on_walk_speed)
                {
                    speed *= pActor.stats["speed"] / 10f;
                    speed = Mathf.Clamp(speed, 4f, speed);
                }
                return speed;
            }
            return pActor.asset.animation_idle_speed;
        }

        private static bool TryGetAnimated(Actor pActor, ActorAnimation pAnimation, float pSpeed, out Sprite pSprite)
        {
            pSprite = null;
            if (pAnimation?.frames == null || pAnimation.frames.Length == 0) return false;
            if (pAnimation.frames.Length == 1)
            {
                pSprite = pAnimation.frames[0];
                return pSprite != null;
            }

            pSprite = AnimationHelper.getSpriteFromList(pActor.GetHashCode(), pAnimation.frames, pSpeed);
            return pSprite != null;
        }

        private static bool TryGetFirst(ActorAnimation pAnimation, out Sprite pSprite)
        {
            pSprite = null;
            if (pAnimation?.frames == null || pAnimation.frames.Length == 0) return false;
            pSprite = pAnimation.frames[0];
            return pSprite != null;
        }

        private static bool TryGetForced(AnimationContainerUnit pContainer, string pId, out Sprite pSprite)
        {
            pSprite = null;
            if (pContainer?.sprites == null || string.IsNullOrEmpty(pId)) return false;
            return pContainer.sprites.TryGetValue(pId, out pSprite) && pSprite != null;
        }

        private static bool TryGetFallback(AnimationContainerUnit pContainer, out Sprite pSprite)
        {
            if (TryGetFirst(pContainer.idle, out pSprite)) return true;
            if (TryGetFirst(pContainer.walking, out pSprite)) return true;
            if (TryGetFirst(pContainer.swimming, out pSprite)) return true;
            pSprite = null;
            return false;
        }

        private static bool TryGetAnySprite(AnimationContainerUnit pContainer, out Sprite pSprite)
        {
            pSprite = null;
            if (pContainer?.sprites == null || pContainer.sprites.Count == 0) return false;
            foreach (Sprite sprite in pContainer.sprites.Values)
            {
                if (sprite == null) continue;
                pSprite = sprite;
                return true;
            }
            return false;
        }

        private static bool TryGetKnownXiaFallback(out Sprite pSprite)
        {
            pSprite = null;
            Sprite[] sprites = SpriteTextureLoader.getSpriteList(XiaRace.TEXTURE_PATH + "male_1", true);
            if (sprites == null || sprites.Length == 0) return false;
            pSprite = sprites[0];
            return pSprite != null;
        }

        private static bool TryGetActorFallback(Actor pActor,
            out Sprite pSprite)
        {
            pSprite = null;
            if (TryGetFallback(pActor?.animation_container, out pSprite) ||
                TryGetAnySprite(pActor?.animation_container, out pSprite))
                return true;
            try
            {
                string texturePath = pActor?.getUnitTexturePath();
                if (!string.IsNullOrEmpty(texturePath))
                {
                    Sprite[] sprites = SpriteTextureLoader.getSpriteList(
                        texturePath, true);
                    if (sprites != null)
                    {
                        foreach (Sprite sprite in sprites)
                        {
                            if (sprite == null) continue;
                            pSprite = sprite;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return LineageService.IsXia(pActor) &&
                   TryGetKnownXiaFallback(out pSprite);
        }

        private static bool HasFrames(ActorAnimation pAnimation)
        {
            return pAnimation?.frames != null && pAnimation.frames.Length > 0 && pAnimation.frames[0] != null;
        }

        private static int FrameCount(ActorAnimation pAnimation)
        {
            return pAnimation?.frames?.Length ?? 0;
        }

        private static string AnimationName(AnimationContainerUnit pContainer, ActorAnimation pAnimation)
        {
            if (pAnimation == null) return "null";
            if (pAnimation == pContainer.idle) return "idle";
            if (pAnimation == pContainer.walking) return "walking";
            if (pAnimation == pContainer.swimming) return "swimming";
            return "unknown";
        }

        private static void LogSuspiciousContainer(Actor pActor, string pReason, AnimationContainerUnit pContainer,
            ActorAnimation pSelected)
        {
            string actorId = pActor?.data != null ? pActor.data.id.ToString() : "no_data";
            string texturePath = SafeTexturePath(pActor);
            string containerId = pContainer?.id ?? "null";
            string key = actorId + "|" + texturePath + "|" + containerId + "|" + pReason;

            lock (DiagnosticLock)
            {
                if (_diagnosticCount >= MaxDiagnosticLogs) return;
                if (!LoggedDiagnostics.Add(key)) return;
                _diagnosticCount++;
            }

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("[sprite diag] reason=").Append(pReason);
                sb.Append(" actor=").Append(SafeActorName(pActor)).Append("#").Append(actorId);
                sb.Append(" asset=").Append(pActor?.asset?.id ?? "null");
                sb.Append(" texture=").Append(texturePath);
                sb.Append(" container=").Append(containerId);
                sb.Append(" selected=").Append(AnimationName(pContainer, pSelected));
                sb.Append(" alive=").Append(SafeBool(() => pActor.isAlive()));
                sb.Append(" baby=").Append(SafeBool(() => pActor.isBaby()));
                sb.Append(" egg=").Append(SafeBool(() => pActor.isEgg()));
                sb.Append(" king=").Append(SafeBool(() => pActor.isKing()));
                sb.Append(" leader=").Append(SafeBool(() => pActor.isCityLeader()));
                sb.Append(" warrior=").Append(SafeBool(() => pActor.isWarrior()));
                sb.Append(" heir=").Append(SafeDataBool(pActor, LineageKeys.IS_HEIR));
                sb.Append(" slave=").Append(SafeBool(() => pActor.hasTrait(LineageKeys.TRAIT_SLAVE)));
                sb.Append(" moving=").Append(pActor != null && pActor.is_moving);
                sb.Append(" liquid=").Append(SafeBool(() => pActor.isAffectedByLiquid()));
                sb.Append(" drowning=").Append(pActor != null && pActor._has_status_drowning);
                sb.Append(" hasIdle=").Append(pContainer != null && pContainer.has_idle);
                sb.Append(" idleFrames=").Append(FrameCount(pContainer?.idle));
                sb.Append(" hasWalk=").Append(pContainer != null && pContainer.has_walking);
                sb.Append(" walkFrames=").Append(FrameCount(pContainer?.walking));
                sb.Append(" hasSwim=").Append(pContainer != null && pContainer.has_swimming);
                sb.Append(" swimFrames=").Append(FrameCount(pContainer?.swimming));
                sb.Append(" spriteCount=").Append(pContainer?.sprites?.Count ?? 0);
                sb.Append(" keys=").Append(FirstSpriteKeys(pContainer));
                ModClass.LogWarning(sb.ToString());
            }
            catch (Exception e)
            {
                ModClass.LogWarning("[sprite diag] failed: " + e.Message);
            }
        }

        private static string SafeTexturePath(Actor pActor)
        {
            try
            {
                return pActor?.getUnitTexturePath() ?? "null";
            }
            catch (Exception e)
            {
                return "error:" + e.GetType().Name;
            }
        }

        private static string SafeActorName(Actor pActor)
        {
            try
            {
                return pActor?.getName() ?? "null";
            }
            catch
            {
                return "name_error";
            }
        }

        private static bool SafeBool(Func<bool> pGetter)
        {
            try
            {
                return pGetter != null && pGetter();
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeDataBool(Actor pActor, string pKey)
        {
            try
            {
                if (pActor?.data == null) return false;
                pActor.data.get(pKey, out bool value, false);
                return value;
            }
            catch
            {
                return false;
            }
        }

        private static string FirstSpriteKeys(AnimationContainerUnit pContainer)
        {
            if (pContainer?.sprites == null || pContainer.sprites.Count == 0) return "-";
            StringBuilder sb = new StringBuilder();
            int i = 0;
            foreach (string key in pContainer.sprites.Keys)
            {
                if (i > 0) sb.Append(",");
                sb.Append(key);
                i++;
                if (i >= 8) break;
            }
            if (pContainer.sprites.Count > i) sb.Append(",...");
            return sb.ToString();
        }
    }
}
