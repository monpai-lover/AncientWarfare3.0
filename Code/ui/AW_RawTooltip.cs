namespace AncientWarfare3.ui
{
    /// <summary>
    ///     自定义 tooltip type "aw_raw":callback 直接把 tip_name/tip_description **原样**写进 Tooltip,
    ///     **不经 .Localize()/getText**。用于显示运行时动态文本(人名/生卒/身份)——这些不可能注册成
    ///     本地化键,若用原版 "normal" type(showNormal 内 tip_name.Localize())会对每个人名调 getText →
    ///     未注册 → Player.log 刷 "missing text: 鸿己" 等噪音并写 missing_locales 文件。
    ///
    ///     注册时机:ModClass.OnModLoad 末尾(AssetManager.tooltips 已 init)。id 唯一,不与原版冲突。
    /// </summary>
    internal static class AW_RawTooltip
    {
        public const string TYPE = "aw_raw";

        public static void Init()
        {
            try
            {
                AssetManager.tooltips.add(new TooltipAsset
                {
                    id = TYPE,
                    callback = ShowRaw
                });
            }
            catch
            {
                // 已注册(热重载二次调用)→ 忽略 duplicate。
            }
        }

        private static void ShowRaw(Tooltip pTooltip, string pType, TooltipData pData)
        {
            if (!string.IsNullOrEmpty(pData.tip_name))
                pTooltip.name.text = pData.tip_name; // 原样,不本地化
            if (!string.IsNullOrEmpty(pData.tip_description))
                pTooltip.setDescription(pData.tip_description);
        }
    }
}
