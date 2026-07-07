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
                    "real", "\u6D1B\u9091,\u9550\u4EAC,\u54B8\u9633,\u4E34\u6DC4,\u90AF\u90F8,\u84DF\u57CE,\u5B9B\u57CE,\u9648\u90FD,\u66F2\u961C,\u59D1\u82CF,\u4F1A\u7A3D,\u664B\u9633,\u5B89\u9091,\u8F6E\u53F0,\u5C09\u7281,\u53F6\u57CE,\u7C73\u5170,\u5174\u5E86,\u9E23\u6C99,\u51C9\u5DDE,\u5BBF\u5DDE,\u8083\u5DDE,\u6CB3\u4E1C,\u798F\u6E05,\u6F33\u6D66,\u9F99\u6EAA,\u9F99\u5CA9,\u6C38\u798F,\u53E4\u7530,\u95FD\u6E05,\u5B81\u5FB7,\u8FDE\u6C5F,\u4F59\u59DA,\u8BF8\u66A8,\u664B\u9675,\u5609\u5174,\u5434,\u534E\u4EAD,\u94B1\u5858,\u5510\u5C71,\u5317\u6D77,\u4E34\u6710,\u5BFF\u5149,\u535A\u660C,\u6DC4\u5DDD,\u957F\u5C71,\u90B9\u5E73,\u6C82\u6C34,\u65B0\u6CF0,\u8D39,\u4E34\u6C82,\u83B1\u829C,\u4E7E\u5C01,\u7455\u4E18,\u5DE8\u91CE,\u987B\u660C,\u5BFF\u5F20,\u9104\u57CE,\u6FEE\u9633,\u5357\u6D77,\u5316\u8499,\u6D48\u9633,\u6D5B\u6D2D,\u5F00\u5EFA,\u6D77\u5B89,\u6D77\u9675,\u6C49\u9633,\u5929\u957F,\u671B\u6C5F,\u6C5F\u590F,\u6B66\u660C,\u5510\u5E74,\u65B0\u6DE6,\u592A\u548C,\u6C38\u65B0,\u5E90\u9675,\u4E34\u5DDD,\u5D07\u4EC1,\u5357\u57CE,\u5357\u4E30,\u4E0B\u90B3,\u4E34\u6DEE,\u7B26\u79BB,\u8572,\u8679,\u7389\u7530,\u4E09\u6CB3,\u6F5E,\u6B66\u6E05,\u84DF,\u90EA,\u6C38\u6CF0,\u98DE\u4E4C,\u5B89\u5CB3,\u76D8\u77F3,\u5185\u6C5F,\u9759\u5357,\u6C38\u5DDD,\u5DEB\u5C71,\u5949\u8282,\u4E91\u5B89,\u897F\u4E61,\u5174\u9053,\u96BE\u6C5F,\u5609\u5DDD,\u80E4\u5C71,\u666F\u8C37,\u957F\u793E,\u9633\u7FDF,\u6276\u6C9F,\u821E\u9633,\u5F00\u5C01,\u5C01\u4E18,\u96CD\u4E18,\u9648\u7559,\u52A1\u5DDD,\u601D\u909B,\u5B81\u5937,\u9F99\u6807,\u90CE\u6EAA,\u6F6D\u9633,\u7EE5\u9633,\u6D0B\u5DDD,\u4E49\u6CC9,\u6DAA\u9675,\u6B66\u9F99,\u4E50\u6E29,\u77F3\u9996,\u5F53\u9633,\u516C\u5B89,\u6148\u5229,\u6FA7\u9633,\u76D1\u5229,\u6C94\u9633,\u7ADF\u9675,\u7A70,\u5357\u9633,\u83CA\u6F6D,\u8944\u9633,\u4E50\u4E61,\u8C37\u57CE,\u4E91\u4E2D,\u5174\u5510,\u5408\u6CB3,\u5B9C\u82B3,\u6613,\u6EE1\u57CE,\u6000\u5FB7,\u5F52\u4EC1,\u5B89\u5316,\u540C\u5DDD,\u4FDD\u5B9A,\u4E34\u6CFE,\u7075\u53F0,\u6D1B\u4EA4,\u6D1B\u5DDD,\u76F4\u7F57,\u80A4\u65BD,\u5EF6\u5DDD,\u6577\u653F,\u5EF6\u660C,\u534E\u539F,\u4E91\u9633,\u957F\u5B89,\u9120,\u84DD\u7530,\u680E\u9633,\u597D\u7564,\u664B\u57CE,\u6C81\u6C34,\u9675\u5DDD,\u8FBD\u5C71,\u548C\u987A,\u6986\u793E,\u5B89\u9633,\u6797\u8651,\u537F\u5DDE,\u74A7\u5C71,\u6C5F\u6D25,\u6C49\u6E90,\u901A\u671B,\u6210\u90FD,\u7075\u6C60,\u53CC\u6D41,\u5CE8\u77F3,\u666E\u5B81,\u5C91\u6EAA,\u9633\u5BFF,\u6B66\u4ED9,\u5BFF\u6625,\u970D\u4E18,\u76DB\u5510,\u56FA\u59CB,\u6BB7\u57CE,\u5149\u5C71,\u5B9A\u57CE,\u6881\u6CC9,\u6CB3\u6C60,\u9F99\u5E73,\u9A6C\u6C5F,\u667A\u5DDE,\u5CE8\u5DDE,\u4E1C\u839E,\u65B0\u4F1A,\u4E49\u5B81,\u5802\u9633,\u961C\u57CE,\u9ED1\u6C34,\u6606\u5C71,\u4E49\u5174,\u957F\u6EAA,\u4F59\u676D,\u4E8E\u6F5C,\u5185\u9EC4,\u5BBF\u8FC1,\u58D5\u955C\u6FB3,\u9EC4\u5B50\u56F4,\u84B2\u573B,\u5DF4\u9675,\u660C\u6C5F,\u6C38\u5174,\u95FD,\u5B89\u798F,\u5C01\u5C71,\u8521\u9F99,\u589E\u57CE,\u56DB\u4F1A,\u6000\u96C6,\u6D0A\u6C34,\u9ED4\u6C5F,\u677E\u5179,\u5E7F\u660C,\u5FFB\u57CE,\u91D1\u6EAA,\u5B9C\u9EC4,\u7EE5\u5B81,\u901A\u5BC6,\u4E09\u90A6,\u5DF4\u5170,\u5E73\u4FAF,\u5F6D\u57CE,\u6D1B\u9633,\u9752\u5DDE,\u767B\u5DDE,\u5156\u5DDE,\u5F90\u5DDE,\u6DEE\u9633,\u6CD7\u5DDE,\u90D3\u5DDE,\u6FEE\u5DDE,\u76F8\u5DDE,\u771F\u5B9A,\u5180\u5DDE,\u6613\u5DDE,\u6CB3\u5185,\u5C9A\u5DDE,\u6CFD\u5DDE,\u851A\u5DDE,\u5E9C\u5DDE,\u84DF\u5DDE,\u5E7D\u5DDE,\u4EAC\u5146,\u5546\u5DDE,\u911C\u5DDE,\u6CFE\u5DDE,\u9647\u897F,\u51E4\u5DDE,\u5170\u5DDE,\u5EFA\u5EB7,\u9152\u6CC9,\u6566\u714C,\u5BA5\u5DDE,\u6C5F\u5B81,\u5E38\u5DDE,\u82CF\u5DDE,\u676D\u5DDE,\u8D8A\u5DDE,\u6D66\u9633,\u798F\u5DDE,\u6F33\u5DDE,\u626C\u5DDE,\u5BFF\u5DDE,\u5149\u5DDE,\u7B60\u5DDE,\u629A\u5DDE,\u5409\u5DDE,\u9093\u5DDE,\u6C5F\u9675,\u590D\u5DDE,\u9102\u5DDE,\u5CB3\u5DDE,\u957F\u6C99,\u6D0B\u5DDE,\u96C6\u5DDE,\u5229\u5DDE,\u5914\u5DDE,\u6DAA\u5DDE,\u666E\u5DDE,\u8D44\u5DDE,\u660C\u5DDE,\u6E1D\u5DDE,\u601D\u5DDE,\u5937\u5DDE,\u7430\u5DDE,\u5949\u5DDE,\u534F\u5DDE,\u5E7F\u5DDE,\u5BA3\u5316,\u5357\u4EAC,\u897F\u4EAC,\u6CB3\u5357,\u4EAC\u757F,\u5E94\u5929,\u5F52\u5FB7,\u5C71\u4E1C,\u5317\u4EAC,\u5927\u540D,\u9AD8\u9633,\u4E1C\u80DC,\u4E30\u5DDE,\u9655\u897F,\u73AF\u5E86,\u911C\u5EF6,\u897F\u51C9,\u5353\u5570,\u5E9C\u5937,\u897F\u5E73,\u4EAC\u5E08,\u4E2D\u5174,\u5730\u65A4,\u9ED1\u5C71,\u5F25\u5A25,\u7701\u5D6C,\u767D\u9A6C,\u4E34\u5B89,\u6893\u5DDE,\u5317\u5B89\u5DDE,\u5174\u5316,\u5949\u5723\u5DDE,\u5F52\u5316\u5DDE,\u5B9D\u660C\u5DDE,\u4E91\u9700,\u677E\u5C71\u5DDE,\u5E94\u660C,\u5B5D\u5B89",
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
