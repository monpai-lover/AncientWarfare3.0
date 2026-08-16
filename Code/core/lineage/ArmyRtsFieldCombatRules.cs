namespace AncientWarfare3.core.lineage
{
    // 野战战斗脱离判定：两军在任意位置交火时，把 actor 交给原版战斗 AI，
    // 战斗结束后再收回 RTS 控制。与城内攻城脱离（ArmyRtsWarLifecycleRules）
    // 互补——后者仅覆盖"目标城内 + 城内有敌"，本规则覆盖野外相撞。
    public static class ArmyRtsFieldCombatRules
    {
        // 交战单位占活跃战斗员比例达到此值即释放到原版战斗。
        // 降至 25% 以应对小规模遭遇战（前排少数士兵先接触敌人）。
        public const int EngageReleasePercent = 25;

        // 已释放后，降到此值以下才收回，制造滞后避免边界反复抖动。
        public const int DisengageResumePercent = 5;

        public static bool ShouldReleaseToFieldCombat(
            bool pAlreadyReleased, int pEngagedCombatants,
            int pLiveCombatants, bool pCaptainEngaged)
        {
            if (pCaptainEngaged) return true;
            int engaged = pEngagedCombatants < 0 ? 0 : pEngagedCombatants;
            if (pLiveCombatants <= 0) return false;
            if (engaged > pLiveCombatants) engaged = pLiveCombatants;

            int percent = (int)((long)engaged * 100L / pLiveCombatants);
            int threshold = pAlreadyReleased
                ? DisengageResumePercent
                : EngageReleasePercent;
            return pAlreadyReleased
                ? percent > threshold
                : percent >= threshold;
        }

        // 野战交接以将领为锚点。若将领没有当前可交战的敌人，成员的
        // 残留 attack_target 不足以让整军持续取消战略路线。
        public static bool ShouldKeepFieldCombat(bool pAlreadyReleased,
            bool pCaptainHasCombatTarget, int pEngagedCombatants,
            int pLiveCombatants, bool pCaptainEngaged)
        {
            if (pAlreadyReleased)
                return pCaptainHasCombatTarget || pEngagedCombatants > 0;
            if (!pCaptainHasCombatTarget) return false;
            return ShouldReleaseToFieldCombat(false, pEngagedCombatants,
                pLiveCombatants, pCaptainEngaged);
        }

        public static bool ShouldAbortFieldCombatFromP0(
            bool pFieldCombatReleased, bool pCaptainHasCombatTarget,
            bool pAnyMemberEngaged)
        {
            return pFieldCombatReleased && !pCaptainHasCombatTarget &&
                   !pAnyMemberEngaged;
        }

        public static bool IsMemberEngaged(bool pImmediateAttackTarget,
            bool pValidBehaviourTarget)
        {
            return pImmediateAttackTarget || pValidBehaviourTarget;
        }

        public static bool ShouldRequestFieldCombatFromP0(
            bool missionActive, bool alreadyReleased,
            bool contactActorIsCaptain,
            bool captainHasValidCombatTarget)
        {
            return missionActive && !alreadyReleased &&
                   contactActorIsCaptain && captainHasValidCombatTarget;
        }
    }
}
