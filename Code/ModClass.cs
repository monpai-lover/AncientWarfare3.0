using AncientWarfare3.content;
using HarmonyLib;
using NeoModLoader.api;

namespace AncientWarfare3
{
    /// <summary>
    ///     AW3.0 模组入口。基于新版 WorldBox(0.51.0+)的 NeoModLoader BasicMod 框架。
    ///     功能按批次逐步添加:种族 → 特质 → 物品 → 建筑/单位 → 命名 → 天命 → 政策 → 附庸 → ...
    /// </summary>
    public class ModClass : BasicMod<ModClass>, IReloadable
    {
        public const string GUID = "ANCIENTWARFARE3";

        protected override void OnModLoad()
        {
            // 注册 Harmony 补丁(扫描本程序集所有 [HarmonyPatch])
            new Harmony(GUID).PatchAll();

            // 通用夺舍工具:扫描 [MethodReplace] 用 Transpiler 重定向目标方法体(保留 Prefix/Postfix 链)
            utils.HarmonyTools.ReplaceMethods();

            // 批A:夏朝 Xia 种族 / 王国 / 贴图
            XiaContent.Init();

#if 一米_中文名
            // 有中文名时:注册 Xia 中文命名(国名/城名/人名/姓氏)。无中文名则用 clone human 命名。
            XiaNaming.Init();
#endif

            LogInfo("Ancient Warfare 3.0 loaded — batch A (Xia race).");
        }

        public void Reload()
        {
            // 热重载入口,后续按需重载本地化/词库等资源。
        }
    }
}
