using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionTooltipService
    {
        internal static string Build(City pCity)
        {
            if (pCity?.data == null || !DeJureRegionStore.TryGetForCity(
                    pCity.data.id, out DeJureRegion region)) return string.Empty;
            List<City> members = (region.MemberCityIds ?? new List<long>())
                .Select(p => World.world?.cities?.get(p))
                .Where(p => p?.data != null && !p.isRekt()).ToList();
            int total = members.Count;
            if (total == 0) return region.RegionName ?? string.Empty;
            var counts = new Dictionary<long, int>();
            var names = new Dictionary<long, string>();
            foreach (City city in members)
            {
                long id = city.kingdom?.data?.id ?? -1L;
                counts[id] = counts.TryGetValue(id, out int count)
                    ? count + 1 : 1;
                if (!names.ContainsKey(id)) names[id] = DisplayKingdom(city.kingdom);
            }
            var builder = new StringBuilder();
            builder.Append(AW_L10n.Text("aw_de_jure_region_label", "De jure state"));
            builder.Append(": ").Append(region.RegionName ?? string.Empty);
            builder.Append("\n").Append(AW_L10n.Text(
                "aw_de_jure_region_members", "Legal members"));
            builder.Append(": ").Append(total);
            foreach (KeyValuePair<long, int> entry in counts.OrderByDescending(
                         p => p.Value).ThenBy(p => p.Key))
            {
                float percentage = total <= 0 ? 0f : entry.Value * 100f / total;
                builder.Append("\n").Append(names[entry.Key]).Append(": ")
                    .Append(entry.Value).Append(" (")
                    .Append(percentage.ToString("0.#")).Append("%)");
            }
            string capitalController = DisplayKingdom(
                World.world?.cities?.get(region.SeatCityId)?.kingdom);
            builder.Append("\n").Append(AW_L10n.Text(
                "aw_de_jure_region_capital", "Capital"));
            builder.Append(": ").Append(capitalController);
            return builder.ToString();
        }

        private static string DisplayKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return AW_L10n.Text(
                "aw_de_jure_region_uncontrolled", "Uncontrolled");
            try
            {
                string name = SuccessionDisputeService.GetDisplayName(pKingdom);
                return string.IsNullOrWhiteSpace(name) ? pKingdom.name : name;
            }
            catch { return pKingdom.name ?? string.Empty; }
        }
    }
}
