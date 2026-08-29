using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content.figures
{
    /// <summary>
    ///     开国君主的史载双亲。与 <see cref="HistoricalFigureDef.All"/> 平行的一张表,
    ///     按 <see cref="HistoricalFigureDef.Id"/> 查 —— 刻意不塞进 def 的行里,那张表
    ///     有 `Count = 91` 与 registry 槽位不可重排的硬约束。
    ///
    ///     **只收确有史载的**。查不到的人物根本不出现在本表里(或只填父不填母),
    ///     绝不用推测或后世传说填充 —— 缺项在 UI 上显示「未详」,家族树上方留空。
    ///     父的姓/氏沿用本人的(同姓同氏),故只给显示名与单名。
    /// </summary>
    internal static class HistoricalFigureParentage
    {
        private static readonly Dictionary<string, HistoricalAncestorParentage>
            ByFigureId = Build();

        internal static bool TryGet(string pFigureId,
            out HistoricalAncestorParentage pParentage)
        {
            pParentage = default;
            if (string.IsNullOrEmpty(pFigureId)) return false;
            return ByFigureId.TryGetValue(pFigureId, out pParentage);
        }

        internal static int Count => ByFigureId.Count;

        private static Dictionary<string, HistoricalAncestorParentage> Build()
        {
            var table = new Dictionary<string, HistoricalAncestorParentage>();

            // ── 先秦 / 秦汉 ──
            P(table, "aw_figure_ji_fa", "姬昌", "昌", "太姒", "姒");
            P(table, "aw_figure_ying_zheng", "嬴异人", "异人", "赵姬", "赵");
            P(table, "aw_figure_liu_bang", "刘煓", "煓");
            P(table, "aw_figure_wang_mang", "王曼", "曼", "渠氏", "渠");
            P(table, "aw_figure_gongsun_shu", "公孙仁", "仁");
            P(table, "aw_figure_liu_xiu", "刘钦", "钦", "樊娴都", "樊");
            P(table, "aw_figure_yuan_shu", "袁逢", "逢");

            // ── 三国两晋 ──
            P(table, "aw_figure_cao_pi", "曹操", "操", "卞氏", "卞");
            P(table, "aw_figure_liu_bei", "刘弘", "弘");
            P(table, "aw_figure_sun_quan", "孙坚", "坚", "吴夫人", "吴");
            P(table, "aw_figure_sima_yan", "司马昭", "昭", "王元姬", "王");
            P(table, "aw_figure_zhang_gui", "张温", "温");
            P(table, "aw_figure_liu_yuan", "刘豹", "豹", "呼延氏", "呼延");
            P(table, "aw_figure_li_xiong", "李特", "特", "罗氏", "罗");
            P(table, "aw_figure_sima_rui", "司马觐", "觐", "夏侯光姬", "夏侯");

            // ── 十六国 ──
            // 石勒本姓非石(受氏于后),父名 周曷朱,故不给单名。
            P(table, "aw_figure_shi_le", "周曷朱", "", "王氏", "王");
            P(table, "aw_figure_murong_huang", "慕容廆", "廆", "段氏", "段");
            P(table, "aw_figure_ran_min", "冉瞻", "瞻");
            P(table, "aw_figure_fu_jian_351", "苻洪", "洪");
            P(table, "aw_figure_murong_hong", "慕容儁", "儁");
            P(table, "aw_figure_murong_chui", "慕容皝", "皝", "兰氏", "兰");
            P(table, "aw_figure_yao_chang", "姚弋仲", "弋仲");
            P(table, "aw_figure_qifu_guoren", "乞伏司繁", "司繁");
            P(table, "aw_figure_lu_guang", "吕婆楼", "婆楼");
            P(table, "aw_figure_tuoba_gui", "拓跋寔", "寔", "贺氏", "贺");
            P(table, "aw_figure_tufa_wugu", "秃发思复鞑", "思复鞑");
            P(table, "aw_figure_murong_de", "慕容皝", "皝", "公孙氏", "公孙");
            P(table, "aw_figure_li_gao", "李昶", "昶");
            P(table, "aw_figure_juqu_mengxun", "沮渠法弘", "法弘");
            // 赫连勃勃父刘卫辰,赫连为其称帝后自改之姓,故父不同姓。
            P(table, "aw_figure_helian_bobo", "刘卫辰", "卫辰");
            P(table, "aw_figure_feng_ba", "冯安", "安");
            P(table, "aw_figure_huan_xuan", "桓温", "温", "马氏", "马");

            // ── 南北朝 ──
            P(table, "aw_figure_liu_yu", "刘翘", "翘", "赵安宗", "赵");
            P(table, "aw_figure_xiao_daocheng", "萧承之", "承之", "陈道止", "陈");
            P(table, "aw_figure_xiao_yan", "萧顺之", "顺之", "张尚柔", "张");
            P(table, "aw_figure_gao_huan", "高树生", "树生", "韩期姬", "韩");
            P(table, "aw_figure_yuwen_tai", "宇文肱", "肱", "王氏", "王");
            P(table, "aw_figure_gao_yang", "高欢", "欢", "娄昭君", "娄");
            P(table, "aw_figure_hou_jing", "侯标", "标");
            P(table, "aw_figure_xiao_cha", "萧统", "统");
            P(table, "aw_figure_yuwen_jue", "宇文泰", "泰", "元胡摩", "元");
            P(table, "aw_figure_chen_baxian", "陈文赞", "文赞", "董氏", "董");

            // ── 隋唐 ──
            P(table, "aw_figure_yang_jian", "杨忠", "忠", "吕苦桃", "吕");
            P(table, "aw_figure_xue_ju", "薛汪", "汪");
            P(table, "aw_figure_xiao_xian", "萧璿", "璿");
            P(table, "aw_figure_li_mi", "李宽", "宽");
            P(table, "aw_figure_yuwen_huaji", "宇文述", "述");
            P(table, "aw_figure_li_yuan", "李昞", "昞", "独孤氏", "独孤");
            P(table, "aw_figure_shen_faxing", "沈恪", "恪");
            P(table, "aw_figure_wu_zhao", "武士彟", "士彟", "杨氏", "杨");
            P(table, "aw_figure_da_zuorong", "乞乞仲象", "仲象");
            P(table, "aw_figure_pi_luoge", "盛逻皮", "逻皮");
            // 安禄山生父不详(继父安延偃),仅母族可考。
            P(table, "aw_figure_an_lushan", "", "", "阿史德氏", "阿史德");
            P(table, "aw_figure_zhu_ci", "朱怀珪", "怀珪");

            // ── 五代十国 ──
            P(table, "aw_figure_zhu_wen", "朱诚", "诚", "王氏", "王");
            P(table, "aw_figure_qian_liu", "钱宽", "宽", "水丘氏", "水丘");
            P(table, "aw_figure_wang_shenzhi", "王恁", "恁");
            P(table, "aw_figure_liu_shouguang", "刘仁恭", "仁恭");
            P(table, "aw_figure_yelu_abaoji", "耶律撒剌的", "撒剌的",
                "萧岩母斤", "萧");
            P(table, "aw_figure_liu_yan", "刘谦", "谦", "段氏", "段");
            P(table, "aw_figure_li_cunxu", "李克用", "克用", "曹氏", "曹");
            P(table, "aw_figure_meng_zhixiang", "孟道", "道");
            P(table, "aw_figure_shi_jingtang", "石绍雍", "绍雍", "何氏", "何");
            P(table, "aw_figure_li_bian", "李荣", "荣");
            P(table, "aw_figure_liu_zhiyuan", "刘琠", "琠", "安氏", "安");
            P(table, "aw_figure_guo_wei", "郭简", "简", "王氏", "王");
            P(table, "aw_figure_liu_chong", "刘琠", "琠");
            P(table, "aw_figure_zhao_kuangyin", "赵弘殷", "弘殷", "杜氏", "杜");

            // ── 明清 ──
            P(table, "aw_figure_li_zicheng", "李守忠", "守忠");
            P(table, "aw_figure_nurhaci", "塔克世", "塔克世", "喜塔腊氏",
                "喜塔腊");

            return table;
        }

        private static void P(
            Dictionary<string, HistoricalAncestorParentage> pTable,
            string pFigureId, string pFatherName,
            string pFatherGivenName = "", string pMotherName = "",
            string pMotherFamilyName = "")
        {
            pTable[pFigureId] = new HistoricalAncestorParentage(pFatherName,
                pFatherGivenName, pMotherName, pMotherFamilyName);
        }
    }
}
