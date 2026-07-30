using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SelectedActorAvatarSafetyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UnitAvatarLoader),
            nameof(UnitAvatarLoader.load), new[] { typeof(Actor) })]
        private static bool LoadActor_Prefix(Actor pActor)
        {
            // Selection can briefly retain an actor while its transfer or
            // disposal has cleared the references AvatarData dereferences.
            if (pActor == null || pActor.data == null ||
                pActor.asset == null || pActor.kingdom == null)
                return false;
            return true;
        }
    }
}
