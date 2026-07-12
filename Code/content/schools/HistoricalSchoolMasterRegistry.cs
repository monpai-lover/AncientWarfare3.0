using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.content.schools
{
    public static class HistoricalSchoolMasterRegistry
    {
        private static readonly HistoricalSchoolMasterDefinition[] Definitions = Build();
        private static readonly IReadOnlyList<HistoricalSchoolMasterDefinition> ReadOnlyDefinitions =
            Array.AsReadOnly(Definitions);
        private static readonly Dictionary<string, HistoricalSchoolMasterDefinition> ById =
            Definitions.ToDictionary(p => p.Id, StringComparer.Ordinal);

        public static IReadOnlyList<HistoricalSchoolMasterDefinition> All => ReadOnlyDefinitions;

        public static HistoricalSchoolMasterDefinition Find(string pId)
        {
            return !string.IsNullOrEmpty(pId) && ById.TryGetValue(pId,
                out HistoricalSchoolMasterDefinition value) ? value : null;
        }

        public static string[] Validate()
        {
            var errors = new List<string>();
            if (Definitions.Length != 84) errors.Add("expected 84 historical school masters");
            foreach (IGrouping<string, HistoricalSchoolMasterDefinition> group in
                     Definitions.GroupBy(p => p.SchoolId, StringComparer.Ordinal))
                if (group.Count() != 6) errors.Add("expected six masters for " + group.Key);
            if (Definitions.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() !=
                Definitions.Length) errors.Add("duplicate historical master ID");
            if (Definitions.Select(p => p.CanonicalName).Distinct(StringComparer.Ordinal).Count() !=
                Definitions.Length) errors.Add("duplicate canonical historical name");
            return errors.ToArray();
        }

        private static HistoricalSchoolMasterDefinition[] Build()
        {
            var result = new List<HistoricalSchoolMasterDefinition>(84);
            AddSchool(result, CourtSchoolId.Ru, A(82, 88, 45, 94),
                V("鲁", "邹", "齐", "汉"),
                V(HistoricalDebateTopicId.Livelihood, HistoricalDebateTopicId.Peace,
                    HistoricalDebateTopicId.Order, HistoricalDebateTopicId.Institutions),
                "aw_school_institution_academy",
                S("孔丘", 40, W("春秋", "论语"), "孔子", "仲尼"),
                S("曾参", 38, W("曾子", "大学"), "曾子", "子舆"),
                S("孔伋", 36, W("中庸", "子思子"), "子思"),
                S("孟轲", 40, W("孟子"), "孟子", "子舆"),
                S("荀况", 42, W("荀子"), "荀子", "孙卿"),
                S("董仲舒", 45, W("春秋繁露", "天人三策")));

            AddSchool(result, CourtSchoolId.Mohist, A(86, 72, 70, 92),
                V("宋", "鲁", "楚", "秦"),
                V(HistoricalDebateTopicId.Livelihood, HistoricalDebateTopicId.Defense,
                    HistoricalDebateTopicId.Peace, HistoricalDebateTopicId.Technology),
                "aw_school_institution_mohist_lodge",
                S("墨翟", 42, W("墨子", "兼爱"), "墨子"),
                S("禽滑釐", 38, W("墨子·备城门"), "禽滑厘", "禽滑离"),
                S("孟胜", 40, W("墨守遗说")),
                S("相里勤", 38, W("墨经"), "相夫勤"),
                S("邓陵子", 36, W("墨辩"), "邓陵氏"),
                S("田鸠", 40, W("墨家遗说"), "田俅"));

            AddSchool(result, CourtSchoolId.Dao, A(68, 80, 32, 97),
                V("周", "郑", "宋", "楚", "汉"),
                V(HistoricalDebateTopicId.Peace, HistoricalDebateTopicId.Livelihood,
                    HistoricalDebateTopicId.Medicine, HistoricalDebateTopicId.Institutions),
                "aw_school_institution_dao_hall",
                S("李耳", 50, W("道德经"), "老子", "老聃"),
                S("列御寇", 42, W("列子"), "列子"),
                S("杨朱", 38, W("列子·杨朱"), "杨子"),
                S("庄周", 40, W("庄子"), "庄子", "南华真人"),
                S("辛钘", 42, W("文子"), "文子", "计然"),
                S("河上公", 55, W("老子河上公章句")));

            AddSchool(result, CourtSchoolId.Legalist, A(92, 76, 78, 90),
                V("魏", "秦", "韩", "赵"),
                V(HistoricalDebateTopicId.Order, HistoricalDebateTopicId.Aggression,
                    HistoricalDebateTopicId.Technology, HistoricalDebateTopicId.War),
                "aw_school_institution_legal_academy",
                S("李悝", 42, W("法经"), "李克"),
                S("公孙鞅", 38, W("商君书"), "商鞅", "卫鞅"),
                S("申不害", 40, W("申子")),
                S("慎到", 42, W("慎子")),
                S("韩非", 38, W("韩非子"), "韩非子"),
                S("李斯", 40, W("谏逐客书", "仓颉篇")));

            AddSchool(result, CourtSchoolId.Military, A(78, 68, 99, 90),
                V("齐", "吴", "鲁", "魏", "秦"),
                V(HistoricalDebateTopicId.War, HistoricalDebateTopicId.Defense,
                    HistoricalDebateTopicId.Aggression, HistoricalDebateTopicId.Order),
                "aw_school_institution_military_academy",
                S("孙武", 40, W("孙子兵法"), "长卿"),
                S("田穰苴", 42, W("司马法"), "司马穰苴"),
                S("吴起", 38, W("吴子")),
                S("孙膑", 38, W("孙膑兵法")),
                S("尉缭", 40, W("尉缭子"), "尉缭子"),
                S("白起", 42, W("阵战遗说"), "公孙起"));

            AddSchool(result, CourtSchoolId.Diplomat, A(72, 100, 55, 92),
                V("魏", "齐", "秦", "赵", "燕"),
                V(HistoricalDebateTopicId.Peace, HistoricalDebateTopicId.Diplomacy,
                    HistoricalDebateTopicId.Aggression, HistoricalDebateTopicId.Commerce),
                "aw_school_institution_diplomat_school",
                S("王诩", 48, W("鬼谷子"), "鬼谷子", "王禅"),
                S("苏秦", 36, W("苏子"), "季子"),
                S("张仪", 38, W("张子")),
                S("公孙衍", 40, W("合纵遗说"), "犀首"),
                S("范雎", 42, W("远交近攻策"), "张禄"),
                S("鲁仲连", 45, W("鲁仲连子"), "鲁连"));

            AddSchool(result, CourtSchoolId.Agrarian, A(98, 64, 42, 84),
                V("滕", "魏", "汉", "北魏", "元"),
                V(HistoricalDebateTopicId.Livelihood, HistoricalDebateTopicId.Famine,
                    HistoricalDebateTopicId.Commerce, HistoricalDebateTopicId.Technology),
                "aw_school_institution_agrarian_school",
                S("许行", 40, W("农家许行之说")),
                S("陈相", 36, W("神农之言")),
                S("陈辛", 35, W("农家遗说")),
                S("氾胜之", 42, W("氾胜之书"), "泛胜之"),
                S("贾思勰", 45, W("齐民要术")),
                S("王祯", 45, W("农书")));

            AddSchool(result, CourtSchoolId.YinYang, A(72, 78, 52, 98),
                V("齐", "楚", "汉"),
                V(HistoricalDebateTopicId.Technology, HistoricalDebateTopicId.Institutions,
                    HistoricalDebateTopicId.Medicine, HistoricalDebateTopicId.Epidemic),
                "aw_school_institution_observatory",
                S("邹衍", 42, W("邹子"), "邹子衍"),
                S("邹奭", 40, W("邹奭十二篇"), "邹爽"),
                S("甘德", 42, W("天文星占", "甘石星经")),
                S("石申", 42, W("天文", "甘石星经"), "石申夫"),
                S("唐昧", 38, W("天星遗占")),
                S("落下闳", 45, W("太初历")));

            AddSchool(result, CourtSchoolId.Logician, A(74, 97, 40, 99),
                V("郑", "齐", "宋", "赵", "魏"),
                V(HistoricalDebateTopicId.Order, HistoricalDebateTopicId.Diplomacy,
                    HistoricalDebateTopicId.Institutions, HistoricalDebateTopicId.Technology),
                "aw_school_institution_names_school",
                S("邓析", 38, W("邓析子")),
                S("尹文", 40, W("尹文子")),
                S("惠施", 40, W("惠施十事"), "惠子"),
                S("公孙龙", 38, W("公孙龙子")),
                S("宋钘", 42, W("宋子"), "宋研", "宋牼"),
                S("桓团", 40, W("桓团之辩")));

            AddSchool(result, CourtSchoolId.Medical, A(90, 80, 30, 99),
                V("齐", "楚", "汉", "晋"),
                V(HistoricalDebateTopicId.Medicine, HistoricalDebateTopicId.Epidemic,
                    HistoricalDebateTopicId.Livelihood, HistoricalDebateTopicId.Technology),
                "aw_school_institution_clinic",
                S("秦越人", 42, W("难经"), "扁鹊"),
                S("文挚", 40, W("文挚医案")),
                S("淳于意", 45, W("诊籍"), "仓公"),
                S("张机", 45, W("伤寒杂病论"), "张仲景"),
                S("华佗", 48, W("青囊书"), "华元化"),
                S("葛洪", 45, W("肘后备急方", "抱朴子"), "葛稚川"));

            AddSchool(result, CourtSchoolId.Syncretist, A(86, 90, 68, 96),
                V("秦", "楚", "汉"),
                V(HistoricalDebateTopicId.Livelihood, HistoricalDebateTopicId.War,
                    HistoricalDebateTopicId.Peace, HistoricalDebateTopicId.Order,
                    HistoricalDebateTopicId.Commerce, HistoricalDebateTopicId.Technology),
                "aw_school_institution_syncretic_academy",
                S("尸佼", 42, W("尸子"), "尸子"),
                S("吕不韦", 45, W("吕氏春秋"), "文信侯"),
                S("刘安", 45, W("淮南子"), "淮南王"),
                S("伍被", 40, W("伍被谏说")),
                S("苏飞", 40, W("淮南遗说")),
                S("东方朔", 42, W("答客难", "非有先生论"), "曼倩"));

            AddSchool(result, CourtSchoolId.Merchant, A(94, 92, 48, 87),
                V("越", "周", "魏", "秦", "汉"),
                V(HistoricalDebateTopicId.Commerce, HistoricalDebateTopicId.Livelihood,
                    HistoricalDebateTopicId.Diplomacy, HistoricalDebateTopicId.Institutions),
                "aw_school_institution_merchant_guild",
                S("范蠡", 42, W("计然策"), "陶朱公", "鸱夷子皮"),
                S("白圭", 40, W("白圭经商法")),
                S("猗顿", 40, W("猗顿商法")),
                S("乌氏倮", 42, W("乌氏牧殖法"), "乌氏裸"),
                S("卓王孙", 45, W("卓氏冶铁法")),
                S("桑弘羊", 42, W("盐铁论策")));

            AddSchool(result, CourtSchoolId.Craftsman, A(92, 66, 62, 97),
                V("鲁", "越", "秦", "赵", "汉"),
                V(HistoricalDebateTopicId.Technology, HistoricalDebateTopicId.Defense,
                    HistoricalDebateTopicId.Institutions, HistoricalDebateTopicId.Commerce),
                "aw_school_institution_craft_workshop",
                S("公输班", 40, W("公输子"), "鲁班", "公输般"),
                S("欧冶子", 42, W("铸剑遗法")),
                S("干将", 38, W("干将铸剑法")),
                S("李冰", 42, W("都江堰治水法")),
                S("郑国", 40, W("郑国渠法")),
                S("丁缓", 40, W("机巧遗法")));

            AddSchool(result, CourtSchoolId.Historian, A(84, 90, 42, 99),
                V("鲁", "汉", "晋"),
                V(HistoricalDebateTopicId.Order, HistoricalDebateTopicId.Diplomacy,
                    HistoricalDebateTopicId.Institutions, HistoricalDebateTopicId.War),
                "aw_school_institution_archive",
                S("左丘明", 45, W("左传", "国语"), "左丘"),
                S("司马谈", 45, W("论六家要旨")),
                S("司马迁", 42, W("史记"), "太史公"),
                S("刘向", 45, W("战国策", "说苑"), "刘更生"),
                S("班固", 42, W("汉书"), "孟坚"),
                S("荀悦", 42, W("汉纪", "申鉴"), "仲豫"));

            return result.ToArray();
        }

        private static void AddSchool(List<HistoricalSchoolMasterDefinition> pResult,
            string pSchoolId, HistoricalSchoolAbilityProfile pAbilities, string[] pStates,
            string[] pTopics, string pInstitutionId, params MasterSeed[] pRoster)
        {
            for (int i = 0; i < pRoster.Length; i++)
            {
                MasterSeed seed = pRoster[i];
                int order = i + 1;
                pResult.Add(new HistoricalSchoolMasterDefinition(pResult.Count,
                    "aw_master_" + pSchoolId + "_" + order.ToString("00"), seed.Name,
                    seed.Aliases, pSchoolId, order, WaveForOrder(order), pStates, seed.Age,
                    pIsMale: true, pAbilities, pTopics, seed.Works, pInstitutionId));
            }
        }

        private static int WaveForOrder(int pOrder)
        {
            if (pOrder == 1) return 1;
            if (pOrder == 2) return 2;
            if (pOrder == 3 || pOrder == 4) return 3;
            if (pOrder == 5) return 4;
            return pOrder == 6 ? 5 : 0;
        }

        private static HistoricalSchoolAbilityProfile A(float pStewardship, float pDiplomacy,
            float pWarfare, float pIntelligence)
        {
            return new HistoricalSchoolAbilityProfile(pStewardship, pDiplomacy, pWarfare,
                pIntelligence);
        }

        private static string[] V(params string[] pValues) => pValues;
        private static string[] W(params string[] pValues) => pValues;

        private static MasterSeed S(string pName, int pAge, string[] pWorks,
            params string[] pAliases)
        {
            return new MasterSeed(pName, pAge, pWorks, pAliases);
        }

        private sealed class MasterSeed
        {
            public MasterSeed(string pName, int pAge, string[] pWorks, string[] pAliases)
            {
                Name = pName;
                Age = pAge;
                Works = pWorks ?? Array.Empty<string>();
                Aliases = pAliases ?? Array.Empty<string>();
            }

            public string Name { get; }
            public int Age { get; }
            public string[] Works { get; }
            public string[] Aliases { get; }
        }
    }
}
