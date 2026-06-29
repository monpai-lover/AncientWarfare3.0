using AncientWarfare3.core.db;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     死亡归档:Prefix Actor.die —— 在死亡逻辑执行前(city/kingdom/clan 等数据仍完整)
    ///     把该 actor 的档案 upsert 进 SQLite 并标记死亡。
    ///     参考 Cultiway 同样用 Prefix 钩 Actor.die(PatchActor.cs)。
    ///
    ///     不接管原方法(无 return false),只在死亡前读数据归档,原死亡流程照常执行。
    /// </summary>
    [HarmonyPatch]
    public static class AW_ActorDeathPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die")]
        public static void Die_Prefix(Actor __instance)
        {
            if (__instance == null || __instance.data == null) return;
            if (!LineageArchiveService.ShouldArchive(__instance)) return;

            LineageArchiveService.ArchiveActor(__instance, pAlive: false);
        }
    }
}
