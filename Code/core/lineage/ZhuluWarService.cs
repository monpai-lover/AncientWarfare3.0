using System;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluWarDeclarationScope
    {
        [ThreadStatic] private static long _declaredDefenderId;
        [ThreadStatic] private static int _depth;

        public static long CurrentDefenderId => _depth > 0
            ? _declaredDefenderId
            : -1L;

        public static IDisposable Open(Kingdom pDefender)
        {
            long previousId = _declaredDefenderId;
            int previousDepth = _depth;
            _declaredDefenderId = pDefender?.data?.id ?? -1L;
            _depth = checked(previousDepth + 1);
            return new Lease(previousId, previousDepth);
        }

        private sealed class Lease : IDisposable
        {
            private readonly long _previousId;
            private readonly int _previousDepth;
            private bool _disposed;

            public Lease(long pPreviousId, int pPreviousDepth)
            {
                _previousId = pPreviousId;
                _previousDepth = pPreviousDepth;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _declaredDefenderId = _previousId;
                _depth = _previousDepth;
                _disposed = true;
            }
        }
    }

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

        public static bool ShouldEnrollInAw3Systems(War pWar)
        {
            if (pWar?.data == null) return false;
            bool active;
            try { active = !pWar.hasEnded(); }
            catch { active = false; }
            return ZhuluWarRules.ShouldEnrollInAw3WarSystems(
                pWar.getAsset()?.id, active);
        }

        public static bool PersistDeclaredDefender(War pWar,
            long pDefenderId)
        {
            if (!IsZhuluWar(pWar, requireActive: false) ||
                pDefenderId < 0L) return false;
            pWar.data.set(LineageKeys.ZHULU_DECLARED_DEFENDER_ID,
                pDefenderId);
            Kingdom defender = null;
            try { defender = World.world?.kingdoms?.get(pDefenderId); }
            catch { }
            pWar.data.set(LineageKeys.ZHULU_DECLARED_DEFENDER_NAME,
                defender?.name ?? "");
            pWar.data.set(LineageKeys.ZHULU_DECLARED_DEFENDER_COLOR,
                defender?.data != null
                    ? HistoryColors.FromKingdom(defender)
                    : "");
            return true;
        }

        public static bool TryGetDeclaredDefenderId(War pWar,
            out long pDefenderId)
        {
            pDefenderId = -1L;
            if (!IsZhuluWar(pWar, requireActive: false)) return false;
            pWar.data.get(LineageKeys.ZHULU_DECLARED_DEFENDER_ID,
                out pDefenderId, -1L);
            return pDefenderId >= 0L;
        }

        public static bool TryGetDeclaredDefenderIdentity(War pWar,
            out long pDefenderId, out string pName, out string pColor)
        {
            pName = "";
            pColor = "";
            if (!TryGetDeclaredDefenderId(pWar, out pDefenderId))
                return false;
            pWar.data.get(LineageKeys.ZHULU_DECLARED_DEFENDER_NAME,
                out pName, "");
            pWar.data.get(LineageKeys.ZHULU_DECLARED_DEFENDER_COLOR,
                out pColor, "");
            return true;
        }

        public static Kingdom ResolveDeclaredDefender(War pWar)
        {
            if (!TryGetDeclaredDefenderId(pWar, out long defenderId))
                return null;
            try
            {
                Kingdom defender = World.world?.kingdoms?.get(defenderId);
                return defender?.data != null ? defender : null;
            }
            catch { return null; }
        }

        public static Kingdom ResolveLiveDeclaredDefender(War pWar)
        {
            Kingdom defender = ResolveDeclaredDefender(pWar);
            return IsValidRealm(defender) ? defender : null;
        }

        public static Kingdom ResolvePrincipalDefender(War pWar)
        {
            if (IsZhuluWar(pWar, requireActive: false))
                return ResolveDeclaredDefender(pWar);
            try { return pWar?.getMainDefender(); }
            catch { return null; }
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
