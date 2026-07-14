using System;
using System.Collections.Generic;

namespace AncientWarfare3.content.schools
{
    public static class HistoricalMasterIdentityRules
    {
        private static readonly Dictionary<string, HistoricalMasterCanonicalIdentity>
            ByCanonicalName = Build();

        public static int Count => ByCanonicalName.Count;

        public static HistoricalMasterCanonicalIdentity Resolve(string pCanonicalName)
        {
            return !string.IsNullOrWhiteSpace(pCanonicalName) &&
                   ByCanonicalName.TryGetValue(pCanonicalName,
                       out HistoricalMasterCanonicalIdentity identity)
                ? identity
                : default;
        }

        public static string NormalizeShiName(string pShiName)
        {
            string value = (pShiName ?? "").Trim();
            while (value.EndsWith("氏", StringComparison.Ordinal) && value.Length > 0)
                value = value.Substring(0, value.Length - 1).Trim();
            return value;
        }

        public static string EnsureSingleShiSuffix(string pShiName)
        {
            string value = NormalizeShiName(pShiName);
            return string.IsNullOrEmpty(value) ? "" : value + "氏";
        }

        private static Dictionary<string, HistoricalMasterCanonicalIdentity> Build()
        {
            var result = new Dictionary<string, HistoricalMasterCanonicalIdentity>(
                StringComparer.Ordinal);
            AddDistinct(result, "孔丘", "孔", "丘", "子");
            AddDistinct(result, "曾参", "曾", "参", "姒");
            AddDistinct(result, "孔伋", "孔", "伋", "子");
            AddDistinct(result, "孟轲", "孟", "轲", "姬");
            AddUnknown(result, "荀况", "荀", "况");
            AddSame(result, "董仲舒", "董", "仲舒");

            AddUnknown(result, "墨翟", "墨", "翟");
            AddUnknown(result, "禽滑釐", "禽", "滑釐", pMilitaryEligible: true);
            AddUnknown(result, "孟胜", "孟", "胜", pMilitaryEligible: true);
            AddUnknown(result, "相里勤", "相里", "勤");
            AddUnknown(result, "邓陵子", "邓陵", "子");
            AddUnknown(result, "田鸠", "田", "鸠");

            AddSame(result, "李耳", "李", "耳");
            AddUnknown(result, "列御寇", "列", "御寇");
            AddUnknown(result, "杨朱", "杨", "朱");
            AddUnknown(result, "庄周", "庄", "周");
            AddUnknown(result, "辛钘", "辛", "钘");
            AddUnknown(result, "河上公", "河上", "公");

            AddUnknown(result, "李悝", "李", "悝");
            AddDistinct(result, "公孙鞅", "公孙", "鞅", "姬", pMilitaryEligible: true);
            AddUnknown(result, "申不害", "申", "不害");
            AddUnknown(result, "慎到", "慎", "到");
            AddDistinct(result, "韩非", "韩", "非", "姬");
            AddUnknown(result, "李斯", "李", "斯");

            AddDistinct(result, "孙武", "孙", "武", "妫", pMilitaryEligible: true);
            AddDistinct(result, "田穰苴", "田", "穰苴", "妫", pMilitaryEligible: true);
            AddUnknown(result, "吴起", "吴", "起", pMilitaryEligible: true);
            AddDistinct(result, "孙膑", "孙", "膑", "妫", pMilitaryEligible: true);
            AddUnknown(result, "尉缭", "尉", "缭", pMilitaryEligible: true);
            AddUnknown(result, "白起", "白", "起", pMilitaryEligible: true);

            AddUnknown(result, "王诩", "王", "诩");
            AddUnknown(result, "苏秦", "苏", "秦");
            AddUnknown(result, "张仪", "张", "仪");
            AddUnknown(result, "公孙衍", "公孙", "衍", pMilitaryEligible: true);
            AddUnknown(result, "范雎", "范", "雎");
            AddUnknown(result, "鲁仲连", "鲁仲", "连");

            AddUnknown(result, "许行", "许", "行");
            AddUnknown(result, "陈相", "陈", "相");
            AddUnknown(result, "陈辛", "陈", "辛");
            AddSame(result, "氾胜之", "氾", "胜之");
            AddSame(result, "贾思勰", "贾", "思勰");
            AddSame(result, "王祯", "王", "祯");

            AddUnknown(result, "邹衍", "邹", "衍");
            AddUnknown(result, "邹奭", "邹", "奭");
            AddUnknown(result, "甘德", "甘", "德");
            AddUnknown(result, "石申", "石", "申");
            AddUnknown(result, "唐昧", "唐", "昧");
            AddSame(result, "落下闳", "落下", "闳");

            AddUnknown(result, "邓析", "邓", "析");
            AddUnknown(result, "尹文", "尹", "文");
            AddUnknown(result, "惠施", "惠", "施");
            AddUnknown(result, "公孙龙", "公孙", "龙");
            AddUnknown(result, "宋钘", "宋", "钘");
            AddUnknown(result, "桓团", "桓", "团");

            AddDistinct(result, "秦越人", "秦", "越人", "姬");
            AddUnknown(result, "文挚", "文", "挚");
            AddSame(result, "淳于意", "淳于", "意");
            AddSame(result, "张机", "张", "机");
            AddSame(result, "华佗", "华", "佗");
            AddSame(result, "葛洪", "葛", "洪");

            AddUnknown(result, "尸佼", "尸", "佼");
            AddDistinct(result, "吕不韦", "吕", "不韦", "姜");
            AddSame(result, "刘安", "刘", "安");
            AddSame(result, "伍被", "伍", "被");
            AddSame(result, "苏飞", "苏", "飞");
            AddSame(result, "东方朔", "东方", "朔");

            AddUnknown(result, "范蠡", "范", "蠡", pMilitaryEligible: true);
            AddUnknown(result, "白圭", "白", "圭");
            AddUnknown(result, "猗顿", "猗", "顿");
            AddUnknown(result, "乌氏倮", "乌氏", "倮");
            AddSame(result, "卓王孙", "卓", "王孙");
            AddSame(result, "桑弘羊", "桑", "弘羊");

            AddDistinct(result, "公输班", "公输", "班", "姬");
            AddUnknown(result, "欧冶子", "欧冶", "子");
            AddUnknown(result, "干将", "干", "将");
            AddUnknown(result, "李冰", "李", "冰");
            AddUnknown(result, "郑国", "郑", "国");
            AddSame(result, "丁缓", "丁", "缓");

            AddUnknown(result, "左丘明", "左丘", "明");
            AddSame(result, "司马谈", "司马", "谈");
            AddSame(result, "司马迁", "司马", "迁");
            AddSame(result, "刘向", "刘", "向");
            AddSame(result, "班固", "班", "固");
            AddSame(result, "荀悦", "荀", "悦");
            return result;
        }

        private static void AddUnknown(
            IDictionary<string, HistoricalMasterCanonicalIdentity> pResult,
            string pCanonicalName, string pShiName, string pGivenName,
            bool pMilitaryEligible = false)
        {
            Add(pResult, pCanonicalName, pShiName, pGivenName, "",
                HistoricalMasterFamilyEvidence.Unknown, pMilitaryEligible);
        }

        private static void AddDistinct(
            IDictionary<string, HistoricalMasterCanonicalIdentity> pResult,
            string pCanonicalName, string pShiName, string pGivenName,
            string pFamilyName, bool pMilitaryEligible = false)
        {
            Add(pResult, pCanonicalName, pShiName, pGivenName, pFamilyName,
                HistoricalMasterFamilyEvidence.KnownDistinct, pMilitaryEligible);
        }

        private static void AddSame(
            IDictionary<string, HistoricalMasterCanonicalIdentity> pResult,
            string pCanonicalName, string pShiName, string pGivenName,
            bool pMilitaryEligible = false)
        {
            Add(pResult, pCanonicalName, pShiName, pGivenName, pShiName,
                HistoricalMasterFamilyEvidence.KnownSame, pMilitaryEligible);
        }

        private static void Add(
            IDictionary<string, HistoricalMasterCanonicalIdentity> pResult,
            string pCanonicalName, string pShiName, string pGivenName,
            string pFamilyName, HistoricalMasterFamilyEvidence pFamilyEvidence,
            bool pMilitaryEligible)
        {
            var identity = new HistoricalMasterCanonicalIdentity(pCanonicalName,
                pShiName, pGivenName, pFamilyName, pFamilyEvidence, pMilitaryEligible);
            if (!identity.IsValid || pResult.ContainsKey(pCanonicalName))
                throw new InvalidOperationException("invalid historical master identity " +
                                                    pCanonicalName);
            pResult.Add(pCanonicalName, identity);
        }
    }
}
