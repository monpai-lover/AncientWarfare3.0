using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AncientWarfare3.core.policy
{
    internal sealed class KingdomDecisionQueueItem
    {
        public string decision_id = "";
        public float progress;
        public long target_kingdom_id = -1;
        public string target_kingdom_name = "";
        public string project_type = "";
        public string war_type = "";
        public string war_goal_type = "";
        public string war_reason_key = "";
        public string war_reason_label = "";
        public long war_target_city_id = -1;
        public string war_target_city_name = "";
        public long war_source_claim_id = -1;
        public long war_source_core_id = -1;
        public long war_restoration_claim_id = -1;
        public long war_claimant_actor_id = -1;
        public string notice_signature = "";
        public int notice_year = -1;
        public int earliest_war_year = -1;
        public int forced_war_year = -1;
        public bool notice_recorded;
    }

    internal static class KingdomDecisionQueueCodec
    {
        public const int MaxQueueSize = 8;

        public static List<KingdomDecisionQueueItem> Decode(string pRaw)
        {
            var result = new List<KingdomDecisionQueueItem>();
            if (string.IsNullOrEmpty(pRaw)) return result;

            string[] rows = pRaw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string row in rows)
            {
                string[] parts = row.Split('|');
                if (parts.Length < 20) continue;
                var item = new KingdomDecisionQueueItem
                {
                    decision_id = DecodeString(parts[0]),
                    progress = ParseFloat(parts[1]),
                    target_kingdom_id = ParseLong(parts[2]),
                    target_kingdom_name = DecodeString(parts[3]),
                    project_type = DecodeString(parts[4]),
                    war_type = DecodeString(parts[5]),
                    war_goal_type = DecodeString(parts[6]),
                    war_reason_key = DecodeString(parts[7]),
                    war_reason_label = DecodeString(parts[8]),
                    war_target_city_id = ParseLong(parts[9]),
                    war_target_city_name = DecodeString(parts[10]),
                    war_source_claim_id = ParseLong(parts[11]),
                    war_source_core_id = ParseLong(parts[12]),
                    war_restoration_claim_id = ParseLong(parts[13]),
                    war_claimant_actor_id = ParseLong(parts[14]),
                    notice_signature = DecodeString(parts[15]),
                    notice_year = ParseInt(parts[16]),
                    earliest_war_year = ParseInt(parts[17]),
                    forced_war_year = ParseInt(parts[18]),
                    notice_recorded = parts[19] == "1"
                };
                if (!string.IsNullOrEmpty(item.decision_id)) result.Add(item);
            }

            return result;
        }

        public static string Encode(List<KingdomDecisionQueueItem> pItems)
        {
            if (pItems == null || pItems.Count == 0) return "";
            var rows = new List<string>();
            int count = Math.Min(MaxQueueSize, pItems.Count);
            for (int i = 0; i < count; i++)
            {
                KingdomDecisionQueueItem item = pItems[i];
                if (item == null || string.IsNullOrEmpty(item.decision_id)) continue;
                rows.Add(string.Join("|", new[]
                {
                    EncodeString(item.decision_id),
                    item.progress.ToString(CultureInfo.InvariantCulture),
                    item.target_kingdom_id.ToString(CultureInfo.InvariantCulture),
                    EncodeString(item.target_kingdom_name),
                    EncodeString(item.project_type),
                    EncodeString(item.war_type),
                    EncodeString(item.war_goal_type),
                    EncodeString(item.war_reason_key),
                    EncodeString(item.war_reason_label),
                    item.war_target_city_id.ToString(CultureInfo.InvariantCulture),
                    EncodeString(item.war_target_city_name),
                    item.war_source_claim_id.ToString(CultureInfo.InvariantCulture),
                    item.war_source_core_id.ToString(CultureInfo.InvariantCulture),
                    item.war_restoration_claim_id.ToString(CultureInfo.InvariantCulture),
                    item.war_claimant_actor_id.ToString(CultureInfo.InvariantCulture),
                    EncodeString(item.notice_signature),
                    item.notice_year.ToString(CultureInfo.InvariantCulture),
                    item.earliest_war_year.ToString(CultureInfo.InvariantCulture),
                    item.forced_war_year.ToString(CultureInfo.InvariantCulture),
                    item.notice_recorded ? "1" : "0"
                }));
            }

            return string.Join(";", rows.ToArray());
        }

        public static string MigrateDecisionIds(string pRaw,
            Func<string, string> pMap,
            out bool pChanged)
        {
            pChanged = false;
            if (pMap == null) return pRaw ?? "";

            List<KingdomDecisionQueueItem> items = Decode(pRaw);
            for (int index = 0; index < items.Count; index++)
            {
                KingdomDecisionQueueItem item = items[index];
                if (item == null) continue;
                string mapped = pMap(item.decision_id) ?? "";
                if (string.Equals(mapped, item.decision_id,
                        StringComparison.Ordinal)) continue;
                item.decision_id = mapped;
                pChanged = true;
            }

            return pChanged ? Encode(items) : pRaw ?? "";
        }

        private static string EncodeString(string pValue)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(pValue ?? ""));
        }

        private static string DecodeString(string pValue)
        {
            if (string.IsNullOrEmpty(pValue)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(pValue)); }
            catch { return ""; }
        }

        private static long ParseLong(string pValue)
        {
            return long.TryParse(pValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
                ? value
                : -1L;
        }

        private static float ParseFloat(string pValue)
        {
            return float.TryParse(pValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : 0f;
        }

        private static int ParseInt(string pValue)
        {
            return int.TryParse(pValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : -1;
        }
    }
}
