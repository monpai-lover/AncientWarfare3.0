using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content.schools
{
    /// <summary>
    ///     学派宗师的史载双亲。按 <see cref="HistoricalSchoolMasterDefinition.CanonicalName"/>
    ///     查 —— 不按 Id,因为 Id 是 `aw_master_{school}_{序号}` 这种**位置派生**的串,
    ///     名册一旦增删就会平移。
    ///
    ///     诸子的家世绝大多数不可考(注册表里 50 位连姓都是 Unknown),所以本表极稀疏,
    ///     只收正史/本人自叙里明确记载的。宁缺毋滥。
    ///
    ///     刻意不收司马迁:其父司马谈**本身也在宗师名册里**,给他造一个合成的司马谈
    ///     会和世上可能同时存在的真司马谈重影。
    /// </summary>
    internal static class HistoricalMasterParentage
    {
        private static readonly Dictionary<string, HistoricalAncestorParentage>
            ByCanonicalName = Build();

        internal static bool TryGet(string pCanonicalName,
            out HistoricalAncestorParentage pParentage)
        {
            pParentage = default;
            if (string.IsNullOrEmpty(pCanonicalName)) return false;
            return ByCanonicalName.TryGetValue(pCanonicalName, out pParentage);
        }

        internal static int Count => ByCanonicalName.Count;

        private static Dictionary<string, HistoricalAncestorParentage> Build()
        {
            var table = new Dictionary<string, HistoricalAncestorParentage>();

            // 孔子:父叔梁纥(名纥,字叔梁),母颜徵在。
            P(table, "孔丘", "叔梁纥", "纥", "颜徵在", "颜");
            // 曾子:父曾点(曾皙),亦孔门弟子。
            P(table, "曾参", "曾点", "点");
            // 子思:孔子之孙,父孔鲤(伯鱼)。
            P(table, "孔伋", "孔鲤", "鲤");
            // 刘向:父刘德,阳城侯。
            P(table, "刘向", "刘德", "德");
            // 班固:父班彪,《汉书》前作。
            P(table, "班固", "班彪", "彪");
            // 淮南王刘安:父淮南厉王刘长。
            P(table, "刘安", "刘长", "长");
            // 葛洪《抱朴子·自叙》自记父葛悌。
            P(table, "葛洪", "葛悌", "悌");
            // 荀悦:荀淑之孙,父荀俭。
            P(table, "荀悦", "荀俭", "俭");

            return table;
        }

        private static void P(
            Dictionary<string, HistoricalAncestorParentage> pTable,
            string pCanonicalName, string pFatherName,
            string pFatherGivenName = "", string pMotherName = "",
            string pMotherFamilyName = "")
        {
            pTable[pCanonicalName] = new HistoricalAncestorParentage(
                pFatherName, pFatherGivenName, pMotherName,
                pMotherFamilyName);
        }
    }
}
