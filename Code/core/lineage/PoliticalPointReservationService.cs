using System;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class PoliticalPointReservationService
    {
        private static readonly PoliticalPointReservationLedger Ledger =
            new PoliticalPointReservationLedger();

        public static bool TryReserve(long pKingdomId, int pAmount,
            out long pReservationId)
        {
            Kingdom kingdom = FindKingdom(pKingdomId);
            int available = kingdom?.data == null
                ? 0
                : Math.Max(0, (int)Math.Floor(
                    KingdomPolicyService.GetPoliticalPoints(kingdom)));
            return Ledger.TryReserve(pKingdomId, pAmount, available,
                out pReservationId);
        }

        public static bool Commit(long pReservationId)
        {
            if (!Ledger.TryCommit(pReservationId, out long kingdomId,
                    out int amount)) return false;
            Kingdom kingdom = FindKingdom(kingdomId);
            if (kingdom?.data == null) return false;
            float current = KingdomPolicyService.GetPoliticalPoints(kingdom);
            kingdom.data.set(LineageKeys.POLICY_POINTS,
                Math.Max(0f, current - amount));
            return true;
        }

        public static void Release(long pReservationId)
        {
            Ledger.Release(pReservationId);
        }

        public static void Clear()
        {
            Ledger.Clear();
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0 || World.world?.kingdoms == null) return null;
            try { return World.world.kingdoms.get(pKingdomId); }
            catch { return null; }
        }
    }
}
