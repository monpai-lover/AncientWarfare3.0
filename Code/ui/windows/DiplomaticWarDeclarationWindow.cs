using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.components;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class DiplomaticWarDeclarationWindow :
        AbstractWindow<DiplomaticWarDeclarationWindow>
    {
        private static readonly Vector2 DefaultSize = new(580f, 420f);
        private static readonly Vector2 MinimumSize = new(500f, 360f);
        private static readonly Vector2 MaximumSize = new(760f, 560f);
        private static long _attackerId = -1L;
        private static long _defenderId = -1L;
        private static string _selectedSignature = "";
        private readonly List<ReasonRow> _reasonRows = new();
        private Vector2 _windowSize = DefaultSize;
        private RectTransform _root;
        private RealmPanel _attackerPanel;
        private RealmPanel _defenderPanel;
        private Image _powerFill;
        private Text _powerSummary;
        private Text _alliesSummary;
        private RectTransform _reasonViewport;
        private RectTransform _reasonContent;
        private ScrollRect _reasonScroll;
        private Button _declareButton;
        private Text _declareText;
        private TipButton _declareTip;
        private Text _emptyReasons;
        private WideWindowChrome _chrome;
        private WarTerritoryService.WarTargetOption _selectedOption;
        private bool _commandPending;
        private bool _commandRefreshRequested;

        private sealed class RealmPanel
        {
            public RectTransform Root;
            public Image FlagBackground;
            public Image FlagIcon;
            public UiUnitAvatarElement Avatar;
            public GameObject AvatarRoot;
            public Text Name;
            public Text Ruler;
            public Text Power;
        }

        private sealed class ReasonRow
        {
            public GameObject Root;
            public Image Background;
            public Image Icon;
            public Text Label;
            public Button Button;
            public TipButton Tip;
            public string Signature = "";
        }

        public static void Open(long pAttackerId, long pDefenderId)
        {
            _attackerId = pAttackerId;
            _defenderId = pDefenderId;
            _selectedSignature = "";
            if (Instance == null)
                CreateAndInit(AW_LineageWindowIds.DIPLOMATIC_WAR_DECLARATION);
            AW_LineageWindowIds.SafeShow(
                AW_LineageWindowIds.DIPLOMATIC_WAR_DECLARATION,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize => { _windowSize = pSize; ApplyLayout(); Refresh(); },
                DefaultSize, MinimumSize, MaximumSize);
            AW3MultiplayerCommandFacade.Changed += OnCommandStateChanged;
        }

        private void OnDestroy()
        {
            AW3MultiplayerCommandFacade.Changed -= OnCommandStateChanged;
        }

        public override void OnNormalEnable() => Refresh();

        private void Update()
        {
            if (!_commandRefreshRequested) return;
            _commandRefreshRequested = false;
            _commandPending = false;
            if (isActiveAndEnabled) Refresh();
        }

        private void OnCommandStateChanged()
        {
            _commandRefreshRequested = true;
        }

        public void Refresh()
        {
            EnsureUi();
            ApplyLayout();
            Kingdom attacker = FindKingdom(_attackerId);
            Kingdom defender = FindKingdom(_defenderId);
            if (attacker?.data == null || defender?.data == null ||
                attacker.isRekt() || defender.isRekt()) return;

            BindRealm(_attackerPanel, attacker);
            BindRealm(_defenderPanel, defender);
            int attackerAllies;
            int defenderAllies;
            int attackerPower = SidePower(attacker, out attackerAllies);
            int defenderPower = SidePower(defender, out defenderAllies);
            float share = attackerPower / (float)Math.Max(1,
                attackerPower + defenderPower);
            _powerFill.rectTransform.anchorMax = new Vector2(
                Mathf.Clamp01(share), 1f);
            _powerSummary.text = attackerPower + "  ⚔  " + defenderPower +
                                 "\n" + PowerVerdict(attackerPower,
                                     defenderPower);
            _alliesSummary.text = AW_L10n.Text(
                "aw_diplomatic_war_potential_allies", "Potential allies") +
                ": " + attackerAllies + "  -  " + defenderAllies;

            List<DiplomaticWarTargetAvailability> targets =
                DiplomaticWarDeclarationService.BuildTargetAvailabilities(
                    attacker, defender);
            var sourceCandidates = new List<DiplomaticWarAvailabilityCandidate>(
                targets.Count);
            for (int i = 0; i < targets.Count; i++)
                sourceCandidates.Add(targets[i].ToCandidate());
            int[] displayOrder = DiplomaticWarAvailabilityRules.
                StableAvailableFirstOrder(sourceCandidates);
            var orderedTargets = new List<DiplomaticWarTargetAvailability>(
                targets.Count);
            var candidates = new List<DiplomaticWarAvailabilityCandidate>(
                targets.Count);
            int preferredIndex = -1;
            for (int i = 0; i < displayOrder.Length; i++)
            {
                DiplomaticWarTargetAvailability target =
                    targets[displayOrder[i]];
                orderedTargets.Add(target);
                candidates.Add(target.ToCandidate());
                if (Signature(target.Option) == _selectedSignature)
                    preferredIndex = i;
            }
            targets = orderedTargets;
            int selectedIndex = DiplomaticWarAvailabilityRules.
                ResolveSelectedGoalIndex(candidates, preferredIndex);
            _selectedOption = null;
            if (selectedIndex >= 0)
            {
                _selectedOption = targets[selectedIndex].Option;
                _selectedSignature = Signature(_selectedOption);
            }
            else
            {
                _selectedSignature = "";
            }
            for (int i = 0; i < targets.Count; i++)
            {
                while (_reasonRows.Count <= i)
                    _reasonRows.Add(CreateReasonRow(_reasonContent));
                BindReason(_reasonRows[i], targets[i], attacker, defender);
            }
            for (int i = targets.Count; i < _reasonRows.Count; i++)
                _reasonRows[i].Root.SetActive(false);
            DiplomaticWarAvailabilityResult pairAvailability =
                DiplomaticWarAvailabilityRules.Resolve(
                    DiplomaticWarDeclarationService.HasPendingForPair(
                        attacker, defender), candidates);
            _emptyReasons.gameObject.SetActive(targets.Count == 0);
            _emptyReasons.text = targets.Count == 0
                ? DiplomacyConversationWindow.ProposalFailure(
                    pairAvailability.FailureReason)
                : "";
            bool canDeclare = _selectedOption != null && !_commandPending;
            _declareButton.interactable = canDeclare;
            _declareText.text = canDeclare
                ? AW_L10n.Text("aw_diplomatic_war_send", "Deliver declaration")
                : AW_L10n.Text("aw_diplomatic_war_unavailable",
                    "Declaration unavailable");
            string declareFailure = canDeclare
                ? ""
                : pairAvailability.FailureReason;
            BindDeclareTip(declareFailure);
            Canvas.ForceUpdateCanvases();
        }

        private void Declare()
        {
            if (_commandPending) return;
            Kingdom attacker = FindKingdom(_attackerId);
            if (attacker?.data == null || _selectedOption == null) return;
            WarTerritoryService.WarTargetOption option = _selectedOption;
            long cityId = option.target_city?.data?.id ?? -1L;
            AW3CommandResult result =
                AW3MultiplayerCommandFacade.DispatchFromUi(
                    AW3CommandRequest.DeclareWar(attacker.id,
                        option.target_kingdom.id, cityId, option.goal_type,
                        DiplomaticWarDeclarationService.WarTypeForGoal(
                            option.goal_type),
                        DiplomaticWarDeclarationService.ReasonKeyForGoal(
                            option.goal_type),
                        string.IsNullOrWhiteSpace(option.label)
                            ? option.goal_type
                            : option.label));
            _commandPending = result.Status == AW3CommandStatus.Pending;
            if (_commandPending)
            {
                Refresh();
                return;
            }
            if (!result.Accepted)
            {
                WorldTip.showNow(DiplomacyConversationWindow.
                    ProposalFailure(result.MessageKey), false, "top");
                return;
            }
            DiplomacyConversationWindow.Open(attacker.id);
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup layout in
                     ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            _root = new GameObject("DiplomaticWarRoot",
                typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(ContentTransform, false);
            _attackerPanel = CreateRealmPanel(_root, "Attacker");
            _defenderPanel = CreateRealmPanel(_root, "Defender");
            Image powerTrack = CreateImage(_root, "PowerTrack",
                new Color(.38f, .18f, .16f, 1f));
            _powerFill = CreateImage(powerTrack.transform, "AttackerPower",
                new Color(.23f, .48f, .66f, 1f));
            _powerFill.rectTransform.anchorMin = Vector2.zero;
            _powerFill.rectTransform.anchorMax = Vector2.one;
            _powerFill.rectTransform.offsetMin = Vector2.zero;
            _powerFill.rectTransform.offsetMax = Vector2.zero;
            _powerSummary = CreateText(_root, "PowerSummary", 9,
                TextAnchor.MiddleCenter);
            _alliesSummary = CreateText(_root, "AlliesSummary", 8,
                TextAnchor.MiddleCenter);
            CreateScrollArea(_root, out _reasonViewport,
                out _reasonContent, out _reasonScroll);
            _emptyReasons = CreateText(_reasonContent, "NoReasons", 9,
                TextAnchor.MiddleCenter);
            _emptyReasons.text = AW_L10n.Text("aw_diplomatic_war_no_reasons",
                "No valid war reasons");
            _emptyReasons.gameObject.AddComponent<LayoutElement>()
                .preferredHeight = 42f;
            _declareButton = CreateButton(_root, "Declare", Declare,
                out _declareText);
            _declareTip = _declareButton.gameObject.
                GetComponent<TipButton>() ??
                _declareButton.gameObject.AddComponent<TipButton>();
            _declareTip.type = AW_RawTooltip.TYPE;
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text(
                    "aw_diplomatic_war_title", "Declare War");
                window.titleText.raycastTarget = false;
            }
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = _windowSize;
            float width = Math.Max(1f, _windowSize.x - 42f);
            float height = Math.Max(1f, _windowSize.y - 58f);
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(_windowSize.x * .5f - 20f,
                    _windowSize.y * .5f - 12f);
            RectTransform titleRect = BackgroundTransform
                ?.Find("TitleBackground")?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * .52f, 30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * .5f - 16f, 0f);
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeScrollRect =
                nativeScroll?.GetComponent<RectTransform>();
            if (nativeScrollRect != null)
            {
                nativeScrollRect.sizeDelta = new Vector2(width, height);
                nativeScrollRect.localPosition = new Vector3(0f, -20f, 0f);
            }
            ScrollRect native = nativeScroll?.GetComponent<ScrollRect>();
            if (native != null)
            {
                native.horizontal = false;
                native.vertical = false;
            }
            Transform nativeScrollbar = BackgroundTransform?.Find(
                "Scroll View/Scrollbar Vertical");
            if (nativeScrollbar != null)
                foreach (Graphic graphic in
                         nativeScrollbar.GetComponentsInChildren<Graphic>(true))
                {
                    graphic.enabled = false;
                    graphic.raycastTarget = false;
                }
            RectTransform nativeViewport =
                ContentTransform?.parent as RectTransform;
            if (nativeViewport != null)
                nativeViewport.sizeDelta = new Vector2(width, height);
            RectTransform nativeContent = ContentTransform as RectTransform;
            if (nativeContent != null)
                nativeContent.sizeDelta = new Vector2(width, height);
            Layout(_root, 0f, 0f, width, height);
            float panelWidth = Math.Min(190f, (width - 90f) * .5f);
            Layout(_attackerPanel.Root, 8f, 4f, panelWidth, 92f);
            Layout(_defenderPanel.Root, width - panelWidth - 8f, 4f,
                panelWidth, 92f);
            Layout(_powerFill.transform.parent.GetComponent<RectTransform>(),
                panelWidth + 20f, 18f,
                Math.Max(80f, width - panelWidth * 2f - 40f), 14f);
            Layout(_powerSummary.rectTransform, panelWidth + 12f, 34f,
                Math.Max(96f, width - panelWidth * 2f - 24f), 38f);
            Layout(_alliesSummary.rectTransform, panelWidth + 12f, 72f,
                Math.Max(96f, width - panelWidth * 2f - 24f), 18f);
            Layout(_reasonViewport, 8f, 104f, width - 16f,
                Math.Max(100f, height - 148f));
            Layout(_declareButton.GetComponent<RectTransform>(),
                width * .5f - 76f, height - 38f, 152f, 32f);
            _chrome?.RepositionResizeHandle();
        }

        private static RealmPanel CreateRealmPanel(Transform pParent,
            string pName)
        {
            var panel = new RealmPanel();
            panel.Root = new GameObject(pName, typeof(RectTransform),
                typeof(Image)).GetComponent<RectTransform>();
            panel.Root.SetParent(pParent, false);
            panel.Root.GetComponent<Image>().color =
                new Color(.10f, .095f, .08f, .94f);
            panel.FlagBackground = CreateImage(panel.Root, "Flag",
                Color.white);
            Layout(panel.FlagBackground.rectTransform, 6f, 6f, 30f, 30f);
            panel.FlagBackground.preserveAspect = true;
            panel.FlagIcon = CreateImage(panel.FlagBackground.transform,
                "FlagIcon", Color.white);
            panel.FlagIcon.rectTransform.anchorMin = Vector2.zero;
            panel.FlagIcon.rectTransform.anchorMax = Vector2.one;
            panel.FlagIcon.rectTransform.offsetMin = Vector2.zero;
            panel.FlagIcon.rectTransform.offsetMax = Vector2.zero;
            panel.FlagIcon.preserveAspect = true;
            panel.AvatarRoot = new GameObject("Portrait", typeof(RectTransform));
            panel.AvatarRoot.transform.SetParent(panel.Root, false);
            Layout(panel.AvatarRoot.GetComponent<RectTransform>(), 40f, 4f,
                56f, 56f);
            UiUnitAvatarElement prefab = FamilyTreeNodeView.GetAvatarPrefab();
            if (prefab != null)
            {
                panel.Avatar = UnityEngine.Object.Instantiate(prefab,
                    panel.AvatarRoot.transform);
                RectTransform avatarRect =
                    panel.Avatar.GetComponent<RectTransform>();
                avatarRect.anchorMin = Vector2.zero;
                avatarRect.anchorMax = Vector2.one;
                avatarRect.offsetMin = Vector2.zero;
                avatarRect.offsetMax = Vector2.zero;
                avatarRect.localScale = Vector3.one;
            }
            panel.Name = CreateText(panel.Root, "Kingdom", 10,
                TextAnchor.MiddleLeft);
            Layout(panel.Name.rectTransform, 100f, 6f, 82f, 22f);
            panel.Ruler = CreateText(panel.Root, "Ruler", 8,
                TextAnchor.MiddleLeft);
            Layout(panel.Ruler.rectTransform, 100f, 30f, 82f, 18f);
            panel.Power = CreateText(panel.Root, "Power", 8,
                TextAnchor.MiddleLeft);
            Layout(panel.Power.rectTransform, 6f, 66f, 176f, 18f);
            return panel;
        }

        private static void BindRealm(RealmPanel pPanel, Kingdom pKingdom)
        {
            string bannerId = "";
            try { bannerId = pKingdom.getActorAsset()?.banner_id ?? ""; }
            catch { }
            KingdomFlagBuilder.Build(bannerId,
                pKingdom.data.banner_icon_id,
                pKingdom.data.banner_background_id,
                HistoryColors.FromKingdom(pKingdom), pKingdom.data.color_id,
                pPanel.FlagBackground, pPanel.FlagIcon);
            pPanel.Name.text = SuccessionDisputeService.GetDisplayName(
                pKingdom);
            pPanel.Ruler.text = pKingdom.king?.getName() ??
                                AW_L10n.Text("aw_diplomacy_unknown_ruler",
                                    "No ruler");
            pPanel.Power.text = AW_L10n.Text("aw_diplomacy_power",
                "Military power") + ": " + Math.Max(0, pKingdom.power);
            bool hasRuler = pKingdom.king?.data != null &&
                            pKingdom.king.isAlive() &&
                            !pKingdom.king.isRekt();
            pPanel.AvatarRoot.SetActive(hasRuler && pPanel.Avatar != null);
            if (hasRuler && pPanel.Avatar != null)
                pPanel.Avatar.show(pKingdom.king);
        }

        private static ReasonRow CreateReasonRow(Transform pParent)
        {
            var row = new ReasonRow();
            row.Root = new GameObject("WarReason", typeof(RectTransform),
                typeof(Image), typeof(Button), typeof(LayoutElement),
                typeof(TipButton));
            row.Root.transform.SetParent(pParent, false);
            row.Background = row.Root.GetComponent<Image>();
            AW_UIStyle.ApplyButton(row.Background, .96f);
            LayoutElement layout = row.Root.GetComponent<LayoutElement>();
            layout.minHeight = 34f;
            layout.preferredHeight = 34f;
            row.Icon = CreateImage(row.Root.transform, "Icon", Color.white);
            Layout(row.Icon.rectTransform, 6f, 5f, 24f, 24f);
            row.Icon.preserveAspect = true;
            row.Label = CreateText(row.Root.transform, "Label", 9,
                TextAnchor.MiddleLeft);
            row.Label.rectTransform.anchorMin = Vector2.zero;
            row.Label.rectTransform.anchorMax = Vector2.one;
            row.Label.rectTransform.offsetMin = new Vector2(36f, 2f);
            row.Label.rectTransform.offsetMax = new Vector2(-6f, -2f);
            row.Button = row.Root.GetComponent<Button>();
            row.Tip = row.Root.GetComponent<TipButton>();
            row.Tip.type = AW_RawTooltip.TYPE;
            return row;
        }

        private void BindReason(ReasonRow pRow,
            DiplomaticWarTargetAvailability pAvailability,
            Kingdom pAttacker,
            Kingdom pDefender)
        {
            WarTerritoryService.WarTargetOption pOption =
                pAvailability.Option;
            pRow.Signature = Signature(pOption);
            string city = pOption.target_city?.data?.name;
            pRow.Label.text = pOption.label +
                              (string.IsNullOrEmpty(city) ? "" : " · " + city);
            pRow.Icon.sprite = SpriteTextureLoader.getSprite(
                WarIconPathRules.ResolveTargetIconPath(pOption.goal_type));
            pRow.Icon.enabled = pRow.Icon.sprite != null;
            bool selected = pRow.Signature == _selectedSignature;
            pRow.Background.color = selected
                ? new Color(.36f, .28f, .15f, .98f)
                : new Color(.13f, .12f, .10f, .96f);
            pRow.Button.onClick.RemoveAllListeners();
            pRow.Button.interactable = !_commandPending && pAvailability.Available;
            string signature = pRow.Signature;
            pRow.Button.onClick.AddListener(() =>
            {
                if (_commandPending || !pAvailability.Available) return;
                _selectedSignature = signature;
                Refresh();
            });
            string desc = SuccessionDisputeService.GetDisplayName(
                              pAttacker) + " → " +
                          SuccessionDisputeService.GetDisplayName(pDefender) +
                          (string.IsNullOrEmpty(city) ? "" : "\n" +
                           AW_L10n.Text("aw_war_target_city", "Target city: ") +
                           city);
            if (!pAvailability.Available)
                desc += "\n\n" + DiplomacyConversationWindow.
                    ProposalFailure(pAvailability.FailureReason);
            pRow.Tip.hoverAction = () => Tooltip.show(pRow.Root,
                AW_RawTooltip.TYPE, new TooltipData
                {
                    tip_name = pOption.label,
                    tip_description = desc
                });
            pRow.Root.SetActive(true);
        }

        private void BindDeclareTip(string pFailureReason)
        {
            if (_declareTip == null) return;
            if (string.IsNullOrWhiteSpace(pFailureReason))
            {
                _declareTip.enabled = false;
                _declareTip.hoverAction = null;
                return;
            }
            _declareTip.enabled = true;
            string description = DiplomacyConversationWindow.
                ProposalFailure(pFailureReason);
            _declareTip.hoverAction = () => Tooltip.show(
                _declareButton.gameObject, AW_RawTooltip.TYPE,
                new TooltipData
                {
                    tip_name = AW_L10n.Text(
                        "aw_diplomatic_war_unavailable",
                        "Declaration unavailable"),
                    tip_description = description
                });
        }

        private static int SidePower(Kingdom pKingdom, out int pAllyCount)
        {
            pAllyCount = 0;
            int power = Math.Max(0, pKingdom?.power ?? 0);
            Alliance alliance = null;
            try { alliance = pKingdom?.getAlliance(); } catch { }
            if (alliance?.kingdoms_hashset == null) return power;
            foreach (Kingdom ally in alliance.kingdoms_hashset)
            {
                if (ally?.data == null || ally == pKingdom || ally.isRekt())
                    continue;
                pAllyCount++;
                power += Math.Max(0, ally.power);
            }
            return power;
        }

        private static string PowerVerdict(int pAttacker, int pDefender)
        {
            float ratio = pAttacker / (float)Math.Max(1, pDefender);
            if (ratio >= 1.6f) return AW_L10n.Text(
                "aw_diplomatic_war_power_overwhelming", "Overwhelming advantage");
            if (ratio >= 1.15f) return AW_L10n.Text(
                "aw_diplomatic_war_power_advantage", "Favorable balance");
            if (ratio <= .62f) return AW_L10n.Text(
                "aw_diplomatic_war_power_desperate", "Severe disadvantage");
            if (ratio <= .87f) return AW_L10n.Text(
                "aw_diplomatic_war_power_disadvantage", "Unfavorable balance");
            return AW_L10n.Text("aw_diplomatic_war_power_even",
                "Evenly matched");
        }

        private static string Signature(
            WarTerritoryService.WarTargetOption pOption)
        {
            return (pOption?.goal_type ?? "") + ":" +
                   (pOption?.target_city?.data?.id ?? -1L) + ":" +
                   (pOption?.source_claim_id ?? -1L) + ":" +
                   (pOption?.source_core_id ?? -1L);
        }

        private static void CreateScrollArea(Transform pParent,
            out RectTransform pRoot, out RectTransform pContent,
            out ScrollRect pScroll)
        {
            pRoot = new GameObject("WarReasons", typeof(RectTransform),
                typeof(ScrollRect)).GetComponent<RectTransform>();
            pRoot.SetParent(pParent, false);
            var viewport = new GameObject("Viewport", typeof(RectTransform),
                typeof(Image), typeof(Mask));
            viewport.transform.SetParent(pRoot, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-7f, 0f);
            viewport.GetComponent<Image>().color =
                new Color(.055f, .052f, .045f, .55f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            pContent = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter))
                .GetComponent<RectTransform>();
            pContent.SetParent(viewport.transform, false);
            pContent.anchorMin = new Vector2(0f, 1f);
            pContent.anchorMax = new Vector2(1f, 1f);
            pContent.pivot = new Vector2(.5f, 1f);
            pContent.anchoredPosition = Vector2.zero;
            pContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup group = pContent.GetComponent<VerticalLayoutGroup>();
            group.spacing = 3f;
            group.padding = new RectOffset(3, 3, 3, 3);
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            pContent.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            pScroll = pRoot.GetComponent<ScrollRect>();
            pScroll.viewport = viewportRect;
            pScroll.content = pContent;
            pScroll.horizontal = false;
            pScroll.vertical = true;
            pScroll.movementType = ScrollRect.MovementType.Clamped;
            pScroll.scrollSensitivity = 22f;
            DiplomacyConversationWindowScrollbar.Attach(pRoot, pScroll);
        }

        private static Button CreateButton(Transform pParent, string pName,
            UnityEngine.Events.UnityAction pAction, out Text pText)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            AW_UIStyle.ApplyButton(obj.GetComponent<Image>(), .96f);
            pText = CreateText(obj.transform, "Text", 9,
                TextAnchor.MiddleCenter);
            pText.rectTransform.anchorMin = Vector2.zero;
            pText.rectTransform.anchorMax = Vector2.one;
            pText.rectTransform.offsetMin = new Vector2(4f, 2f);
            pText.rectTransform.offsetMax = new Vector2(-4f, -2f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(pAction);
            return button;
        }

        private static Image CreateImage(Transform pParent, string pName,
            Color pColor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Image));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.color = pColor;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(Transform pParent, string pName,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform),
                typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 6;
            text.resizeTextMaxSize = pSize;
            text.raycastTarget = false;
            return text;
        }

        private static void Layout(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }
    }

    internal static class DiplomacyConversationWindowScrollbar
    {
        public static Scrollbar Attach(RectTransform pRoot, ScrollRect pScroll)
        {
            var track = new GameObject("Scrollbar Vertical",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            track.transform.SetParent(pRoot, false);
            RectTransform trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(1f, 0f);
            trackRect.anchorMax = Vector2.one;
            trackRect.pivot = new Vector2(1f, .5f);
            trackRect.sizeDelta = new Vector2(6f, 0f);
            track.GetComponent<Image>().color =
                new Color(.08f, .075f, .06f, .92f);
            var handle = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handle.transform.SetParent(track.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(1f, 1f);
            handleRect.offsetMax = new Vector2(-1f, -1f);
            handle.GetComponent<Image>().color =
                new Color(.72f, .57f, .28f, .95f);
            Scrollbar scrollbar = track.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            pScroll.verticalScrollbar = scrollbar;
            pScroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
            return scrollbar;
        }
    }
}
