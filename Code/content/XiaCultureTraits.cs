namespace AncientWarfare3.content
{
    internal static class XiaCultureTraits
    {
        public const string IntegratedTraitId = "aw_xia_integrated";
        public const string FullyIntegratedTraitId =
            "aw_xia_fully_integrated";

        public static void Init()
        {
            EnsureTrait(IntegratedTraitId);
            EnsureTrait(FullyIntegratedTraitId);
        }

        private static void EnsureTrait(string pTraitId)
        {
            if (AssetManager.culture_traits.get(pTraitId) != null) return;

            AssetManager.culture_traits.add(new CultureTrait
            {
                id = pTraitId,
                group_id = "special",
                path_icon = "ui/Icons/iconXias",
                can_be_given = false,
                can_be_removed = false,
                can_be_in_book = false,
                spawn_random_trait_allowed = false,
                has_description_2 = false
            });

            // Culture.save() persists both markers in CultureData.saved_traits.
        }
    }
}
