namespace AncientWarfare3.core.lineage
{
    public sealed class ArmyRtsMissionTargetFacts
    {
        public ArmyRtsProposalKind Kind { get; set; }
        public ArmyRtsObjectiveState Objective { get; set; }
        public bool CityLive { get; set; }
        public bool ArmyKingdomLive { get; set; }
        public bool WarActive { get; set; }
        public bool ArmyKingdomInWar { get; set; }
        public bool TargetKingdomInWar { get; set; }
        public bool TargetFriendly { get; set; }
        public bool TargetSafe { get; set; }
        public bool ControlledFront { get; set; }
    }

    public sealed class ArmyRtsMissionTargetDecision
    {
        public bool Valid { get; internal set; }
        public string Reason { get; internal set; } = string.Empty;

        internal static ArmyRtsMissionTargetDecision Accept()
        {
            return new ArmyRtsMissionTargetDecision
            {
                Valid = true,
                Reason = "valid"
            };
        }

        internal static ArmyRtsMissionTargetDecision Reject(string pReason)
        {
            return new ArmyRtsMissionTargetDecision
            {
                Valid = false,
                Reason = pReason ?? "invalid"
            };
        }
    }

    public static class ArmyRtsMissionTargetRules
    {
        public static ArmyRtsMissionTargetDecision Validate(
            ArmyRtsMissionTargetFacts pFacts)
        {
            if (pFacts == null) return ArmyRtsMissionTargetDecision.
                Reject("missing_facts");
            if (!pFacts.CityLive) return ArmyRtsMissionTargetDecision.
                Reject("missing_city");
            if (!pFacts.ArmyKingdomLive) return ArmyRtsMissionTargetDecision.
                Reject("missing_army_kingdom");
            if (!pFacts.WarActive || !pFacts.ArmyKingdomInWar)
                return ArmyRtsMissionTargetDecision.Reject("war_inactive");

            switch (pFacts.Kind)
            {
                case ArmyRtsProposalKind.Attack:
                    // During a city assault the attacker can become the
                    // temporary controller before the capture transaction is
                    // finalized. Keep the attack mission alive while that
                    // city is an open defense objective; otherwise the
                    // director invalidates the mission mid-siege and the
                    // army falls back to "awaiting orders".
                    return pFacts.TargetKingdomInWar &&
                           ((!pFacts.TargetFriendly &&
                             pFacts.Objective == ArmyRtsObjectiveState.OpenAttack) ||
                            (pFacts.TargetFriendly &&
                             pFacts.Objective == ArmyRtsObjectiveState.OpenDefense))
                        ? ArmyRtsMissionTargetDecision.Accept()
                        : ArmyRtsMissionTargetDecision.Reject(
                            "attack_target_not_open_enemy");
                case ArmyRtsProposalKind.Defend:
                    return pFacts.TargetFriendly &&
                           pFacts.Objective == ArmyRtsObjectiveState.OpenDefense
                        ? ArmyRtsMissionTargetDecision.Accept()
                        : ArmyRtsMissionTargetDecision.Reject(
                            "defense_target_not_open_friendly");
                case ArmyRtsProposalKind.Retreat:
                    return pFacts.TargetFriendly && pFacts.TargetSafe
                        ? ArmyRtsMissionTargetDecision.Accept()
                        : ArmyRtsMissionTargetDecision.Reject(
                            "retreat_target_not_safe");
                case ArmyRtsProposalKind.FrontHold:
                    return pFacts.TargetFriendly && pFacts.ControlledFront
                        ? ArmyRtsMissionTargetDecision.Accept()
                        : ArmyRtsMissionTargetDecision.Reject(
                            "front_target_not_controlled");
                default:
                    return ArmyRtsMissionTargetDecision.Reject(
                        "unknown_mission_kind");
            }
        }
    }
}
