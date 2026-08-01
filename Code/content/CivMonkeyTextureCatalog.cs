using System;

namespace AncientWarfare3.content
{
    /// <summary>
    /// Civ monkey ships with one concrete skin for each unit state, although it
    /// inherits the ten-slot advanced-civilization skin catalogue. Old saves can
    /// therefore point at warrior_2 through warrior_10, which have no sprites.
    /// Keep the slot count so saved skin ids remain valid, but map every slot to
    /// the concrete textures that exist in the game resources.
    /// </summary>
    internal static class CivMonkeyTextureCatalog
    {
        private const string TexturePathBase = "actors/species/civs/civ_monkey/";

        internal static void Repair(ActorAsset pAsset)
        {
            if (pAsset == null ||
                pAsset.id != CivMonkeyNamingRules.ActorAssetId)
                return;

            int slotCount = Math.Max(1, Math.Max(
                pAsset.skin_citizen_male?.Length ?? 0,
                Math.Max(pAsset.skin_citizen_female?.Length ?? 0,
                    pAsset.skin_warrior?.Length ?? 0)));

            pAsset.skin_citizen_male = Repeat("male_1", slotCount);
            pAsset.skin_citizen_female = Repeat("female_1", slotCount);
            pAsset.skin_warrior = Repeat("warrior_1", slotCount);
        }

        /// <summary>
        /// Saved subspecies cache skin names. A pre-repair save can consequently
        /// still return warrior_10 even after the asset catalogue was corrected.
        /// Select the concrete civ monkey texture at the final path boundary so
        /// those stale cached names cannot create an empty animation container.
        /// </summary>
        internal static bool TryGetRuntimeTexturePath(Actor pActor, out string pTexture)
        {
            pTexture = null;
            if (pActor?.asset == null ||
                pActor.asset.id != CivMonkeyNamingRules.ActorAssetId)
                return false;

            if (pActor.isEgg() || pActor.isBaby() || pActor.isKing() || pActor.isCityLeader())
                return false;

            string skin = pActor.isWarrior()
                ? "warrior_1"
                : pActor.isSexFemale() ? "female_1" : "male_1";
            pTexture = TexturePathBase + skin;
            return true;
        }

        private static string[] Repeat(string pSkin, int pCount)
        {
            var result = new string[Math.Max(1, pCount)];
            for (int i = 0; i < result.Length; i++) result[i] = pSkin;
            return result;
        }
    }
}
