using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
#if 一米_中文名
using Chinese_Name;
#endif

namespace AncientWarfare3.content
{
    internal static class CivMonkeyNamingContent
    {
        private const string SeedParameter = "aw_civ_monkey_seed";
        private const string InheritedFamilyParameter = "aw_civ_monkey_inherited_family";
        private const string SurnameLibraryId = "猴族姓氏";
        private const string GivenNameLibraryId = "猴族名";
        private const string CityLibraryId = "猴族城市";
        private const string KingdomLibraryId = "猴族国家";

        public static void Init()
        {
            ActorAsset actor = AssetManager.actor_library.get(CivMonkeyNamingRules.ActorAssetId);
            if (actor == null)
            {
                ModClass.LogWarning("[civ_monkey naming] Missing actor asset: " +
                                    CivMonkeyNamingRules.ActorAssetId);
                return;
            }

            RegisterVanillaGenerators();
            RegisterVanillaNameSet(actor);
            actor.name_template_sets = new[] { CivMonkeyNamingRules.NameSetId };

#if 一米_中文名
            RegisterChineseNameGenerators();
#endif
        }

        internal static long ActorSeed(long pActorId)
        {
            return unchecked(WorldSeed() + pActorId * 543L);
        }

        internal static long MetaSeed(long pObjectId)
        {
            return unchecked(WorldSeed() + pObjectId);
        }

        internal static CivMonkeyLineageIdentity ResolveLineageIdentity(
            string pInheritedOrExistingShi, long pActorId)
        {
            IReadOnlyList<string> surnames = CivMonkeyNamingRules.Surnames;
#if 一米_中文名
            surnames = CivMonkeyChineseNameGenerator.GetWords(SurnameLibraryId,
                CivMonkeyNamingRules.Surnames);
#endif
            return CivMonkeyNamingRules.ResolveLineageIdentity(
                pInheritedOrExistingShi, ActorSeed(pActorId), surnames);
        }

        internal static string ResolveInheritedFamily(Actor pActor)
        {
            string family = ReadFamily(pActor);
            if (!string.IsNullOrEmpty(family)) return family;
            if (pActor == null) return "";

            foreach (Actor parent in pActor.getParents())
            {
                if (parent?.data == null || parent.data.sex != ActorSex.Male) continue;
                family = ReadFamily(parent);
                if (!string.IsNullOrEmpty(family)) return family;
            }

            return "";
        }

        private static string ReadFamily(Actor pActor)
        {
            if (pActor?.data == null) return "";
            pActor.data.get("chinese_family_name", out string family, "");
            if (!string.IsNullOrWhiteSpace(family)) return family.Trim();
            pActor.data.get("family_name", out family, "");
            return family?.Trim() ?? "";
        }

        private static long WorldSeed()
        {
            return World.world?.map_stats?.life_dna ?? 0L;
        }

        private static void RegisterVanillaNameSet(ActorAsset pActor)
        {
            NameSetAsset original = ResolveOriginalNameSet(pActor);
            NameSetAsset set = AssetManager.name_sets.has(CivMonkeyNamingRules.NameSetId)
                ? AssetManager.name_sets.get(CivMonkeyNamingRules.NameSetId)
                : AssetManager.name_sets.add(new NameSetAsset
                {
                    id = CivMonkeyNamingRules.NameSetId
                });

            set.city = CivMonkeyNamingRules.CityGeneratorId;
            set.clan = CivMonkeyNamingRules.ClanGeneratorId;
            set.culture = OriginalOrMonkey(original?.culture);
            set.family = OriginalOrMonkey(original?.family);
            set.kingdom = CivMonkeyNamingRules.KingdomGeneratorId;
            set.language = OriginalOrMonkey(original?.language);
            set.unit = CivMonkeyNamingRules.ActorGeneratorId;
            set.religion = OriginalOrMonkey(original?.religion);
        }

        private static NameSetAsset ResolveOriginalNameSet(ActorAsset pActor)
        {
            string[] templateSets = pActor?.name_template_sets;
            if (templateSets != null)
            {
                foreach (string templateSetId in templateSets)
                {
                    if (string.IsNullOrEmpty(templateSetId) ||
                        templateSetId == CivMonkeyNamingRules.NameSetId ||
                        !AssetManager.name_sets.has(templateSetId))
                        continue;
                    return AssetManager.name_sets.get(templateSetId);
                }
            }

            return AssetManager.name_sets.has("monkey_set")
                ? AssetManager.name_sets.get("monkey_set")
                : null;
        }

        private static string OriginalOrMonkey(string pGeneratorId)
        {
            return string.IsNullOrEmpty(pGeneratorId) ? "monkey_name" : pGeneratorId;
        }

        private static void RegisterVanillaGenerators()
        {
            RegisterVanillaGenerator(CivMonkeyNamingRules.ActorGeneratorId,
                BuildFallbackActorNames(), new[] { "i", "派", "k", "猴", "娜", "妹",
                    "姐", "弟", "宝", "毛" });
            RegisterVanillaGenerator(CivMonkeyNamingRules.CityGeneratorId,
                CivMonkeyNamingRules.CityNames, new[] { "城", "邑", "寨", "关", "岭", "乡" });
            RegisterVanillaGenerator(CivMonkeyNamingRules.ClanGeneratorId,
                CivMonkeyNamingRules.Surnames, new[] { "蒙", "猴", "侯" });
            RegisterVanillaGenerator(CivMonkeyNamingRules.KingdomGeneratorId,
                CivMonkeyNamingRules.KingdomNames, new[] { "国" });
        }

        private static string[] BuildFallbackActorNames()
        {
            var names = new string[CivMonkeyNamingRules.Surnames.Length *
                                   CivMonkeyNamingRules.GivenNames.Length];
            int index = 0;
            foreach (string surname in CivMonkeyNamingRules.Surnames)
                foreach (string givenName in CivMonkeyNamingRules.GivenNames)
                    names[index++] = surname + givenName;
            return names;
        }

        private static void RegisterVanillaGenerator(string pId, string[] pNames,
            string[] pAllowedFemaleEndings)
        {
            NameGeneratorAsset generator = AssetManager.name_generator.has(pId)
                ? AssetManager.name_generator.get(pId)
                : AssetManager.name_generator.add(new NameGeneratorAsset { id = pId });

            generator.use_dictionary = true;
            generator.dict_parts = new Dictionary<string, string>
            {
                ["fixed"] = string.Join(",", pNames)
            };
            generator.templates = new List<string[]> { new[] { "fixed" } };
            generator.onomastics_templates = new List<string>();
            generator.vowels = pAllowedFemaleEndings;
            generator.consonants = NameGeneratorAsset.consonants_all;
            generator.finalizer = pName => string.Equals(pName, "Monpai",
                StringComparison.Ordinal) ? "monpai" : pName;
        }

#if 一米_中文名
        private static void RegisterChineseNameGenerators()
        {
            string libraryDirectory = Path.Combine(
                ModClass.Instance.GetDeclaration().FolderPath, "name_generators", "lib");
            WordLibraryManager.SubmitDirectoryToLoad(libraryDirectory);

            const string actorGetter = "aw_civ_monkey_actor";
            const string cityGetter = "aw_civ_monkey_city";
            const string clanGetter = "aw_civ_monkey_clan";
            const string kingdomGetter = "aw_civ_monkey_kingdom";

            ParameterGetters.PutActorParameterGetter(actorGetter, (pActor, pParameters) =>
            {
                PutSeed(pParameters, ActorSeed(pActor?.getID() ?? 0L));
                string family = ResolveInheritedFamily(pActor);
                pParameters[InheritedFamilyParameter] = family;
                if (!string.IsNullOrEmpty(family)) pParameters["family_name"] = family;
            });
            ParameterGetters.PutCityParameterGetter(cityGetter, (pCity, pParameters) =>
                PutSeed(pParameters, MetaSeed(pCity?.getID() ?? 0L)));
            ParameterGetters.PutClanParameterGetter(clanGetter,
                (pClan, pActor, pParameters) =>
                {
                    PutSeed(pParameters, ActorSeed(pActor?.getID() ?? 0L));
                    string family = ResolveInheritedFamily(pActor);
                    pParameters[InheritedFamilyParameter] = family;
                    pParameters["founder_family_name"] = family;
                    pParameters["founder_home"] = string.IsNullOrEmpty(
                        pClan?.data?.founder_city_name)
                        ? pClan?.data?.founder_kingdom_name ?? ""
                        : pClan.data.founder_city_name;
                });
            ParameterGetters.PutKingdomParameterGetter(kingdomGetter, (pKingdom, pParameters) =>
                PutSeed(pParameters, MetaSeed(pKingdom?.getID() ?? 0L)));

            CN_NameGeneratorLibrary.Submit(new CivMonkeyChineseNameGenerator(
                CivMonkeyNamingRules.ActorGeneratorId, actorGetter, MonkeyNameKind.Actor));
            CN_NameGeneratorLibrary.Submit(new CivMonkeyChineseNameGenerator(
                CivMonkeyNamingRules.CityGeneratorId, cityGetter, MonkeyNameKind.City));
            CN_NameGeneratorLibrary.Submit(new CivMonkeyChineseNameGenerator(
                CivMonkeyNamingRules.ClanGeneratorId, clanGetter, MonkeyNameKind.Clan));
            CN_NameGeneratorLibrary.Submit(new CivMonkeyChineseNameGenerator(
                CivMonkeyNamingRules.KingdomGeneratorId, kingdomGetter, MonkeyNameKind.Kingdom));
        }

        private static void PutSeed(Dictionary<string, string> pParameters, long pSeed)
        {
            pParameters[SeedParameter] = pSeed.ToString(CultureInfo.InvariantCulture);
        }

        internal static bool ChineseNameOwns(MetaType pType)
        {
            string id = pType == MetaType.Unit
                ? CivMonkeyNamingRules.ActorGeneratorId
                : pType == MetaType.City
                    ? CivMonkeyNamingRules.CityGeneratorId
                    : pType == MetaType.Clan
                        ? CivMonkeyNamingRules.ClanGeneratorId
                        : pType == MetaType.Kingdom
                            ? CivMonkeyNamingRules.KingdomGeneratorId
                            : "";
            return !string.IsNullOrEmpty(id) && CN_NameGeneratorLibrary.Get(id) != null;
        }

        private enum MonkeyNameKind
        {
            Actor,
            City,
            Clan,
            Kingdom
        }

        private sealed class CivMonkeyChineseNameGenerator : CN_NameGeneratorAsset
        {
            private static readonly FieldInfo WordLibraryInstanceField =
                typeof(WordLibraryManager).GetField("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            private readonly MonkeyNameKind _kind;
            private readonly CN_NameTemplate _template;

            public CivMonkeyChineseNameGenerator(string pId, string pParameterGetter,
                MonkeyNameKind pKind)
            {
                id = pId;
                parameter_getter = pParameterGetter;
                _kind = pKind;
                string format = pKind == MonkeyNameKind.Actor
                    ? "{猴族姓氏:family_name}{猴族名:given_name}"
                    : pKind == MonkeyNameKind.City
                        ? "{猴族城市:city_name}"
                        : pKind == MonkeyNameKind.Clan
                            ? "$founder_home$#的#$founder_family_name$#家族#"
                            : "{猴族国家:kingdom_name}";
                _template = CN_NameTemplate.Create(format, 1f);
                templates.Add(_template);
            }

            public override string GenerateName(Dictionary<string, string> pParameters)
            {
                pParameters ??= new Dictionary<string, string>();
                long seed = ReadSeed(pParameters);

                switch (_kind)
                {
                    case MonkeyNameKind.Actor:
                    {
                        pParameters.TryGetValue(InheritedFamilyParameter,
                            out string inheritedFamily);
                        if (string.IsNullOrWhiteSpace(inheritedFamily))
                            pParameters.TryGetValue("family_name", out inheritedFamily);
                        CivMonkeyNamingRules.BuildActorName(inheritedFamily, seed,
                            (int)MetaType.Unit, GetWords(SurnameLibraryId,
                                CivMonkeyNamingRules.Surnames),
                            GetWords(GivenNameLibraryId, CivMonkeyNamingRules.GivenNames),
                            out string surname, out string givenName);
                        pParameters["family_name"] = surname;
                        pParameters["given_name"] = givenName;
                        break;
                    }
                    case MonkeyNameKind.City:
                        pParameters["city_name"] = CivMonkeyNamingRules.PickCity(seed,
                            (int)MetaType.City,
                            GetWords(CityLibraryId, CivMonkeyNamingRules.CityNames));
                        break;
                    case MonkeyNameKind.Clan:
                    {
                        pParameters.TryGetValue(InheritedFamilyParameter,
                            out string inheritedFamily);
                        if (string.IsNullOrWhiteSpace(inheritedFamily))
                            pParameters.TryGetValue("founder_family_name",
                                out inheritedFamily);
                        pParameters["founder_family_name"] =
                            CivMonkeyNamingRules.ResolveSurname(inheritedFamily, seed,
                                GetWords(SurnameLibraryId,
                                    CivMonkeyNamingRules.Surnames));
                        break;
                    }
                    case MonkeyNameKind.Kingdom:
                        pParameters["kingdom_name"] = CivMonkeyNamingRules.PickKingdom(seed,
                            (int)MetaType.Kingdom,
                            GetWords(KingdomLibraryId, CivMonkeyNamingRules.KingdomNames));
                        break;
                }

                string generated = _template.GenerateName(pParameters);
                if (!string.IsNullOrWhiteSpace(generated) &&
                    !string.Equals(generated, "name", StringComparison.OrdinalIgnoreCase))
                    return generated;

                return _kind == MonkeyNameKind.Actor
                    ? CivMonkeyNamingRules.BuildActorName("", seed, (int)MetaType.Unit)
                    : _kind == MonkeyNameKind.City
                        ? CivMonkeyNamingRules.PickCity(seed, (int)MetaType.City)
                        : _kind == MonkeyNameKind.Clan
                            ? CivMonkeyNamingRules.ResolveSurname("", seed) + "家族"
                            : CivMonkeyNamingRules.PickKingdom(seed, (int)MetaType.Kingdom);
            }

            private static long ReadSeed(IReadOnlyDictionary<string, string> pParameters)
            {
                if (pParameters.TryGetValue(SeedParameter, out string seedText) &&
                    long.TryParse(seedText, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long seed))
                    return seed;
                return 0L;
            }

            internal static IReadOnlyList<string> GetWords(string pId,
                IReadOnlyList<string> pFallback)
            {
                try
                {
                    var manager = WordLibraryInstanceField?.GetValue(null) as WordLibraryManager;
                    if (manager != null && manager.dict.TryGetValue(pId,
                            out WordLibraryAsset library) && library?.words?.Count > 0)
                        return library.words;
                }
                catch (Exception e)
                {
                    ModClass.LogWarning("[civ_monkey naming] Cannot read word library " +
                                        pId + ": " + e.Message);
                }

                return pFallback;
            }
        }
#endif
    }
}
