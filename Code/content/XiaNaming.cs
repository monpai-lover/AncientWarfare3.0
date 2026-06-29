#if 一米_中文名
using System.Collections.Generic;
using Chinese_Name;
using NeoModLoader.General;
using Random = UnityEngine.Random;

namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝中文命名接入(仅在编译符号 一米_中文名 启用时)。
    ///     移植自 AW2 Code/CustomNameGenerator.cs。
    ///
    ///     职责(阶段2 后已收窄):
    ///     1. 注册 mod 自带的 name_generators/Xia(国名/城名/文化/氏族 json)与 lib 词库目录;
    ///     2. 注册 Xia_name 人名生成器 —— 只产"单名/双名"素材(不带姓)。
    ///
    ///     姓/氏的赋予、父系继承、贵族晋升、显示名拼接 全部交给 core/lineage/LineageService,
    ///     本文件不再写 family_name/clan_name(那会与谱系系统冲突)。
    ///     无中文名时:本文件整体不编译,Xia 沿用 clone human 命名;姓氏逻辑仍由 LineageService 跑
    ///     (用 LineageNamePool 内置古姓/氏池,不依赖中文名 mod 词库)。
    /// </summary>
    internal static class XiaNaming
    {
        /// <summary>由 ModClass.OnModLoad 调用:注册命名资源目录 + 人名生成器。</summary>
        public static void Init()
        {
            string modPath = ModClass.Instance.GetDeclaration().FolderPath;

            CN_NameGeneratorLibrary.SubmitDirectoryToLoad(modPath + "/name_generators/Xia");
            WordLibraryManager.SubmitDirectoryToLoad(modPath + "/name_generators/lib");

            InitActorNameGenerator();

            LM.AddToCurrentLocale("familyname", "姓");
            LM.AddToCurrentLocale("clanname", "氏");
            LM.ApplyLocale();
        }

        private static void InitActorNameGenerator()
        {
            // 阶段2 起:Xia_name 只生成"单名/双名"素材(不带姓),姓/氏拼接交给 LineageService。
            // 这样游戏内真名 = 单名,符合任务书"平民只单名、贵族才有姓氏"的基线。
            var generator = new XiaActorNameGenerator("Xia_name", "default");
            generator.AddTemplate("{中文名字}{千字文}", 1);
            generator.AddTemplate("{千字文}", 1);
            generator.AddTemplate("{中文名字}", 1);
            CN_NameGeneratorLibrary.Submit(generator);

            // 不再追加写 family_name/clan_name 的 ParameterGetter ——
            // 姓氏的赋予/继承/显示完全由 core/lineage/LineageService 负责(出生 hook + 晋升 + ApplyDisplayName)。
        }

        /// <summary>Xia 人名生成器:只产单名/双名,姓氏由 LineageService 后置拼接。</summary>
        private class XiaActorNameGenerator : CN_NameGeneratorAsset
        {
            public XiaActorNameGenerator(string pId, string pParameterGetter)
            {
                id = pId;
                parameter_getter = pParameterGetter;
                templates ??= new List<CN_NameTemplate>();
            }

            public void AddTemplate(string pFormat, float pWeight)
            {
                templates.Add(CN_NameTemplate.Create(pFormat, pWeight));
            }

            public override string GenerateName(Dictionary<string, string> pParameters)
            {
                return templates[Random.Range(0, templates.Count)].GenerateName(pParameters);
            }
        }
    }
}
#endif
