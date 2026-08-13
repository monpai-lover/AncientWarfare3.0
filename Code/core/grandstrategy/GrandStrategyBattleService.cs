using System;
using System.Linq;

namespace AncientWarfare3.core.grandstrategy
{
    public sealed class GrandStrategyBattleService
    {
        public GrandStrategyBattleRoundResult ResolveRound(
            GrandStrategyBattleState state, GrandStrategyBattleRoundInput input)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (state.Phase == GrandStrategyBattlePhase.Completed)
                throw new InvalidOperationException("battle_completed");
            if (state.LastInputFingerprint == input.Fingerprint &&
                state.Report != null && state.Report.Rounds.Count > 0)
                return state.Report.Rounds[state.Report.Rounds.Count - 1];
            int round = state.Round + 1;
            if (state.CommittedRounds.Contains(round))
                return state.Report.Rounds.First(r => r.Round == round);
            ApplyReinforcements(state, round);
            int attackerFront = GrandStrategyBattleRules.ResolveFrontline(
                state.AttackerStrength, state.Frontage);
            int defenderFront = GrandStrategyBattleRules.ResolveFrontline(
                state.DefenderStrength, state.Frontage);
            int attackerRoll = GrandStrategyBattleRules.Roll(input.WorldSeed,
                state.WarId, state.BattleId, round);
            int defenderRoll = GrandStrategyBattleRules.Roll(input.WorldSeed,
                state.WarId, state.BattleId + 1, round);
            int attackerPower = GrandStrategyBattleRules.ApplyModifier(
                attackerFront, input.AttackerTechnology,
                input.AttackerTraining, input.AttackerEquipment, 1.0, 1.0,
                input.AttackerCommanderBonus, attackerRoll + input.WeatherModifier);
            int defenderPower = GrandStrategyBattleRules.ApplyModifier(
                defenderFront, input.DefenderTechnology,
                input.DefenderTraining, input.DefenderEquipment, 1.0, 1.0,
                input.DefenderCommanderBonus, defenderRoll - input.TerrainModifier);
            int total = Math.Max(1, attackerPower + defenderPower);
            int attackerLosses = Math.Min(state.AttackerStrength,
                Math.Max(1, defenderPower * 8 / total));
            int defenderLosses = Math.Min(state.DefenderStrength,
                Math.Max(1, attackerPower * 8 / total));
            state.AttackerStrength -= attackerLosses;
            state.DefenderStrength -= defenderLosses;
            state.Round = round;
            var result = new GrandStrategyBattleRoundResult
            {
                Round = round,
                AttackerRoll = attackerRoll,
                DefenderRoll = defenderRoll,
                AttackerLosses = attackerLosses,
                DefenderLosses = defenderLosses
            };
            state.CommittedRounds.Add(round);
            state.LastInputFingerprint = input.Fingerprint;
            if (state.Report == null) state.Report = new GrandStrategyBattleReport(state.BattleId);
            state.Report.Rounds.Add(result);
            if (state.AttackerStrength == 0 || state.DefenderStrength == 0)
                state.Phase = GrandStrategyBattlePhase.Pursuit;
            return result;
        }

        public bool AddReinforcement(GrandStrategyBattleState state,
            long armyId, int strength, bool isAttacker, int arriveRound)
        {
            if (state == null || state.Phase == GrandStrategyBattlePhase.Completed ||
                strength <= 0 || arriveRound <= state.Round) return false;
            state.PendingReinforcements.Add(new GrandStrategyReinforcement(
                armyId, strength, isAttacker, arriveRound));
            return true;
        }

        public void OrderWithdrawal(GrandStrategyBattleState state,
            bool isAttacker)
        {
            if (state == null || state.Phase == GrandStrategyBattlePhase.Completed)
                return;
            state.Phase = GrandStrategyBattlePhase.Rout;
        }

        public bool ResolvePursuit(GrandStrategyBattleState state)
        {
            if (state == null || state.Phase != GrandStrategyBattlePhase.Rout &&
                state.Phase != GrandStrategyBattlePhase.Pursuit) return false;
            state.Phase = GrandStrategyBattlePhase.Completed;
            if (state.Report == null) state.Report = new GrandStrategyBattleReport(state.BattleId);
            return true;
        }

        private static void ApplyReinforcements(GrandStrategyBattleState state,
            int round)
        {
            for (int i = state.PendingReinforcements.Count - 1; i >= 0; i--)
            {
                GrandStrategyReinforcement item = state.PendingReinforcements[i];
                if (item.ArriveRound > round) continue;
                if (item.IsAttacker) state.AttackerStrength += item.Strength;
                else state.DefenderStrength += item.Strength;
                state.PendingReinforcements.RemoveAt(i);
            }
        }
    }
}
