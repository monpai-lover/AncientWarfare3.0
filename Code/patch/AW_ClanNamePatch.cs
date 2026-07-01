using AncientWarfare3.content;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     原版 Clan 命名接管:Postfix Clan.newClan(pFounder, ...)(newClan 在 Clan 自身声明,typeof 正确)。
    ///     游戏默认给 clan 起随机名;这里按领袖身份重命名:
    ///       领袖是国王 → 国名 + 氏 + "氏";领袖非国王 → 城市名 + 氏 + "氏"。
    ///     只处理 Xia 领袖(RenameClanByLeader 内部 IsXia 守卫),其余原版种族 clan 保持原名。
    ///
    ///     时序:自然生成的 clan,newClan 时 founder 的 CLAN_NAME 通常已就绪 → 直接命名。
    ///     称王分封(OnKingFoundBranch)是"先 newClan 后 set CLAN_NAME",此时氏未就绪本方法自动跳过,
    ///     由该流程在 set 好氏后显式再调 RenameClanByLeader 补名(不重复、幂等)。
    /// </summary>
    [HarmonyPatch]
    public static class AW_ClanNamePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Clan), nameof(Clan.newClan))]
        public static void NewClan_Postfix(Clan __instance, Actor pFounder)
        {
            XiaClanBanners.ApplyToClan(__instance, pFounder);
            LineageService.RenameClanByLeader(__instance, pFounder);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ClanBanner), "setupBanner")]
        public static void SetupBanner_Postfix(ClanBanner __instance)
        {
            XiaClanBanners.ApplyFrameToBanner(__instance);
        }
    }
}
