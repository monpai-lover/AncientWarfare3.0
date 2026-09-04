using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_DiplomacyConversationPatch
    {
        private static int _creatingAllianceDepth;

        private sealed class AllianceCreationFrame
        {
            public bool Released;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AllianceManager), nameof(AllianceManager.newAlliance))]
        private static void NewAlliancePrefix(out AllianceCreationFrame __state)
        {
            __state = new AllianceCreationFrame();
            _creatingAllianceDepth++;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AllianceManager), nameof(AllianceManager.newAlliance))]
        private static void ReleaseNewAlliancePostfix(
            Kingdom pKingdom, Kingdom pKingdom2, Alliance __result,
            AllianceCreationFrame __state)
        {
            ReleaseCreationFrame(__state);
            bool hasTwoFounders = __result?.data != null &&
                                  pKingdom?.data != null &&
                                  pKingdom2?.data != null &&
                                  __result.hasKingdom(pKingdom) &&
                                  __result.hasKingdom(pKingdom2);
            if (!AllianceConversationRules.ShouldRecordCreation(
                    namingCallbacksCompleted: true, hasTwoFounders)) return;
            string finalName = AllianceConversationRules.ResolveRecordedName(
                __result.name, "");
            if (string.IsNullOrEmpty(finalName)) return;
            DiplomacyConversationService.RecordAllianceFormed(pKingdom,
                pKingdom2, finalName);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(AllianceManager), nameof(AllianceManager.newAlliance))]
        private static void NewAllianceFinalizer(
            AllianceCreationFrame __state)
        {
            ReleaseCreationFrame(__state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Alliance), nameof(Alliance.join))]
        private static void AllianceJoinPrefix(Alliance __instance,
            Kingdom pKingdom, out List<long> __state)
        {
            __state = null;
            if (__instance?.data == null || pKingdom?.data == null ||
                __instance.hasKingdom(pKingdom)) return;
            __state = SnapshotMemberIds(__instance, pKingdom);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Alliance), nameof(Alliance.join))]
        private static void AllianceJoinPostfix(Alliance __instance,
            Kingdom pKingdom, List<long> __state, bool __result)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (!__result || __state == null ||
                __instance?.data == null || pKingdom?.data == null ||
                !AllianceConversationRules.ShouldRecordJoin(
                    _creatingAllianceDepth > 0)) return;
            bool founded = __state.Count == 1;
            for (int i = 0; i < __state.Count; i++)
            {
                Kingdom member = FindKingdom(__state[i]);
                if (member?.data == null) continue;
                if (founded)
                    DiplomacyConversationService.RecordAllianceFormed(
                        member, pKingdom, __instance.name ?? "");
                else
                    DiplomacyConversationService.RecordAllianceJoined(
                        pKingdom, member, __instance.name ?? "");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Alliance), nameof(Alliance.leave))]
        private static void AllianceLeavePrefix(Alliance __instance,
            Kingdom pKingdom, out List<long> __state)
        {
            __state = __instance?.data != null && pKingdom?.data != null &&
                      __instance.hasKingdom(pKingdom)
                ? SnapshotMemberIds(__instance, pKingdom)
                : null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Alliance), nameof(Alliance.leave))]
        private static void AllianceLeavePostfix(Alliance __instance,
            Kingdom pKingdom, List<long> __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__state == null || __instance?.data == null ||
                pKingdom?.data == null || __instance.hasKingdom(pKingdom))
                return;
            for (int i = 0; i < __state.Count; i++)
            {
                Kingdom member = FindKingdom(__state[i]);
                if (member?.data == null) continue;
                DiplomacyConversationService.RecordAllianceLeft(pKingdom,
                    member, __instance.name ?? "");
            }
        }

        private static List<long> SnapshotMemberIds(Alliance pAlliance,
            Kingdom pExcluded)
        {
            var result = new List<long>();
            if (pAlliance?.kingdoms_hashset == null) return result;
            foreach (Kingdom member in pAlliance.kingdoms_hashset)
                if (member?.data != null && member != pExcluded)
                    result.Add(member.id);
            return result;
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static void ReleaseCreationFrame(
            AllianceCreationFrame pFrame)
        {
            if (pFrame == null || pFrame.Released) return;
            pFrame.Released = true;
            if (_creatingAllianceDepth > 0) _creatingAllianceDepth--;
        }
    }
}
