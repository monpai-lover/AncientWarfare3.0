using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class DeJureRegionMergeWindow : AbstractWindow<DeJureRegionMergeWindow>
    {
        private static long _kingdomId = -1L;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private Text _status;
        private Transform _list;
        private Button _confirm;
        private long _primaryRegionId = -1L;
        private long _secondaryRegionId = -1L;
        private long _secondaryCityId = -1L;
        private bool _pending;
        private bool _refreshRequested;

        internal static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(
                AW_LineageWindowIds.DE_JURE_REGION_MERGE);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.DE_JURE_REGION_MERGE,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            BuildUi();
            AW3MultiplayerCommandFacade.Changed += OnCommandChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandChanged;
        }

        public override void OnNormalEnable() => Refresh();

        private void Update()
        {
            if (!_refreshRequested) return;
            _refreshRequested = false;
            _pending = false;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandChanged() => _refreshRequested = true;

        private void BuildUi()
        {
            if (_status != null || ContentTransform == null) return;
            _status = MakeText("Status", ContentTransform, 10,
                TextAnchor.MiddleCenter);
            var listObject = new GameObject("Candidates", typeof(RectTransform),
                typeof(VerticalLayoutGroup));
            listObject.transform.SetParent(ContentTransform, false);
            _list = listObject.transform;
            VerticalLayoutGroup layout = listObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _confirm = MakeButton("Confirm", ContentTransform, Confirm);
            Button cancel = MakeButton("Cancel", ContentTransform, Cancel);
            Position(_status.rectTransform, 8f, -8f, 360f, 28f);
            Position(listObject.GetComponent<RectTransform>(), 8f, -42f, 360f, 190f);
            Position(_confirm.GetComponent<RectTransform>(), 184f, -242f, 84f, 26f);
            Position(cancel.GetComponent<RectTransform>(), 284f, -242f, 84f, 26f);
            ScrollWindow scroll = GetComponent<ScrollWindow>();
            if (scroll?.titleText != null)
                scroll.titleText.text = AW_L10n.Text(
                    "aw_de_jure_merge_window", "合并单城州法理");
        }

        private void Refresh()
        {
            BuildUi();
            foreach (GameObject row in _rows) if (row != null) Destroy(row);
            _rows.Clear();
            if (_primaryRegionId < 0)
            {
                _secondaryRegionId = -1L;
                _secondaryCityId = -1L;
            }
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            IReadOnlyList<DeJureRegionMergeCandidate> candidates =
                DeJureRegionMergeService.GetMergeCandidates(kingdom);
            List<DeJureRegionMergeCandidate> visible = _primaryRegionId < 0
                ? candidates.Where(p => p.PrimaryRegionId > 0).ToList()
                : candidates.Where(p => p.PrimaryRegionId == _primaryRegionId)
                    .ToList();
            foreach (DeJureRegionMergeCandidate candidate in visible)
            {
                bool primary = _primaryRegionId < 0;
                GameObject row = DeJureRegionMergeListItem.Create(_list,
                    candidate, primary, () => Select(candidate, primary));
                _rows.Add(row);
            }
            _status.text = _primaryRegionId < 0
                ? AW_L10n.Text("aw_de_jure_merge_select_primary", "选择保留的主州")
                : AW_L10n.Text("aw_de_jure_merge_select_secondary", "选择要并入的州");
            if (candidates.Count == 0)
                _status.text = AW_L10n.Text("aw_de_jure_merge_no_candidates",
                    "No eligible adjacent single-city regions");
            _confirm.interactable = !_pending && _primaryRegionId > 0 &&
                                    _secondaryRegionId > 0;
        }

        private void Select(DeJureRegionMergeCandidate pCandidate, bool pPrimary)
        {
            if (_pending || pCandidate == null) return;
            if (_primaryRegionId < 0)
            {
                _primaryRegionId = pCandidate.PrimaryRegionId;
                Refresh();
                return;
            }
            _secondaryRegionId = pPrimary ? pCandidate.SecondaryRegionId :
                pCandidate.PrimaryRegionId;
            _secondaryCityId = pPrimary ? pCandidate.SecondaryCityId :
                pCandidate.PrimaryCityId;
            Refresh();
        }

        private void Confirm()
        {
            if (_pending || _primaryRegionId <= 0 || _secondaryRegionId <= 0)
                return;
            _pending = true;
            IReadOnlyList<DeJureRegionMergeCandidate> candidates =
                DeJureRegionMergeService.GetMergeCandidates(
                    World.world?.kingdoms?.get(_kingdomId));
            if (!candidates.Any(p => p.PrimaryRegionId == _primaryRegionId &&
                                     p.SecondaryRegionId == _secondaryRegionId))
            {
                _pending = false;
                _status.text = AW_L10n.Text("aw_de_jure_merge_invalid_target",
                    "目标已失效，请重新选择");
                return;
            }
            AW3CommandResult result = AW3MultiplayerCommandFacade.DispatchFromUi(
                AW3CommandRequest.MergeDeJureRegions(_kingdomId,
                    _primaryRegionId, _secondaryRegionId));
            if (result.Status == AW3CommandStatus.Accepted)
            {
                GetComponent<ScrollWindow>()?.clickHide();
                return;
            }
            if (result.Status != AW3CommandStatus.Pending)
                _pending = false;
            _status.text = result.Status == AW3CommandStatus.Pending
                ? AW_L10n.Text("aw_de_jure_merge_committing", "正在合并")
                : AW_L10n.Text("aw_de_jure_merge_invalid_target", "合并失败");
        }

        private void Cancel()
        {
            if (_pending) return;
            GetComponent<ScrollWindow>()?.clickHide();
        }

        private static Text MakeText(string pName, Transform pParent, int pSize,
            TextAnchor pAnchor)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(string pName, Transform pParent,
            Action pAction)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), 0.95f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() => pAction?.Invoke());
            Text text = MakeText("Text", obj.transform, 9,
                TextAnchor.MiddleCenter);
            text.text = pName == "Confirm"
                ? AW_L10n.Text("aw_de_jure_merge_confirm", "确认合并")
                : AW_L10n.Text("aw_de_jure_merge_cancel", "取消");
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static void Position(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            pRect.anchorMin = new Vector2(0f, 1f);
            pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }
    }
}
