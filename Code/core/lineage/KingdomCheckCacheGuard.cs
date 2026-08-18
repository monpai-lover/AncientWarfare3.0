using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     原版 <c>KingdomCheckCache.dict</c> 是裸 <c>Dictionary&lt;long, bool&gt;</c>,
    ///     而 <c>WarManager.isInWarWith</c> / <c>Kingdom.isEnemy</c> 都是
    ///     「先 TryGetValue 再写回」的非原子读改写。AW3 的并行 actor 管线
    ///     (<c>AWCooperativeActorPostRunner+SearchWorkItem.Search</c> →
    ///     <c>BaseSimObject.checkObjectList</c> → <c>canAttackTarget</c> →
    ///     <c>areFoes</c> → <c>Kingdom.isEnemy</c>)会让多个工作线程同时 miss
    ///     同一对王国:
    ///
    ///     * <c>isInWarWith</c> 用 <c>dict.Add</c> → 第二个线程抛
    ///       <c>An item with the same key has already been added.</c>
    ///     * <c>isEnemy</c> 用索引器赋值 → 不抛,但与并发读同时进行会破坏
    ///       字典内部结构(Dictionary 的读在有写时同样不安全)。
    ///
    ///     所以读、写、清空必须走同一把锁,而不是只锁写。
    ///
    ///     这里保持原版 <c>dict</c> 作为唯一事实来源,好让原版
    ///     <c>WarManager.warStateChanged()</c> 里的两次 <c>clear()</c> 仍然
    ///     正确失效缓存。昂贵的战争扫描在锁外算,锁内只做一次字典查找/写入;
    ///     写回用索引器赋值,幂等,重复计算也不会抛。
    /// </summary>
    public static class KingdomCheckCacheGuard
    {
        // 两个缓存共用一把锁:isEnemy 会在 miss 后调用 isInWarWith,
        // 用两把锁会形成嵌套。改为「先放锁,再算,再取锁写回」。
        private static readonly object Gate = new object();

        public static object SyncRoot => Gate;

        public static bool TryRead(Dictionary<long, bool> pDict, long pHash,
            out bool pValue)
        {
            pValue = false;
            if (pDict == null) return false;
            lock (Gate)
            {
                return pDict.TryGetValue(pHash, out pValue);
            }
        }

        /// <summary>
        ///     写回并返回最终生效值。已有条目时保留既有值,保证同一帧内
        ///     不同线程看到一致结果。
        /// </summary>
        public static bool Publish(Dictionary<long, bool> pDict, long pHash,
            bool pValue)
        {
            if (pDict == null) return pValue;
            lock (Gate)
            {
                if (pDict.TryGetValue(pHash, out bool existing))
                    return existing;
                pDict[pHash] = pValue;
                return pValue;
            }
        }

        public static void Clear(Dictionary<long, bool> pDict)
        {
            if (pDict == null) return;
            lock (Gate)
            {
                pDict.Clear();
            }
        }
    }
}
