using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     并行敌人搜索（b3_findEnemyTarget，运行在模拟 worker 线程池上）会让多个线程同时调用
    ///     原版 Kingdom.isEnemy / WarManager.isInWarWith / KingdomAsset.isFoe。这三处各自维护一份
    ///     惰性写入的普通 Dictionary 缓存：
    ///       1. Kingdom.cache_enemy_check（static，全局共享）—— isEnemy，写入用索引器，并发写会静默损坏字典
    ///          （常表现为 worker 未跑完全部任务，即 "did not execute all scheduled work"）。
    ///       2. WarManager.cache_war_check —— isInWarWith，写入用 dict.Add，并发插入同一 key 会抛
    ///          "An item with the same key has already been added"（正是上报截图里的报错）。
    ///       3. KingdomAsset._cached_enemies —— isFoe，同样用 dict.Add；针对非文明目标（怪物/野兽等）。
    ///     isEnemy 命中缓存前会下探到 2 或 3，因此三者都会在并行窗口内竞争。
    ///
    ///     本补丁用前缀整体接管这三个方法，改走线程安全的 ConcurrentDictionary 影子缓存：
    ///       - 读取无锁；写入由 ConcurrentDictionary 内部分段锁序列化，绝不会损坏或抛重复键。
    ///       - 影子缓存在原版清理点（Kingdom.clear / WarManager.warStateChanged）同步清空，语义与原版一致。
    ///       - isFoe 完全只读重写：原版会把 pTarget.id 写进 pTarget.list_tags 再做 friendly 判定，
    ///         等价于把该 id 直接纳入 Overlaps 判断（见 ComputeFoe 注释），因此 worker 上不再修改任何共享集合。
    ///     原版这三处的 Dictionary 因为前缀 return false 被跳过，始终保持为空，不存在双份真相。
    /// </summary>
    [HarmonyPatch]
    internal static class AW_EnemyRelationThreadSafetyPatch
    {
        // Kingdom.cache_enemy_check 的线程安全影子（key = 原版 getHash(k1,k2)）。
        private static readonly ConcurrentDictionary<long, bool> _enemyShadow =
            new ConcurrentDictionary<long, bool>();

        // WarManager.cache_war_check 的线程安全影子（key = 原版 getHash(k1,k2)）。
        private static readonly ConcurrentDictionary<long, bool> _warShadow =
            new ConcurrentDictionary<long, bool>();

        // KingdomAsset._cached_enemies 的线程安全影子；每个 asset 一份，asset 是常驻单例、原版从不清空。
        private static readonly ConditionalWeakTable<
            KingdomAsset,
            ConcurrentDictionary<KingdomAsset, bool>> _foeShadow =
            new ConditionalWeakTable<
                KingdomAsset,
                ConcurrentDictionary<KingdomAsset, bool>>();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.isEnemy))]
        private static bool IsEnemyPrefix(
            Kingdom __instance,
            Kingdom pKingdomTarget,
            ref bool __result)
        {
            if (pKingdomTarget == null)
            {
                __result = true;
                return false;
            }

            long hash = Kingdom.cache_enemy_check.getHash(
                __instance,
                pKingdomTarget);
            if (_enemyShadow.TryGetValue(hash, out bool cached))
            {
                __result = cached;
                return false;
            }

            bool value = ComputeEnemy(__instance, pKingdomTarget);
            // GetOrAdd 幂等：并发下若已被别的线程写入，沿用先到的值即可，结果确定不变。
            __result = _enemyShadow.GetOrAdd(hash, value);
            return false;
        }

        private static bool ComputeEnemy(
            Kingdom pSource,
            Kingdom pTarget)
        {
            // 与原版 Kingdom.isEnemy 逐分支等价。
            if (pSource.isCiv() && pTarget.isCiv())
            {
                if (pTarget == pSource)
                {
                    return false;
                }

                // 经 isInWarWith 前缀走 _warShadow，线程安全。
                return World.world.wars.isInWarWith(pSource, pTarget);
            }

            // 经 isFoe 前缀走 _foeShadow，线程安全。
            return pSource.asset.isFoe(pTarget.asset);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.isInWarWith))]
        private static bool IsInWarWithPrefix(
            WarManager __instance,
            Kingdom pKingdom,
            Kingdom pKingdomTarget,
            ref bool __result)
        {
            long hash = __instance.cache_war_check.getHash(
                pKingdom,
                pKingdomTarget);
            if (_warShadow.TryGetValue(hash, out bool cached))
            {
                __result = cached;
                return false;
            }

            bool value = ComputeInWar(pKingdom, pKingdomTarget);
            __result = _warShadow.GetOrAdd(hash, value);
            return false;
        }

        private static bool ComputeInWar(
            Kingdom pKingdom,
            Kingdom pKingdomTarget)
        {
            // getWars 是惰性迭代器、只读；War.isInWarWith 亦只读。主线程在并行搜索屏障处阻塞，
            // 战争列表本 tick 不会被增删，worker 并发枚举安全。
            foreach (War war in pKingdom.getWars())
            {
                if (war.isInWarWith(pKingdom, pKingdomTarget))
                {
                    return true;
                }
            }

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomAsset), nameof(KingdomAsset.isFoe))]
        private static bool IsFoePrefix(
            KingdomAsset __instance,
            KingdomAsset pTarget,
            ref bool __result)
        {
            ConcurrentDictionary<KingdomAsset, bool> perAsset =
                _foeShadow.GetValue(
                    __instance,
                    static _ => new ConcurrentDictionary<KingdomAsset, bool>());
            if (perAsset.TryGetValue(pTarget, out bool cached))
            {
                __result = cached;
                return false;
            }

            bool value = ComputeFoe(__instance, pTarget);
            __result = perAsset.GetOrAdd(pTarget, value);
            return false;
        }

        private static bool ComputeFoe(
            KingdomAsset pSelf,
            KingdomAsset pTarget)
        {
            // 与原版 KingdomAsset.isFoe 等价，但不修改任何共享集合。
            if (pSelf.nature || pTarget.nature)
            {
                return false;
            }

            if (pSelf == pTarget)
            {
                return pSelf.always_attack_each_other;
            }

            if (pSelf.enemy_tags.Count > 0 &&
                pSelf.enemy_tags.Overlaps(pTarget.list_tags))
            {
                return true;
            }

            // 原版此处会执行 pTarget.list_tags.Add(pTarget.id) 与 pSelf.list_tags.Add(pSelf.id)，
            // 随后判断 friendly_tags.Overlaps(pTarget.list_tags)。既然唯一被追加进 pTarget.list_tags
            // 的元素是 pTarget.id，Overlaps(list_tags ∪ {id}) 等价于
            // Overlaps(list_tags) || friendly_tags.Contains(id)，无需真正写入集合。
            if (pSelf.friendly_tags.Count > 0 &&
                (pSelf.friendly_tags.Overlaps(pTarget.list_tags) ||
                 pSelf.friendly_tags.Contains(pTarget.id)))
            {
                return false;
            }

            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.warStateChanged))]
        private static void WarStateChangedPostfix()
        {
            // 原版此处清空 cache_war_check 与 Kingdom.cache_enemy_check，影子同步清空。
            _warShadow.Clear();
            _enemyShadow.Clear();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.clear))]
        private static void KingdomClearPostfix()
        {
            // 原版 Kingdom.clear() 清空静态 cache_enemy_check，影子同步清空。
            _enemyShadow.Clear();
        }
    }
}
