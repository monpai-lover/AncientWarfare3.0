using System;
using System.Globalization;
using System.Text;

namespace AncientWarfare3.core.historyapi
{
    internal readonly struct AW3HistoryCursorKey
    {
        public AW3HistoryCursorKey(double worldTime, string domain,
            string source, long recordId)
        {
            WorldTime = worldTime;
            Domain = domain ?? "";
            Source = source ?? "";
            RecordId = recordId;
        }

        public double WorldTime { get; }
        public string Domain { get; }
        public string Source { get; }
        public long RecordId { get; }
    }

    internal static class AW3HistoryCursorRules
    {
        private const string Version = "1";

        public static int Compare(AW3HistoryCursorKey left,
            AW3HistoryCursorKey right)
        {
            int time = left.WorldTime.CompareTo(right.WorldTime);
            if (time != 0) return time;
            int domain = string.CompareOrdinal(left.Domain, right.Domain);
            if (domain != 0) return domain;
            int source = string.CompareOrdinal(left.Source, right.Source);
            if (source != 0) return source;
            return left.RecordId.CompareTo(right.RecordId);
        }

        public static string Encode(AW3HistoryCursorKey key)
        {
            if (!IsFinite(key.WorldTime)) return "";
            string payload = Version + "|" +
                key.WorldTime.ToString("R", CultureInfo.InvariantCulture) + "|" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(key.Domain ?? "")) + "|" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(key.Source ?? "")) + "|" +
                key.RecordId.ToString(CultureInfo.InvariantCulture);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        }

        public static bool TryDecode(string encoded,
            out AW3HistoryCursorKey key)
        {
            key = default;
            if (string.IsNullOrWhiteSpace(encoded)) return false;
            try
            {
                string payload = Encoding.UTF8.GetString(
                    Convert.FromBase64String(encoded));
                string[] parts = payload.Split('|');
                if (parts.Length != 5 || parts[0] != Version) return false;
                if (!double.TryParse(parts[1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double time) ||
                    !IsFinite(time)) return false;
                if (!long.TryParse(parts[4], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long id)) return false;
                string domain = DecodeText(parts[2]);
                string source = DecodeText(parts[3]);
                if (domain == null || source == null) return false;
                key = new AW3HistoryCursorKey(time, domain, source, id);
                return true;
            }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static string DecodeText(string value)
        {
            if (value == null) return null;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch (FormatException) { return null; }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
