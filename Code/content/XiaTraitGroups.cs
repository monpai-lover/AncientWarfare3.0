namespace AncientWarfare3.content
{
    /// <summary>
    ///     特质组注册(移植自 AW2 TraitGroupLibrary)。
    ///     - aw2:古代战争主特质组(蓝 #3BAFFF)
    ///     - aw_social_identity:社会身份组(橙 #FF9300,组内互斥——同一单位只能有一个社会身份)
    ///
    ///     新版 API:AssetManager.trait_groups(ActorTraitGroupLibrary),add(new ActorTraitGroupAsset{...})。
    /// </summary>
    public static class XiaTraitGroups
    {
        public const string AW2 = "aw2";
        public const string SOCIAL_IDENTITY = "aw_social_identity";

        public static void Init()
        {
            // 新版 ActorTraitGroupAsset.color 是十六进制字符串(基类 BaseCategoryAsset.color)
            var aw2 = new ActorTraitGroupAsset
            {
                id = AW2,
                name = "trait_group_aw2",
                color = "#3BAFFF"
            };
            AssetManager.trait_groups.add(aw2);

            var social = new ActorTraitGroupAsset
            {
                id = SOCIAL_IDENTITY,
                name = "trait_group_social_identity",
                color = "#FF9300"
            };
            AssetManager.trait_groups.add(social);
        }
    }
}
