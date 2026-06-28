namespace AncientWarfare3.content
{
    /// <summary>
    ///     夏朝王国资产注册:nomads_Xia(野生/游牧)与 Xia(文明)。
    ///     <see cref="XiaRace"/> 的 kingdom_id_wild / kingdom_id_civilization 指向这两个 id。
    ///
    ///     克隆链沿用原版 human:nomads_human → nomads_Xia,human → Xia。
    ///     (蓝图 1.97:Xia/nomads_Xia,tag civ+Xia,友好 human/Xia/neutral/good。)
    /// </summary>
    public static class XiaKingdom
    {
        public static void Init()
        {
            // —— 野生/游牧夏国 ——
            KingdomAsset nomads = AssetManager.kingdoms.clone("nomads_Xia", "nomads_human");
            nomads.group_main = true;
            nomads.default_kingdom_color = ColorAsset.tryMakeNewColorAsset("#C8A24D");
            nomads.setIcon("ui/Icons/iconXias");
            nomads.addTag("Xia");
            nomads.addTag("sliceable");
            nomads.addFriendlyTag("Xia");
            nomads.addFriendlyTag("human");

            // —— 文明夏国 ——
            KingdomAsset civ = AssetManager.kingdoms.clone("Xia", "nomads_Xia");
            civ.group_main = true;
            civ.clearKingdomColor();
            civ.setIcon("ui/Icons/iconXias");
            civ.civ = true;
            civ.mobs = false;
            civ.addTag("civ");
            civ.addTag("Xia");
            civ.addFriendlyTag("Xia");
            civ.addFriendlyTag("human");
            civ.addFriendlyTag("neutral");
            civ.addFriendlyTag("good");
            civ.addEnemyTag("bandit");

            // 原版 human 王国对夏国友好
            KingdomAsset human = AssetManager.kingdoms.get("human");
            human?.addFriendlyTag("Xia");
            KingdomAsset nomadsHuman = AssetManager.kingdoms.get("nomads_human");
            nomadsHuman?.addFriendlyTag("Xia");
        }
    }
}
