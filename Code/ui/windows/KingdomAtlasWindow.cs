using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.atlas;
using AncientWarfare3.core.presentation;
using AncientWarfare3.ui.components;
using NeoModLoader.api;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class KingdomAtlasWindow : AbstractWindow<KingdomAtlasWindow>
    {
        private const int AtlasRenderLayer = 30;
        private const float DefaultWidth = 480f;
        private const float DefaultHeight = 310f;
        private const float MinWidth = 480f;
        private const float MinHeight = 310f;
        private const float MaxWidth = 480f;
        private const float MaxHeight = 310f;

        private static long _requestedKingdomId = -1L;
        private long _kingdomId = -1L;
        private Vector2 _windowSize = new Vector2(DefaultWidth, DefaultHeight);
        private WideWindowChrome _chrome;
        private RectTransform _root;
        private RectTransform _mapViewport;
        private RectTransform _mapContent;
        private RawImage _mapImage;
        private RectTransform _chronicleViewport;
        private RectTransform _chronicleContent;
        private ScrollRect _chronicleScroll;
        private Scrollbar _chronicleScrollbar;
        private Text _status;
        private Text _nodeText;
        private Button _previous;
        private Button _next;
        private Button _png;
        private Button _gif;
        private Button _cancel;
        private Button _resolutionButton;
        private AWFontDropdown _fontDropdown;
        private int _resolution = 768;
        private List<KingdomAtlasNode> _nodes = new List<KingdomAtlasNode>();
        private int _nodeIndex;
        private Texture2D _texture;
        private ArmyRtsPlanWorldTerrainSnapshot _terrain;
        private readonly List<GameObject> _labelObjects = new List<GameObject>();
        private readonly List<GameObject> _chronicleColumns =
            new List<GameObject>();
        private Coroutine _generationCoroutine;
        private bool _cancelGeneration;
        private bool _previewRequested;
        private bool _forcePreviewRender;

        private sealed class ChronicleDisplayColumn
        {
            internal string Text = "";
            internal Color Color = Color.white;
        }

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
            _terrain = null;
        }

        private void ApplyRequest(bool pUpdateStatus = true)
        {
            _kingdomId = _requestedKingdomId;
            _terrain = null;
            _nodes = KingdomAtlasHistoryService.BuildNodes(_kingdomId);
            _nodeIndex = Math.Max(0, _nodes.Count - 1);
            _previewRequested = false;
            _forcePreviewRender = false;
            ClearPreview();
            SetNavigationButtons();
            if (pUpdateStatus)
                SetStatus(_nodes.Count == 0
                    ? AW_L10n.Text("aw_kingdom_atlas_empty",
                        "No archived territorial events.")
                    : string.Format(AW_L10n.Text("aw_kingdom_atlas_ready",
                        "Ready: {0} nodes"), _nodes.Count));
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            foreach (LayoutGroup group in
                     ContentTransform.GetComponents<LayoutGroup>())
                group.enabled = false;
            ContentSizeFitter fitter =
                ContentTransform.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            _root = NewRect("KingdomAtlasRoot", ContentTransform);

            _status = Label(_root, "Status", "", 11, TextAnchor.MiddleLeft);

            _mapViewport = NewRect("MapViewport", _root);
            Image viewportImage = _mapViewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            viewportImage.raycastTarget = true;
            _mapViewport.gameObject.AddComponent<RectMask2D>();
            var viewport = _mapViewport.gameObject.AddComponent<KingdomAtlasMapViewport>();
            _mapContent = NewRect("MapContent", _mapViewport);
            _mapContent.anchorMin = new Vector2(0.5f, 0.5f);
            _mapContent.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContent.pivot = new Vector2(0.5f, 0.5f);
            viewport.Setup(_mapContent);
            _mapImage = _mapContent.gameObject.AddComponent<RawImage>();
            _mapImage.raycastTarget = false;
            _mapImage.rectTransform.sizeDelta = new Vector2(420f, 420f);

            BuildChronicleScroller();

            _nodeText = Label(_root, "Node", "", 10, TextAnchor.MiddleCenter);
            _previous = Button(_root, "Previous", "<", () => ChangeNode(-1));
            _next = Button(_root, "Next", ">", () => ChangeNode(1));

            _png = Button(_root, "GeneratePng", AW_L10n.Text(
                "aw_kingdom_atlas_png", "PNG"), () => Generate(false));
            _gif = Button(_root, "GenerateGif", AW_L10n.Text(
                "aw_kingdom_atlas_gif", "GIF"), () => Generate(true));
            _cancel = Button(_root, "CancelGeneration", AW_L10n.Text(
                "aw_title_cancel", "Cancel"), CancelGeneration);
            _resolutionButton = Button(_root, "Resolution", "768", CycleResolution);
            _fontDropdown = AWFontDropdown.Create(_root, "MapFont", 100f,
                24f, OnMapFontSelected);
            _cancel.gameObject.SetActive(false);
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            float width = Mathf.Max(1f, _windowSize.x - 42f);
            float height = Mathf.Max(1f, _windowSize.y - 58f);
            RectTransform background =
                BackgroundTransform?.GetComponent<RectTransform>();
            if (background != null) background.sizeDelta = _windowSize;
            Transform close = BackgroundTransform?.parent?.Find(
                "CloseBackground");
            if (close != null)
                close.localPosition = new Vector3(
                    _windowSize.x * 0.5f - 20f,
                    _windowSize.y * 0.5f - 12f);
            Transform titleBackground = BackgroundTransform?.Find(
                "TitleBackground");
            RectTransform titleRect =
                titleBackground?.GetComponent<RectTransform>();
            if (titleRect != null)
            {
                titleRect.sizeDelta = new Vector2(_windowSize.x * 0.58f,
                    30f);
                titleRect.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
            }
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
            {
                window.titleText.text = AW_L10n.Text("aw_kingdom_atlas",
                    "Kingdom Atlas");
                window.titleText.transform.localPosition = new Vector3(0f,
                    _windowSize.y * 0.5f - 16f, 0f);
                window.titleText.raycastTarget = false;
            }
            DisableNativeScroll(width, height);
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(width, height);

            const float bodyTop = 48f;
            float footerTop = height - 28f;
            float bodyHeight = Mathf.Max(80f, footerTop - bodyTop - 6f);
            float mapWidth = Mathf.Max(250f,
                Mathf.Floor((width - 24f) * 0.64f));
            mapWidth = Mathf.Min(mapWidth, width - 150f);
            float chronicleX = 10f + mapWidth + 8f;
            float chronicleWidth = Mathf.Max(1f, width - chronicleX - 10f);
            SetRect(_status.rectTransform, 10f, 4f, width - 20f, 40f);
            SetRect(_mapViewport, 10f, bodyTop, mapWidth, bodyHeight);
            SetRect(_chronicleViewport, chronicleX, bodyTop, chronicleWidth,
                bodyHeight);
            SetRect(_chronicleScrollbar?.GetComponent<RectTransform>(),
                chronicleX, bodyTop + bodyHeight - 7f, chronicleWidth, 6f);

            float mapCanvasSize = Mathf.Max(1f,
                Mathf.Min(mapWidth, bodyHeight));
            if (_mapContent != null)
                _mapContent.sizeDelta = new Vector2(mapCanvasSize,
                    mapCanvasSize);
            if (_mapImage != null)
                _mapImage.rectTransform.sizeDelta = new Vector2(mapCanvasSize,
                    mapCanvasSize);

            const float controlWidth = 280f;
            const float footerButtonWidth = 40f;
            const float footerGap = 4f;
            const float fontButtonWidth = 100f;
            float controlStart = Mathf.Max(94f, width - controlWidth);
            SetRect(_previous.GetComponent<RectTransform>(), 10f, footerTop,
                footerButtonWidth, 24f);
            SetRect(_next.GetComponent<RectTransform>(),
                10f + footerButtonWidth + footerGap, footerTop,
                footerButtonWidth, 24f);
            const float nodeStart = 10f + footerButtonWidth * 2f +
                                    footerGap * 2f;
            SetRect(_nodeText.rectTransform, nodeStart, footerTop,
                Mathf.Max(1f, controlStart - nodeStart - footerGap), 24f);
            SetRect(_png.GetComponent<RectTransform>(), controlStart,
                footerTop, footerButtonWidth, 24f);
            SetRect(_gif.GetComponent<RectTransform>(),
                controlStart + footerButtonWidth + footerGap,
                footerTop, footerButtonWidth, 24f);
            SetRect(_cancel.GetComponent<RectTransform>(),
                controlStart + (footerButtonWidth + footerGap) * 2f,
                footerTop, footerButtonWidth, 24f);
            SetRect(_resolutionButton.GetComponent<RectTransform>(),
                controlStart + (footerButtonWidth + footerGap) * 3f,
                footerTop, footerButtonWidth, 24f);
            SetRect(_fontDropdown?.RectTransform,
                controlStart + (footerButtonWidth + footerGap) * 4f,
                footerTop, fontButtonWidth, 24f);
            if (_previewRequested) RenderNode();
            _chrome?.RepositionResizeHandle();
        }

        private void BuildChronicleScroller()
        {
            var viewportObject = new GameObject("ChronicleViewport",
                typeof(RectTransform), typeof(Image), typeof(RectMask2D),
                typeof(ScrollRect));
            viewportObject.transform.SetParent(_root, false);
            _chronicleViewport = viewportObject.GetComponent<RectTransform>();
            Image panel = viewportObject.GetComponent<Image>();
            AW_UIStyle.ApplyPanel(panel, 0.82f);
            panel.raycastTarget = true;

            var contentObject = new GameObject("ChronicleContent",
                typeof(RectTransform));
            contentObject.transform.SetParent(_chronicleViewport, false);
            _chronicleContent = contentObject.GetComponent<RectTransform>();
            _chronicleContent.anchorMin = _chronicleContent.anchorMax =
                new Vector2(0f, 1f);
            _chronicleContent.pivot = new Vector2(0f, 1f);

            _chronicleScroll = viewportObject.GetComponent<ScrollRect>();
            _chronicleScroll.viewport = _chronicleViewport;
            _chronicleScroll.content = _chronicleContent;
            _chronicleScroll.horizontal = true;
            _chronicleScroll.vertical = false;
            _chronicleScroll.movementType = ScrollRect.MovementType.Clamped;
            _chronicleScroll.scrollSensitivity = 18f;
            _chronicleScrollbar = CreateHorizontalScrollbar(_root,
                _chronicleScroll);
        }

        private void DisableNativeScroll(float pWidth, float pHeight)
        {
            Transform nativeScroll = BackgroundTransform?.Find("Scroll View");
            RectTransform nativeRect = nativeScroll?.GetComponent<RectTransform>();
            if (nativeRect != null)
            {
                nativeRect.sizeDelta = new Vector2(pWidth, pHeight);
                nativeRect.localPosition = new Vector3(0f, -20f, 0f);
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
            RectTransform viewport = ContentTransform?.parent as RectTransform;
            if (viewport != null)
                viewport.sizeDelta = new Vector2(pWidth, pHeight);
            RectTransform content = ContentTransform as RectTransform;
            if (content != null)
                content.sizeDelta = new Vector2(pWidth, pHeight);
        }

        private void Generate(bool pGif)
        {
            if (_generationCoroutine != null) return;
            _cancelGeneration = false;
            SetStatus(AW_L10n.Text("aw_kingdom_atlas_generating",
                "Generating 0%"));
            SetGenerationButtons(false);
            _generationCoroutine = StartCoroutine(GenerateRoutine(pGif));
        }

        private IEnumerator GenerateRoutine(bool pGif)
        {
            KingdomAtlasGenerationResult result = null;
            KingdomAtlasGenerationSession session = null;
            Func<KingdomAtlasNode, KingdomAtlasRaster, KingdomAtlasRaster>
                previousRenderer = KingdomAtlasRasterizer.ExternalLabelRenderer;
            KingdomAtlasRasterizer.ExternalLabelRenderer = RenderBitmapLabels;
            try
            {
                if (EnsureTerrain(out string terrainError))
                    session = KingdomAtlasArtifactWriter.Begin(_kingdomId,
                        _resolution, pGif, _terrain);
                else
                    result = new KingdomAtlasGenerationResult
                    {
                        Error = terrainError
                    };
            }
            catch (Exception error)
            {
                result = new KingdomAtlasGenerationResult
                {
                    Error = error.Message
                };
            }
            try
            {
                while (session != null && !session.IsComplete)
                {
                    KingdomAtlasProgress progress;
                    bool advanced;
                    try
                    {
                        advanced = session.MoveNext(
                            () => _cancelGeneration, out progress);
                    }
                    catch (Exception error)
                    {
                        result = new KingdomAtlasGenerationResult
                        {
                            Error = error.Message
                        };
                        break;
                    }
                    if (advanced)
                        SetStatus(string.Format(AW_L10n.Text(
                            "aw_kingdom_atlas_generating_progress",
                            "Generating {0}%"), progress.Percent));
                    if (!session.IsComplete) yield return null;
                }
                if (result == null && session != null)
                    result = session.Result;
            }
            finally
            {
                KingdomAtlasRasterizer.ExternalLabelRenderer = previousRenderer;
            }
            _generationCoroutine = null;
            SetGenerationButtons(true);
            if (result != null && result.Success)
            {
                string exportPath = pGif && !string.IsNullOrWhiteSpace(
                    result.GifPath) ? result.GifPath : result.OutputDirectory;
                SetStatus(string.Format(AW_L10n.Text(
                        "aw_kingdom_atlas_generated",
                        "Generated {0} PNG node(s)"), result.NodesGenerated) +
                    "\n" + string.Format(AW_L10n.Text(
                        "aw_kingdom_atlas_export_path", "Export path: {0}"),
                        exportPath));
            }
            else
                SetStatus(result?.Error ?? AW_L10n.Text(
                    "aw_kingdom_atlas_failed", "Atlas generation failed."));
            ApplyRequest(false);
        }

        private static KingdomAtlasRaster RenderBitmapLabels(
            KingdomAtlasNode pNode, KingdomAtlasRaster pRaster)
        {
            Font font = ResolveMapFont();
            if (font == null || pNode?.VisibleZones == null ||
                pNode.VisibleZones.Count == 0) return null;

            IReadOnlyList<KingdomAtlasLabel> labels =
                KingdomAtlasRasterizer.BuildLabels(pNode, pRaster.Width);
            ApplyMapModeLabelPlacement(labels, pNode, pRaster.Width);
            ResolveLabelBounds(pNode, out int minX, out int maxX,
                out int minY, out int maxY);

            Texture2D baseTexture = null;
            RenderTexture target = null;
            GameObject canvasObject = null;
            GameObject cameraObject = null;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                baseTexture = new Texture2D(pRaster.Width, pRaster.Height,
                    TextureFormat.RGBA32, false);
                baseTexture.LoadRawTextureData(pRaster.Rgba);
                baseTexture.Apply(false, false);

                target = new RenderTexture(pRaster.Width, pRaster.Height, 24,
                    RenderTextureFormat.ARGB32);
                target.Create();
                cameraObject = new GameObject("KingdomAtlasExportCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = pRaster.Height * 0.5f;
                camera.aspect = pRaster.Width / (float)pRaster.Height;
                camera.transform.position = new Vector3(
                    pRaster.Width * 0.5f, pRaster.Height * 0.5f, -10f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << AtlasRenderLayer;
                camera.useOcclusionCulling = false;
                camera.targetTexture = target;

                canvasObject = new GameObject("KingdomAtlasExportCanvas",
                    typeof(RectTransform), typeof(Canvas));
                canvasObject.layer = AtlasRenderLayer;
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                canvas.sortingOrder = 32767;
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(pRaster.Width,
                    pRaster.Height);
                canvasRect.position = new Vector3(pRaster.Width * 0.5f,
                    pRaster.Height * 0.5f, 0f);

                var backgroundObject = new GameObject(
                    "KingdomAtlasExportBackground", typeof(RectTransform),
                    typeof(RawImage));
                backgroundObject.layer = AtlasRenderLayer;
                backgroundObject.transform.SetParent(canvasObject.transform,
                    false);
                RawImage image = backgroundObject.GetComponent<RawImage>();
                image.texture = baseTexture;
                image.color = Color.white;
                RectTransform backgroundRect =
                    backgroundObject.GetComponent<RectTransform>();
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.offsetMin = Vector2.zero;
                backgroundRect.offsetMax = Vector2.zero;
                backgroundRect.localScale = Vector3.one;

                for (int index = 0; index < labels.Count; index++)
                {
                    KingdomAtlasLabel atlasLabel = labels[index];
                    var labelObject = new GameObject("KingdomAtlasExportLabel",
                        typeof(RectTransform), typeof(Text), typeof(Outline));
                    labelObject.layer = AtlasRenderLayer;
                    labelObject.transform.SetParent(canvasObject.transform,
                        false);
                    Text text = labelObject.GetComponent<Text>();
                    text.font = font;
                    text.fontSize = Mathf.Max(4,
                        Mathf.RoundToInt(atlasLabel.Size));
                    text.text = atlasLabel.Text;
                    text.alignment = TextAnchor.MiddleCenter;
                    text.horizontalOverflow = HorizontalWrapMode.Overflow;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                    text.color = new Color(atlasLabel.Color.Red / 255f,
                        atlasLabel.Color.Green / 255f,
                        atlasLabel.Color.Blue / 255f, 1f);
                    Outline outline = labelObject.GetComponent<Outline>();
                    outline.effectColor = Color.black;
                    outline.effectDistance = new Vector2(1f, -1f);
                    RectTransform rect = labelObject.GetComponent<RectTransform>();
                    rect.sizeDelta = new Vector2(Mathf.Max(32f,
                        text.preferredWidth + 12f), Mathf.Max(24f,
                        text.preferredHeight + 8f));
                    float x = (atlasLabel.X - minX + 0.5f) /
                        Math.Max(1f, maxX - minX + 1f) - 0.5f;
                    float y = (atlasLabel.Y - minY + 0.5f) /
                        Math.Max(1f, maxY - minY + 1f) - 0.5f;
                    rect.anchoredPosition = new Vector2(
                        x * pRaster.Width, y * pRaster.Height);
                    rect.localRotation = Quaternion.Euler(0f, 0f,
                        atlasLabel.Angle);
                }

                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var output = new Texture2D(pRaster.Width, pRaster.Height,
                    TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0f, 0f, pRaster.Width,
                    pRaster.Height), 0, 0);
                output.Apply(false, false);
                Color32[] pixels = output.GetPixels32();
                byte[] rgba = new byte[pixels.Length * 4];
                // ReadPixels and LoadRawTextureData both use Unity's bottom-up
                // texture coordinates. Keep the atlas raster convention here;
                // file encoders perform the file-format row conversion.
                for (int y = 0; y < pRaster.Height; y++)
                    for (int x = 0; x < pRaster.Width; x++)
                    {
                        Color32 pixel = pixels[y * pRaster.Width + x];
                        int destination = (y * pRaster.Width + x) * 4;
                        rgba[destination] = pixel.r;
                        rgba[destination + 1] = pixel.g;
                        rgba[destination + 2] = pixel.b;
                        rgba[destination + 3] = pixel.a;
                    }
                UnityEngine.Object.Destroy(output);
                return new KingdomAtlasRaster(pRaster.Width,
                    pRaster.Height, rgba);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (baseTexture != null) UnityEngine.Object.Destroy(baseTexture);
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.Destroy(target);
                }
                if (canvasObject != null)
                    UnityEngine.Object.Destroy(canvasObject);
                if (cameraObject != null)
                    UnityEngine.Object.Destroy(cameraObject);
            }
        }

        private void CancelGeneration()
        {
            if (_generationCoroutine == null) return;
            _cancelGeneration = true;
            SetStatus(AW_L10n.Text("aw_kingdom_atlas_cancelling",
                "Cancelling atlas generation..."));
        }

        private void SetGenerationButtons(bool pEnabled)
        {
            if (_png != null) _png.interactable = pEnabled;
            if (_gif != null) _gif.interactable = pEnabled;
            if (_resolutionButton != null)
                _resolutionButton.interactable = pEnabled;
            _fontDropdown?.SetInteractable(pEnabled);
            if (_cancel != null)
            {
                _cancel.interactable = !pEnabled;
                _cancel.gameObject.SetActive(!pEnabled);
            }
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
            if (_previewRequested) RenderNode();
        }

        private void OnMapFontSelected(int pIndex)
        {
            HierarchicalVassalMapFontSettings.PersistSelectedFont();
            _forcePreviewRender = true;
            if (_previewRequested) RenderNode();
        }

        private void RenderNode()
        {
            if (_nodes == null || _nodes.Count == 0)
            {
                ClearPreview();
                return;
            }
            if (!_previewRequested) return;
            _nodeIndex = Mathf.Clamp(_nodeIndex, 0, _nodes.Count - 1);
            KingdomAtlasNode node = _nodes[_nodeIndex];
            UpdateNodeDetails(node);
            bool forceRender = _forcePreviewRender;
            _forcePreviewRender = false;
            if (!forceRender && TryRenderCachedPreviewPng(node)) return;
            if (!forceRender && TryRenderGeneratedPng(node)) return;
            if (!EnsureTerrain(out string terrainError))
            {
                ClearMapImage();
                SetStatus(terrainError);
                return;
            }
            KingdomAtlasRaster raster = KingdomAtlasLiveTerrainService.Render(
                node, _resolution, _terrain);
            KingdomAtlasRaster display = RenderBitmapLabels(node, raster) ??
                raster;
            KingdomAtlasArtifactWriter.CachePreviewPng(_kingdomId,
                _resolution, _nodeIndex, node.Event.EventId, display,
                HierarchicalVassalMapFontSettings.SelectedIndex);
            SetRasterTexture(display);
            ClearLabels();
        }

        private bool EnsureTerrain(out string pError)
        {
            pError = "";
            if (_terrain != null) return true;
            try
            {
                _terrain = KingdomAtlasLiveTerrainService.Capture(
                    Math.Max(768, _resolution));
                for (int index = 0; index < _nodes.Count; index++)
                    KingdomAtlasLiveTerrainService.AttachNodeGeometry(
                        _nodes[index], _terrain);
                return true;
            }
            catch (Exception error)
            {
                _terrain = null;
                pError = error.Message;
                return false;
            }
        }

        private bool TryRenderCachedPreviewPng(KingdomAtlasNode pNode)
        {
            if (pNode?.Event == null ||
                !KingdomAtlasArtifactWriter.TryLoadCachedPreviewPng(_kingdomId,
                    _resolution, _nodeIndex, pNode.Event.EventId,
                    out byte[] png,
                    HierarchicalVassalMapFontSettings.SelectedIndex)) return false;
            if (!TrySetCachedPreviewTexture(png, pNode)) return false;
            return true;
        }

        private bool TryRenderGeneratedPng(KingdomAtlasNode pNode)
        {
            if (pNode?.Event == null ||
                !KingdomAtlasArtifactWriter.TryLoadCachedPng(_kingdomId,
                    _resolution, _nodeIndex, pNode.Event.EventId,
                    out byte[] png) || !TrySetCachedTexture(png)) return false;
            ClearLabels();
            return true;
        }

        private bool TrySetCachedTexture(byte[] pPng)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, pPng, false))
            {
                Destroy(texture);
                return false;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            SetMapTexture(texture);
            return true;
        }

        private bool TrySetCachedPreviewTexture(byte[] pPng,
            KingdomAtlasNode pNode)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(source, pPng, false))
            {
                Destroy(source);
                return false;
            }
            Color32[] pixels = source.GetPixels32();
            byte[] rgba = new byte[pixels.Length * 4];
            for (int index = 0; index < pixels.Length; index++)
            {
                int offset = index * 4;
                rgba[offset] = pixels[index].r;
                rgba[offset + 1] = pixels[index].g;
                rgba[offset + 2] = pixels[index].b;
                rgba[offset + 3] = pixels[index].a;
            }
            KingdomAtlasRaster raster = new KingdomAtlasRaster(source.width,
                source.height, rgba);
            Destroy(source);
            SetRasterTexture(raster);
            ClearLabels();
            return true;
        }

        private void SetRasterTexture(KingdomAtlasRaster pRaster)
        {
            var texture = new Texture2D(pRaster.Width, pRaster.Height,
                TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(pRaster.Rgba);
            texture.Apply(false, false);
            SetMapTexture(texture);
        }

        private void SetMapTexture(Texture2D pTexture)
        {
            if (_texture != null) Destroy(_texture);
            _texture = pTexture;
            if (_mapImage != null) _mapImage.texture = _texture;
        }

        private void ClearMapImage()
        {
            if (_texture != null) Destroy(_texture);
            _texture = null;
            if (_mapImage != null) _mapImage.texture = null;
        }

        private void UpdateNodeDetails(KingdomAtlasNode pNode)
        {
            if (pNode?.Event == null) return;
            string eventText = pNode.Event.CityName;
            if (pNode.NodeKind == KingdomAtlasNodeKind.VassalStart)
                eventText = AW_L10n.Text(
                    "aw_kingdom_atlas_vassal_gained", "Obtained vassal");
            else if (pNode.NodeKind == KingdomAtlasNodeKind.VassalEnd)
                eventText = AW_L10n.Text(
                    "aw_kingdom_atlas_vassal_lost", "Lost vassal");
            _nodeText.text = string.Format(AW_L10n.Text(
                "aw_kingdom_atlas_node", "Node {0}/{1}"),
                _nodeIndex + 1, _nodes.Count) + "  " + pNode.Event.YearText +
                "  " + eventText;
            RenderChronicleColumns(pNode);
        }

        private void RenderChronicleColumns(KingdomAtlasNode pNode)
        {
            ClearChronicleColumns();
            if (_chronicleViewport == null || _chronicleContent == null ||
                pNode?.Event == null) return;
            const float columnWidth = 17f;
            const float columnPadding = 6f;
            int maximumRows = Mathf.Max(1, Mathf.FloorToInt(
                Mathf.Max(12f, _chronicleViewport.rect.height - 12f) / 12f));
            var columns = new List<ChronicleDisplayColumn>();
            List<string> entityNames = BuildChronicleEntityNames(pNode);
            AppendChronicleColumns(columns, pNode.Event.OldKingdomName,
                pNode.OldChronicle, maximumRows, ResolveChronicleColor(pNode,
                    pNode.Event.OldKingdomId,
                    pNode.Event.OldKingdomColor), entityNames);
            AppendChronicleColumns(columns, pNode.Event.NewKingdomName,
                pNode.NewChronicle, maximumRows, ResolveChronicleColor(pNode,
                    pNode.Event.NewKingdomId,
                    pNode.Event.NewKingdomColor), entityNames);

            float contentWidth = Mathf.Max(_chronicleViewport.rect.width,
                columnPadding * 2f + columns.Count * columnWidth);
            _chronicleContent.sizeDelta = new Vector2(contentWidth,
                Mathf.Max(1f, _chronicleViewport.rect.height));
            for (int index = 0; index < columns.Count; index++)
            {
                var columnObject = new GameObject("ChronicleColumn",
                    typeof(RectTransform), typeof(Text));
                columnObject.transform.SetParent(_chronicleContent, false);
                Text column = columnObject.GetComponent<Text>();
                column.font = ResolveMapFont();
                column.fontSize = 9;
                column.alignment = TextAnchor.UpperCenter;
                column.color = Color.white;
                column.supportRichText = true;
                column.horizontalOverflow = HorizontalWrapMode.Overflow;
                column.verticalOverflow = VerticalWrapMode.Overflow;
                column.raycastTarget = false;
                column.text = columns[index].Text;
                float x = contentWidth - columnPadding -
                    (index + 1) * columnWidth;
                SetRect(column.rectTransform, x, columnPadding,
                    columnWidth, Mathf.Max(1f,
                        _chronicleViewport.rect.height - 12f));
                _chronicleColumns.Add(columnObject);
            }
            Canvas.ForceUpdateCanvases();
            if (_chronicleScroll != null)
                _chronicleScroll.horizontalNormalizedPosition = 1f;
        }

        private static void AppendChronicleColumns(
            List<ChronicleDisplayColumn> pColumns,
            string pName, IReadOnlyList<KingdomAtlasChronicleRow> pRows,
            int pMaximumRows, Color pColor, IReadOnlyList<string> pEntityNames)
        {
            AppendVerticalColumns(pColumns, pName, pMaximumRows, pColor,
                pEntityNames);
            if (pRows != null)
                for (int index = Math.Max(0, pRows.Count - 12);
                     index < pRows.Count; index++)
                {
                    KingdomAtlasChronicleRow row = pRows[index];
                    AppendVerticalColumns(pColumns,
                        (row.YearText ?? "") + " " +
                        (row.Content ?? ""), pMaximumRows, pColor,
                        pEntityNames);
                }
            pColumns.Add(new ChronicleDisplayColumn
            {
                Text = " ",
                Color = pColor
            });
        }

        private static void AppendVerticalColumns(
            List<ChronicleDisplayColumn> pColumns,
            string pText, int pMaximumRows, Color pColor,
            IReadOnlyList<string> pEntityNames)
        {
            if (pColumns == null || string.IsNullOrWhiteSpace(pText)) return;
            pText = KingdomAtlasRules.SanitizeChronicleDisplayText(pText);
            var column = new StringBuilder();
            int rows = 0;
            for (int index = 0; index < pText.Length; index++)
            {
                char character = pText[index];
                if (character == '\r') continue;
                if (character == '\n')
                {
                    if (rows > 0)
                    {
                        AddChronicleColumn(pColumns, column, pColor,
                            pEntityNames);
                        column.Length = 0;
                        rows = 0;
                    }
                    continue;
                }
                if (rows >= pMaximumRows)
                {
                    AddChronicleColumn(pColumns, column, pColor,
                        pEntityNames);
                    column.Length = 0;
                    rows = 0;
                }
                if (rows > 0) column.Append('\n');
                column.Append(character);
                rows++;
            }
            if (rows > 0) AddChronicleColumn(pColumns, column, pColor,
                pEntityNames);
        }

        private static void AddChronicleColumn(
            List<ChronicleDisplayColumn> pColumns, StringBuilder pText,
            Color pColor, IReadOnlyList<string> pEntityNames)
        {
            pColumns.Add(new ChronicleDisplayColumn
            {
                Text = KingdomAtlasRules.ColorizeChronicleEntities(
                    pText.ToString(), pEntityNames,
                    ColorUtility.ToHtmlStringRGB(pColor)),
                Color = pColor
            });
        }

        private static List<string> BuildChronicleEntityNames(
            KingdomAtlasNode pNode)
        {
            var names = new List<string>();
            if (pNode == null) return names;
            AddChronicleEntityName(names, pNode.Event?.CityName);
            AddChronicleEntityName(names, pNode.Event?.OldKingdomName);
            AddChronicleEntityName(names, pNode.Event?.NewKingdomName);
            if (pNode.Events != null)
                for (int index = 0; index < pNode.Events.Count; index++)
                    AddChronicleEntityName(names,
                        pNode.Events[index]?.CityName);
            if (pNode.Kingdoms != null)
                foreach (KingdomAtlasKingdomSnapshot snapshot in pNode.Kingdoms.Values)
                    AddChronicleEntityName(names, snapshot?.Name);
            if (pNode.VassalRelations != null)
                for (int index = 0; index < pNode.VassalRelations.Count; index++)
                {
                    KingdomAtlasVassalRelationSnapshot relation =
                        pNode.VassalRelations[index];
                    AddChronicleEntityName(names, relation?.VassalName);
                    AddChronicleEntityName(names, relation?.SuzerainName);
                }
            return names;
        }

        private static void AddChronicleEntityName(List<string> pNames,
            string pName)
        {
            if (!string.IsNullOrWhiteSpace(pName) && !pNames.Contains(pName))
                pNames.Add(pName);
        }

        private static Color ResolveChronicleColor(KingdomAtlasNode pNode,
            long pKingdomId, string pFallback)
        {
            KingdomAtlasColor color;
            if (pNode?.DisplayColors != null &&
                pNode.DisplayColors.TryGetValue(pKingdomId, out color) ||
                KingdomAtlasRules.TryParseColor(pFallback, out color))
                return new Color(color.Red / 255f, color.Green / 255f,
                    color.Blue / 255f, 1f);
            return new Color(0.95f, 0.9f, 0.76f, 1f);
        }

        private void ChangeNode(int pDelta)
        {
            if (_nodes == null || _nodes.Count == 0) return;
            _nodeIndex = Mathf.Clamp(_nodeIndex + pDelta, 0, _nodes.Count - 1);
            _previewRequested = true;
            RenderNode();
        }

        private void SetStatus(string pText) { if (_status != null) _status.text = pText ?? ""; }

        private void AddMapLabel(KingdomAtlasLabel pLabel, KingdomAtlasNode pNode)
        {
            if (pLabel == null || pNode?.VisibleZones == null || pNode.VisibleZones.Count == 0) return;
            ResolveLabelBounds(pNode, out int minX, out int maxX,
                out int minY, out int maxY);
            var label = new GameObject("AtlasLabel", typeof(RectTransform), typeof(Text), typeof(Outline));
            label.transform.SetParent(_mapContent, false);
            Text text = label.GetComponent<Text>(); text.font = ResolveMapFont();
            text.fontSize = Mathf.RoundToInt(pLabel.Size); text.text = pLabel.Text;
            text.alignment = TextAnchor.MiddleCenter; text.color = new Color(pLabel.Color.Red / 255f,
                pLabel.Color.Green / 255f, pLabel.Color.Blue / 255f, 1f);
            Outline outline = label.GetComponent<Outline>(); outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);
            RectTransform rect = label.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(130f, 24f);
            float x = (pLabel.X - minX + 0.5f) / Math.Max(1f, maxX - minX + 1f) - 0.5f;
            float y = (pLabel.Y - minY + 0.5f) /
                Math.Max(1f, maxY - minY + 1f) - 0.5f;
            float mapCanvasSize = Mathf.Max(1f, Mathf.Min(
                _mapContent.rect.width, _mapContent.rect.height));
            rect.anchoredPosition = new Vector2(x * mapCanvasSize,
                y * mapCanvasSize);
            rect.localRotation = Quaternion.Euler(0f, 0f, pLabel.Angle);
            _labelObjects.Add(label);
        }

        private static void ApplyMapModeLabelPlacement(
            IReadOnlyList<KingdomAtlasLabel> pLabels, KingdomAtlasNode pNode,
            int pResolution)
        {
            if (pLabels == null || pNode?.VisibleZones == null) return;
            ResolveLabelBounds(pNode, out int minX, out int maxX,
                out int minY, out int maxY);
            int worldWidth = Math.Max(1, maxX - minX + 1);
            int worldHeight = Math.Max(1, maxY - minY + 1);
            for (int labelIndex = 0; labelIndex < pLabels.Count; labelIndex++)
            {
                KingdomAtlasLabel label = pLabels[labelIndex];
                var tiles = new List<Vector2Int>();
                for (int cellIndex = 0; cellIndex < pNode.VisibleZones.Count;
                     cellIndex++)
                {
                    KingdomAtlasZoneCell cell = pNode.VisibleZones[cellIndex];
                    if (cell.Water || !pNode.CityOwners.TryGetValue(
                            cell.CityId, out long owner) ||
                        owner != label.KingdomId) continue;
                    tiles.Add(new Vector2Int(cell.X, cell.Y));
                }
                if (tiles.Count == 0) continue;
                HierarchicalVassalMapModeLabelPlacement placement =
                    HierarchicalVassalMapModeGeometry.CalculateLabelPlacement(
                        tiles, label.Text);
                label.X = Mathf.RoundToInt(placement.Centroid.x);
                label.Y = Mathf.RoundToInt(placement.Centroid.y);
                label.Angle = placement.Angle;
                float renderedWorldSize =
                    HierarchicalVassalMapModeRules.ResolveRenderedLabelSize(
                        placement.Size, true);
                float fittedPixelSize = KingdomAtlasRules.
                    CalculateLabelPixelSize(
                    renderedWorldSize, pResolution, worldWidth, worldHeight);
                label.Size = KingdomAtlasRules.
                    ScaleAtlasCountryLabelForTerritory(fittedPixelSize,
                        tiles.Count, worldWidth, worldHeight);
            }
        }

        private static void ResolveLabelBounds(KingdomAtlasNode pNode,
            out int pMinX, out int pMaxX, out int pMinY, out int pMaxY)
        {
            if (pNode != null && pNode.TerrainWorldWidth > 1 &&
                pNode.TerrainWorldHeight > 1)
            {
                pMinX = 0;
                pMaxX = pNode.TerrainWorldWidth - 1;
                pMinY = 0;
                pMaxY = pNode.TerrainWorldHeight - 1;
                return;
            }

            pMinX = int.MaxValue;
            pMaxX = int.MinValue;
            pMinY = int.MaxValue;
            pMaxY = int.MinValue;
            IReadOnlyList<KingdomAtlasZoneCell> cells =
                pNode?.VisibleZones;
            if (cells != null)
                for (int index = 0; index < cells.Count; index++)
                {
                    KingdomAtlasZoneCell cell = cells[index];
                    pMinX = Math.Min(pMinX, cell.X);
                    pMaxX = Math.Max(pMaxX, cell.X);
                    pMinY = Math.Min(pMinY, cell.Y);
                    pMaxY = Math.Max(pMaxY, cell.Y);
                }
            if (pMinX == int.MaxValue)
            {
                pMinX = 0;
                pMaxX = 0;
                pMinY = 0;
                pMaxY = 0;
            }
        }

        private void ClearLabels()
        {
            for (int i = 0; i < _labelObjects.Count; i++)
                if (_labelObjects[i] != null) Destroy(_labelObjects[i]);
            _labelObjects.Clear();
        }

        private void ClearPreview()
        {
            if (_texture != null) Destroy(_texture);
            _texture = null;
            if (_mapImage != null) _mapImage.texture = null;
            ClearLabels();
            ClearChronicleColumns();
            if (_nodeText != null)
                _nodeText.text = _nodes == null || _nodes.Count == 0
                    ? AW_L10n.Text("aw_kingdom_atlas_no_nodes", "No nodes")
                    : string.Format(AW_L10n.Text("aw_kingdom_atlas_ready",
                        "Ready: {0} nodes"), _nodes.Count);
        }

        private void ClearChronicleColumns()
        {
            for (int index = 0; index < _chronicleColumns.Count; index++)
                if (_chronicleColumns[index] != null)
                    Destroy(_chronicleColumns[index]);
            _chronicleColumns.Clear();
            if (_chronicleContent != null)
                _chronicleContent.sizeDelta = new Vector2(
                    Mathf.Max(1f, _chronicleViewport?.rect.width ?? 1f),
                    Mathf.Max(1f, _chronicleViewport?.rect.height ?? 1f));
        }

        private void SetNavigationButtons()
        {
            bool hasNodes = _nodes != null && _nodes.Count > 0;
            if (_previous != null) _previous.interactable = hasNodes;
            if (_next != null) _next.interactable = hasNodes;
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
            text.font = ResolveMapFont(); text.fontSize = pSize;
            text.color = Color.white; text.alignment = pAnchor; text.text = pText;
            return text;
        }

        private static Font ResolveMapFont()
        {
            try
            {
                if (HierarchicalVassalMapFontSettings.UseBundledFont)
                {
                    Font mapFont = HierarchicalVassalMapFontLoader.TryLoad(16);
                    if (mapFont != null) return mapFont;
                }
                Font selected = HierarchicalVassalMapFontSettings.
                    TryCreateSelectedFont(16);
                if (selected != null) return selected;
            }
            catch { }
            return LocalizedTextManager.current_font;
        }

        private static Button Button(Transform pParent, string pName, string pText,
            UnityEngine.Events.UnityAction pAction, float pWidth = 48f)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(pParent, false);
            RectTransform rect = obj.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(pWidth, 24f);
            Image image = obj.GetComponent<Image>(); AW_UIStyle.ApplyButton(image, .95f);
            Button button = obj.GetComponent<Button>(); button.onClick.AddListener(pAction);
            Text label = Label(obj.transform, "Text", pText, 9, TextAnchor.MiddleCenter);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 6;
            label.resizeTextMaxSize = 9;
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static Scrollbar CreateHorizontalScrollbar(Transform pParent,
            ScrollRect pScroll)
        {
            var barObject = new GameObject("ChronicleScrollbar",
                typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barObject.transform.SetParent(pParent, false);
            barObject.GetComponent<Image>().color =
                new Color(0.08f, 0.075f, 0.065f, 0.98f);
            var slidingObject = new GameObject("Sliding Area",
                typeof(RectTransform));
            slidingObject.transform.SetParent(barObject.transform, false);
            RectTransform sliding = slidingObject.GetComponent<RectTransform>();
            sliding.anchorMin = Vector2.zero;
            sliding.anchorMax = Vector2.one;
            sliding.offsetMin = new Vector2(1f, 1f);
            sliding.offsetMax = new Vector2(-1f, -1f);
            var handleObject = new GameObject("Handle", typeof(RectTransform),
                typeof(Image));
            handleObject.transform.SetParent(sliding, false);
            RectTransform handle = handleObject.GetComponent<RectTransform>();
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.76f, 0.61f, 0.28f, 1f);
            Scrollbar scrollbar = barObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
            pScroll.horizontalScrollbar = scrollbar;
            pScroll.horizontalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.Permanent;
            return scrollbar;
        }

        private static void SetRect(RectTransform pRect, float pX, float pY,
            float pWidth, float pHeight)
        {
            if (pRect == null) return;
            pRect.anchorMin = pRect.anchorMax = new Vector2(0f, 1f);
            pRect.pivot = new Vector2(0f, 1f);
            pRect.anchoredPosition = new Vector2(pX, -pY);
            pRect.sizeDelta = new Vector2(Mathf.Max(1f, pWidth),
                Mathf.Max(1f, pHeight));
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
