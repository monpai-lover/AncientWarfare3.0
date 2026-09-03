using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class HistoricalFigureDrawWindow :
        AbstractWindow<HistoricalFigureDrawWindow>
    {
        private enum DrawState { Idle, Rolling, Reveal, Details, Placement,
            PlacementConfirm, Deploying }

        private static HistoricalFigureCardCollectionStore Store =>
            HistoricalFigureCardRuntimeService.Collection;
        private static HistoricalFigureCardDefinition _selectedCard;
        private static HistoricalFigureCardRevealResult _lastReveal;
        private static City _selectedCity;
        private static string _deploymentId = "";
        private static DrawState _state = DrawState.Idle;
        private static float _rollStartedAt;
        private static int _rollingIndex;
        private static int _lastAudioIndex = -1;

        private Text _body;
        private Text _status;
        private RectTransform _trackViewport;
        private RectTransform _track;
        private RectTransform _collectionRoot;
        private readonly List<Button> _collectionButtons = new List<Button>();
        private readonly List<HistoricalFigureCardListItem> _trackItems =
            new List<HistoricalFigureCardListItem>();
        private Button _draw;
        private Button _skip;
        private Button _deploy;
        private Button _confirm;
        private Button _cancel;
        private bool _built;

        public static void Open()
        {
            HistoricalFigureCardRuntimeService.Initialize();
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.HISTORICAL_FIGURE_CARDS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.HISTORICAL_FIGURE_CARDS,
                () => Instance?.Refresh());
        }

        internal static bool IsPlacementActive =>
            _state == DrawState.Placement || _state == DrawState.PlacementConfirm;

        internal static void ResetTransientState()
        {
            _selectedCard = null;
            _lastReveal = null;
            _selectedCity = null;
            _deploymentId = "";
            _state = DrawState.Idle;
            _rollStartedAt = 0f;
            _rollingIndex = 0;
            _lastAudioIndex = -1;
            Instance?.UpdateTrack(null, -1);
            if (Instance != null && Instance.isActiveAndEnabled)
                Instance.Refresh();
        }

        internal static void SelectMapCity(City pCity)
        {
            if (!IsPlacementActive || pCity?.data == null || pCity.isRekt() ||
                !pCity.isAlive() || pCity.kingdom?.data == null ||
                pCity.kingdom.isRekt() || !pCity.kingdom.isCiv() ||
                pCity.kingdom.isNeutral()) return;
            _selectedCity = pCity;
            _state = DrawState.PlacementConfirm;
            Open();
        }

        private void SelectCollectionCard(string pCardId)
        {
            HistoricalFigureCardDefinition card =
                HistoricalFigureCardCatalog.Get(pCardId);
            if (_state != DrawState.Idle || card == null ||
                Store.GetOwnedCount(card.CardId) <= 0) return;
            _selectedCard = card;
            _lastReveal = null;
            _state = DrawState.Details;
            Refresh();
        }

        protected override void Init()
        {
            BuildUi();
            ApplyLayout();
        }

        public override void OnNormalEnable() => Refresh();

        private void Update()
        {
            if (!isActiveAndEnabled || _state != DrawState.Rolling) return;
            float progress = Mathf.Clamp01((Time.unscaledTime - _rollStartedAt) / 6f);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            _rollingIndex = Mathf.Min(HistoricalFigureCardDrawService.WinnerIndex,
                Mathf.FloorToInt(eased * HistoricalFigureCardDrawService.WinnerIndex));
            if (_rollingIndex != _lastAudioIndex)
            {
                _lastAudioIndex = _rollingIndex;
                HistoricalFigureCardAudioService.PlayScroll();
            }
            HistoricalFigureCardDefinition rollingCard = _lastReveal?.RollingCards == null ||
                _lastReveal.RollingCards.Count <= _rollingIndex ? _selectedCard :
                _lastReveal.RollingCards[_rollingIndex];
            _status.text = "Rolling " + (_rollingIndex + 1) + "/" +
                HistoricalFigureCardDrawService.RollingCardCount + ": " +
                (rollingCard?.DisplayName ?? "-");
            _body.text = "The card track is moving...\n\n" +
                "Current: " + (rollingCard?.DisplayName ?? "-") +
                "\nWinner position: " + HistoricalFigureCardDrawService.WinnerIndex;
            UpdateTrack(_lastReveal.RollingCards, _rollingIndex);
            if (progress >= 1f) FinishRoll();
        }

        private void BuildUi()
        {
            if (_built || ContentTransform == null) return;
            _built = true;
            foreach (LayoutGroup layout in ContentTransform.GetComponents<LayoutGroup>())
                layout.enabled = false;
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = new Vector2(620f, 480f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.text = "Historical Figure Cards";

            _status = MakeText("Status", ContentTransform, 12,
                TextAnchor.UpperLeft);
            Position(_status.rectTransform, 18f, -12f, 584f, 28f);
            _body = MakeText("Body", ContentTransform, 9,
                TextAnchor.UpperLeft);
            Position(_body.rectTransform, 18f, -160f, 584f, 190f);
            GameObject viewport = new GameObject("CardTrackViewport",
                typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(ContentTransform, false);
            _trackViewport = viewport.GetComponent<RectTransform>();
            Position(_trackViewport, 18f, -48f, 584f, 96f);
            GameObject track = new GameObject("CardTrack",
                typeof(RectTransform));
            track.transform.SetParent(_trackViewport, false);
            _track = track.GetComponent<RectTransform>();
            _track.anchorMin = new Vector2(0f, .5f);
            _track.anchorMax = new Vector2(0f, .5f);
            _track.pivot = new Vector2(0f, .5f);
            _track.sizeDelta = new Vector2(
                HistoricalFigureCardDrawService.RollingCardCount * 86f, 92f);
            _track.anchoredPosition = Vector2.zero;
            GameObject collection = new GameObject("Collection",
                typeof(RectTransform));
            collection.transform.SetParent(ContentTransform, false);
            _collectionRoot = collection.GetComponent<RectTransform>();
            Position(_collectionRoot, 18f, -160f, 584f, 190f);
            _draw = MakeButton("Draw", ContentTransform, "Draw", Draw);
            Position(_draw.GetComponent<RectTransform>(), 18f, -366f, 110f, 28f);
            _skip = MakeButton("Skip", ContentTransform, "Skip", Skip);
            Position(_skip.GetComponent<RectTransform>(), 136f, -366f, 90f, 28f);
            _deploy = MakeButton("Deploy", ContentTransform, "Deploy to city", BeginPlacement);
            Position(_deploy.GetComponent<RectTransform>(), 234f, -366f, 124f, 28f);
            _confirm = MakeButton("Confirm", ContentTransform, "Confirm city", ConfirmPlacement);
            Position(_confirm.GetComponent<RectTransform>(), 366f, -366f, 120f, 28f);
            _cancel = MakeButton("Cancel", ContentTransform, "Cancel", CancelPlacement);
            Position(_cancel.GetComponent<RectTransform>(), 494f, -366f, 108f, 28f);
        }

        private void Refresh()
        {
            BuildUi();
            Store.Load();
            if (_state != DrawState.Rolling)
                UpdateTrack(_lastReveal?.RollingCards,
                    _lastReveal?.WinnerIndex ?? -1);
            _draw.interactable = HistoricalFigureCardRuntimeService.
                IsCatalogueAvailable &&
                (_state == DrawState.Idle || _state == DrawState.Details);
            _skip.interactable = _state == DrawState.Rolling;
            _deploy.interactable = _state == DrawState.Details && _selectedCard != null;
            _confirm.interactable = _state == DrawState.PlacementConfirm && _selectedCity != null;
            _cancel.interactable = _state == DrawState.Placement ||
                _state == DrawState.PlacementConfirm;
            if (_state == DrawState.Rolling) return;

            if (_state == DrawState.Placement || _state == DrawState.PlacementConfirm)
            {
                _collectionRoot?.gameObject.SetActive(false);
                _status.text = _state == DrawState.Placement
                    ? "Select a living civilization city"
                    : "Target: " + (_selectedCity?.name ?? "-");
                _body.text = CardText(_selectedCard) +
                    "\n\nClick Confirm city after selecting the target city.";
                return;
            }
            if (_selectedCard != null && (_state == DrawState.Details || _state == DrawState.Reveal))
            {
                _collectionRoot?.gameObject.SetActive(false);
                _status.text = _selectedCard.DisplayName + "  [" +
                    _selectedCard.Rarity?.DisplayName + "]";
                _body.text = CardText(_selectedCard) +
                    "\n\nOwned: " + Store.GetOwnedCount(_selectedCard.CardId);
                return;
            }

            _status.text = "Draw a historical figure";
            _collectionRoot?.gameObject.SetActive(true);
            var lines = new List<string>
            {
                "Rarity: Gold 0.26% | Red 0.64% | Pink 3.20% | Purple 15.98% | Blue 79.92%",
                "Collection sorted by fame. Select an owned card for details.",
            };
            _body.text = string.Join("\n", lines);
            UpdateCollection();
        }

        private void Draw()
        {
            if (_state == DrawState.Rolling) return;
            _lastReveal = HistoricalFigureCardDrawService.DrawAndCommit(
                HistoricalFigureCardCatalog.All, Store);
            if (!_lastReveal.Succeeded) { _status.text = _lastReveal.Error; return; }
            _selectedCard = _lastReveal.Winner;
            _state = DrawState.Rolling;
            _rollStartedAt = Time.unscaledTime;
            _rollingIndex = 0;
            _lastAudioIndex = -1;
            _draw.interactable = false;
            _skip.interactable = true;
            _deploy.interactable = false;
            _confirm.interactable = false;
            _cancel.interactable = false;
            HistoricalFigureCardAudioService.PlayUnlock();
        }

        private void Skip()
        {
            if (_state != DrawState.Rolling || _lastReveal == null) return;
            FinishRoll();
            HistoricalFigureCardAudioService.PlayImmediateUnlock();
        }

        private void FinishRoll()
        {
            if (_state != DrawState.Rolling) return;
            _state = DrawState.Reveal;
            _status.text = "Reveal: " + _selectedCard.DisplayName +
                "  index " + HistoricalFigureCardDrawService.WinnerIndex;
            Refresh();
            _state = DrawState.Details;
            Refresh();
            HistoricalFigureCardAudioService.PlayReveal(_selectedCard.Rarity);
        }

        private void BeginPlacement()
        {
            if (_state != DrawState.Details || _selectedCard == null) return;
            _selectedCity = null;
            _deploymentId = Guid.NewGuid().ToString("N");
            _state = DrawState.Placement;
            GetComponent<ScrollWindow>()?.clickHide();
        }

        private void ConfirmPlacement()
        {
            if (_state != DrawState.PlacementConfirm || _selectedCard == null ||
                _selectedCity == null) return;
            _state = DrawState.Deploying;
            HistoricalFigureCardDeploymentResult result =
                HistoricalFigureCardDeploymentService.TryDeploy(
                    new HistoricalFigureCardDeploymentRequest(_selectedCard.CardId,
                        _lastReveal?.DrawId ?? "", _deploymentId,
                        _selectedCity));
            if (result.Succeeded)
            {
                _status.text = "Deployed " + _selectedCard.DisplayName +
                    " as " + result.KingdomName;
                _state = DrawState.Details;
            }
            else
            {
                _status.text = "Deployment failed: " + result.Error;
                _state = DrawState.Placement;
            }
            Refresh();
        }

        private void CancelPlacement()
        {
            if (!IsPlacementActive) return;
            _selectedCity = null;
            _state = DrawState.Details;
            Refresh();
        }

        private static string CardText(HistoricalFigureCardDefinition pCard)
        {
            if (pCard == null) return "No card selected.";
            return "Name: " + pCard.DisplayName +
                "\nFamily: " + pCard.FamilyName + "  Clan: " + pCard.ClanName +
                "\nDynasty: " + pCard.DynastyName + "  Era: " + pCard.HistoricalEra +
                "\nYears: " + pCard.BirthYear + " - " + pCard.DeathYear +
                "  Fame: " + pCard.FameScore +
                "\nHistorical kingdom: " + pCard.HistoricalKingdomName +
                "\nFather: " + pCard.ParentDisplayName(true) +
                "\nMother: " + pCard.ParentDisplayName(false) +
                "\n\n" + pCard.Biography;
        }

        private void UpdateTrack(
            IReadOnlyList<HistoricalFigureCardDefinition> pCards,
            int pSelectedIndex)
        {
            if (_track == null) return;
            int count = pCards?.Count ?? 0;
            while (_trackItems.Count < count)
                _trackItems.Add(HistoricalFigureCardListItem.Create(_track));
            for (int i = 0; i < _trackItems.Count; i++)
            {
                bool visible = i < count;
                _trackItems[i].SetVisible(visible);
                if (!visible) continue;
                _trackItems[i].SetCard(pCards[i], i == pSelectedIndex);
                _trackItems[i].SetPosition(i * 86f);
            }
            _track.anchoredPosition = pSelectedIndex >= 0
                ? new Vector2(292f - pSelectedIndex * 86f, 0f)
                : Vector2.zero;
        }

        private void UpdateCollection()
        {
            if (_collectionRoot == null) return;
            IReadOnlyList<HistoricalFigureCardDefinition> cards =
                HistoricalFigureCardCatalog.SortForDisplay(
                    HistoricalFigureCardCatalog.All);
            while (_collectionButtons.Count < cards.Count)
            {
                int index = _collectionButtons.Count;
                Button button = MakeButton("CollectionCard" + index,
                    _collectionRoot, "", () =>
                    {
                        if (index < cards.Count)
                            SelectCollectionCard(cards[index].CardId);
                    });
                _collectionButtons.Add(button);
            }
            for (int i = 0; i < _collectionButtons.Count; i++)
            {
                bool visible = i < cards.Count;
                Button button = _collectionButtons[i];
                button.gameObject.SetActive(visible);
                if (!visible) continue;
                HistoricalFigureCardDefinition card = cards[i];
                Text label = button.GetComponentInChildren<Text>();
                if (label != null)
                    label.text = card.DisplayName + "  [" +
                        (card.Rarity?.ShortName ?? "") + "] x" +
                        Store.GetOwnedCount(card.CardId);
                Position(button.GetComponent<RectTransform>(),
                    (i % 2) * 292f, -((i / 2) * 25f), 284f, 22f);
                button.interactable = Store.GetOwnedCount(card.CardId) > 0 &&
                    _state == DrawState.Idle;
            }
            _collectionRoot.sizeDelta = new Vector2(584f,
                Mathf.Max(190f, Mathf.Ceil(cards.Count / 2f) * 25f));
        }

        private static Text MakeText(string pName, Transform pParent, int pSize,
            TextAnchor pAnchor)
        {
            Text text = new GameObject(pName, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(pParent, false);
            text.font = LocalizedTextManager.current_font;
            text.fontSize = pSize;
            text.alignment = pAnchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(string pName, Transform pParent,
            string pText, Action pAction)
        {
            GameObject obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            Image image = obj.GetComponent<Image>();
            image.sprite = SpriteTextureLoader.getSprite("ui/Icons/iconXias");
            image.color = new Color(.25f, .22f, .18f, .95f);
            Button button = obj.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                HistoricalFigureCardAudioService.PlayButtonPress();
                pAction?.Invoke();
            });
            EventTrigger trigger = obj.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerEnter
            };
            entry.callback.AddListener(_ =>
                HistoricalFigureCardAudioService.PlayItemHover());
            trigger.triggers.Add(entry);
            Text text = MakeText("Text", obj.transform, 8, TextAnchor.MiddleCenter);
            text.text = pText;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(2f, 1f);
            text.rectTransform.offsetMax = new Vector2(-2f, -1f);
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

        private void ApplyLayout()
        {
            RectTransform background = BackgroundTransform as RectTransform;
            if (background != null) background.sizeDelta = new Vector2(620f, 480f);
        }
    }
}
