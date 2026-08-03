using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class WarForceEliminationSettlementService
    {
        private const int WarsPerAuthorityCycle = 2;
        private static readonly MonthlyAuthorityWorkQueue<long> MonthlyWork =
            new MonthlyAuthorityWorkQueue<long>();
        private static readonly Dictionary<long, WarForceObservationState>
            Observations =
                new Dictionary<long, WarForceObservationState>();

        public static void ClearRuntime()
        {
            MonthlyWork.Clear();
            Observations.Clear();
        }

        public static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world?.wars == null) return;
            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            var liveWarIds = new List<long>();
            var liveSet = new HashSet<long>();
            try
            {
                foreach (War war in World.world.wars)
                {
                    if (!IsLiveWar(war)) continue;
                    liveWarIds.Add(war.data.id);
                    liveSet.Add(war.data.id);
                }
            }
            catch { return; }
            if (MonthlyWork.ScheduleMonth(monthKey, liveWarIds))
                RemoveEndedObservations(liveSet);
            MonthlyWork.Drain(WarsPerAuthorityCycle,
                (queuedMonth, warId) => ObserveWar(
                    FindWar(warId), queuedMonth, out _));
        }

        public static bool QueueIfReady(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !IsLiveWar(pWar)) return false;
            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            ObserveWar(pWar, monthKey, out _);
            return false;
        }

        internal static bool TryGetConfirmedDecision(War pWar,
            out WarForceEliminationDecision pDecision)
        {
            pDecision = default;
            if (!TryReadPotentials(pWar, out int attackers,
                    out int defenders) ||
                !Observations.TryGetValue(pWar.data.id,
                    out WarForceObservationState state) ||
                !TryReadAttackerScore(pWar, out int score)) return false;
            pDecision = WarForceEliminationRules.Resolve(attackers,
                defenders, state.AttackerZeroStreak,
                state.DefenderZeroStreak, score);
            return pDecision.Kind != WarForceEliminationDecisionKind.None;
        }

        private static bool ObserveWar(War pWar, int pMonthKey,
            out WarForceEliminationDecision pDecision)
        {
            pDecision = default;
            if (!TryReadPotentials(pWar, out int attackers,
                    out int defenders) ||
                !TryReadAttackerScore(pWar, out int score)) return false;
            if (!Observations.TryGetValue(pWar.data.id,
                    out WarForceObservationState state))
            {
                state = new WarForceObservationState();
                Observations[pWar.data.id] = state;
            }
            state.Observe(pMonthKey, attackers, defenders);
            pDecision = WarForceEliminationRules.Resolve(attackers,
                defenders, state.AttackerZeroStreak,
                state.DefenderZeroStreak, score);
            return pDecision.Kind != WarForceEliminationDecisionKind.None;
        }

        private static bool TryReadPotentials(War pWar,
            out int pAttackers, out int pDefenders)
        {
            pAttackers = 0;
            pDefenders = 0;
            if (!IsLiveWar(pWar) || MainAttacker(pWar)?.data == null ||
                MainDefender(pWar)?.data == null) return false;
            try
            {
                pAttackers = WarForceEliminationRules.AddPotential(
                    Math.Max(0, pWar.countAttackersWarriors()),
                    CountSideReserves(pWar.getAttackers()));
                pDefenders = WarForceEliminationRules.AddPotential(
                    Math.Max(0, pWar.countDefendersWarriors()),
                    CountSideReserves(pWar.getDefenders()));
                return true;
            }
            catch { return false; }
        }

        private static int CountSideReserves(IEnumerable<Kingdom> pSide)
        {
            int total = 0;
            if (pSide == null) return total;
            foreach (Kingdom kingdom in pSide)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                total = WarForceEliminationRules.AddPotential(total,
                    CityReservePoolService.CountAvailable(kingdom));
            }
            return total;
        }

        private static bool TryReadAttackerScore(War pWar, out int pScore)
        {
            pScore = 0;
            Kingdom attacker = MainAttacker(pWar);
            if (attacker?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot snapshot)) return false;
            pScore = snapshot.Score;
            return true;
        }

        private static void RemoveEndedObservations(HashSet<long> pLive)
        {
            if (Observations.Count == 0) return;
            var stale = new List<long>();
            foreach (long warId in Observations.Keys)
                if (!pLive.Contains(warId)) stale.Add(warId);
            for (int i = 0; i < stale.Count; i++)
                Observations.Remove(stale[i]);
        }

        private static bool IsLiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static Kingdom MainAttacker(War pWar)
        {
            try { return pWar?.getMainAttacker(); }
            catch { return null; }
        }

        private static Kingdom MainDefender(War pWar)
        {
            try { return pWar?.getMainDefender(); }
            catch { return null; }
        }
    }
}
