using System;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.ui
{
    internal sealed class ArmyRtsOperationRowRefreshAdapter : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = 0.2f;

        private Actor _actor;
        private KeyValueField _row;
        private KeyValueField _taskRow;
        private float _nextRefresh;
        private string _lastOperation = "";
        private string _lastTaskText = "";

        public static void Bind(UnitWindow pWindow, Actor pActor,
            KeyValueField pRow, KeyValueField pTaskRow)
        {
            if (pWindow == null || pActor?.data == null || pRow == null)
                return;
            ArmyRtsOperationRowRefreshAdapter adapter = pWindow
                .GetComponent<ArmyRtsOperationRowRefreshAdapter>() ??
                pWindow.gameObject
                    .AddComponent<ArmyRtsOperationRowRefreshAdapter>();
            adapter._actor = pActor;
            adapter._row = pRow;
            adapter._taskRow = pTaskRow;
            adapter._nextRefresh = 0f;
            adapter._lastOperation = "";
            adapter._lastTaskText = "";
            adapter.RefreshNow();
        }

        public static void Clear(UnitWindow pWindow)
        {
            ArmyRtsOperationRowRefreshAdapter adapter = pWindow == null
                ? null
                : pWindow.GetComponent<
                    ArmyRtsOperationRowRefreshAdapter>();
            if (adapter == null) return;
            adapter._actor = null;
            adapter._row = null;
            adapter._taskRow = null;
            adapter._nextRefresh = 0f;
            adapter._lastOperation = "";
            adapter._lastTaskText = "";
        }

        private void Update()
        {
            if (_actor?.data == null || _row == null) return;
            float now = Time.unscaledTime;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshIntervalSeconds;
            RefreshNow();
        }

        private void RefreshNow()
        {
            KeyValueField row = _row;
            if (!TryCompose(_actor, out string operation))
            {
                RefreshTaskText(null);
                if (row?.gameObject.activeSelf == true)
                    row.gameObject.SetActive(false);
                _lastOperation = "";
                return;
            }
            RefreshTaskText(operation);
            if (row == null || row.value == null) return;
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
            if (string.Equals(_lastOperation, operation,
                    StringComparison.Ordinal)) return;
            row.value.text = operation;
            _lastOperation = operation;
        }

        private void RefreshTaskText(string pOperation)
        {
            KeyValueField taskRow = _taskRow;
            if (taskRow?.value == null) return;
            string text = string.IsNullOrWhiteSpace(pOperation)
                ? SafeTaskText(_actor)
                : pOperation;
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(_lastTaskText, text,
                    StringComparison.Ordinal)) return;
            taskRow.value.text = text;
            _lastTaskText = text;
        }

        private static string SafeTaskText(Actor pActor)
        {
            try { return pActor?.getTaskText() ?? ""; }
            catch { return ""; }
        }

        internal static bool TryCompose(Actor pActor, out string pOperation)
        {
            pOperation = "";
            Army army = pActor?.army;
            if (army?.data == null) return false;
            Actor captain;
            try { captain = army.getCaptain(); }
            catch { return false; }
            if (captain != pActor ||
                !ArmyRtsControllerService.TryGetProjection(army,
                    out ArmyRtsStrategicProjection projection))
                return false;

            ArmyRtsTransportPhase transportPhase =
                ArmyRtsTransportService.GetPhase(army);
            string state = AW_L10n.Text(
                ArmyRtsPresentationRules.OperationLocalizationKey(
                    projection.State, transportPhase),
                ArmyRtsPresentationRules.OperationFallback(projection.State,
                    transportPhase));
            string role = AW_L10n.Text(
                ArmyRtsPresentationRules.RoleLocalizationKey(
                    projection.Role),
                ArmyRtsPresentationRules.RoleFallback(projection.Role));
            string target = AW_L10n.Text("aw_army_rts_target_unknown",
                "Unknown target");
            try
            {
                City targetCity = World.world?.cities?.get(
                    projection.TargetCityId);
                if (!string.IsNullOrWhiteSpace(targetCity?.data?.name))
                    target = targetCity.data.name;
            }
            catch { }
            pOperation = ArmyRtsPresentationRules.ComposeOperation(
                state, role, target, projection.PlayerOrder,
                AW_L10n.Text("aw_army_rts_player_order", "Player order"));
            return !string.IsNullOrWhiteSpace(pOperation);
        }
    }
}
