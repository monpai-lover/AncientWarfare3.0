namespace AncientWarfare3.ui
{
    internal static class AW_L10n
    {
        public static string Text(string pKey, string pFallback)
        {
            if (string.IsNullOrEmpty(pKey)) return pFallback ?? "";
            try
            {
                string value = LocalizedTextManager.getText(pKey);
                if (!string.IsNullOrEmpty(value) && value != pKey) return value;
            }
            catch
            {
                // Missing locale entries should never break custom UI.
            }
            return pFallback ?? pKey;
        }
    }
}
