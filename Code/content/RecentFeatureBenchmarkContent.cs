using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.content
{
    internal static class RecentFeatureBenchmarkContent
    {
        public const string ToolId = "AW3 Recent Runtime";
        public const string ArmyRtsToolId = "AW3 Army RTS";

        public static void Init()
        {
            try
            {
                string diagnosticSwitch =
                    System.Environment.GetEnvironmentVariable(
                        RecentFeatureBenchmarkRules.EnvironmentVariable);
                if (RecentFeatureBenchmarkRules.ShouldEnableFromEnvironment(
                        diagnosticSwitch))
                    Bench.bench_enabled = true;

                if (AssetManager.debug_tool_library == null) return;
                DebugToolAsset template =
                    AssetManager.debug_tool_library.get("Benchmark Actors");
                if (template == null) return;

                if (!AssetManager.debug_tool_library.has(ToolId))
                    AssetManager.debug_tool_library.add(new DebugToolAsset
                    {
                        id = ToolId,
                        name = ToolId,
                        type = DebugToolType.Benchmarks,
                        priority = 2,
                        show_benchmark_buttons = true,
                        split_benchmark = true,
                        benchmark_group_id = RecentFeatureBenchmarkRules.Group,
                        benchmark_total = RecentFeatureBenchmarkRules.Total,
                        benchmark_total_group =
                            RecentFeatureBenchmarkRules.TotalParentGroup,
                        action_start = template.action_start,
                        action_1 = template.action_1,
                        action_2 = template.action_2
                    });
                if (!AssetManager.debug_tool_library.has(ArmyRtsToolId))
                    AssetManager.debug_tool_library.add(new DebugToolAsset
                {
                    id = ArmyRtsToolId,
                    name = ArmyRtsToolId,
                    type = DebugToolType.Benchmarks,
                    priority = 3,
                    show_benchmark_buttons = true,
                    split_benchmark = true,
                    show_last_count = true,
                    benchmark_group_id = ArmyRtsBenchmark.Group,
                    benchmark_total = ArmyRtsBenchmark.Total,
                    benchmark_total_group =
                        ArmyRtsBenchmark.TotalParentGroup,
                    action_start = template.action_start,
                    action_1 = template.action_1,
                    action_2 = template.action_2
                });
            }
            catch (System.Exception error)
            {
                ModClass.LogWarning("AW3 benchmark tool registration failed: " +
                                    error.Message);
            }
        }
    }
}
