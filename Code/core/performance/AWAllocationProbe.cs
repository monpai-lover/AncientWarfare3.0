using System;

namespace AncientWarfare3.core.performance
{
    /// <summary>
    /// 分配量取样器。
    ///
    /// 上一轮直接用了 GC.GetAllocatedBytesForCurrentThread():net48 下能编译,
    /// 但 Unity 的 Mono 把它实现成空桩,整局日志 alloc_total_kb 全是 0 —— 能
    /// 编译不等于有实现。所以这里在运行时实测挑一个真的会动的来源,并且把选
    /// 中的来源名字发进日志,下一份日志能自证探针是否有效。
    ///
    /// 三个候选,按精度从高到低:
    ///   thread_alloc  GC.GetAllocatedBytesForCurrentThread —— 毛分配量,单调
    ///   domain_alloc  AppDomain.MonitoringTotalAllocatedMemorySize —— 毛分配
    ///                 量,单调,但要先打开 MonitoringIsEnabled
    ///   heap_delta    GC.GetTotalMemory(false) —— 净堆大小。回收时会变小,所
    ///                 以调用方必须丢弃负增量;GC 之后那一段会少算,是下界。
    /// </summary>
    internal static class AWAllocationProbe
    {
        private enum Source
        {
            Unresolved,
            ThreadAllocated,
            DomainAllocated,
            HeapTotal,
            Unavailable
        }

        private static Source _source = Source.Unresolved;

        internal static string SourceName
        {
            get
            {
                Resolve();
                switch (_source)
                {
                    case Source.ThreadAllocated: return "thread_alloc";
                    case Source.DomainAllocated: return "domain_alloc";
                    case Source.HeapTotal: return "heap_delta";
                    default: return "unavailable";
                }
            }
        }

        /// <summary>净增量来源(heap_delta)必须丢弃负值,毛分配量来源不必。</summary>
        internal static bool IsNetHeapSource
        {
            get
            {
                Resolve();
                return _source == Source.HeapTotal;
            }
        }

        /// <summary>
        /// 在模组加载时先把来源定下来。探测本身要分配十几 MB 的 ballast,如果
        /// 拖到第一次 Step() 里才触发,那一步会被凭空记上这笔账 —— 而这本账
        /// 正是用来找分配大户的,头一个读数就是假的没法用。
        /// </summary>
        internal static void Initialize()
        {
            Resolve();
        }

        internal static long Sample()
        {
            Resolve();
            switch (_source)
            {
                case Source.ThreadAllocated:
                    return ReadThread();
                case Source.DomainAllocated:
                    return ReadDomain();
                case Source.HeapTotal:
                    return ReadHeap();
                default:
                    return 0L;
            }
        }

        private static void Resolve()
        {
            if (_source != Source.Unresolved) return;
            // 先假定不可用,避免探测过程里的递归调用又落回 Resolve。
            _source = Source.Unavailable;
            if (Moves(ReadThread))
            {
                _source = Source.ThreadAllocated;
                return;
            }

            try { AppDomain.MonitoringIsEnabled = true; }
            catch { }
            if (Moves(ReadDomain))
            {
                _source = Source.DomainAllocated;
                return;
            }

            if (Moves(ReadHeap)) _source = Source.HeapTotal;
        }

        /// <summary>
        /// 实测这个来源是否真的随分配变化:取样,故意分配一批,再取样。
        /// 空桩会返回同一个值(通常是 0),就会被淘汰掉。
        ///
        /// ballast 取 4MB 而不是几百 KB:GetTotalMemory 报的是堆大小,Mono 按块
        /// 增长,分配得太少可能整个块内消化掉、数字纹丝不动,那就会把一个其实
        /// 可用的来源误判成空桩。
        /// </summary>
        private static bool Moves(Func<long> pRead)
        {
            long before;
            try { before = pRead(); }
            catch { return false; }

            object[] ballast = new object[64];
            for (int i = 0; i < ballast.Length; i++)
                ballast[i] = new byte[64 * 1024];
            long after;
            try { after = pRead(); }
            catch { return false; }
            // 拿一下,免得整段被优化掉。
            return after != before && ballast[0] != null;
        }

        private static long ReadThread()
        {
            return GC.GetAllocatedBytesForCurrentThread();
        }

        private static long ReadDomain()
        {
            return AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
        }

        private static long ReadHeap()
        {
            return GC.GetTotalMemory(false);
        }
    }
}
