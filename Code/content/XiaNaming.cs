using System;
using System.Collections.Generic;
using System.IO;
using AncientWarfare3.core.naming;
using NeoModLoader.General;

namespace AncientWarfare3.content
{
    /// <summary>
    /// Registers Xia-specific resources into AW3's integrated naming engine.
    /// Family and Shi identity remain owned by LineageService.
    /// </summary>
    internal static class XiaNaming
    {
        public static void Init()
        {
            string modPath = ModClass.Instance.GetDeclaration().FolderPath;
            string wordDirectory = Path.Combine(modPath, "name_generators", "lib");
            foreach (AWWordLibraryAsset library in
                     AWNamingResourceLoader.LoadWordLibraries(wordDirectory,
                         ModClass.LogWarning))
                AWWordLibraryManager.Instance.Submit(library);

            AWNameGeneratorLibrary.SubmitDirectoryToLoad(
                Path.Combine(modPath, "name_generators", "Xia"),
                ModClass.LogWarning);

            InitActorNameGenerator();
            OverrideClanParameterGetter();
            OverrideAllianceParameterGetter();

            AWNameGeneratorAsset allianceGenerator =
                AWNameGeneratorLibrary.Get(XiaNameSets.AllianceGenerator);
            if (allianceGenerator == null)
                ModClass.LogWarning(
                    "[Xia alliance naming] integrated route unavailable: Xia_alliance missing");
            else
                ModClass.LogInfo(
                    "[Xia alliance naming] integrated route ready: generator=" +
                    allianceGenerator.Id + " templates=" +
                    allianceGenerator.Templates.Count);

            LM.AddToCurrentLocale("familyname", "姓");
            LM.AddToCurrentLocale("clanname", "氏");
            LM.ApplyLocale();
        }

        private static void OverrideClanParameterGetter()
        {
            try
            {
                AWNameParameterGetters.PutClanParameterGetter("default",
                    (pClan, pActor, pParameters) =>
                    {
                        pParameters["race"] =
                            pClan?.data?.original_actor_asset ?? string.Empty;
                        pParameters["founder_home"] = string.IsNullOrEmpty(
                            pClan?.data?.founder_city_name)
                            ? pClan?.data?.founder_kingdom_name ?? string.Empty
                            : pClan.data.founder_city_name;

                        string shi = ResolveClanShi(pClan, pActor);
                        if (XiaNameRepairRules.IsInvalidGeneratedMetaName(shi))
                        {
                            long seed = pActor?.data?.id ??
                                        pClan?.getID() ?? 0L;
                            shi = XiaFallbackNameRules.LocalClanShiName(seed);
                        }
                        pParameters["founder_family_name"] = shi;
                    });
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Failed to register Xia clan naming parameters: " +
                    error.Message);
            }
        }

        private static void OverrideAllianceParameterGetter()
        {
            try
            {
                AWNameParameterGetters.PutAllianceParameterGetter(
                    "aw_xia_alliance", (pAlliance, pParameters) =>
                    {
                        AWNameParameterGetters
                            .GetAllianceParameterGetter("default")
                            (pAlliance, pParameters);
                        pParameters["meeting_city"] =
                            ResolveAllianceMeetingCity(pAlliance);
                    });
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Failed to register Xia alliance naming parameters: " +
                    error.Message);
            }
        }

        private static string ResolveAllianceMeetingCity(Alliance pAlliance)
        {
            if (pAlliance?.data == null) return string.Empty;
            Kingdom founder = null;
            foreach (Kingdom kingdom in pAlliance.kingdoms_list)
            {
                if (kingdom?.data == null) continue;
                if (kingdom.getID() == pAlliance.data.founder_kingdom_id)
                {
                    founder = kingdom;
                    break;
                }
                if (founder == null) founder = kingdom;
            }
            if (founder?.data == null) return string.Empty;

            string capital = founder.capital?.data?.name;
            string firstCity = string.Empty;
            foreach (City city in founder.cities)
            {
                if (city?.data == null || city.isRekt() ||
                    string.IsNullOrWhiteSpace(city.data.name))
                    continue;
                firstCity = city.data.name;
                break;
            }
            return XiaAllianceNamingRules.ResolveMeetingCity(capital, firstCity);
        }

        private static string ResolveClanShi(Clan pClan, Actor pActor)
        {
            if (pActor?.data != null)
            {
                pActor.data.get("clan_name", out string clan, string.Empty);
                if (!string.IsNullOrEmpty(clan)) return clan;
                pActor.data.get("chinese_family_name", out string family,
                    string.Empty);
                return family;
            }

            if (pClan?.units == null) return string.Empty;
            foreach (Actor unit in pClan.units)
            {
                if (unit?.data == null) continue;
                unit.data.get("clan_name", out string clan, string.Empty);
                if (!string.IsNullOrEmpty(clan)) return clan;
            }
            foreach (Actor unit in pClan.units)
            {
                if (unit?.data == null) continue;
                unit.data.get("chinese_family_name", out string family,
                    string.Empty);
                if (!string.IsNullOrEmpty(family)) return family;
            }
            return string.Empty;
        }

        private static void InitActorNameGenerator()
        {
            var templates = new List<AWNameTemplate>
            {
                AWNameTemplate.Create("{千字文}{千字文}", 4f),
                AWNameTemplate.Create("{中文名字}{千字文}", 3f),
                AWNameTemplate.Create("{千字文}{中文名字}", 2f)
            };
            AWNameGeneratorLibrary.Submit(new AWNameGeneratorAsset(
                XiaNameSets.UnitGenerator, templates,
                AWNameTemplate.Create("{千字文}", 1f), "default"));
        }
    }
}
