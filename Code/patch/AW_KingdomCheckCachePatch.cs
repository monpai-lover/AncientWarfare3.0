using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     接管原版两个王国关系缓存的读改写,使其线程安全。
    ///
    ///     背景:<c>WarManager.isInWarWith</c> 与 <c>Kingdom.isEnemy</c> 对
    ///     <c>KingdomCheckCache.dict</c>(裸 Dictionary)做非原子的
    ///     TryGetValue→Add/赋值。AW3 并行 actor 搜索会让多个工作线程同时
    ///     miss 同一对王国,<c>isInWarWith</c> 的 <c>dict.Add</c> 于是抛
    ///     <c>An item with the same key has already been added.</c>,
    ///     经 <c>AWSimulationWorkerPool.Complete</c> 重抛后由
    ///     <c>AW_FramePrioritySchedulerPatch</c> 报边界失败并暂停游戏。
    ///
    ///     全原版只有三处碰这两个缓存:这两个方法,以及
    ///     <c>WarManager.warStateChanged</c> 里的两次 <c>clear()</c>。
    ///     这里仍以原版 <c>dict</c> 为事实来源,所以 warStateChanged 的失效
    ///     行为不变;<c>clear()</c> 也一并上锁,否则清空与并发读仍会撞。
    ///
    ///     锁外算战争扫描、锁内只做一次查找/写入,详见
    ///     <see cref="KingdomCheckCacheGuard"/>。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_KingdomCheckCachePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.isInWarWith))]
        private static bool IsInWarWith_Prefix(WarManager __instance,
            Kingdom pKingdom, Kingdom pKingdomTarget, ref bool __result)
        {
            KingdomCheckCache cache = __instance?.cache_war_check;
            if (cache?.dict == null || pKingdom == null ||
                pKingdomTarget == null) return true;

            long hash = cache.getHash(pKingdom, pKingdomTarget);
            if (KingdomCheckCacheGuard.TryRead(cache.dict, hash,
                    out bool cached))
            {
                __result = cached;
                return false;
            }

            // 扫描放在锁外:getWars() 会遍历世界战争列表,不能占着锁。
            bool inWar = false;
            foreach (War war in pKingdom.getWars())
            {
                if (!war.isInWarWith(pKingdom, pKingdomTarget)) continue;
                inWar = true;
                break;
            }

            __result = KingdomCheckCacheGuard.Publish(cache.dict, hash,
                inWar);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.isEnemy))]
        private static bool IsEnemy_Prefix(Kingdom __instance,
            Kingdom pKingdomTarget, ref bool __result)
        {
            if (pKingdomTarget == null)
            {
                __result = true;
                return false;
            }

            KingdomCheckCache cache = Kingdom.cache_enemy_check;
            // asset 判空不是多余的保守:原版 isCiv() 就是 return asset.civ,
            // asset 为 null 时它自己会抛。这里退回原版即保持原样(照样抛),
            // 不在补丁里另造一套「被 Dispose 的王国算不算敌人」的语义。
            // 真正该修的是别过早 Dispose,见 AW_KingdomExtinctionPatch。
            if (cache?.dict == null || __instance?.asset == null ||
                pKingdomTarget.asset == null) return true;

            long hash = cache.getHash(__instance, pKingdomTarget);
            if (KingdomCheckCacheGuard.TryRead(cache.dict, hash,
                    out bool cached))
            {
                __result = cached;
                return false;
            }

            bool enemy;
            if (__instance.isCiv() && pKingdomTarget.isCiv())
                enemy = pKingdomTarget != __instance &&
                        World.world.wars.isInWarWith(__instance,
                            pKingdomTarget);
            else
                enemy = __instance.asset.isFoe(pKingdomTarget.asset);

            __result = KingdomCheckCacheGuard.Publish(cache.dict, hash,
                enemy);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(KingdomCheckCache),
            nameof(KingdomCheckCache.clear))]
        private static bool Clear_Prefix(KingdomCheckCache __instance)
        {
            if (__instance?.dict == null) return true;
            KingdomCheckCacheGuard.Clear(__instance.dict);
            return false;
        }
    }
}
