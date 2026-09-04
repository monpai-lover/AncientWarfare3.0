using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityManagerMutationBoundaryPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityManager), nameof(CityManager.update))]
        private static void CityUpdatePrefix()
        {
            CityManagerMutationScope.EnterCityUpdate();
        }

        // 返回类型必须是 void。HarmonyX 只要有**任何一个** finalizer 声明
        // 返回 Exception,生成的包装体在异常路径上就会走
        // `ldloc ex; throw`(HarmonyManipulator.WriteFinalizers ——
        // `if (method.ReturnType != typeof(void)) { Stloc exceptionVar;
        // result = false; }`,result=false 时发 Throw 而不是 Rethrow)。
        // Mono 上 `throw ex` 会把栈重置到包装体自身,原始栈帧全部丢失 ——
        // 玩家日志里那条 "at (wrapper dynamic-method)
        // CityManager.DMD<CityManager::update>" 之上什么都没有,就是这么来的:
        // 真正的抛出点(City.update → updateCapture → finishCapture → …)
        // 被这个 finalizer 抹掉了。声明成 void 则发 Rethrow,栈完整保留。
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(CityManager), nameof(CityManager.update))]
        private static void CityUpdateFinalizer()
        {
            CityManagerMutationScope.ExitCityUpdate();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorldPrefix()
        {
            CityManagerMutationScope.Reset();
        }
    }
}
