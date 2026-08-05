using System;
using System.Collections.Generic;
using System.Text;
using AncientWarfare3.core.atlas;
using AncientWarfare3.ui.components;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class KingdomAtlasWindow : AbstractWindow<KingdomAtlasWindow>
    {
        private const float DefaultWidth = 760f;
        private const float DefaultHeight = 500f;
        private const float MinWidth = 560f;
        private const float MinHeight = 360f;
        private const float MaxWidth = 1100f;
        private const float MaxHeight = 760f;

        private static long _requestedKingdomId = -1L;
        private long _kingdomId = -1L;
        private Vector2 _windowSize = new Vector2(DefaultWidth, DefaultHeight);
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private RectTransform _mapViewport;
        private RectTransform _mapContent;
        private RawImage _mapImage;
        private Text _chronicle;
        private Text _status;
        private Text _nodeText;
        private Button _previous;
        private Button _next;
        private Button _png;
        private Button _gif;
        private Button _resolutionButton;
        private int _resolution = 768;
        private List<KingdomAtlasNode> _nodes = new List<KingdomAtlasNode>();
        private int _nodeIndex;
        private Texture2D _texture;
        private readonly List<GameObject> _labelObjects = new List<GameObject>();

        internal static void Open(long pKingdomId)
        {
            _requestedKingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.KINGDOM_ATLAS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.KINGDOM_ATLAS,
                () => Instance?.ApplyRequest());
        }

        protected override void Init()
        {
            EnsureUi();
            _chrome = WideWindowChrome.Attach(BackgroundTransform,
                () => _windowSize,
                pSize => { _windowSize = pSize; ApplyLayout(); },
                new Vector2(DefaultWidth, DefaultHeight),
                new Vector2(MinWidth, MinHeight),
                new Vector2(MaxWidth, MaxHeight));
            ApplyLayout();
        }

        public override void OnNormalEnable() { ApplyRequest(); }

        public override void OnNormalDisable()
        {
            if (_texture != null) Destroy(_texture);
            _texture = null;
        }

        private void ApplyRequest()
        {
            _kingdomId = _requestedKingdomId;
            _nodes = KingdomAtlasHistoryService.BuildNodes(_kingdomId);
            _nodeIndex = Math.Max(0, _nodes.Count - 1);
            RenderNode();
            SetStatus(_nodes.Count == 0 ? "No archived territorial events." :
                "Ready: " + _nodes.Count + " nodes");
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup group in ContentTransform.GetComponents<LayoutGroup>()) group.enabled = false;
            _root = NewRect("KingdomAtlasRoot", ContentTransform);

            _status = Label(_root, "Status", "", 11, TextAnchor.MiddleLeft);
            _status.rectTransform.anchorMin = new Vector2(0f, 1f);
            _status.rectTransform.anchorMax = new Vector2(1f, 1f);
            _status.rectTransform.offsetMin = new Vector2(12f, -28f);
            _status.rectTransform.offsetMax = new Vector2(-12f, -6f);

            _mapViewport = NewRect("MapViewport", _root);
            _mapViewport.anchorMin = new Vector2(0f, 0f);
            _mapViewport.anchorMax = new Vector2(0.66f, 1f);
            _mapViewport.offsetMin = new Vector2(10f, 42f);
            _mapViewport.offsetMax = new Vector2(-5f, -34f);
            Image viewportImage = _mapViewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            viewportImage.raycastTarget = true;
            var viewport = _mapViewport.gameObject.AddComponent<KingdomAtlasMapViewport>();
            _mapContent = NewRect("MapContent", _mapViewport);
            _mapContent.anchorMin = new Vector2(0.5f, 0.5f);
            _mapContent.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContent.pivot = new Vector2(0.5f, 0.5f);
            viewport.Setup(_mapContent);
            _mapImage = _mapContent.gameObject.AddComponent<RawImage>();
            _mapImage.raycastTarget = false;
            _mapImage.rectTransform.sizeDelta = new Vector2(420f, 420f);

            _chronicle = Label(_root, "Chronicle", "", 9, TextAnchor.UpperLeft);
            _chronicle.rectTransform.anchorMin = new Vector2(0.66f, 0f);
            _chronicle.rectTransform.anchorMax = new Vector2(1f, 1f);
            _chronicle.rectTransform.offsetMin = new Vector2(5f, 42f);
            _chronicle.rectTransform.offsetMax = new Vector2(-10f, -34f);
            _chronicle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chronicle.verticalOverflow = VerticalWrapMode.Overflow;

            _nodeText = Label(_root, "Node", "", 10, TextAnchor.MiddleCenter);
            _nodeText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _nodeText.rectTransform.anchorMax = new Vector2(0.66f, 0f);
            _nodeText.rectTransform.offsetMin = new Vector2(100f, 10f);
            _nodeText.rectTransform.offsetMax = new Vector2(-100f, 34f);
            _previous = Button(_root, "Previous", "<", () => ChangeNode(-1));
            _previous.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
            _previous.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 0f);
            _previous.GetComponent<RectTransform>().anchoredPosition = new Vector2(34f, 22f);
            _next = Button(_root, "Next", ">", () => ChangeNode(1));
            _next.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
            _next.GetComponent<RectTransform>().anchorMax = new Vector2(0f, 0f);
            _next.GetComponent<RectTransform>().anchoredPosition = new Vector2(66f, 22f);

            _png = Button(_root, "GeneratePng", "PNG", () => Generate(false));
            _gif = Button(_root, "GenerateGif", "GIF", () => Generate(true));
            _resolutionButton = Button(_root, "Resolution", "768", CycleResolution);
            PositionButton(_png, 0.66f, 0f, -110f, 22f);
            PositionButton(_gif, 0.66f, 0f, -55f, 22f);
            PositionButton(_resolutionButton, 0.66f, 0f, -165f, 22f);
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform rect = BackgroundTransform?.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = _windowSize;
            _mapImage?.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Max(260f, _windowSize.y - 120f));
            _mapImage?.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(260f, _windowSize.y - 120f));
            _chrome?.RepositionResizeHandle();
        }

        private void Generate(bool pGif)
        {
            SetStatus("Generating 0%");
            KingdomAtlasGenerationResult result = KingdomAtlasArtifactWriter.Generate(
                _kingdomId, _resolution, pGif,
                pProgress => SetStatus("Generating " + pProgress.Percent + "%"),
                () => !isActiveAndEnabled);
            SetStatus(result.Success ? "Generated " + result.NodesGenerated +
                " PNG node(s)" : result.Error);
            ApplyRequest();
        }

        private void CycleResolution()
        {
            switch (_resolution)
            {
                case 512: _resolution = 768; break;
                case 768: _resolution = 1024; break;
                case 1024: _resolution = 1536; break;
                default: _resolution = 512; break;
            }
            if (_resolutionButton != null)
            {
                Text label = _resolutionButton.transform.Find("Text")?.GetComponent<Text>();
                if (label != null) label.text = _resolution.ToString();
            }
            RenderNode();
        }

        private void RenderNode()
        {
            if (_nodes == null || _nodes.Count == 0)
            {
                _chronicle.text = ""; _nodeText.text = "No nodes"; return;
            }
            _nodeIndex = Mathf.Clamp(_nodeIndex, 0, _nodes.Count - 1);
            KingdomAtlasNode node = _nodes[_nodeIndex];
            KingdomAtlasRaster raster = KingdomAtlasRasterizer.Render(node, _resolution);
            if (_texture != null) Destroy(_texture);
            _texture = new Texture2D(raster.Width, raster.Height, TextureFormat.RGBA32, false);
            _texture.LoadRawTextureData(raster.Rgba); _texture.Apply(false, false);
            _mapImage.texture = _texture;
            ClearLabels();
            IReadOnlyList<KingdomAtlasLabel> labels = KingdomAtlasRasterizer.BuildLabels(node, _resolution);
            for (int i = 0; i < labels.Count; i++) AddMapLabel(labels[i], node);
            _nodeText.text = "Node " + (_nodeIndex + 1) + "/" + _nodes.Count +
                "  " + node.Event.YearText + "  " + node.Event.CityName;
            var text = new StringBuilder();
            AppendChronicle(text, node.Event.OldKingdomName, node.OldChronicle);
            AppendChronicle(text, node.Event.NewKingdomName, node.NewChronicle);
            _chronicle.text = text.ToString();
        }

        private void AppendChronicle(StringBuilder pText, string pName,
            IReadOnlyList<KingdomAtlasChronicleRow> pRows)
        {
            if (!string.IsNullOrWhiteSpace(pName)) pText.AppendLine(pName);
            if (pRows == null) return;
            for (int i = Math.Max(0, pRows.Count - 12); i < pRows.Count; i++)
                pText.AppendLine((pRows[i].YearText ?? "") + " " + (pRows[i].Content ?? ""));
            pText.AppendLine();
        }

        private void ChangeNode(int pDelta)
        {
            if (_nodes == null || _nodes.Count == 0) return;
            _nodeIndex = Mathf.Clamp(_nodeIndex + pDelta, 0, _nodes.Count - 1);
            RenderNode();
        }

        private void SetStatus(string pText) { if (_status != null) _status.text = pText ?? ""; }

        private void AddMapLabel(KingdomAtlasLabel pLabel, KingdomAtlasNode pNode)
        {
            if (pLabel == null || pNode?.VisibleZones == null || pNode.VisibleZones.Count == 0) return;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < pNode.VisibleZones.Count; i++)
            {
                KingdomAtlasZoneCell cell = pNode.VisibleZones[i];
                minX = Math.Min(minX, cell.X); maxX = Math.Max(maxX, cell.X);
                minY = Math.Min(minY, cell.Y); maxY = Math.Max(maxY, cell.Y);
            }
            var label = new GameObject("AtlasLabel", typeof(RectTransform), typeof(Text), typeof(Outline));
            label.transform.SetParent(_mapContent, false);
            Text text = label.GetComponent<Text>(); text.font = LocalizedTextManager.current_font;
            text.fontSize = Mathf.RoundToInt(pLabel.Size); text.text = pLabel.Text;
            text.alignment = TextAnchor.MiddleCenter; text.color = new Color(pLabel.Color.Red / 255f,
                pLabel.Color.Green / 255f, pLabel.Color.Blue / 255f, 1f);
            Outline outline = label.GetComponent<Outline>(); outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);
            RectTransform rect = label.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(130f, 24f);
            float x = (pLabel.X - minX + 0.5f) / Math.Max(1f, maxX - minX + 1f) - 0.5f;
            float y = 0.5f - (pLabel.Y - minY + 0.5f) / Math.Max(1f, maxY - minY + 1f);
            rect.anchoredPosition = new Vector2(x * _resolution, y * _resolution);
            _labelObjects.Add(label);
        }

        private void ClearLabels()
        {
            for (int i = 0; i < _labelObjects.Count; i++)
                if (_labelObjects[i] != null) Destroy(_labelObjects[i]);
            _labelObjects.Clear();
        }

        private static RectTransform NewRect(string pName, Transform pParent)
        {
            var obj = new GameObject(pName, typeof(RectTransform));
            obj.transform.SetParent(pParent, false);
            return obj.GetComponent<RectTransform>();
        }

        private static Text Label(Transform pParent, string pName, string pText,
            int pSize, TextAnchor pAnchor)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(pParent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font; text.fontSize = pSize;
            text.color = Color.white; text.alignment = pAnchor; text.text = pText;
            return text;
        }

        private static Button Button(Transform pParent, string pName, string pText,
            UnityEngine.Events.UnityAction pAction)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(48f, 24f);
            Image image = obj.GetComponent<Image>(); AW_UIStyle.ApplyButton(image, .95f);
            Button button = obj.GetComponent<Button>(); button.onClick.AddListener(pAction);
            Text label = Label(obj.transform, "Text", pText, 9, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static void PositionButton(Button pButton, float pAnchorX,
            float pAnchorY, float pOffsetX, float pOffsetY)
        {
            RectTransform rect = pButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(pAnchorX, pAnchorY); rect.anchorMax = rect.anchorMin;
            rect.anchoredPosition = new Vector2(pOffsetX, pOffsetY);
        }
    }
}
