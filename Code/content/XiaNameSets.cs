using System.Collections.Generic;

namespace AncientWarfare3.content
{
    internal static class XiaNameSets
    {
        private const string SINO_ONOMASTICS = "016V4Cf|0:ch g h l m q sh w x y zh;1:ang ao ei en i ia in iu ou u uo;4:a e i o u;6:b ch d f g h k m n sh";

        internal const string DefaultSet = "Xia_default_set";
        internal const string CityGenerator = "Xia_city";
        internal const string ClanGenerator = "Xia_clan";
        internal const string CultureGenerator = "Xia_culture";
        internal const string KingdomGenerator = "Xia_kingdom";
        internal const string LanguageGenerator = "Xia_language";
        internal const string ReligionGenerator = "Xia_religion";
        internal const string SubspeciesGenerator = "Xia_subspecies";
        internal const string UnitGenerator = "Xia_name";

        public static void Init()
        {
            RegisterVanillaFallbackGenerators();
            RegisterNameSet();
        }

        private static void RegisterNameSet()
        {
            var set = AssetManager.name_sets.has(DefaultSet)
                ? AssetManager.name_sets.get(DefaultSet)
                : AssetManager.name_sets.add(new NameSetAsset { id = DefaultSet });

            set.city = CityGenerator;
            set.clan = ClanGenerator;
            set.culture = CultureGenerator;
            set.family = ClanGenerator;
            set.kingdom = KingdomGenerator;
            set.language = LanguageGenerator;
            set.unit = UnitGenerator;
            set.religion = ReligionGenerator;
        }

        private static void RegisterVanillaFallbackGenerators()
        {
            RegisterDictionaryGenerator(
                UnitGenerator,
                new[]
                {
                    "given", "\u53D1,\u653F,\u90A6,\u4E15,\u708E,\u660C,\u8D77,\u4FE1,\u826F,\u6BC5,\u5E73,\u5EF6,\u6B66,\u6587,\u5FB7,\u660E,\u5B89,\u6210,\u662D,\u5BA3,\u7A46,\u6853,\u666F,\u5143,\u4F2F,\u4EF2,\u53D4,\u5B63",
                    "given2", "\u4E4B,\u5143,\u5B50,\u5FB7,\u660E,\u6587,\u6B66,\u5B89,\u5E73,\u6210"
                },
                "given",
                "given,given2");

            RegisterDictionaryGenerator(
                CityGenerator,
                new[]
                {
                    "real", "\u6D1B\u9091,\u9550\u4EAC,\u54B8\u9633,\u4E34\u6DC4,\u90AF\u90F8,\u84DF\u57CE,\u5B9B\u57CE,\u9648\u90FD,\u66F2\u961C,\u59D1\u82CF,\u4F1A\u7A3D,\u664B\u9633,\u5B89\u9091",
                    "prefix", "\u6D1B,\u6DC4,\u9091,\u5B89,\u5E73,\u9633,\u9634,\u5B9B,\u9648,\u66F2,\u84DF,\u90AF,\u6C74,\u6DEE,\u6C49",
                    "suffix", "\u9091,\u90FD,\u57CE,\u5173,\u9633,\u9634,\u9675,\u539F,\u91CC,\u6CC9,\u5DDD"
                },
                "real",
                "prefix,suffix");

            RegisterDictionaryGenerator(
                ClanGenerator,
                new[]
                {
                    "home", "\u6D1B,\u5468,\u79E6,\u664B,\u9C81,\u9F50,\u695A,\u71D5,\u8D75,\u5B8B,\u90D1,\u536B,\u9648,\u8521,\u5434,\u8D8A",
                    "shi", "\u59EC,\u5B34,\u5218,\u66F9,\u53F8\u9A6C,\u59DC,\u59DA,\u59D2,\u4EFB,\u98CE,\u718A,\u5B50,\u59AB,\u59AB,\u59D3"
                },
                "home,shi");

            RegisterDictionaryGenerator(
                CultureGenerator,
                new[]
                {
                    "origin", "\u8BF8\u590F,\u534E\u590F,\u4E2D\u539F,\u6CB3\u6D1B,\u4E5D\u5DDE,\u793C\u4E50,\u9752\u94DC",
                    "suffix", "\u6587\u5316,\u96C5\u98CE,\u793C\u5236"
                },
                "origin,suffix");

            RegisterDictionaryGenerator(
                KingdomGenerator,
                new[]
                {
                    "name", "\u590F,\u5546,\u5468,\u79E6,\u6C49,\u9B4F,\u664B,\u695A,\u9F50,\u9C81,\u71D5,\u8D75,\u5B8B,\u90D1,\u536B,\u9648,\u8521,\u5434,\u8D8A,\u5510,\u865E"
                },
                "name");

            RegisterDictionaryGenerator(
                LanguageGenerator,
                new[]
                {
                    "root", "\u590F,\u534E\u590F,\u8BF8\u590F,\u96C5,\u4E2D\u539F,\u6CB3\u6D1B,\u4E5D\u5DDE,\u738B\u757F,\u793C\u4E50,\u90A6\u56FD",
                    "ending", "\u8BED",
                    "classic_ending", "\u96C5\u8A00,\u534E\u8A00",
                    "fixed_language", "\u590F\u8BED,\u96C5\u8A00,\u534E\u8A00,\u8BF8\u590F\u96C5\u8A00,\u4E5D\u5DDE\u96C5\u8A00"
                },
                "root,ending",
                "root,classic_ending",
                "fixed_language");

            RegisterDictionaryGenerator(
                ReligionGenerator,
                new[]
                {
                    "root", "\u793E\u7A37,\u5B97\u5E99,\u793C\u4E50,\u534E\u590F,\u8BF8\u590F,\u5929\u547D,\u738B\u757F,\u6CB3\u6D1B,\u4E5D\u5DDE",
                    "ending", "\u793C,\u796D,\u7956\u7940,\u5927\u793C,\u4FE1\u4EF0",
                    "fixed_religion", "\u793E\u7A37\u793C,\u5B97\u5E99\u793C,\u793C\u4E50\u7956\u7940,\u534E\u590F\u5927\u793C,\u8BF8\u590F\u793C,\u5929\u547D\u793C,\u738B\u757F\u7956\u7940,\u6CB3\u6D1B\u793C"
                },
                "root,ending",
                "fixed_religion");

            RegisterDictionaryGenerator(
                SubspeciesGenerator,
                new[]
                {
                    "prefix", "\u6CB3\u6D1B,\u4E2D\u539F,\u4E5D\u5DDE,\u738B\u757F,\u9752\u94DC,\u7384\u9E1F,\u793C\u4E50,\u90A6\u56FD,\u6D1B\u9091,\u9550\u4EAC",
                    "base", "\u590F\u4EBA",
                    "fixed_subspecies", "\u534E\u590F\u4EBA,\u8BF8\u590F\u4EBA,\u6CB3\u6D1B\u590F\u4EBA,\u4E2D\u539F\u590F\u4EBA,\u4E5D\u5DDE\u590F\u4EBA,\u738B\u757F\u590F\u4EBA,\u793C\u4E50\u590F\u4EBA,\u7384\u9E1F\u590F\u4EBA"
                },
                "prefix,base",
                "fixed_subspecies");
        }

        private static void RegisterDictionaryGenerator(string pId, string[] pDictPairs, params string[] pTemplates)
        {
            var generator = AssetManager.name_generator.has(pId)
                ? AssetManager.name_generator.get(pId)
                : AssetManager.name_generator.add(new NameGeneratorAsset { id = pId });

            generator.use_dictionary = true;
            generator.onomastics_templates = new List<string>();
            generator.dict_parts = new Dictionary<string, string>();
            generator.templates = new List<string[]>();
            generator.vowels = NameGeneratorAsset.vowels_all;
            generator.consonants = NameGeneratorAsset.consonants_all;
            generator.onomastics_templates = new List<string>();
            generator.addOnomastic(SINO_ONOMASTICS);

            for (int i = 0; i + 1 < pDictPairs.Length; i += 2)
            {
                generator.addDictPart(pDictPairs[i], pDictPairs[i + 1]);
            }

            foreach (string template in pTemplates)
            {
                generator.addTemplate(template);
            }
        }
    }
}
