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
    ///     职责:
    ///     1. 注册 mod 自带的 name_generators/Xia(国名/城名/文化/氏族 json)与 lib 词库目录;
    ///     2. 注册 Xia_name 人名生成器(姓+名/千字文);
    ///     3. 追加 "default" actor 参数获取器,处理姓(血统姓)与氏(clan_name)。
    ///
    ///     无中文名时:本文件整体不编译,Xia 直接沿用 clone human 的 name_template_sets,不做中文处理。
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
            var generator = new XiaActorNameGenerator("Xia_name", "default");
            generator.AddTemplate("$family_name${中文名字}", 1);
            generator.AddTemplate("$family_name${中文名字}{中文名字}", 1);
            generator.AddTemplate("{中文名字}{千字文}", 1);
            generator.AddTemplate("{千字文}", 1);
            CN_NameGeneratorLibrary.Submit(generator);

            ParameterGetters.PutActorParameterGetter("default",
                (Action<Actor, Dictionary<string, string>>)Delegate.Combine(
                    ParameterGetters.GetActorParameterGetter("default"),
                    (Action<Actor, Dictionary<string, string>>)ActorParameterGetter));
        }

        /// <summary>姓/氏参数注入:从词库取氏,写入 actor.data 的 family/clan/chinese_family_name。</summary>
        private static void ActorParameterGetter(Actor pActor, Dictionary<string, string> pParameters)
        {
            pActor.data.get("family_name", out string familyName, "");
            pActor.data.get("clan_name", out string clanName, "");
            pActor.data.get("chinese_family_name", out string chineseFamilyName, "");

            if (string.IsNullOrEmpty(familyName) && string.IsNullOrEmpty(clanName) &&
                string.IsNullOrEmpty(chineseFamilyName))
            {
                familyName = WordLibraryManager.GetRandomWord("氏");
                clanName = familyName;
                pActor.data.set("chinese_family_name", familyName);
                pActor.data.set("family_name", familyName);
                pActor.data.set("clan_name", familyName);
            }
            else if (string.IsNullOrEmpty(familyName) && string.IsNullOrEmpty(clanName) &&
                     !string.IsNullOrEmpty(chineseFamilyName))
            {
                familyName = chineseFamilyName;
                pActor.data.set("family_name", familyName);
                pActor.data.set("clan_name", familyName);
            }

            pParameters["family_name"] = familyName ?? "";
            pParameters["clan_name"] = string.IsNullOrEmpty(clanName) ? (familyName ?? "") : clanName;
        }

        /// <summary>Xia 人名生成器:有姓用带姓模板,无姓用千字文模板。</summary>
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
                if (pParameters.ContainsKey("family_name") && !string.IsNullOrEmpty(pParameters["family_name"]))
                {
                    return templates[Random.Range(0, 2)].GenerateName(pParameters);
                }

                return templates[Random.Range(2, 4)].GenerateName(pParameters);
            }
        }
    }
}
#endif
