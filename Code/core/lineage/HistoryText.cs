namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     History record text: Plain is used by legacy/search paths, Rich is used by colored UI.
    ///     Both are persisted; old saves without Rich can still fall back to Plain.
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
            return new HistoryText(text.Plain, text.Rich, "kingdom", pKingdom?.data?.id ?? -1L);
        }

        public static HistoryText City(City pCity, Kingdom pContextKingdom = null, string pFallbackName = "")
        {
            string name = pFallbackName ?? "";
            long id = -1L;
            bool valid = false;
            try
            {
                if (pCity?.data != null)
                {
                    if (!string.IsNullOrEmpty(pCity.data.name)) name = pCity.data.name;
                    id = pCity.data.id;
                    valid = id >= 0;
                }
            }
            catch
            {
                valid = false;
            }

            var text = Colored(name, HistoryColors.FromCity(valid ? pCity : null, pContextKingdom));
            return new HistoryText(text.Plain, text.Rich, valid ? "city" : "", valid ? id : -1L);
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
            try
            {
                if (pKingdom?.data == null) return "";
                int colorId = pKingdom.data.color_id;
                if (colorId >= 0)
                {
                    ColorAsset direct = AssetManager.kingdom_colors_library.getColorByIndex(colorId);
                    string directColor = Normalize(direct?.color_text);
                    if (!string.IsNullOrEmpty(directColor)) return directColor;
                }
            }
            catch { }

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
            return pText.Replace("<", "\uff1c").Replace(">", "\uff1e");
        }
    }
}
