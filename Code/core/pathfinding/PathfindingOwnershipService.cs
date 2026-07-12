using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace AncientWarfare3.core.pathfinding
{
    internal static class PathfindingOwnershipService
    {
        private const string ExactCultiwayOwner = "inmny.cultiway";
        private static readonly PathfindingOwnershipRules Rules = new PathfindingOwnershipRules();
        private static int _assemblyInvalidated;
        private static int _auditTicks;
        private static bool _prepared;

        public static AWPathOwnerState State => Rules.State;
        public static bool IsAw3Owner => State == AWPathOwnerState.Aw3;
        public static bool ShouldIntercept => IsAw3Owner;

        public static void Prepare()
        {
            if (_prepared) return;
            _prepared = true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        public static void BeginStabilization()
        {
            Rules.BeginStabilization();
        }

        public static AWPathOwnerState ProcessMainThreadTick()
        {
            if (Interlocked.Exchange(ref _assemblyInvalidated, 0) != 0)
                Rules.OnMatchingAssemblyLoad();
            _auditTicks++;
            bool needsScan = State == AWPathOwnerState.Pending ||
                             State == AWPathOwnerState.Suspending || _auditTicks >= 300;
            if (!needsScan) return State;
            _auditTicks = 0;
            return Rules.ObserveOwners(ReadMovementOwners());
        }

        public static void ResetWorld()
        {
            Rules.ResetWorld();
            _auditTicks = 0;
            Interlocked.Exchange(ref _assemblyInvalidated, 0);
        }

        private static IEnumerable<string> ReadMovementOwners()
        {
            var owners = new HashSet<string>(StringComparer.Ordinal);
            AddOwners(owners, AccessTools.Method(typeof(Actor), nameof(Actor.goTo)));
            AddOwners(owners, AccessTools.Method(typeof(Actor), nameof(Actor.updatePathMovement)));
            AddOwners(owners, AccessTools.Method(typeof(Actor), nameof(Actor.isUsingPath)));
            AddOwners(owners, AccessTools.Method(typeof(Actor), "updateMovement",
                new[] { typeof(float), typeof(float) }));
            return owners;
        }

        private static void AddOwners(HashSet<string> pOwners, MethodBase pMethod)
        {
            Patches patches = pMethod == null ? null : Harmony.GetPatchInfo(pMethod);
            if (patches?.Owners == null) return;
            foreach (string owner in patches.Owners)
                if (!string.IsNullOrEmpty(owner)) pOwners.Add(owner);
        }

        private static void OnAssemblyLoad(object pSender, AssemblyLoadEventArgs pArgs)
        {
            string name = pArgs?.LoadedAssembly?.GetName()?.Name;
            if (!string.Equals(name, "Cultiway", StringComparison.OrdinalIgnoreCase)) return;
            Interlocked.Exchange(ref _assemblyInvalidated, 1);
        }
    }
}
