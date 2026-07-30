using System;

namespace AncientWarfare3.content.figures
{
    public enum HistoricalFigureSex
    {
        Male,
        Female
    }

    /// <summary>
    ///     历史人物的静态定义。RegistryIndex 是随存档持久化的稳定槽位；
    ///     SpawnOrder 是历史顺序，两者分离以保留旧档中曹丕=3、司马炎=4。
    /// </summary>
    public sealed class HistoricalFigureDef
    {
        public readonly int RegistryIndex;
        public readonly int SpawnOrder;
        public readonly int Order;
        public readonly string Id;
        public readonly string Key;
        public readonly string FamilyName;
        public readonly string ClanName;
        public readonly string GivenName;
        public readonly string DynastyName;
        public readonly string KingdomName;
        public readonly string NameLocaleKey;
        public readonly string DynastyLocaleKey;
        public readonly int FoundingYear;
        public readonly HistoricalFigureSex Sex;
        public readonly bool RequiresIntegration;
        public readonly float Chance;

        private HistoricalFigureDef(int pRegistryIndex, int pSpawnOrder,
            string pId, string pKey, string pFamily, string pClan,
            string pGiven, string pDynasty, string pKingdom,
            int pFoundingYear, bool pReqIntegration, float pChance,
            HistoricalFigureSex pSex)
        {
            RegistryIndex = pRegistryIndex;
            SpawnOrder = pSpawnOrder;
            Order = pRegistryIndex;
            Id = pId ?? "";
            Key = pKey ?? "";
            FamilyName = pFamily ?? "";
            ClanName = pClan ?? "";
            GivenName = pGiven ?? "";
            DynastyName = pDynasty ?? "";
            KingdomName = pKingdom ?? "";
            NameLocaleKey = Id;
            DynastyLocaleKey = Id + "_dynasty";
            FoundingYear = pFoundingYear;
            Sex = pSex;
            RequiresIntegration = pReqIntegration;
            Chance = pChance;
        }

        /// <summary>
        ///     按稳定 registry 槽位排列。五项不可重排；新增人物从槽位 5 追加。
        /// </summary>
        public static readonly HistoricalFigureDef[] All =
        {
            D(0, 0, "aw_figure_ji_fa", "姬发", "姬", "姬", "发", "周", "周", -1046, false, 0.80f),
            D(1, 1, "aw_figure_ying_zheng", "嬴政", "嬴", "赵", "政", "秦", "秦", -221, false, 0.005f),
            D(2, 2, "aw_figure_liu_bang", "刘邦", "刘", "刘", "邦", "漢", "漢", -202, true, 0.005f),
            D(3, 7, "aw_figure_cao_pi", "曹丕", "曹", "曹", "丕", "魏", "魏", 220, true, 0.005f),
            D(4, 10, "aw_figure_sima_yan", "司马炎", "司马", "司马", "炎", "晋", "晋", 266, true, 0.005f),
            D(5, 3, "aw_figure_wang_mang", "王莽", "王", "王", "莽", "新", "新", 9, true, 0.005f),
            D(6, 5, "aw_figure_liu_xiu", "刘秀", "刘", "刘", "秀", "漢", "漢", 25, true, 0.005f),
            D(7, 8, "aw_figure_liu_bei", "刘备", "刘", "刘", "备", "漢", "漢", 221, true, 0.005f),
            D(8, 9, "aw_figure_sun_quan", "孙权", "孙", "孙", "权", "吴", "吴", 229, true, 0.005f),
            D(9, 11, "aw_figure_zhang_gui", "张轨", "张", "张", "轨", "凉", "凉", 301, true, 0.005f),
            D(10, 12, "aw_figure_liu_yuan", "刘渊", "刘", "刘", "渊", "漢", "漢", 304, true, 0.005f),
            D(11, 13, "aw_figure_li_xiong", "李雄", "李", "李", "雄", "漢", "漢", 304, true, 0.005f),
            D(12, 14, "aw_figure_sima_rui", "司马睿", "司马", "司马", "睿", "晋", "晋", 317, true, 0.005f),
            D(13, 15, "aw_figure_shi_le", "石勒", "石", "石", "勒", "赵", "赵", 319, true, 0.005f),
            D(14, 16, "aw_figure_murong_huang", "慕容皝", "慕容", "慕容", "皝", "燕", "燕", 337, true, 0.005f),
            D(15, 18, "aw_figure_fu_jian_351", "苻健", "苻", "苻", "健", "秦", "秦", 351, true, 0.005f),
            D(16, 21, "aw_figure_yao_chang", "姚苌", "姚", "姚", "苌", "秦", "秦", 384, true, 0.005f),
            D(17, 22, "aw_figure_qifu_guoren", "乞伏国仁", "乞伏", "乞伏", "国仁", "秦", "秦", 385, true, 0.005f),
            D(18, 19, "aw_figure_murong_chui", "慕容垂", "慕容", "慕容", "垂", "燕", "燕", 384, true, 0.005f),
            D(19, 23, "aw_figure_lu_guang", "吕光", "吕", "吕", "光", "凉", "凉", 386, true, 0.005f),
            D(20, 24, "aw_figure_tuoba_gui", "拓跋珪", "拓跋", "拓跋", "珪", "魏", "魏", 386, true, 0.005f),
            D(21, 25, "aw_figure_tufa_wugu", "秃发乌孤", "秃发", "秃发", "乌孤", "凉", "凉", 397, true, 0.005f),
            D(22, 26, "aw_figure_murong_de", "慕容德", "慕容", "慕容", "德", "燕", "燕", 398, true, 0.005f),
            D(23, 27, "aw_figure_li_gao", "李暠", "李", "李", "暠", "凉", "凉", 400, true, 0.005f),
            D(24, 28, "aw_figure_juqu_mengxun", "沮渠蒙逊", "沮渠", "沮渠", "蒙逊", "凉", "凉", 401, true, 0.005f),
            D(25, 30, "aw_figure_helian_bobo", "赫连勃勃", "赫连", "赫连", "勃勃", "胡夏", "夏", 407, true, 0.005f),
            D(26, 31, "aw_figure_feng_ba", "冯跋", "冯", "冯", "跋", "燕", "燕", 409, true, 0.005f),
            D(27, 32, "aw_figure_liu_yu", "刘裕", "刘", "刘", "裕", "刘宋", "宋", 420, true, 0.005f),
            D(28, 33, "aw_figure_xiao_daocheng", "萧道成", "萧", "萧", "道成", "齐", "齐", 479, true, 0.005f),
            D(29, 34, "aw_figure_xiao_yan", "萧衍", "萧", "萧", "衍", "梁", "梁", 502, true, 0.005f),
            D(30, 35, "aw_figure_gao_huan", "高欢", "高", "高", "欢", "魏", "魏", 534, true, 0.005f),
            D(31, 36, "aw_figure_yuwen_tai", "宇文泰", "宇文", "宇文", "泰", "魏", "魏", 535, true, 0.005f),
            D(32, 37, "aw_figure_gao_yang", "高洋", "高", "高", "洋", "齐", "齐", 550, true, 0.005f),
            D(33, 40, "aw_figure_yuwen_jue", "宇文觉", "宇文", "宇文", "觉", "周", "周", 557, true, 0.005f),
            D(34, 41, "aw_figure_chen_baxian", "陈霸先", "陈", "陈", "霸先", "陈", "陈", 557, true, 0.005f),
            D(35, 42, "aw_figure_yang_jian", "杨坚", "杨", "杨", "坚", "隋", "隋", 581, true, 0.005f),
            D(36, 44, "aw_figure_lin_shihong", "林士弘", "林", "林", "士弘", "林楚", "楚", 616, true, 0.005f),
            D(37, 45, "aw_figure_xue_ju", "薛举", "薛", "薛", "举", "薛秦", "秦", 617, true, 0.005f),
            D(38, 46, "aw_figure_liu_wuzhou", "刘武周", "刘", "刘", "武周", "定杨", "定杨", 617, true, 0.005f),
            D(39, 47, "aw_figure_liang_shidu", "梁师都", "梁", "梁", "师都", "梁", "梁", 617, true, 0.005f),
            D(40, 48, "aw_figure_xiao_xian", "萧铣", "萧", "萧", "铣", "萧梁", "梁", 617, true, 0.005f),
            D(41, 49, "aw_figure_li_mi", "李密", "李", "李", "密", "瓦岗魏", "魏", 617, true, 0.005f),
            D(42, 50, "aw_figure_dou_jiande", "窦建德", "窦", "窦", "建德", "窦夏", "夏", 617, true, 0.005f),
            D(43, 51, "aw_figure_li_gui", "李轨", "李", "李", "轨", "李凉", "凉", 617, true, 0.005f),
            D(44, 52, "aw_figure_zhu_can", "朱粲", "朱", "朱", "粲", "朱楚", "楚", 617, true, 0.005f),
            D(45, 53, "aw_figure_yuwen_huaji", "宇文化及", "宇文", "宇文", "化及", "许", "许", 618, true, 0.005f),
            D(46, 54, "aw_figure_li_yuan", "李渊", "李", "李", "渊", "唐", "唐", 618, true, 0.005f),
            D(47, 55, "aw_figure_wang_shichong", "王世充", "王", "王", "世充", "郑", "郑", 619, true, 0.005f),
            D(48, 56, "aw_figure_li_zitong", "李子通", "李", "李", "子通", "吴", "吴", 619, true, 0.005f),
            D(49, 57, "aw_figure_shen_faxing", "沈法兴", "沈", "沈", "法兴", "梁", "梁", 619, true, 0.005f),
            D(50, 58, "aw_figure_gao_kaidao", "高开道", "高", "高", "开道", "燕", "燕", 619, true, 0.005f),
            D(51, 60, "aw_figure_fu_gongshi", "辅公祏", "辅", "辅", "公祏", "宋", "宋", 623, true, 0.005f),
            D(52, 61, "aw_figure_wu_zhao", "武曌", "武", "武", "曌", "周", "周", 690, true, 0.005f, HistoricalFigureSex.Female),
            D(53, 70, "aw_figure_yang_xingmi", "杨行密", "杨", "杨", "行密", "吴", "吴", 902, true, 0.005f),
            D(54, 71, "aw_figure_zhu_wen", "朱温", "朱", "朱", "温", "梁", "梁", 907, true, 0.005f),
            D(55, 72, "aw_figure_wang_jian", "王建", "王", "王", "建", "蜀", "蜀", 907, true, 0.005f),
            D(56, 73, "aw_figure_qian_liu", "钱镠", "钱", "钱", "镠", "吴越", "吴越", 907, true, 0.005f),
            D(57, 74, "aw_figure_ma_yin", "马殷", "马", "马", "殷", "马楚", "楚", 907, true, 0.005f),
            D(58, 75, "aw_figure_wang_shenzhi", "王审知", "王", "王", "审知", "闽", "闽", 909, true, 0.005f),
            D(59, 78, "aw_figure_liu_yan", "刘岩", "刘", "刘", "岩", "漢", "漢", 917, true, 0.005f),
            D(60, 79, "aw_figure_li_cunxu", "李存勖", "李", "李", "存勖", "唐", "唐", 923, true, 0.005f),
            D(61, 80, "aw_figure_gao_jixing", "高季兴", "高", "高", "季兴", "荆", "荆", 924, true, 0.005f),
            D(62, 81, "aw_figure_meng_zhixiang", "孟知祥", "孟", "孟", "知祥", "蜀", "蜀", 934, true, 0.005f),
            D(63, 82, "aw_figure_shi_jingtang", "石敬瑭", "石", "石", "敬瑭", "晋", "晋", 936, true, 0.005f),
            D(64, 84, "aw_figure_li_bian", "李昪", "李", "李", "昪", "唐", "唐", 937, true, 0.005f),
            D(65, 85, "aw_figure_liu_zhiyuan", "刘知远", "刘", "刘", "知远", "漢", "漢", 947, true, 0.005f),
            D(66, 86, "aw_figure_guo_wei", "郭威", "郭", "郭", "威", "周", "周", 951, true, 0.005f),
            D(67, 87, "aw_figure_liu_chong", "刘崇", "刘", "刘", "崇", "漢", "漢", 951, true, 0.005f),
            D(68, 88, "aw_figure_zhao_kuangyin", "赵匡胤", "赵", "赵", "匡胤", "宋", "宋", 960, true, 0.005f),
            D(69, 43, "aw_figure_du_fuwei", "杜伏威", "杜", "杜", "伏威", "杜吴", "吴", 613, true, 0.005f),
            D(70, 59, "aw_figure_xu_yuanlang", "徐圆朗", "徐", "徐", "圆朗", "徐鲁", "鲁", 621, true, 0.005f),
            D(71, 4, "aw_figure_gongsun_shu", "公孙述", "公孙", "公孙", "述", "成家", "成", 25, true, 0.005f),
            D(72, 6, "aw_figure_yuan_shu", "袁术", "袁", "袁", "术", "仲氏", "仲", 197, true, 0.005f),
            D(73, 17, "aw_figure_ran_min", "冉闵", "冉", "冉", "闵", "冉魏", "魏", 350, true, 0.005f),
            D(74, 20, "aw_figure_murong_hong", "慕容泓", "慕容", "慕容", "泓", "燕", "燕", 384, true, 0.005f),
            D(75, 29, "aw_figure_huan_xuan", "桓玄", "桓", "桓", "玄", "桓楚", "楚", 403, true, 0.005f),
            D(76, 38, "aw_figure_hou_jing", "侯景", "侯", "侯", "景", "侯漢", "漢", 551, true, 0.005f),
            D(77, 39, "aw_figure_xiao_cha", "萧詧", "萧", "萧", "詧", "梁", "梁", 555, true, 0.005f),
            D(78, 62, "aw_figure_da_zuorong", "大祚荣", "大", "大", "祚荣", "渤海", "渤海", 698, true, 0.005f),
            D(79, 63, "aw_figure_pi_luoge", "皮逻阁", "皮", "皮", "逻阁", "诏", "诏", 738, true, 0.005f),
            D(80, 64, "aw_figure_an_lushan", "安禄山", "安", "安", "禄山", "燕", "燕", 756, true, 0.005f),
            D(81, 65, "aw_figure_zhu_ci", "朱泚", "朱", "朱", "泚", "朱秦", "秦", 783, true, 0.005f),
            D(82, 66, "aw_figure_li_xilie", "李希烈", "李", "李", "希烈", "楚", "楚", 784, true, 0.005f),
            D(83, 67, "aw_figure_huang_chao", "黄巢", "黄", "黄", "巢", "大齐", "齐", 881, true, 0.005f),
            D(84, 68, "aw_figure_dong_chang", "董昌", "董", "董", "昌", "大越罗平", "越", 895, true, 0.005f),
            D(85, 69, "aw_figure_li_maozhen", "李茂贞", "李", "李", "茂贞", "岐", "岐", 901, true, 0.005f),
            D(86, 76, "aw_figure_liu_shouguang", "刘守光", "刘", "刘", "守光", "燕", "燕", 911, true, 0.005f),
            D(87, 77, "aw_figure_yelu_abaoji", "耶律阿保机", "耶律", "耶律", "阿保机", "辽", "辽", 916, true, 0.005f),
            D(88, 83, "aw_figure_duan_siping", "段思平", "段", "段", "思平", "大理", "大理", 937, true, 0.005f)
        };

        public static readonly HistoricalFigureDef[] SpawnSequence =
            BuildSpawnSequence();
        public static readonly int[] SpawnRegistryOrder =
            BuildSpawnRegistryOrder();

        public const int Count = 89;

        public static HistoricalFigureDef Get(int pRegistryIndex)
        {
            if (pRegistryIndex < 0 || pRegistryIndex >= All.Length)
                return null;
            HistoricalFigureDef candidate = All[pRegistryIndex];
            if (candidate.RegistryIndex == pRegistryIndex) return candidate;
            for (int i = 0; i < All.Length; i++)
                if (All[i].RegistryIndex == pRegistryIndex) return All[i];
            return null;
        }

        private static HistoricalFigureDef D(int pRegistryIndex,
            int pSpawnOrder, string pId, string pKey, string pFamily,
            string pClan, string pGiven, string pDynasty, string pKingdom,
            int pFoundingYear, bool pReqIntegration, float pChance,
            HistoricalFigureSex pSex = HistoricalFigureSex.Male)
        {
            return new HistoricalFigureDef(pRegistryIndex, pSpawnOrder,
                pId, pKey, pFamily, pClan, pGiven, pDynasty, pKingdom,
                pFoundingYear, pReqIntegration, pChance, pSex);
        }

        private static HistoricalFigureDef[] BuildSpawnSequence()
        {
            var sequence = new HistoricalFigureDef[All.Length];
            Array.Copy(All, sequence, All.Length);
            Array.Sort(sequence, delegate(HistoricalFigureDef left,
                HistoricalFigureDef right)
            {
                return left.SpawnOrder.CompareTo(right.SpawnOrder);
            });
            return sequence;
        }

        private static int[] BuildSpawnRegistryOrder()
        {
            var registryOrder = new int[SpawnSequence.Length];
            for (int i = 0; i < SpawnSequence.Length; i++)
                registryOrder[i] = SpawnSequence[i].RegistryIndex;
            return registryOrder;
        }
    }
}
