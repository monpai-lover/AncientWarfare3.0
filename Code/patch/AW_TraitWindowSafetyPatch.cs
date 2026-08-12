using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(TraitsContainer<ActorTrait, ActorTraitButton>), "sortTraits")]
    internal static class AW_TraitWindowSafetyPatch
    {
        [HarmonyFinalizer]
        private static System.Exception Finalizer(System.Exception __exception)
        {
            return __exception is System.NullReferenceException ? null : __exception;
        }
    }
}
