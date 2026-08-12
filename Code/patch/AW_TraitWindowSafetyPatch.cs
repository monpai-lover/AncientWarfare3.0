using HarmonyLib;
using System;
using UnityEngine;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(TraitsContainer<ActorTrait, ActorTraitButton>), "sortTraits")]
    internal static class AW_TraitWindowSafetyPatch
    {
        private static int _lastLoggedFrame = -1;

        [HarmonyFinalizer]
        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (!(__exception is NullReferenceException)) return __exception;
            int frame = Time.frameCount;
            if (_lastLoggedFrame != frame)
            {
                _lastLoggedFrame = frame;
                ModClass.LogWarning("Trait sort skipped during incomplete UnitWindow binding " +
                                    "(frame=" + frame + ", stack=" + __exception.StackTrace + ")");
            }
            return null;
        }
    }
}
