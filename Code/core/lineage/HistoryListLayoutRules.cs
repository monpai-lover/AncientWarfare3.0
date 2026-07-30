using System;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    public static class HistoryListLayoutRules
    {
        public const int BodyFontSize = 8;
        public const float BaseRowHeight = 22f;

        private const float CharacterWidth = 10f;
        private const float WrappedLineHeight = 11f;
        private const float WrappedVerticalPadding = 8f;
        private const float HorizontalPadding = 12f;
        private const float MinimumCharactersPerLine = 18f;

        public static float EstimateHeight(string pText, float pWidth)
        {
            string plain = StripRichText(pText ?? "");
            float usableWidth = Math.Max(0f, pWidth - HorizontalPadding);
            float charactersPerLine = Math.Max(MinimumCharactersPerLine,
                usableWidth / CharacterWidth);
            int lines = 0;
            string[] parts = plain.Split('\n');
            foreach (string part in parts)
            {
                int length = string.IsNullOrEmpty(part) ? 1 : part.Length;
                lines += Math.Max(1,
                    (int)Math.Ceiling(length / charactersPerLine));
            }

            return Math.Max(BaseRowHeight,
                lines * WrappedLineHeight + WrappedVerticalPadding);
        }

        private static string StripRichText(string pText)
        {
            if (string.IsNullOrEmpty(pText)) return "";
            var builder = new StringBuilder(pText.Length);
            bool insideTag = false;
            foreach (char character in pText)
            {
                if (character == '<')
                {
                    insideTag = true;
                    continue;
                }

                if (character == '>')
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag) builder.Append(character);
            }

            return builder.ToString();
        }
    }
}
