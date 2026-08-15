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
            pState.Raid ??= new BanditRaidMissionState();
            pState.Raid.MemberActorIds ??=
                new System.Collections.Generic.List<long>();
            pState.SuppressionExpiryByKingdomId ??=
                new System.Collections.Generic.Dictionary<long, int>();
        }
    }
}
