namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     历史记录文本:Plain 用于旧逻辑/搜索,Rich 用于 UI 上色显示。
    ///     内容写入时保留两份,老存档没有 Rich 时仍显示 Plain。
    /// </summary>
    public readonly struct HistoryText
    {
        public readonly string Plain;
        public readonly string Rich;
        public readonly string TargetType;
        public readonly long TargetId;

        public HistoryText(string pPlain, string pRich = null, string pTargetType = "", long pTargetId = -1)
        {
            Plain = pPlain ?? "";
            Rich = string.IsNullOrEmpty(pRich) ? HistoryColors.EscapeRich(Plain) : pRich;
            TargetType = pTargetType ?? "";
            TargetId = pTargetId;
        }

        public static HistoryText PlainText(string pText)
        {
            return new HistoryText(pText);
        }

        public static HistoryText Colored(string pText, string pColor)
        {
            string plain = pText ?? "";
            string color = HistoryColors.Normalize(pColor);
            if (string.IsNullOrEmpty(color)) return new HistoryText(plain);
            return new HistoryText(plain, "<color=" + color + ">" + HistoryColors.EscapeRich(plain) + "</color>");
        }

        public static HistoryText Actor(Actor pActor, string pFallbackName = "")
        {
            string name = pActor?.getName() ?? pFallbackName ?? "";
            var text = Colored(name, HistoryColors.FromActor(pActor));
            return new HistoryText(text.Plain, text.Rich, "actor", pActor?.data?.id ?? -1L);
        }

        public static HistoryText Kingdom(Kingdom pKingdom, string pFallbackName = "")
        {
            string name = pKingdom?.name ?? pFallbackName ?? "";
            var text = Colored(name, HistoryColors.FromKingdom(pKingdom));
            return new HistoryText(text.Plain, text.Rich, "kingdom", pKingdom?.id ?? -1L);
        }

        public static HistoryText City(City pCity, Kingdom pContextKingdom = null, string pFallbackName = "")
        {
            string name = pCity?.data?.name ?? pFallbackName ?? "";
            var text = Colored(name, HistoryColors.FromCity(pCity, pContextKingdom));
            return new HistoryText(text.Plain, text.Rich, "city", pCity?.id ?? -1L);
        }

        public static HistoryText ClanName(string pName, Clan pClan, Kingdom pFallbackKingdom = null)
        {
            return Colored(pName ?? "", HistoryColors.FromClan(pClan, pFallbackKingdom));
        }

        public static implicit operator HistoryText(string pText)
        {
            return PlainText(pText);
        }

        public static HistoryText operator +(HistoryText pLeft, HistoryText pRight)
        {
            string targetType = !string.IsNullOrEmpty(pLeft.TargetType) && pLeft.TargetId >= 0
                ? pLeft.TargetType
                : pRight.TargetType;
            long targetId = !string.IsNullOrEmpty(pLeft.TargetType) && pLeft.TargetId >= 0
                ? pLeft.TargetId
                : pRight.TargetId;
            return new HistoryText((pLeft.Plain ?? "") + (pRight.Plain ?? ""),
                (pLeft.Rich ?? "") + (pRight.Rich ?? ""), targetType, targetId);
        }

        public override string ToString()
        {
            return Plain;
        }
    }

    internal static class HistoryColors
    {
        public static string Normalize(string pColor)
        {
            if (string.IsNullOrEmpty(pColor)) return "";
            string color = pColor.Trim();
            if (string.IsNullOrEmpty(color)) return "";
            return color[0] == '#' ? color : "#" + color;
        }

        public static string FromKingdom(Kingdom pKingdom)
        {
            try { return Normalize(pKingdom?.getColor()?.color_text); }
            catch { return ""; }
        }

        public static string FromActor(Actor pActor)
        {
            try
            {
                string color = FromKingdom(pActor?.kingdom);
                if (!string.IsNullOrEmpty(color)) return color;
            }
            catch { }
            return "";
        }

        public static string FromCity(City pCity, Kingdom pContextKingdom = null)
        {
            string color = FromKingdom(pContextKingdom);
            if (!string.IsNullOrEmpty(color)) return color;
            try { return FromKingdom(pCity?.kingdom); }
            catch { return ""; }
        }

        public static string FromClan(Clan pClan, Kingdom pFallbackKingdom = null)
        {
            try
            {
                string color = Normalize(pClan?.getColor()?.color_text);
                if (!string.IsNullOrEmpty(color)) return color;
            }
            catch { }
            return FromKingdom(pFallbackKingdom);
        }

        public static string EscapeRich(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return "";
            return pText.Replace("<", "＜").Replace(">", "＞");
        }
    }
}
