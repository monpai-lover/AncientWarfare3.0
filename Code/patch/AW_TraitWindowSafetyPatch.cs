using HarmonyLib;
using System;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(TraitsContainer<ActorTrait, ActorTraitButton>), "sortTraits")]
    internal static class AW_TraitWindowSafetyPatch
    {
        private static int _lastLoggedFrame = -1;

        [HarmonyPrefix]
        private static bool Prefix(
            TraitsContainer<ActorTrait, ActorTraitButton> __instance)
        {
            if (__instance == null) return false;
            // TraitsContainer.Awake binds this owner, but Unity can invoke a
            // grid callback while the UnitWindow is transitioning. In that
            // interval the native method dereferences a null owner.
            object owner = Traverse.Create(__instance).Field("_trait_window").GetValue();
            if (owner != null) return true;
            LogOnce("Trait sort skipped before UnitWindow trait owner binding");
            return false;
        }

        [HarmonyFinalizer]
        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (!(__exception is NullReferenceException)) return __exception;
            LogOnce("Trait sort skipped during incomplete UnitWindow binding " +
                    "(stack=" + __exception.StackTrace + ")");
            return null;
        }

        private static void LogOnce(string pMessage)
        {
            int frame = Time.frameCount;
            if (_lastLoggedFrame == frame) return;
            _lastLoggedFrame = frame;
            ModClass.LogWarning(pMessage + " (frame=" + frame + ")");
        }
    }
}
