namespace AncientWarfare3.core.lineage
{
    public enum PosthumousDimension
    {
        Civil,
        Territory,
        War,
        Order,
        Ending,
        Balanced
    }

    public enum PosthumousGrade
    {
        PraiseHigh,
        Praise,
        Neutral,
        Blame,
        BlameHigh
    }

    public readonly struct PosthumousTitleChar
    {
        public readonly string Char;
        public readonly PosthumousDimension Dimension;
        public readonly PosthumousGrade MinGrade;

        public PosthumousTitleChar(string pChar, PosthumousDimension pDimension, PosthumousGrade pMinGrade)
        {
            Char = pChar;
            Dimension = pDimension;
            MinGrade = pMinGrade;
        }
    }

    public static class PosthumousTitleDefs
    {
        public static readonly PosthumousTitleChar[] Pool =
        {
            new PosthumousTitleChar("文", PosthumousDimension.Civil, PosthumousGrade.PraiseHigh),
            new PosthumousTitleChar("明", PosthumousDimension.Civil, PosthumousGrade.PraiseHigh),
            new PosthumousTitleChar("睿", PosthumousDimension.Civil, PosthumousGrade.PraiseHigh),
            new PosthumousTitleChar("宣", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("昭", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("景", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("德", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("惠", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("仁", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("孝", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("康", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("懿", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("端", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("恪", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("勤", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("宪", PosthumousDimension.Civil, PosthumousGrade.Praise),
            new PosthumousTitleChar("敏", PosthumousDimension.Civil, PosthumousGrade.Praise),

            new PosthumousTitleChar("成", PosthumousDimension.Territory, PosthumousGrade.PraiseHigh),
            new PosthumousTitleChar("元", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("定", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("襄", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("献", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("穆", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("庄", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("肃", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("靖", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("宁", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("绥", PosthumousDimension.Territory, PosthumousGrade.Praise),
            new PosthumousTitleChar("贞", PosthumousDimension.Order, PosthumousGrade.Praise),

            new PosthumousTitleChar("武", PosthumousDimension.War, PosthumousGrade.PraiseHigh),
            new PosthumousTitleChar("桓", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("烈", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("威", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("毅", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("勇", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("壮", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("刚", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("雄", PosthumousDimension.War, PosthumousGrade.Praise),
            new PosthumousTitleChar("胜", PosthumousDimension.War, PosthumousGrade.Praise),

            new PosthumousTitleChar("平", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("安", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("顺", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("恭", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("简", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("敬", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("静", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("和", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("质", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("隐", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("僖", PosthumousDimension.Order, PosthumousGrade.Neutral),
            new PosthumousTitleChar("节", PosthumousDimension.Order, PosthumousGrade.Praise),

            new PosthumousTitleChar("哀", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("闵", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("悼", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("怀", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("殇", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("冲", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("愍", PosthumousDimension.Ending, PosthumousGrade.Blame),
            new PosthumousTitleChar("思", PosthumousDimension.Ending, PosthumousGrade.Neutral),

            new PosthumousTitleChar("厉", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("幽", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("荒", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("废", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("炀", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("灵", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("戾", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("刺", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("谬", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("惑", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("蛊", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("险", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("悖", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("傲", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("逆", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("暴", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("虐", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("昏", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh),
            new PosthumousTitleChar("愎", PosthumousDimension.Balanced, PosthumousGrade.BlameHigh)
        };
    }
}
