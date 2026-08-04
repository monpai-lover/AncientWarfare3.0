namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluWarService
    {
        public static bool CanDeclare(Kingdom attacker, Kingdom defender,
            out string reason)
        {
            return CanDeclare(attacker, defender,
                pZhuluAgeOverride: false, out reason);
        }

        public static bool CanDeclare(Kingdom attacker, Kingdom defender,
            bool pZhuluAgeOverride, out string reason)
        {
            reason = "";
            string currentAgeId = World.world?.map_stats?.world_age_id;
            if (!ZhuluWarRules.CanCreateDeclaration(currentAgeId))
            {
                reason = ZhuluWarRules.HopeAgeBlockedReason;
                return false;
            }
            bool ageOverride = ZhuluAgeRules.ShouldUseWarOverride(
                pZhuluAgeOverride,
                currentAgeId);
            Kingdom attackerRoot = VassalService.GetRootSuzerain(attacker);
            Kingdom defenderRoot = VassalService.GetRootSuzerain(defender);
            bool sameRoot = attackerRoot?.data != null &&
                            defenderRoot?.data != null &&
                            attackerRoot == defenderRoot;
            bool alreadyAtWar = false;
            try
            {
                alreadyAtWar = World.world?.wars?.getWar(attacker, defender,
                    pOnlyMain: false) != null;
            }
            catch { }
            bool hasMandateHistory = false;
            try
            {
                hasMandateHistory = MandateService.ReadReport()?.period_id >= 0;
            }
            catch { }

            bool allowed = ZhuluWarRules.CanStart(
                new ZhuluEligibilityFacts(
                    MandatePhaseService.CurrentPhase,
                    IsValidRealm(attacker), IsValidRealm(defender),
                    XiaizationService.CanUseMandateSystem(attacker),
                    XiaizationService.CanUseMandateSystem(defender),
                    (ageOverride
                        ? VassalService.GetSuzerain(attacker)
                        : VassalService.GetDiplomaticSuzerain(attacker))
                    ?.data != null,
                    sameRoot,
                    DiplomacyProposalService.HasActiveWarBlocker(attacker,
                        defender),
                    WarTerritoryService.AreInSameAlliance(attacker,
                        defender),
                    alreadyAtWar,
                    ageOverride: ageOverride,
                    hasMandateHistory: hasMandateHistory));
            if (!allowed) reason = "zhulu_ineligible";
            return allowed;
        }

        public static bool TryDeclare(Kingdom attacker, Kingdom defender,
            out string reason)
        {
            return TryDeclare(attacker, defender,
                pZhuluAgeOverride: false, out reason);
        }

        public static bool TryDeclare(Kingdom attacker, Kingdom defender,
            bool pZhuluAgeOverride, out string reason)
        {
            bool ageOverride = ZhuluAgeRules.ShouldUseWarOverride(
                pZhuluAgeOverride,
                World.world?.map_stats?.world_age_id);
            if (!CanDeclare(attacker, defender, ageOverride,
                    out reason)) return false;
            War war = ageOverride
                ? WarDecisionService.TryStartSystemWar(attacker, defender,
                    ZhuluWarRules.WarTypeId, ZhuluWarRules.GoalTypeId)
                : WarDecisionService.TryStartWarWithResult(attacker,
                    defender, ZhuluWarRules.WarTypeId,
                    ZhuluWarRules.GoalTypeId);
            if (war?.data == null)
            {
                reason = "zhulu_war_start_failed";
                return false;
            }
            reason = "";
            return true;
        }

        public static bool IsZhuluWar(War war, bool requireActive = true)
        {
            if (war?.data == null ||
                war.getAsset()?.id != ZhuluWarRules.WarTypeId)
                return false;
            if (!requireActive) return true;
            try { return !war.hasEnded(); }
            catch { return false; }
        }

        public static bool IsOpposingZhuluCapture(City city,
            Kingdom capturer)
        {
            Kingdom oldOwner = city?.kingdom;
            if (city?.data == null || capturer?.data == null ||
                oldOwner?.data == null || capturer == oldOwner)
                return false;
            try
            {
                if (World.world?.wars == null) return false;
                foreach (War war in World.world.wars)
                {
                    if (!IsZhuluWar(war)) continue;
                    if (war.isInWarWith(capturer, oldOwner)) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool HasActivePrincipalWars()
        {
            try
            {
                if (World.world?.wars == null) return false;
                foreach (War war in World.world.wars)
                    if (IsZhuluWar(war)) return true;
            }
            catch { }
            return false;
        }

        internal static bool IsValidRealm(Kingdom kingdom)
        {
            return kingdom?.data != null && !kingdom.isRekt() &&
                   kingdom.isCiv() && !kingdom.isNeutral();
        }
    }
}
