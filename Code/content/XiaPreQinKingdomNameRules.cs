using System;
using System.Collections.Generic;

namespace AncientWarfare3.content
{
    public static class XiaPreQinKingdomNameRules
    {
        private static readonly string[] Names =
        {
            "齐", "鲁", "晋", "燕", "卫", "宋", "郑", "陈", "蔡", "曹", "滕", "杞", "许", "邢",
            "吴", "越", "秦", "楚", "韩", "赵", "魏", "中山", "代", "梁", "唐", "南燕",
            "管", "霍", "郕", "郜", "毛", "毕", "邘", "应", "蒋", "芮", "沈", "单", "召", "刘",
            "荣", "甘", "樊", "祭", "温", "滑", "苏", "原", "詹", "酆", "聃", "密", "杜", "霸",
            "虢", "东虢", "西虢", "南虢", "北虢", "虞", "贾", "荀", "耿", "冀", "井", "缙", "杨",
            "凡", "共", "巩", "邾", "邹", "小邾", "郳", "莒", "纪", "莱", "谭", "遂", "鄅", "郯",
            "鄫", "任", "宿", "须句", "颛臾", "根牟", "牟", "介", "鄣", "蒲姑", "奄", "薛", "葛",
            "戴", "萧", "徐", "舒", "舒鸠", "舒蓼", "舒庸", "舒龙", "舒鲍", "钟离", "钟吾", "六",
            "英", "黄", "江", "弦", "息", "道", "房", "顿", "胡", "项", "申", "吕", "谢", "鄀",
            "鄾", "鄂", "随", "曾", "罗", "邓", "绞", "权", "庸", "麇", "夔", "郧", "贰", "轸",
            "巴", "蜀", "苴", "鱼", "彭", "巢", "桐", "柏", "赖", "黎", "州", "淳于", "莘", "焦",
            "茅", "费", "郇", "胙", "郐", "鄢", "阳", "章", "程", "习", "邿", "鄟", "蓼", "厉",
            "肥", "鼓", "潞", "蓟", "鲜虞", "仇由", "孤竹", "令支", "无终", "义渠", "大荔", "犬戎",
            "山戎", "骊戎", "姜戎", "陆浑", "白狄", "赤狄", "林胡", "楼烦", "东胡", "戎蛮",
            "曲沃", "安陵", "东周", "西周", "有穷", "甲父", "祝其", "微", "丰", "瑕", "观", "骀",
            "箕", "蓐", "向", "谷", "祝", "聂", "叶"
        };

        private static readonly HashSet<string> KnownNames =
            new HashSet<string>(Names, StringComparer.Ordinal);

        public static string Csv { get; } = string.Join(",", Names);

        public static string[] All()
        {
            return (string[])Names.Clone();
        }

        public static bool IsKnown(string pName)
        {
            return !string.IsNullOrEmpty(pName) && KnownNames.Contains(pName);
        }

        public static string Pick(long pSeed)
        {
            if (Names.Length == 0) return "";
            var random = new Random(unchecked((int)(pSeed * 1103515245L + 12345L)));
            return Names[random.Next(Names.Length)];
        }
    }
}
