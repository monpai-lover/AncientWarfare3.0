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
            bool ageOverride = ZhuluAgeRules.ShouldUseWarOverride(
                pZhuluAgeOverride,
                World.world?.map_stats?.world_age_id);
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
            City targetCity = defender.capital ??
                              WarTerritoryService.FindFirstTargetCity(
                                  defender);
            if (targetCity?.data == null)
            {
                reason = "zhulu_target_city_unavailable";
                return false;
            }

            var goal = new WarTerritoryService.WarGoalRequest
            {
                goal_type = ZhuluWarRules.GoalTypeId,
                target_kingdom = defender,
                target_city = targetCity
            };
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

            WarGoalCreateResult persisted =
                WarTerritoryService.CreateGoalForWar(war, goal);
            if (!persisted.Success)
                ZhuluWarSettlementService.AbortFailedDeclaration(war);
            reason = persisted.Success ? "" : persisted.Reason;
            return persisted.Success;
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

        public static bool TryResolveCaptureRecipient(City city,
            Kingdom capturer, out War zhuluWar, out Kingdom principal)
        {
            zhuluWar = null;
            principal = null;
            Kingdom oldOwner = city?.kingdom;
            if (city?.data == null || capturer?.data == null ||
                oldOwner?.data == null || capturer == oldOwner)
                return false;
            try
            {
                foreach (War war in capturer.getWars())
                {
                    if (!IsZhuluWar(war)) continue;
                    bool capturerAttacker = war.isAttacker(capturer);
                    bool capturerDefender = war.isDefender(capturer);
                    bool ownerAttacker = war.isAttacker(oldOwner);
                    bool ownerDefender = war.isDefender(oldOwner);
                    if (!(capturerAttacker && ownerDefender) &&
                        !(capturerDefender && ownerAttacker)) continue;
                    Kingdom mainAttacker = war.getMainAttacker();
                    Kingdom mainDefender = war.getMainDefender();
                    long recipientId = ZhuluWarRules.ResolveCaptureRecipient(
                        capturerAttacker, mainAttacker?.id ?? -1L,
                        mainDefender?.id ?? -1L);
                    Kingdom recipient = recipientId == mainAttacker?.id
                        ? mainAttacker
                        : recipientId == mainDefender?.id
                            ? mainDefender
                            : null;
                    if (!IsValidRealm(recipient)) continue;
                    zhuluWar = war;
                    principal = recipient;
                    return true;
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
