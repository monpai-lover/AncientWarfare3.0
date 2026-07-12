using System.Globalization;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    public static class XiaGivenNameRules
    {
        public static string NormalizeGenerated(string pRawName, bool pHasClanIdentity)
        {
            string raw = (pRawName ?? "").Trim();
            if (raw.Length == 0) return "";

            var result = new StringBuilder();
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(raw);
            int limit = pHasClanIdentity ? 1 : 2;
            int count = 0;
            while (count < limit && elements.MoveNext())
            {
                result.Append(elements.GetTextElement());
                count++;
            }
            return result.ToString();
        }
    }
}
