namespace AncientWarfare3.core.performance
{
    /// <summary>
    /// 诊断采集的统一开关。
    ///
    /// [AW3 PERF] 那行文本本来就受 AW3_ENABLE_PERFORMANCE_DIAGNOSTICS 控制,但
    /// 采集侧没有门控:开关关掉时那些探针照样跑。其中最贵的是按步骤的分配量
    /// 取样 —— AWAllocationProbe 在 Unity Mono 上只能退化成
    /// GC.GetTotalMemory(false),而权威周期约 45 个步骤、每区间约 30 轮,一进
    /// 一出就是每区间几千次调用。这类纯观测代码在开关关闭时应当完全不产生
    /// 成本。
    ///
    /// 所有探针入口都先问这里。单独抽出来而不是各处直接引用
    /// AWPerformanceSettings,是为了让「哪些东西属于可关闭的诊断」有一个唯一
    /// 的答案,以后加探针时不容易漏。
    /// </summary>
    internal static class AWDiagnosticsGate
    {
        // 规则测试项目不编译 AWPerformanceSettings(它会拖进 Unity 侧类型),
        // 那里只验纯逻辑,所以退化成恒开 —— 探针在测试里本就无害。
#if AW3_RULES_TESTS
        internal static bool Enabled => true;
#else
        internal static bool Enabled =>
            AWPerformanceSettings.EnablePerformanceDiagnostics;
#endif
    }
}
