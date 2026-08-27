using System;
using System.Collections.Generic;
using System.Globalization;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyMissionRestoreDisposition
    {
        Invalid = 0,
        Restored = 1,
        NeedsAssignment = 2
    }

    public sealed class ArmyMissionStoredIntent
    {
        public long WarId { get; set; } = -1L;
        public long FrontId { get; set; } = -1L;
        public string Role { get; set; } = string.Empty;
        public string Posture { get; set; } = string.Empty;
        public string ProposalKind { get; set; } = "attack";
        public long TargetCityId { get; set; } = -1L;
        public int TargetStrength { get; set; }
        public bool PlayerOrder { get; set; }
        public double IssuedTime { get; set; } = -1d;

        public ArmyMissionStoredIntent Copy()
        {
            return (ArmyMissionStoredIntent)MemberwiseClone();
        }
    }

    public sealed class ArmyMissionRestoreFacts
    {
        public long ArmyId { get; set; } = -1L;
        public long KingdomId { get; set; } = -1L;
        public bool ArmyAlive { get; set; }
        public bool ArmyKingdomMatches { get; set; }
        public bool WarActive { get; set; }
        public bool TargetAlive { get; set; }
        public bool TargetBelongsToMissionWar { get; set; }

        public ArmyMissionRestoreFacts Copy(bool? pArmyAlive = null,
            bool? pArmyKingdomMatches = null, bool? pWarActive = null,
            bool? pTargetAlive = null,
            bool? pTargetBelongsToMissionWar = null)
        {
            return new ArmyMissionRestoreFacts
            {
                ArmyId = ArmyId,
                KingdomId = KingdomId,
                ArmyAlive = pArmyAlive ?? ArmyAlive,
                ArmyKingdomMatches = pArmyKingdomMatches ??
                                        ArmyKingdomMatches,
                WarActive = pWarActive ?? WarActive,
                TargetAlive = pTargetAlive ?? TargetAlive,
                TargetBelongsToMissionWar = pTargetBelongsToMissionWar ??
                                            TargetBelongsToMissionWar
            };
        }
    }

    public static class ArmyMissionPersistenceRules
    {
        private static readonly string[] Fields =
        {
            "war_id",
            "front_id",
            "role",
            "posture",
            "proposal_kind",
            "target_city_id",
            "target_strength",
            "player_order",
            "issued_time"
        };

        public static IReadOnlyList<string> PersistedFieldNames => Fields;

        public static ArmyMissionStoredIntent Encode(ArmyRtsMission pMission)
        {
            if (pMission == null) return null;
            return new ArmyMissionStoredIntent
            {
                WarId = pMission.WarId,
                FrontId = pMission.FrontId,
                Role = pMission.Role.ToString().ToLowerInvariant(),
                Posture = pMission.Posture.ToString().ToLowerInvariant(),
                ProposalKind = pMission.ProposalKind.ToString().
                    ToLowerInvariant(),
                TargetCityId = pMission.TargetCityId,
                TargetStrength = Math.Max(0, pMission.TargetStrength),
                PlayerOrder = pMission.PlayerOrder,
                IssuedTime = pMission.IssuedTime
            };
        }

        public static bool TryRestore(ArmyMissionStoredIntent pStored,
            ArmyMissionRestoreFacts pFacts, out ArmyRtsMission pMission,
            out ArmyRtsState pInitialState)
        {
            double currentWorldTime = pStored?.IssuedTime >= 0d &&
                                      !double.IsNaN(pStored.IssuedTime) &&
                                      !double.IsInfinity(pStored.IssuedTime)
                ? pStored.IssuedTime
                : 0d;
            return ResolveRestore(pStored, pFacts, currentWorldTime,
                       out pMission, out pInitialState) ==
                   ArmyMissionRestoreDisposition.Restored;
        }

        public static ArmyMissionRestoreDisposition ResolveRestore(
            ArmyMissionStoredIntent pStored,
            ArmyMissionRestoreFacts pFacts, double currentWorldTime,
            out ArmyRtsMission pMission,
            out ArmyRtsState pInitialState)
        {
            pMission = null;
            pInitialState = ArmyRtsState.Idle;
            if (pStored == null || pFacts == null ||
                !pFacts.ArmyAlive || !pFacts.ArmyKingdomMatches ||
                !pFacts.WarActive || pStored.WarId < 0L)
                return ArmyMissionRestoreDisposition.Invalid;

            pInitialState = ArmyRtsState.Rally;
            if (!pFacts.TargetAlive ||
                !pFacts.TargetBelongsToMissionWar ||
                pStored.TargetCityId < 0L)
                return ArmyMissionRestoreDisposition.NeedsAssignment;

            ArmyRtsRole role = ParseOrDefault(pStored.Role,
                ArmyRtsRole.Assault);
            ArmyRtsPosture posture = ParseOrDefault(pStored.Posture,
                ArmyRtsPosture.Automatic);
            ArmyRtsProposalKind proposalKind = ParseOrDefault(
                pStored.ProposalKind, ArmyRtsProposalKind.Attack);
            double issuedTime = pStored.IssuedTime;
            if (issuedTime < 0d || double.IsNaN(issuedTime) ||
                double.IsInfinity(issuedTime))
                issuedTime = NormalizeWorldTime(currentWorldTime);

            pMission = new ArmyRtsMission
            {
                ArmyId = pFacts.ArmyId,
                KingdomId = pFacts.KingdomId,
                WarId = pStored.WarId,
                FrontId = pStored.FrontId >= 0L
                    ? pStored.FrontId
                    : pStored.TargetCityId,
                TargetCityId = pStored.TargetCityId,
                TargetStrength = Math.Max(0, pStored.TargetStrength),
                ProposalKind = proposalKind,
                Role = role,
                Posture = posture,
                PlayerOrder = pStored.PlayerOrder,
                IssuedTime = issuedTime
            };
            return ArmyMissionRestoreDisposition.Restored;
        }

        private static T ParseOrDefault<T>(string pValue, T pDefault)
            where T : struct, Enum
        {
            return TryParseDefined(pValue, out T parsed)
                ? parsed
                : pDefault;
        }

        private static double NormalizeWorldTime(double pWorldTime)
        {
            return pWorldTime >= 0d && !double.IsNaN(pWorldTime) &&
                   !double.IsInfinity(pWorldTime)
                ? pWorldTime
                : 0d;
        }

        private static bool TryParseDefined<T>(string pValue,
            out T pResult) where T : struct, Enum
        {
            return Enum.TryParse(pValue, true, out pResult) &&
                   Enum.IsDefined(typeof(T), pResult);
        }
    }

#if !AW3_RULES_TESTS
    internal static class ArmyMissionPersistence
    {
        private static readonly Dictionary<long, ArmyRtsMission> Restored =
            new Dictionary<long, ArmyRtsMission>();

        public static void Persist(Army pArmy, ArmyRtsMission pMission)
        {
            if (pArmy?.data == null || pMission == null) return;
            ArmyMissionStoredIntent stored =
                ArmyMissionPersistenceRules.Encode(pMission);
            if (stored == null) return;
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_WAR_ID, stored.WarId);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_FRONT_ID, stored.FrontId);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_ROLE, stored.Role);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_POSTURE, stored.Posture);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_PROPOSAL_KIND,
                stored.ProposalKind);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_TARGET_CITY_ID,
                stored.TargetCityId);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_TARGET_STRENGTH,
                stored.TargetStrength);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_PLAYER_ORDER,
                stored.PlayerOrder);
            pArmy.data.set(LineageKeys.AW_ARMY_RTS_ISSUED_TIME,
                stored.IssuedTime.ToString("R", CultureInfo.InvariantCulture));
            Restored[pArmy.id] = pMission;
        }

        public static bool TryRestore(Army pArmy,
            out ArmyRtsMission pMission, out ArmyRtsState pInitialState)
        {
            pMission = null;
            pInitialState = ArmyRtsState.Idle;
            if (pArmy?.data == null) return false;
            ArmyMissionStoredIntent stored = Read(pArmy);
            if (stored == null)
            {
                // 存档里没有任务意图(存档时就无任务,或字段未持久化)。
                // 这里原本静默返回,军队于是永久停在 Idle 显示"等待军令",
                // 之后没有任何机制会再为它分配任务。交回战争总监重新评估。
                RequestDirectorAssignment(pArmy);
                return false;
            }
            Kingdom kingdom = SafeKingdom(pArmy);
            War war = FindWar(stored.WarId);
            City target = FindCity(stored.TargetCityId);
            var facts = new ArmyMissionRestoreFacts
            {
                ArmyId = pArmy.id,
                KingdomId = kingdom?.id ?? -1L,
                ArmyAlive = SafeArmyAlive(pArmy),
                ArmyKingdomMatches = IsLiveKingdom(kingdom) &&
                                     SafeWarHasKingdom(war, kingdom),
                WarActive = IsActiveWar(war),
                TargetAlive = IsLiveCity(target),
                TargetBelongsToMissionWar =
                    IsTargetInWar(war, target, kingdom)
            };
            ArmyMissionRestoreDisposition disposition =
                ArmyMissionPersistenceRules.ResolveRestore(stored, facts,
                    LineageService.CurTime(), out pMission,
                    out pInitialState);
            if (disposition == ArmyMissionRestoreDisposition.Invalid)
                return RejectRestore(pArmy);
            if (disposition ==
                ArmyMissionRestoreDisposition.NeedsAssignment)
            {
                Restored.Remove(pArmy.id);
                KingdomWarDirectorService.OnArmyChanged(kingdom);
                return false;
            }
            if (!ArmyRtsControllerService.ValidateMissionTarget(pArmy,
                    pMission))
                return RejectRestore(pArmy);
            Persist(pArmy, pMission);
            KingdomWarDirectorService.OnArmyChanged(kingdom);
            return true;
        }

        public static bool TryGetRestored(Army pArmy,
            out ArmyRtsMission pMission)
        {
            pMission = null;
            return pArmy?.data != null &&
                   Restored.TryGetValue(pArmy.id, out pMission);
        }

        public static void RebuildRuntime()
        {
            Restored.Clear();
            if (World.world?.armies == null) return;
            foreach (Army army in World.world.armies)
                TryRestore(army, out _, out _);
        }

        public static void OnArmyDisposed(Army pArmy)
        {
            if (pArmy == null) return;
            Restored.Remove(pArmy.id);
        }

        public static void Invalidate(Army pArmy)
        {
            Clear(pArmy);
        }

        public static void ClearRuntime()
        {
            Restored.Clear();
        }

        private static ArmyMissionStoredIntent Read(Army pArmy)
        {
            if (pArmy?.data == null) return null;
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_WAR_ID,
                out long warId, -1L);
            if (warId < 0L) return null;
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_FRONT_ID,
                out long frontId, -1L);
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_ROLE,
                out string role, string.Empty);
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_POSTURE,
                out string posture, string.Empty);
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_PROPOSAL_KIND,
                out string proposalKind, "attack");
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_TARGET_CITY_ID,
                out long targetCityId, -1L);
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_TARGET_STRENGTH,
                out int targetStrength, 0);
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_PLAYER_ORDER,
                out bool playerOrder, false);
            pArmy.data.get(LineageKeys.AW_ARMY_RTS_ISSUED_TIME,
                out string issuedText, string.Empty);
            double.TryParse(issuedText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double issuedTime);
            return new ArmyMissionStoredIntent
            {
                WarId = warId,
                FrontId = frontId,
                Role = role ?? string.Empty,
                Posture = posture ?? string.Empty,
                ProposalKind = string.IsNullOrWhiteSpace(proposalKind)
                    ? "attack"
                    : proposalKind,
                TargetCityId = targetCityId,
                TargetStrength = Math.Max(0, targetStrength),
                PlayerOrder = playerOrder,
                IssuedTime = issuedTime
            };
        }

        private static void Clear(Army pArmy)
        {
            if (pArmy?.data == null) return;
            pArmy.data.removeLong(LineageKeys.AW_ARMY_RTS_WAR_ID);
            pArmy.data.removeLong(LineageKeys.AW_ARMY_RTS_FRONT_ID);
            pArmy.data.removeString(LineageKeys.AW_ARMY_RTS_ROLE);
            pArmy.data.removeString(LineageKeys.AW_ARMY_RTS_POSTURE);
            pArmy.data.removeString(LineageKeys.AW_ARMY_RTS_PROPOSAL_KIND);
            pArmy.data.removeLong(LineageKeys.AW_ARMY_RTS_TARGET_CITY_ID);
            pArmy.data.removeInt(LineageKeys.AW_ARMY_RTS_TARGET_STRENGTH);
            pArmy.data.removeBool(LineageKeys.AW_ARMY_RTS_PLAYER_ORDER);
            pArmy.data.removeString(LineageKeys.AW_ARMY_RTS_ISSUED_TIME);
            Restored.Remove(pArmy.id);
        }

        private static bool RejectRestore(Army pArmy)
        {
            if (!AW3MultiplayerReplicaScope.IsReplicaSession)
                Clear(pArmy);
            // 旧任务作废后同样要交回战争总监,否则军队清掉任务就再没人管,
            // 停在"等待军令"直到下次战争事件偶然唤醒。
            RequestDirectorAssignment(pArmy);
            return false;
        }

        // 让战争总监在下一轮把这支军队纳入提案评估。
        private static void RequestDirectorAssignment(Army pArmy)
        {
            if (pArmy?.data == null) return;
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            Kingdom kingdom = SafeKingdom(pArmy);
            if (kingdom?.data == null) return;
            try { KingdomWarDirectorService.OnArmyChanged(kingdom); }
            catch { }
        }

        private static bool IsTargetInWar(War pWar, City pTarget,
            Kingdom pArmyKingdom)
        {
            if (!IsActiveWar(pWar) || !IsLiveCity(pTarget) ||
                !IsLiveKingdom(pArmyKingdom) ||
                !IsLiveKingdom(pTarget.kingdom)) return false;
            try
            {
                return pTarget.kingdom == pArmyKingdom ||
                       pWar.hasKingdom(pTarget.kingdom);
            }
            catch { return false; }
        }

        private static bool SafeArmyAlive(Army pArmy)
        {
            try { return pArmy?.data != null && pArmy.isAlive(); }
            catch { return false; }
        }

        private static bool IsActiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static bool IsLiveKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && !pKingdom.isRekt() &&
                       pKingdom.isAlive();
            }
            catch { return false; }
        }

        private static bool IsLiveCity(City pCity)
        {
            try
            {
                return pCity?.data != null && !pCity.isRekt() &&
                       pCity.isAlive();
            }
            catch { return false; }
        }

        private static bool SafeWarHasKingdom(War pWar, Kingdom pKingdom)
        {
            try { return IsActiveWar(pWar) && pWar.hasKingdom(pKingdom); }
            catch { return false; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
#endif
}
