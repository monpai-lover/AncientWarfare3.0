using System;
using System.Collections.Generic;

namespace AncientWarfare3.content.schools
{
    public readonly struct HistoricalMasterCanonicalIdentity
    {
        public HistoricalMasterCanonicalIdentity(string pCanonicalName, string pShiName,
            string pGivenName, string pFamilyName = null)
        {
            CanonicalName = pCanonicalName ?? "";
            ShiName = pShiName ?? "";
            GivenName = pGivenName ?? "";
            FamilyName = string.IsNullOrWhiteSpace(pFamilyName)
                ? ShiName
                : pFamilyName;
        }

        public string CanonicalName { get; }
        public string ShiName { get; }
        public string GivenName { get; }
        public string FamilyName { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(CanonicalName) &&
                               !string.IsNullOrWhiteSpace(ShiName) &&
                               !string.IsNullOrWhiteSpace(GivenName) &&
                               !string.IsNullOrWhiteSpace(FamilyName) &&
                               CanonicalName == ShiName + GivenName;
    }

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
            Add(result, "孔丘", "孔", "丘");
            Add(result, "曾参", "曾", "参");
            Add(result, "孔伋", "孔", "伋");
            Add(result, "孟轲", "孟", "轲");
            Add(result, "荀况", "荀", "况");
            Add(result, "董仲舒", "董", "仲舒");

            Add(result, "墨翟", "墨", "翟");
            Add(result, "禽滑釐", "禽", "滑釐");
            Add(result, "孟胜", "孟", "胜");
            Add(result, "相里勤", "相里", "勤");
            Add(result, "邓陵子", "邓陵", "子");
            Add(result, "田鸠", "田", "鸠");

            Add(result, "李耳", "李", "耳");
            Add(result, "列御寇", "列", "御寇");
            Add(result, "杨朱", "杨", "朱");
            Add(result, "庄周", "庄", "周");
            Add(result, "辛钘", "辛", "钘");
            Add(result, "河上公", "河上", "公");

            Add(result, "李悝", "李", "悝");
            Add(result, "公孙鞅", "公孙", "鞅");
            Add(result, "申不害", "申", "不害");
            Add(result, "慎到", "慎", "到");
            Add(result, "韩非", "韩", "非");
            Add(result, "李斯", "李", "斯");

            Add(result, "孙武", "孙", "武");
            Add(result, "田穰苴", "田", "穰苴");
            Add(result, "吴起", "吴", "起");
            Add(result, "孙膑", "孙", "膑");
            Add(result, "尉缭", "尉", "缭");
            Add(result, "白起", "白", "起");

            Add(result, "王诩", "王", "诩");
            Add(result, "苏秦", "苏", "秦");
            Add(result, "张仪", "张", "仪");
            Add(result, "公孙衍", "公孙", "衍");
            Add(result, "范雎", "范", "雎");
            Add(result, "鲁仲连", "鲁仲", "连");

            Add(result, "许行", "许", "行");
            Add(result, "陈相", "陈", "相");
            Add(result, "陈辛", "陈", "辛");
            Add(result, "氾胜之", "氾", "胜之");
            Add(result, "贾思勰", "贾", "思勰");
            Add(result, "王祯", "王", "祯");

            Add(result, "邹衍", "邹", "衍");
            Add(result, "邹奭", "邹", "奭");
            Add(result, "甘德", "甘", "德");
            Add(result, "石申", "石", "申");
            Add(result, "唐昧", "唐", "昧");
            Add(result, "落下闳", "落下", "闳");

            Add(result, "邓析", "邓", "析");
            Add(result, "尹文", "尹", "文");
            Add(result, "惠施", "惠", "施");
            Add(result, "公孙龙", "公孙", "龙");
            Add(result, "宋钘", "宋", "钘");
            Add(result, "桓团", "桓", "团");

            Add(result, "秦越人", "秦", "越人");
            Add(result, "文挚", "文", "挚");
            Add(result, "淳于意", "淳于", "意");
            Add(result, "张机", "张", "机");
            Add(result, "华佗", "华", "佗");
            Add(result, "葛洪", "葛", "洪");

            Add(result, "尸佼", "尸", "佼");
            Add(result, "吕不韦", "吕", "不韦");
            Add(result, "刘安", "刘", "安");
            Add(result, "伍被", "伍", "被");
            Add(result, "苏飞", "苏", "飞");
            Add(result, "东方朔", "东方", "朔");

            Add(result, "范蠡", "范", "蠡");
            Add(result, "白圭", "白", "圭");
            Add(result, "猗顿", "猗", "顿");
            Add(result, "乌氏倮", "乌氏", "倮");
            Add(result, "卓王孙", "卓", "王孙");
            Add(result, "桑弘羊", "桑", "弘羊");

            Add(result, "公输班", "公输", "班");
            Add(result, "欧冶子", "欧冶", "子");
            Add(result, "干将", "干", "将");
            Add(result, "李冰", "李", "冰");
            Add(result, "郑国", "郑", "国");
            Add(result, "丁缓", "丁", "缓");

            Add(result, "左丘明", "左丘", "明");
            Add(result, "司马谈", "司马", "谈");
            Add(result, "司马迁", "司马", "迁");
            Add(result, "刘向", "刘", "向");
            Add(result, "班固", "班", "固");
            Add(result, "荀悦", "荀", "悦");
            return result;
        }

        private static void Add(
            IDictionary<string, HistoricalMasterCanonicalIdentity> pResult,
            string pCanonicalName, string pShiName, string pGivenName)
        {
            var identity = new HistoricalMasterCanonicalIdentity(pCanonicalName,
                pShiName, pGivenName, pShiName);
            if (!identity.IsValid || pResult.ContainsKey(pCanonicalName))
                throw new InvalidOperationException("invalid historical master identity " +
                                                    pCanonicalName);
            pResult.Add(pCanonicalName, identity);
        }
    }
}
