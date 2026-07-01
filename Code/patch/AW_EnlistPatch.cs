using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     入伍编年史:Postfix City.makeWarrior(Actor)(makeWarrior 在 City 自身声明,typeof 正确)。
    ///     贵族被征为战士 → 记一条"入伍从军"(war 分类)。仅贵族(ChronicleEvents 内部门槛)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_EnlistPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.makeWarrior))]
        public static void MakeWarrior_Postfix(Actor pActor)
        {
            ChronicleEvents.OnEnlisted(pActor);
        }
    }
}
