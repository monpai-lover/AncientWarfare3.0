using UnityEngine;

namespace AncientWarfare3.content.schools
{
    internal static class SchoolAcademyBuildingContent
    {
        public const string BuildingId = "academy_Xia";
        public const string BuildingTypeId = "type_aw_school_academy";
        public const string MainPath = "buildings/civ_main/Xia/";

        public static BuildingAsset Asset { get; private set; }

        public static void Init(ArchitectureAsset pArchitecture)
        {
            if (pArchitecture == null) return;
            BuildingAsset source = AssetManager.buildings.get("library_Xia");
            if (source == null)
            {
                ModClass.LogWarning("School academy: library_Xia is unavailable.");
                return;
            }

            BuildingAsset academy = AssetManager.buildings.get(BuildingId) ??
                                    AssetManager.buildings.clone(BuildingId, "library_Xia");
            academy.type = BuildingTypeId;
            academy.group = "civ_building";
            academy.main_path = MainPath;
            academy.civ_kingdom = XiaArchitecture.ID;
            academy.kingdom = "nomads_" + XiaArchitecture.ID;
            academy.has_sprite_construction = true;
            academy.book_slots = source.book_slots;
            academy.scale_base = new Vector3(0.07975f, 0.07975f, 0.25f);
            academy.fundament = new BuildingFundament(3, 3, 2, 0);
            academy.can_be_upgraded = false;
            academy.upgrade_to = string.Empty;
            academy.upgraded_from = string.Empty;
            academy.atlas_asset = AssetManager.dynamic_sprites_library.get(academy.atlas_id)
                                  ?? AssetManager.dynamic_sprites_library.get("buildings");

            pArchitecture.addBuildingOrderKey("order_library", BuildingId);
            Asset = academy;
        }
    }
}
