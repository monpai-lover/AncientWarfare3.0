namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     年号系统(参考 AW2 KingdomYearName.Make_New_YearName)。原版无年号,自建。
    ///     存 kingdom.data 的 aw_year_name(string)/aw_year_start(double)。
    ///
    ///     在世规则(统治者活着时):
    ///     - 天命/帝国(Emperor):两字年号(内置雅字池随机两字,如"远景"),中文名 mod 在时可换"国号前+国号后"词库。
    ///     - 普通国家:国号 + 头衔单字 + 王名首字(如"鲁伯姬")。
    ///     年份:N=Date.getYearsSince(start),0→"元年",否则→(N+1)+"年"。完整"鲁伯姬元年"/"远景二年"。
    ///
    ///     谥号(哀/桓/武…)是死后按生平定的,需"家族/王朝历史记录"系统支撑(待办,未做),
    ///     做好后把谥号插入中间字替代当前王名首字。
    /// </summary>
    internal static class YearNameService
    {
        // mod 私有随机:绝不用 UnityEngine.Random(被 MapBox 世界生成固定播种,见 aw3-random-seed-pitfall)。
        private static readonly System.Random Rng = new System.Random();

        // 帝国两字年号的雅字池(内置兜底,不依赖中文名 mod)。
        private static readonly string[] ERA_CHARS =
        {
            "建", "元", "天", "太", "始", "景", "嘉", "永", "和", "平",
            "光", "兴", "隆", "宁", "康", "贞", "顺", "昌", "盛", "德",
            "宣", "武", "文", "成", "昭", "明", "章", "安", "定", "靖",
            "神", "圣", "洪", "显", "崇", "正", "乾", "泰", "保", "延",
            "咸", "通", "丰", "熙", "祐", "祯", "弘", "至", "大", "中"
        };

        /// <summary>新王即位时换年号。由 setKing Postfix 调用。</summary>
        public static void OnNewKing(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.king == null) return;

            string yearName = MakeYearNameStem(pKingdom);
            pKingdom.data.set(LineageKeys.KINGDOM_YEAR_NAME, yearName);
            pKingdom.data.set(LineageKeys.KINGDOM_YEAR_START, (float)World.world.getCurWorldTime());
            // 纪元记录:换年号即开新纪元(EraRecordWriter 内部关旧开新)。
            EraRecordWriter.OnEraChanged(pKingdom, yearName);
        }

        /// <summary>年号词干(不含年份)。帝国=两字雅号;普通=国号+头衔单字+王名首字。</summary>
        private static string MakeYearNameStem(Kingdom pKingdom)
        {
            if (KingdomTitleService.IsEmperor(pKingdom))
                return TwoDistinctEraChars();

            string kingdomName = pKingdom.name ?? "";
            string titleChar = KingdomTitleService.GetTitleChar(KingdomTitleService.GetTitle(pKingdom));
            // 王名字取本名 GIVEN_NAME(存好的单名"发"),不取 getName()——后者被 ApplyDisplayName
            // 拼成"氏+名"(如"幸发"),FirstChar 会取到氏("幸")而非名,致年号误成"周伯幸"。
            string kingFirst = FirstChar(GivenNameOf(pKingdom.king));
            return kingdomName + titleChar + kingFirst;
        }

        /// <summary>取两个不重复的雅字(避免"建建"叠字)。</summary>
        private static string TwoDistinctEraChars()
        {
            int i = Rng.Next(0, ERA_CHARS.Length);
            int j = Rng.Next(0, ERA_CHARS.Length - 1);
            if (j >= i) j++; // 跳过 i,保证 j != i
            return ERA_CHARS[i] + ERA_CHARS[j];
        }

        /// <summary>完整年号文本,如"鲁伯姬元年"/"远景二年"。无年号返回空。</summary>
        public static string GetYearName(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_NAME, out string stem, "");
            if (string.IsNullOrEmpty(stem)) return "";

            pKingdom.data.get(LineageKeys.KINGDOM_YEAR_START, out float start, 0f);
            int years = Date.getYearsSince(start); // 0 = 即位当年(float 隐式转 double)
            string yearPart = years == 0 ? "元年" : (years + 1) + "年";
            return stem + yearPart;
        }

        private static string FirstChar(string pName)
        {
            return string.IsNullOrEmpty(pName) ? "" : pName.Substring(0, 1);
        }

        /// <summary>王的本名单字(存档 GIVEN_NAME,如"发")。缺失时退回 getName()(老档兜底)。</summary>
        private static string GivenNameOf(Actor pKing)
        {
            if (pKing?.data == null) return pKing?.getName() ?? "";
            pKing.data.get(LineageKeys.GIVEN_NAME, out string given, "");
            return string.IsNullOrEmpty(given) ? pKing.getName() : given;
        }
    }
}
