using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content
{
    internal static class ZhuluWorldAgeContent
    {
        private const string IconPath =
            "ui/Icons/traits/iconTianming";
        private const string BackgroundPath =
            "ui/AgeWheel/backgrounds/age_zhulu_background";

        internal static void Init()
        {
            WorldAgeLibrary library = AssetManager.era_library;
            if (library == null)
            {
                ModClass.LogWarning(
                    "Zhulu age registration skipped: era library unavailable.");
                return;
            }

            WorldAgeAsset age = library.get(ZhuluAgeRules.AgeId);
            if (age == null)
            {
                age = AssetManager.era_library.add(new WorldAgeAsset
                {
                    id = ZhuluAgeRules.AgeId
                });
            }

            Configure(age);
            LinkNormalAge(library, age);
            LinkSlotPools(library, age);
            LinkCurrentWorldLaw(age);
        }

        private static void Configure(WorldAgeAsset age)
        {
            age.path_icon = IconPath;
            age.path_background = BackgroundPath;
            age.rate = 1;
            age.years_min = 35;
            age.years_max = 55;
            age.global_unfreeze_world = true;
            age.title_color = Toolbox.makeColor("#D9B44A");
            age.clouds = new List<string> { "cloud_normal" };
            age.biomes = new HashSet<string>();
            age.default_slots = new List<int>
            {
                2, 3, 4, 5, 6, 7, 8
            };
            age.link_default_slots = false;
        }

        private static void LinkNormalAge(WorldAgeLibrary library,
            WorldAgeAsset age)
        {
            if (library.list_only_normal == null)
            {
                library.list_only_normal = new List<WorldAgeAsset>();
                for (int i = 0; i < library.list.Count; i++)
                {
                    WorldAgeAsset item = library.list[i];
                    if (item?.id != "age_unknown")
                        library.list_only_normal.Add(item);
                }
            }
            if (!library.list_only_normal.Contains(age))
                library.list_only_normal.Add(age);
            else
            {
                library.list_only_normal.Remove(age);
                library.list_only_normal.Add(age);
            }

            // Keep the vanilla first-age ordering stable even when the mod is
            // reloaded and the asset already exists in the library.
            library.list.Remove(age);
            library.list.Add(age);
        }

        private static void LinkSlotPools(WorldAgeLibrary library,
            WorldAgeAsset age)
        {
            if (library.pool_by_slots == null)
                library.pool_by_slots =
                    new Dictionary<int, List<WorldAgeAsset>>();
            foreach (List<WorldAgeAsset> pool in library.pool_by_slots.Values)
                pool?.RemoveAll(item => ReferenceEquals(item, age));

            if (age.default_slots == null) return;
            for (int i = 0; i < age.default_slots.Count; i++)
            {
                int slot = age.default_slots[i];
                if (slot < 1 || slot > 8) continue;
                if (!library.pool_by_slots.TryGetValue(slot,
                        out List<WorldAgeAsset> pool))
                {
                    pool = new List<WorldAgeAsset>();
                    library.pool_by_slots[slot] = pool;
                }
                if (!pool.Contains(age)) pool.Add(age);
            }
        }

        private static void LinkCurrentWorldLaw(WorldAgeAsset age)
        {
            WorldLaws worldLaws = World.world?.world_laws;
            if (worldLaws == null || worldLaws.list == null ||
                worldLaws.dict == null) return;
            worldLaws.add(new PlayerOptionData(age.id)
            {
                boolVal = true
            });
        }
    }
}
