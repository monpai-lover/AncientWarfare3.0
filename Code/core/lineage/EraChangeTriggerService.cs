using System;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class EraChangeTriggerService
    {
        public static void Mark(long pKingdomId, EraChangeReason pReason,
            string pSourceEventId)
        {
            Kingdom kingdom = FindKingdom(pKingdomId);
            if (kingdom != null) Mark(kingdom, pReason, pSourceEventId);
        }

        public static void Mark(Kingdom pKingdom, EraChangeReason pReason,
            string pSourceEventId)
        {
            if (pKingdom?.data == null || !EraNameRules.IsMajorAiReason(pReason)) return;
            pKingdom.data.get(LineageKeys.KINGDOM_ERA_CHANGE_REASON,
                out int currentValue, (int)EraChangeReason.None);
            EraChangeReason current = Enum.IsDefined(typeof(EraChangeReason), currentValue)
                ? (EraChangeReason)currentValue
                : EraChangeReason.None;
            EraChangeReason selected = EraNameRules.StrongerReason(current, pReason);
            if (selected == current && current != EraChangeReason.None) return;
            string source = string.IsNullOrWhiteSpace(pSourceEventId)
                ? ReasonId(pReason) + ":" + Date.getCurrentYear()
                : pSourceEventId.Trim();
            pKingdom.data.set(LineageKeys.KINGDOM_ERA_CHANGE_REASON, (int)selected);
            pKingdom.data.set(LineageKeys.KINGDOM_ERA_SOURCE_EVENT_ID, source);
        }

        public static bool TryProcessAnnualAi(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_ERA_CHANGE_REASON,
                out int reasonValue, (int)EraChangeReason.None);
            if (!Enum.IsDefined(typeof(EraChangeReason), reasonValue))
            {
                Clear(pKingdom);
                return false;
            }
            EraChangeReason reason = (EraChangeReason)reasonValue;
            if (!EraNameRules.IsMajorAiReason(reason) ||
                !KingdomPolicyService.IsPolicyAIEnabled(pKingdom)) return false;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.KINGDOM_ERA_LAST_AI_CHECK_YEAR,
                out int lastYear, int.MinValue);
            if (!EraNameRules.ShouldAiConsider(reason, lastYear == year)) return false;
            pKingdom.data.set(LineageKeys.KINGDOM_ERA_LAST_AI_CHECK_YEAR, year);
            pKingdom.data.get(LineageKeys.KINGDOM_ERA_SOURCE_EVENT_ID,
                out string sourceEventId, "");
            EraChangeResult result = YearNameService.TryChangeEra(
                pKingdom, pKingdom.king, "", EraChangeKind.AiMajorEvent,
                reason, sourceEventId);
            if (result.Success || EraNameRules.IsTerminalAiBlock(result.BlockReason))
                Clear(pKingdom);
            return result.Success;
        }

        public static void MarkTerritoryRecovery(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (pCity?.data == null || pNewKingdom?.data == null ||
                pOldKingdom == pNewKingdom) return;
            EraChangeReason reason = pNewKingdom.capital == pCity
                ? EraChangeReason.CapitalRecovered
                : WarTerritoryService.HasCore(pNewKingdom, pCity)
                    ? EraChangeReason.LegalCoreRecovered
                    : EraChangeReason.None;
            if (reason == EraChangeReason.None) return;
            Mark(pNewKingdom, reason, "city:" + pCity.data.id + ":" +
                 pNewKingdom.id + ":" + Date.getCurrentYear());
        }

        public static void Clear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.KINGDOM_ERA_CHANGE_REASON,
                (int)EraChangeReason.None);
            pKingdom.data.set(LineageKeys.KINGDOM_ERA_SOURCE_EVENT_ID, "");
        }

        private static string ReasonId(EraChangeReason pReason)
        {
            return pReason switch
            {
                EraChangeReason.RestoredMandate => "restored_mandate",
                EraChangeReason.AutonomousRestoration => "autonomous_restoration",
                EraChangeReason.MajorVictory => "major_victory",
                EraChangeReason.CapitalRecovered => "capital_recovered",
                EraChangeReason.LegalCoreRecovered => "legal_core_recovered",
                EraChangeReason.EnteredRevival => "entered_revival",
                EraChangeReason.CentralReform => "central_reform",
                EraChangeReason.CapitalRelocated => "capital_relocated",
                EraChangeReason.GrandSacrificeBlessing => "grand_sacrifice_blessing",
                _ => "era_event"
            };
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0 || World.world?.kingdoms == null) return null;
            try { return World.world.kingdoms.get(pKingdomId); }
            catch { return null; }
        }
    }
}
