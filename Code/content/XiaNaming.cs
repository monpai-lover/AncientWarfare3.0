#if 一米_中文名
using System;
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

            // 消除 "duplicate asset - overwriting" 警告:ChineseName(OptionalDependency,先加载)的 default
            // 包自带同名 Xia_city/clan/culture/kingdom/name 生成器。AW3 后注册同名会触发游戏基类
            // AssetLibrary.add 的 overwriting 日志(AssetLibrary.cs:69)。提交前先**静默移除**已存在的同名条目
            // (直接清 dict/list,不走 add),这样 AW3 注册时 dict 无同名 → 不再打警告。AW3 模板与 ChineseName
            // 不同(用中文城名上/下、中文国名前缀),是有意覆盖,故必须用 AW3 版本而非复用。
            RemoveExistingGenerators("Xia_city", "Xia_clan", "Xia_culture", "Xia_kingdom", "Xia_name");

            CN_NameGeneratorLibrary.SubmitDirectoryToLoad(modPath + "/name_generators/Xia");
            WordLibraryManager.SubmitDirectoryToLoad(modPath + "/name_generators/lib");

            InitActorNameGenerator();
            OverrideClanParameterGetter();

            LM.AddToCurrentLocale("familyname", "姓");
            LM.AddToCurrentLocale("clanname", "氏");
            LM.ApplyLocale();
        }

        /// <summary>
        ///     修复"原版 Clan(氏族)命名把姓当氏":ChineseName 的 Xia_clan 模板用 $founder_family_name$,
        ///     其 default_clan_parameter_getter 把该参数填成 actor.data["chinese_family_name"](=我们的**姓**)
        ///     → 氏族名变成 "城名+姓+家/氏/族"(如 泾阳滕家,滕是姓不是氏)。
        ///     这里覆盖 clan 参数 getter:对 Xia,founder_family_name 改填该人**氏(clan_name)**;
        ///     非 Xia 或无氏时回退原行为(姓)。ParameterGetters 是 Chinese_Name.dll 的 public API,可直接覆盖 "default"。
        /// </summary>
        private static void OverrideClanParameterGetter()
        {
            try
            {
                ParameterGetters.PutClanParameterGetter("default", (pClan, pActor, pParameters) =>
                {
                    pParameters["race"] = pClan.data.original_actor_asset;
                    pParameters["founder_home"] = string.IsNullOrEmpty(pClan.data.founder_city_name)
                        ? pClan.data.founder_kingdom_name
                        : pClan.data.founder_city_name;

                    // 优先取"氏"(clan_name);无氏回退"姓"(chinese_family_name),保证模板有值。
                    string shi = ResolveClanShi(pClan, pActor);
                    pParameters["founder_family_name"] = string.IsNullOrEmpty(shi) ? "无名" : shi;
                });
            }
            catch (Exception e)
            {
                ModClass.LogWarning("覆盖 Clan 命名参数 getter 失败(氏族名可能仍用姓): " + e.Message);
            }
        }

        /// <summary>取氏族命名用的"氏":pActor 优先;motto 路径 pActor==null 时遍历 clan 成员找第一个有氏者。</summary>
        private static string ResolveClanShi(Clan pClan, Actor pActor)
        {
            if (pActor != null)
            {
                pActor.data.get("clan_name", out string clan, "");
                if (!string.IsNullOrEmpty(clan)) return clan;
                pActor.data.get("chinese_family_name", out string fam, "");
                return fam; // 无氏回退姓
            }

            foreach (var unit in pClan.units)
            {
                unit.data.get("clan_name", out string clan, "");
                if (!string.IsNullOrEmpty(clan)) return clan;
            }
            foreach (var unit in pClan.units)
            {
                unit.data.get("chinese_family_name", out string fam, "");
                if (!string.IsNullOrEmpty(fam)) return fam;
            }
            return "";
        }

        /// <summary>
        ///     从 CN_NameGeneratorLibrary 静默移除指定 id 的已注册生成器。
        ///     用于消除 AW3 覆盖 ChineseName 同名 Xia 生成器时的 overwriting 警告——AW3 提交前先清掉旧的。
        ///     Chinese_Name.dll 非 publicized,Instance(internal)/dict/list(protected)编译期不可访问,故用反射。
        /// </summary>
        private static void RemoveExistingGenerators(params string[] pIds)
        {
            try
            {
                var libType = typeof(CN_NameGeneratorLibrary);
                // internal static CN_NameGeneratorLibrary Instance
                var instanceField = libType.GetField("Instance",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                object lib = instanceField?.GetValue(null);
                if (lib == null) return;

                // protected Dictionary<string,T> dict; protected List<T> list (AssetLibrary<T> 基类)
                var dict = GetMemberValue(lib, "dict") as System.Collections.IDictionary;
                var list = GetMemberValue(lib, "list") as System.Collections.IList;
                if (dict == null) return;

                foreach (string id in pIds)
                {
                    if (!dict.Contains(id)) continue;
                    object asset = dict[id];
                    dict.Remove(id);
                    list?.Remove(asset);
                }
            }
            catch (Exception e)
            {
                // 反射失败不影响功能(只是 overwriting 警告仍会出现),不崩。
                ModClass.LogWarning("移除 ChineseName 同名 Xia 生成器失败(overwriting 警告将保留): " + e.Message);
            }
        }

        /// <summary>反射取字段或属性值(沿继承链向上找,含 protected/private)。</summary>
        private static object GetMemberValue(object pObj, string pName)
        {
            var t = pObj.GetType();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            while (t != null)
            {
                var f = t.GetField(pName, flags);
                if (f != null) return f.GetValue(pObj);
                var p = t.GetProperty(pName, flags);
                if (p != null) return p.GetValue(pObj);
                t = t.BaseType;
            }
            return null;
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
