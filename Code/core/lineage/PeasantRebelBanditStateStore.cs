using System;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditStateStore
    {
        internal static bool TryRead(Kingdom pKingdom,
            out PeasantRebelBanditStrongholdState pState)
        {
            pState = null;
            if (pKingdom?.data == null) return false;
            try
            {
                pKingdom.data.get(
                    LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE,
                    out string json, "");
                if (string.IsNullOrWhiteSpace(json)) return false;
                PeasantRebelBanditStrongholdState state =
                    JsonConvert.DeserializeObject<
                        PeasantRebelBanditStrongholdState>(json);
                if (!IsReadable(state)) return false;
                Normalize(state);
                pState = state;
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit stronghold state read failed: " +
                                    e.Message);
                return false;
            }
        }

        internal static bool Write(Kingdom pKingdom,
            PeasantRebelBanditStrongholdState pState)
        {
            if (pKingdom?.data == null || pState == null) return false;
            try
            {
                pState.SchemaVersion =
                    PeasantRebelBanditStrongholdState.CurrentSchemaVersion;
                Normalize(pState);
                pKingdom.data.set(
                    LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE,
                    JsonConvert.SerializeObject(pState));
                PeasantRebelBanditPressureService.InvalidateTargetIndex();
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Bandit stronghold state write failed: " +
                                    e.Message);
                return false;
            }
        }

        internal static void Clear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(
                LineageKeys.MANDATE_REBEL_BANDIT_STRONGHOLD_STATE, "");
            PeasantRebelBanditPressureService.InvalidateTargetIndex();
        }

        internal static bool TryResolveActive(Kingdom pKingdom,
            out PeasantRebelBanditStrongholdState pState)
        {
            return TryRead(pKingdom, out pState) &&
                   pState.Phase == BanditStrongholdPhase.Active &&
                   pState.StrongholdCityId > 0 &&
                   pState.MotherCityId > 0 &&
                   pState.OriginKingdomId > 0 &&
                   pState.FixedZoneKeys.Count > 0;
        }

        private static bool IsReadable(
            PeasantRebelBanditStrongholdState pState)
        {
            return pState != null && pState.SchemaVersion > 0 &&
                   pState.SchemaVersion <=
                   PeasantRebelBanditStrongholdState.CurrentSchemaVersion;
        }

        private static void Normalize(
            PeasantRebelBanditStrongholdState pState)
        {
            pState.FixedZoneKeys ??= new System.Collections.Generic.List<
                string>();
            pState.WallPoints ??= new System.Collections.Generic.List<
                BanditStrongholdPoint>();
            foreach (BanditStrongholdPoint point in pState.WallPoints)
                if (point != null) point.OriginalTopTypeId ??= "";
            pState.Towers ??= new System.Collections.Generic.List<
                BanditStrongholdTower>();
            foreach (BanditStrongholdTower tower in pState.Towers)
                if (tower != null) tower.AssetId ??= "";
            pState.Raid ??= new BanditRaidMissionState();
            pState.Raid.MemberActorIds ??=
                new System.Collections.Generic.List<long>();
            pState.Raid.CarriedFoodByResourceId ??=
                new System.Collections.Generic.Dictionary<string, int>();
            pState.Raid.CarriedFoodByActorId ??=
                new System.Collections.Generic.Dictionary<long,
                    System.Collections.Generic.Dictionary<string, int>>();
            foreach (long actorId in new System.Collections.Generic.List<long>(
                         pState.Raid.CarriedFoodByActorId.Keys))
                pState.Raid.CarriedFoodByActorId[actorId] ??=
                    new System.Collections.Generic.Dictionary<string, int>();
            pState.SuppressionExpiryByKingdomId ??=
                new System.Collections.Generic.Dictionary<long, int>();
            pState.InheritedStrongholdCityIds ??=
                new System.Collections.Generic.List<long>();
            pState.Pressure = System.Math.Max(0, System.Math.Min(
                PeasantRebelBanditPressureRules.MaximumPressure,
                pState.Pressure));
        }
    }
}
