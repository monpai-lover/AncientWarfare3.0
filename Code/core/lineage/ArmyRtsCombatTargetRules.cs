namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Cheap, side-effect-free gate for RTS actor targets.  Civilian status is
    /// intentionally not a rejection condition: population protection is
    /// enforced by the engine's canAttackTarget result and remains authoritative.
    /// Royal and city authorities are excluded before a target is handed to a
    /// combat behaviour so soldiers do not spend combat ticks chasing them.
    /// </summary>
    public static class ArmyRtsCombatTargetRules
    {
        public static bool ShouldAllowTarget(bool targetAlive,
            bool targetHostile, bool targetCanBeAttacked,
            bool targetIsKing, bool targetIsCityLeader,
            bool targetIsHeir)
        {
            return targetAlive && targetHostile && targetCanBeAttacked &&
                   !targetIsKing && !targetIsCityLeader && !targetIsHeir;
        }
    }
}
