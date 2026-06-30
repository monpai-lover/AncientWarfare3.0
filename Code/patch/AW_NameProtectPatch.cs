using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     防"夏人/历史人物长大后变成不同的人"(等价 AW2 的 name_set 保护)。
    ///
    ///     真凶:`Actor.getName()`(Actor.cs:2346)在 `data.name` 为空时调 `generateNewName()`(:2361)
    ///     用 NameGenerator **随机重生成名字**。任何时刻 data.name 被清空(存档往返、克隆、setName 传空…),
    ///     下次取名就把 LineageService 拼好的"氏+名"/历史人物预设名冲成无关随机名 → 看起来"变了个人"。
    ///     引擎本身的长大/成年(checkGrowthEvent/calcAgeStates)**不重命名**,所以唯一闸门就是 generateNewName。
    ///
    ///     做法:Prefix `generateNewName`——若是 Xia 且**已有谱系单名 GIVEN_NAME**(谱系/历史人物已建名),
    ///     用 `LineageService.ApplyDisplayName` 从 GIVEN_NAME+FAMILY/CLAN 重建全名并 setName,return false 阻断随机。
    ///     GIVEN_NAME 为空(尚无谱系名)→ return true 放行原版,让它先生成,之后出生钩(OnActorBorn/
    ///     OnActorBornWithParents)再接管命名。**绝不在 GIVEN_NAME 空时调 ApplyDisplayName**(它会回调 getName→
    ///     generateNewName 无限递归)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_NameProtectPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "generateNewName")]
        public static bool GenerateNewName_Prefix(Actor __instance)
        {
            if (__instance?.data == null) return true;
            if (!LineageService.IsXia(__instance)) return true;

            // 只保护"已有谱系单名"的夏人:GIVEN_NAME 非空才接管,否则放行原版随机(防递归)。
            __instance.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            if (string.IsNullOrEmpty(given)) return true;

            // 从谱系重建全名并写回真名(ApplyDisplayName 内部会 setName);跳过原版随机生成。
            LineageService.ApplyDisplayName(__instance);

            // 兜底:万一 ApplyDisplayName 没写出名(理论不会,given 非空),至少用 given 保底,避免 data.name 仍空再次触发。
            if (string.IsNullOrEmpty(__instance.data.name))
                __instance.setName(given);

            return false; // 阻断原版随机命名
        }
    }
}
