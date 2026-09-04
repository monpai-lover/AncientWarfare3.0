using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     读档早期修复 <c>asset == null</c> 的王国。
    ///
    ///     <para>
    ///     成因:<c>Kingdom.loadData</c>(Kingdom.cs:784) 用
    ///     <c>AssetManager.kingdoms.get(actorAsset.kingdom_id_civilization)</c> 解析
    ///     <c>kingdom.asset</c>,取不到时**静默**赋 null(存档里的王国 id 指向一个当时
    ///     没注册上的资产:改过 id / 读档时 mod 资产未就绪)。之后任何裸解引用
    ///     <c>kingdom.asset</c> 的路径都崩 —— 典型是 <c>Building.setKingdom</c> 里的
    ///     <c>isKingdomCiv()</c>(=裸 <c>return asset.civ;</c>,Building.cs:546/584)。
    ///     </para>
    ///
    ///     <para>
    ///     时机:读档的 SmoothLoader 动作顺序是
    ///     <c>loadKingdoms</c>(1008) → <c>loadCities</c>(1009) → ... →
    ///     <c>loadActors</c>(1014) → <c>loadBuildings</c>(1023)。
    ///     <c>Building.setKingdom</c> 的 NRE 发生在 <c>loadBuildings</c> 里,而
    ///     <c>AW_SavePatch.RepairNullKingdomAssets</c> 只在
    ///     <c>WildKingdomsManager.beginChecksBuildings</c>(更晚)跑 —— 赶不上。
    ///     这里把修复提前到 <c>loadBuildings</c> 的前缀,恰好落在第一个
    ///     <c>setKingdom</c> 之前;此时 kingdoms / units 都已加载,从成员解析 asset 可行。
    ///     <c>loadBuildings</c> 本身是每读档只跑一次的 SmoothLoader 动作,无需去重标志。
    ///     </para>
    /// </summary>
    [HarmonyPatch]
    internal static class AW_LoadBuildingsKingdomRepairPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(SaveManager), "loadBuildings")]
        private static void Prefix()
        {
            AW_SavePatch.RepairNullKingdomAssets();
        }
    }
}
