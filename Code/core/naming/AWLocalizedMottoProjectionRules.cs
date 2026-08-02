using System;

namespace AncientWarfare3.core.naming
{
    public sealed class AWLocalizedMottoProjection
    {
        public string NativeMotto { get; }
        public string ChineseMotto { get; }
        public string ProjectedMotto { get; }
        public bool NeedsChineseGeneration { get; }

        public AWLocalizedMottoProjection(string pNativeMotto,
            string pChineseMotto, string pProjectedMotto,
            bool pNeedsChineseGeneration)
        {
            NativeMotto = pNativeMotto ?? string.Empty;
            ChineseMotto = pChineseMotto ?? string.Empty;
            ProjectedMotto = pProjectedMotto ?? string.Empty;
            NeedsChineseGeneration = pNeedsChineseGeneration;
        }
    }

    public static class AWLocalizedMottoProjectionRules
    {
        public static AWLocalizedMottoProjection Resolve(string pLanguage,
            string pObservedMotto, string pNativeMotto,
            string pChineseMotto)
        {
            string observed = Normalize(pObservedMotto);
            string native = Normalize(pNativeMotto);
            string chinese = Normalize(pChineseMotto);

            if (observed.Length > 0 &&
                !string.Equals(observed, native, StringComparison.Ordinal) &&
                !string.Equals(observed, chinese, StringComparison.Ordinal))
            {
                if (native.Length == 0 && chinese.Length == 0)
                {
                    if (ContainsHanCharacter(observed)) chinese = observed;
                    else native = observed;
                }
                else if (AWNamingLanguageRules.IsChinesePresentation(
                    pLanguage))
                {
                    chinese = observed;
                }
                else
                {
                    native = observed;
                }
            }

            bool needsChineseGeneration = chinese.Length == 0 &&
                AWNamingLanguageRules.IsChinesePresentation(pLanguage);
            string projected = AWLocalizedNameProjectionRules.Select(
                pLanguage, native, chinese);
            return new AWLocalizedMottoProjection(native, chinese, projected,
                needsChineseGeneration);
        }

        public static AWLocalizedMottoProjection ResolveEdit(
            string pLanguage, string pEditedMotto, string pNativeMotto,
            string pChineseMotto)
        {
            string edited = Normalize(pEditedMotto);
            string native = Normalize(pNativeMotto);
            string chinese = Normalize(pChineseMotto);
            if (AWNamingLanguageRules.IsChinesePresentation(pLanguage))
                chinese = edited;
            else
                native = edited;
            string projected = AWLocalizedNameProjectionRules.Select(
                pLanguage, native, chinese);
            return new AWLocalizedMottoProjection(native, chinese, projected,
                chinese.Length == 0 && native.Length > 0);
        }

        private static string Normalize(string pValue)
        {
            return string.IsNullOrWhiteSpace(pValue)
                ? string.Empty
                : pValue.Trim();
        }

        private static bool ContainsHanCharacter(string pValue)
        {
            for (int i = 0; i < pValue.Length; i++)
            {
                int value = pValue[i];
                if ((value >= 0x3400 && value <= 0x4DBF) ||
                    (value >= 0x4E00 && value <= 0x9FFF) ||
                    (value >= 0xF900 && value <= 0xFAFF))
                    return true;
            }
            return false;
        }
    }
}
