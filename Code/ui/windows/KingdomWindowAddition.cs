using System.Linq;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     KingdomWindow 顶部中段注入(照 AW2 KingdomWindowAdditionComponent 的 **NML 嵌套布局** 范式,
    ///     适配新版)。结构(插在 Content 的 content_motto 兄弟位,横排):
    ///       [国王头像] [ 年号(带框) / 阶级占位框 国策占位框 ]
    ///     继承人**不在此画**——原版 showStatsRows 已用 tryToShowActor 在国王框下方画同款继承人框
    ///     (无继承人时由 AW_KingdomWindowPatch 的 tryToShowActor Prefix 抑制空行)。
    ///
    ///     为何用 NML AutoVertLayoutGroup 而非裸 Unity 布局:新版 Content 已自带 VerticalLayoutGroup,
    ///     裸插带 LayoutElement 的区块会与原版 stat 行尺寸约定冲突挤乱布局(上一版的教训)。
    ///     NML 的 BeginHoriGroup/BeginVertGroup 嵌套 + childControl/ForceExpand=false 能正确按固定尺寸排。
    ///     AutoLayoutGroup.GetLayoutGroup 复用 Content 已有 VerticalLayoutGroup(GetComponent??AddComponent),不重复加。
    ///
    ///     挂载:AW_KingdomWindowPatch 在 KingdomWindow.showTopPartInformation Prefix 里 AddComponent(查重)。
    /// </summary>
    internal class KingdomWindowAddition : MonoBehaviour
    {
        private const string MIDDLE_OBJ = "AW_KingdomMiddle";
        private const string XIAIZATION_OBJ = "AW_XiaizationStatus";
        private const float AvatarColumnWidth = 44f;

        private bool _inited;
        private KingdomWindow _window;
        private GameObject _middle;
        private GameObject _xiaizationRow;
        private Text _xiaizationText;
        private TipButton _xiaizationTip;
        private Text _yearText;
        private Text _policyStateText;
        private Text _policyExecText;
        private Text _policyDecisionText;
        private Image _policyStateIcon;
        private Image _policyExecIcon;
        private Image _policyDecisionIcon;
        private TipButton _policyStateTip;
        private TipButton _policyExecTip;
        private TipButton _policyDecisionTip;
        private Text _vassalStatusText;
        private TipButton _vassalStatusTip;
        private Text _courtText;
        private Image _courtIcon;
        private TipButton _courtTip;
        private Text _reservedText;
        private Image _reservedIcon;
        private TipButton _reservedTip;
        private Text _inheritanceText;
        private Image _inheritanceIcon;
        private TipButton _inheritanceTip;
        private UiUnitAvatarElement _kingAvatar;
        private UiUnitAvatarElement _heirAvatar;
        private GameObject _kingCol;   // 国王头像+标签竖列(整体显隐)
        private GameObject _heirCol;   // 继承人头像+标签竖列(无继承人时整列隐藏,不顶国王位)

        private void Awake() => TryInit();

        private void OnEnable()
        {
            TryInit();
            Refresh();
        }

        // ───────────────────────── 建结构(一次性) ─────────────────────────

        private void TryInit()
        {
            if (_inited) return;
            _window = GetComponent<KingdomWindow>();
            if (_window == null) return;

            Transform content = transform.Find("Background/Scroll View/Viewport/Content");
            if (content == null) return;

            // 复用已建的(热重载 / 反复进入)。
            Transform existing = content.Find(MIDDLE_OBJ);
            if (existing != null) { CacheRefs(existing.gameObject); _inited = true; return; }

            // 头像克隆模板:content_king 里现成的 UnitAvatarElement(对层级变化免疫;无则放弃,不崩)。
            var avatarTemplate = GetComponentsInChildren<UiUnitAvatarElement>(true).FirstOrDefault();
            if (avatarTemplate == null) return;

            _inited = true;

            // Content 上挂 AutoVertLayoutGroup 作 root(GetLayoutGroup 复用 Content 已有 VerticalLayoutGroup)。
            var root = content.gameObject.GetComponent<AutoVertLayoutGroup>()
                       ?? content.gameObject.AddComponent<AutoVertLayoutGroup>();

            // 中段横排 200×36(照 AW2 custom_part)。
            var custom = root.BeginHoriGroup(new Vector2(206, CourtUiRules.KingdomMiddleHeight), TextAnchor.MiddleCenter, 2, new RectOffset(0, 0, 2, 2));
            custom.name = MIDDLE_OBJ;
            _middle = custom.gameObject;
            // 插到 content_motto 兄弟位之后(新版 motto 在 content_motto 容器,非 AW2 的直接 MottoName)。
            Transform motto = content.Find("content_motto");
            if (motto != null) _middle.transform.SetSiblingIndex(motto.GetSiblingIndex() + 1);

            // 左:国王头像 + 下方"国王"标签(竖列)。
            _kingCol = BuildAvatarColumn(custom, avatarTemplate, "AW_KingAvatar", "aw_label_king", out _kingAvatar);
            custom.AddChild(_kingCol);

            // 中:竖排(年号框 + 一排两个国策占位框)。
            var middleBar = custom.BeginVertGroup(new Vector2(114, CourtUiRules.KingdomMiddleHeight), TextAnchor.UpperCenter, 2, new RectOffset(0, 0, 0, 0));

            GameObject yearBox = BuildBox("Year", new Vector2(114, CourtUiRules.PolicyRowHeight), out _yearText);
            middleBar.AddChild(yearBox);

            var policyRow = middleBar.BeginHoriGroup(new Vector2(114, CourtUiRules.PolicyRowHeight), TextAnchor.MiddleCenter, 2, new RectOffset(0, 0, 0, 0));
            policyRow.AddChild(BuildPolicyIconButton("PolicyState", new Vector2(28, 16),
                "ui/icons/iconDiplomacy", out _policyStateText, out _policyStateIcon, out _policyStateTip, OpenClassStateWindow));
            policyRow.AddChild(BuildPolicyIconButton("PolicyExec", new Vector2(28, 16),
                "ui/icons/iconKnowledge", out _policyExecText, out _policyExecIcon, out _policyExecTip, OpenResearchWindow));
            policyRow.AddChild(BuildPolicyIconButton("PolicyDecision", new Vector2(28, 16),
                "ui/icons/iconPlotsList", out _policyDecisionText, out _policyDecisionIcon, out _policyDecisionTip, OpenDecisionWindow));
            policyRow.AddChild(BuildTextButton("VassalStatus", new Vector2(22, 16),
                out _vassalStatusText, out _vassalStatusTip, OpenVassalWindow));

            middleBar.AddChild(BuildPolicyIconButton("CourtStatus",
                new Vector2(CourtUiRules.CourtButtonWidth, CourtUiRules.CourtButtonHeight),
                "ui/icons/iconDiplomacy", out _courtText, out _courtIcon, out _courtTip, OpenCourtWindow));
            GameObject reserved = BuildPolicyIconButton("ReservedStatus",
                new Vector2(CourtUiRules.CourtButtonWidth, CourtUiRules.CourtButtonHeight),
                "ui/icons/iconLock", out _reservedText, out _reservedIcon,
                out _reservedTip, null);
            ConfigureDiplomacyButton(reserved);
            middleBar.AddChild(reserved);
            middleBar.AddChild(BuildPolicyIconButton("InheritanceStatus",
                new Vector2(CourtUiRules.CourtButtonWidth,
                    CourtUiRules.CourtButtonHeight),
                "ui/Icons/iconKings", out _inheritanceText,
                out _inheritanceIcon, out _inheritanceTip,
                OpenInheritanceWindow));
            // 右:继承人头像 + 下方"继承人"标签(与国王对称)。show(heir) 自带点击→打开继承人窗;
            //    无继承人时 Refresh 里整列隐藏(不顶国王位 —— 用户报"继承人顶替了国王显示位")。
            _heirCol = BuildAvatarColumn(custom, avatarTemplate, "AW_HeirAvatar", "aw_label_heir", out _heirAvatar);
            custom.AddChild(_heirCol);

            EnsureXiaizationRow(content, root);
        }

        /// <summary>头像竖列:头像(克隆)在上 + 身份标签在下,整列作为 HoriGroup 的一个固定宽度子项。</summary>
        private GameObject BuildAvatarColumn(AutoHoriLayoutGroup pParentHori, UiUnitAvatarElement pTemplate,
            string pAvatarName, string pLabelKey, out UiUnitAvatarElement pAvatar)
        {
            var col = pParentHori.BeginVertGroup(new Vector2(AvatarColumnWidth, 36), TextAnchor.UpperCenter, 1, new RectOffset(0, 0, 0, 0));
            col.name = pAvatarName + "_Col";

            pAvatar = CloneAvatar(pTemplate, col.gameObject, pAvatarName);
            col.AddChild(pAvatar.gameObject);

            // 标签(国王/继承人),本地化键 aw_label_king / aw_label_heir。
            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var lrt = labelObj.GetComponent<RectTransform>();
            lrt.sizeDelta = new Vector2(AvatarColumnWidth, 10);
            var le = labelObj.GetComponent<LayoutElement>();
            le.minWidth = AvatarColumnWidth; le.preferredWidth = AvatarColumnWidth;
            le.minHeight = 10; le.preferredHeight = 10;
            var lt = labelObj.GetComponent<Text>();
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = LocalizedTextManager.current_font;
            lt.fontSize = 8;
            lt.resizeTextForBestFit = true; lt.resizeTextMinSize = 5; lt.resizeTextMaxSize = 9;
            lt.horizontalOverflow = HorizontalWrapMode.Overflow;
            lt.color = Color.white;
            lt.raycastTarget = false;
            var loc = labelObj.AddComponent<LocalizedText>();
            loc.setKeyAndUpdate(pLabelKey);
            col.AddChild(labelObj);

            return col.gameObject;
        }

        private void CacheRefs(GameObject middle)
        {
            _middle = middle;
            SetLayoutSize(middle.transform, 206f, CourtUiRules.KingdomMiddleHeight);
            _yearText = middle.GetComponentsInChildren<Text>(true)
                              .FirstOrDefault(t => t.transform.parent != null && t.transform.parent.name == "Year");
            Transform middleBar = middle.transform.FindRecursive("Year")?.parent;
            SetLayoutSize(middleBar, CourtUiRules.CourtButtonWidth, CourtUiRules.KingdomMiddleHeight);
            CachePolicyBox(middle.transform.FindRecursive("PolicyState"), out _policyStateText, out _policyStateIcon,
                out _policyStateTip, OpenClassStateWindow);
            CachePolicyBox(middle.transform.FindRecursive("PolicyExec"), out _policyExecText, out _policyExecIcon,
                out _policyExecTip, OpenResearchWindow);
            Transform decision = middle.transform.FindRecursive("PolicyDecision");
            if (decision == null)
            {
                Transform row = middle.transform.FindRecursive("PolicyExec")?.parent;
                if (row != null)
                {
                    GameObject obj = BuildPolicyIconButton("PolicyDecision", new Vector2(28, 16),
                        "ui/icons/iconPlotsList", out _policyDecisionText, out _policyDecisionIcon, out _policyDecisionTip, OpenDecisionWindow);
                    obj.transform.SetParent(row, false);
                }
            }
            else
            {
                CachePolicyBox(decision, out _policyDecisionText, out _policyDecisionIcon, out _policyDecisionTip, OpenDecisionWindow);
            }
            Transform vassal = middle.transform.FindRecursive("VassalStatus");
            if (vassal == null)
            {
                Transform row = middle.transform.FindRecursive("PolicyDecision")?.parent;
                if (row != null)
                {
                    GameObject obj = BuildTextButton("VassalStatus", new Vector2(22, 16),
                        out _vassalStatusText, out _vassalStatusTip, OpenVassalWindow);
                    obj.transform.SetParent(row, false);
                }
            }
            else
            {
                CacheTextButton(vassal, out _vassalStatusText, out _vassalStatusTip, OpenVassalWindow);
            }
            Transform court = middle.transform.FindRecursive("CourtStatus");
            if (court == null)
            {
                if (middleBar != null)
                {
                    GameObject obj = BuildPolicyIconButton("CourtStatus",
                        new Vector2(CourtUiRules.CourtButtonWidth, CourtUiRules.CourtButtonHeight),
                        "ui/icons/iconDiplomacy", out _courtText, out _courtIcon, out _courtTip, OpenCourtWindow);
                    obj.transform.SetParent(middleBar, false);
                }
            }
            else
            {
                CachePolicyBox(court, out _courtText, out _courtIcon, out _courtTip, OpenCourtWindow);
            }
            Transform reserved = middle.transform.FindRecursive("ReservedStatus");
            if (reserved == null && middleBar != null)
            {
                GameObject obj = BuildPolicyIconButton("ReservedStatus",
                    new Vector2(CourtUiRules.CourtButtonWidth,
                        CourtUiRules.CourtButtonHeight),
                    "ui/icons/iconLock", out _reservedText, out _reservedIcon,
                    out _reservedTip, null);
                ConfigureDiplomacyButton(obj);
                obj.transform.SetParent(middleBar, false);
            }
            else if (reserved != null)
            {
                CachePolicyBox(reserved, out _reservedText, out _reservedIcon,
                    out _reservedTip, null);
                ConfigureDiplomacyButton(reserved.gameObject);
            }
            Transform inheritance = middle.transform.FindRecursive(
                "InheritanceStatus");
            if (inheritance == null && middleBar != null)
            {
                GameObject obj = BuildPolicyIconButton("InheritanceStatus",
                    new Vector2(CourtUiRules.CourtButtonWidth,
                        CourtUiRules.CourtButtonHeight),
                    "ui/Icons/iconKings", out _inheritanceText,
                    out _inheritanceIcon, out _inheritanceTip,
                    OpenInheritanceWindow);
                obj.transform.SetParent(middleBar, false);
            }
            else if (inheritance != null)
            {
                CachePolicyBox(inheritance, out _inheritanceText,
                    out _inheritanceIcon, out _inheritanceTip,
                    OpenInheritanceWindow);
            }
            Transform virtualTitles = middle.transform.FindRecursive(
                "VirtualTitles");
            if (virtualTitles != null)
                Object.Destroy(virtualTitles.gameObject);
            var avatars = middle.GetComponentsInChildren<UiUnitAvatarElement>(true);
            // 约定建立顺序:[0]=国王(AW_KingAvatar)、[1]=继承人(AW_HeirAvatar)。
            _kingAvatar = avatars.FirstOrDefault(a => a.name == "AW_KingAvatar") ?? (avatars.Length > 0 ? avatars[0] : null);
            _heirAvatar = avatars.FirstOrDefault(a => a.name == "AW_HeirAvatar") ?? (avatars.Length > 1 ? avatars[1] : null);
            // 竖列容器(头像名 + "_Col")。
            Transform kc = middle.transform.Find("AW_KingAvatar_Col");
            Transform hc = middle.transform.Find("AW_HeirAvatar_Col");
            _kingCol = kc != null ? kc.gameObject : (_kingAvatar != null ? _kingAvatar.transform.parent.gameObject : null);
            _heirCol = hc != null ? hc.gameObject : (_heirAvatar != null ? _heirAvatar.transform.parent.gameObject : null);
            Transform content = middle.transform.parent;
            if (content != null)
            {
                var root = content.GetComponent<AutoVertLayoutGroup>() ??
                           content.gameObject.AddComponent<AutoVertLayoutGroup>();
                EnsureXiaizationRow(content, root);
            }
        }

        private void EnsureXiaizationRow(Transform pContent,
            AutoVertLayoutGroup pRoot)
        {
            if (pContent == null) return;
            Transform existing = pContent.Find(XIAIZATION_OBJ);
            if (existing != null)
            {
                _xiaizationRow = existing.gameObject;
                SetLayoutSize(existing, 206f, 14f);
                CacheTextButton(existing, out _xiaizationText,
                    out _xiaizationTip, null);
            }
            else
            {
                _xiaizationRow = BuildTextButton(XIAIZATION_OBJ,
                    new Vector2(206, 14), out _xiaizationText,
                    out _xiaizationTip, null);
                if (pRoot != null) pRoot.AddChild(_xiaizationRow);
                else _xiaizationRow.transform.SetParent(pContent, false);
            }
            if (_middle != null)
                _xiaizationRow.transform.SetSiblingIndex(
                    _middle.transform.GetSiblingIndex() + 1);
        }

        /// <summary>克隆现成头像;show(actor) 自带 banner 显隐 + 点击 openUnitWindow,无需手绑。</summary>
        private UiUnitAvatarElement CloneAvatar(UiUnitAvatarElement template, GameObject parent, string name)
        {
            var clone = Object.Instantiate(template, parent.transform);
            clone.name = name;
            clone.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
            clone.gameObject.SetActive(false); // Refresh 按有无王再开
            return clone;
        }

        /// <summary>带背景框的盒子(Image 框 + 居中 Text 子物体)。框 sprite 取不到则用半透明底兜底。</summary>
        private GameObject BuildBox(string name, Vector2 size, out Text text)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rt = box.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            box.transform.localScale = Vector3.one;

            var img = box.GetComponent<Image>();
            Sprite frame = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
            if (frame != null) { img.sprite = frame; img.type = Image.Type.Sliced; img.color = Color.white; }
            else img.color = new Color(0f, 0f, 0f, 0.25f);
            img.raycastTarget = false;

            var le = box.GetComponent<LayoutElement>();
            le.minWidth = size.x; le.preferredWidth = size.x;
            le.minHeight = size.y; le.preferredHeight = size.y;

            var tObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tObj.transform.SetParent(box.transform, false);
            var tr = tObj.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
            text = tObj.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.font = LocalizedTextManager.current_font;
            text.fontSize = 9;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 1;
            text.resizeTextMaxSize = 10;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;
            return box;
        }

        /// <summary>国策占位框:带框 + 浅色占位本地化字(留桩)。</summary>
        private GameObject BuildPolicyBox(string name, Vector2 size, out Text text, out TipButton tip,
            System.Action pClick)
        {
            var box = BuildBox(name, size, out text);
            text.color = Color.white;
            text.fontSize = 8;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 1;
            text.resizeTextMaxSize = 8;

            var button = box.GetComponent<Button>() ?? box.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => pClick?.Invoke());

            tip = box.GetComponent<TipButton>() ?? box.AddComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            return box;
        }

        private GameObject BuildPolicyIconButton(string name, Vector2 size, string iconPath, out Text text,
            out Image icon,
            out TipButton tip, System.Action pClick)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement),
                typeof(Button), typeof(TipButton));
            var rt = box.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            box.transform.localScale = Vector3.one;

            AW_UIStyle.ApplyButton(box.GetComponent<Image>(), 0.95f);

            var le = box.GetComponent<LayoutElement>();
            le.minWidth = size.x; le.preferredWidth = size.x;
            le.minHeight = size.y; le.preferredHeight = size.y;

            var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconObj.transform.SetParent(box.transform, false);
            var irt = iconObj.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(12, 12);
            irt.anchoredPosition = new Vector2(2, 0);
            icon = iconObj.GetComponent<Image>();
            icon.sprite = SpriteTextureLoader.getSprite(iconPath)
                          ?? SpriteTextureLoader.getSprite("ui/icons/iconKingdomList")
                          ?? SpriteTextureLoader.getSprite("ui/special/button");
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var tObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tObj.transform.SetParent(box.transform, false);
            var tr = tObj.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(15, 0);
            tr.offsetMax = new Vector2(-2, 0);
            text = tObj.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.font = LocalizedTextManager.current_font;
            text.fontSize = 8;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 1;
            text.resizeTextMaxSize = 8;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;

            var button = box.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => pClick?.Invoke());

            tip = box.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            return box;
        }

        private GameObject BuildTextButton(string name, Vector2 size, out Text text, out TipButton tip,
            System.Action pClick)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement),
                typeof(Button), typeof(TipButton));
            var rt = box.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            box.transform.localScale = Vector3.one;

            AW_UIStyle.ApplyButton(box.GetComponent<Image>(), 0.95f);

            var le = box.GetComponent<LayoutElement>();
            le.minWidth = size.x; le.preferredWidth = size.x;
            le.minHeight = size.y; le.preferredHeight = size.y;

            var tObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tObj.transform.SetParent(box.transform, false);
            var tr = tObj.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = Vector2.zero;
            tr.offsetMax = Vector2.zero;
            text = tObj.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.font = LocalizedTextManager.current_font;
            text.fontSize = 8;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 1;
            text.resizeTextMaxSize = 9;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = Color.white;

            var button = box.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => pClick?.Invoke());

            tip = box.GetComponent<TipButton>();
            tip.type = AW_RawTooltip.TYPE;
            return box;
        }

        private void CachePolicyBox(Transform pBox, out Text pText, out Image pIcon, out TipButton pTip, System.Action pClick)
        {
            pText = null;
            pIcon = null;
            pTip = null;
            if (pBox == null) return;
            pText = pBox.GetComponentsInChildren<Text>(true).FirstOrDefault();
            Transform icon = pBox.Find("Icon");
            pIcon = icon != null ? icon.GetComponent<Image>() : null;

            var button = pBox.GetComponent<Button>() ?? pBox.gameObject.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => pClick?.Invoke());

            pTip = pBox.GetComponent<TipButton>() ?? pBox.gameObject.AddComponent<TipButton>();
            pTip.type = AW_RawTooltip.TYPE;
        }

        private void CacheTextButton(Transform pBox, out Text pText, out TipButton pTip, System.Action pClick)
        {
            pText = null;
            pTip = null;
            if (pBox == null) return;
            pText = pBox.GetComponentsInChildren<Text>(true).FirstOrDefault();

            var button = pBox.GetComponent<Button>() ?? pBox.gameObject.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => pClick?.Invoke());

            pTip = pBox.GetComponent<TipButton>() ?? pBox.gameObject.AddComponent<TipButton>();
            pTip.type = AW_RawTooltip.TYPE;
        }

        private void OpenClassStateWindow()
        {
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom == null || kingdom.isRekt()) return;
            KingdomPolicyWindow.OpenClassState(kingdom.id);
        }

        private void OpenResearchWindow()
        {
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom == null || kingdom.isRekt()) return;
            KingdomPolicyWindow.OpenResearch(kingdom.id);
        }

        private void OpenDecisionWindow()
        {
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom == null || kingdom.isRekt()) return;
            KingdomPolicyWindow.OpenDecision(kingdom.id);
        }

        private void OpenVassalWindow()
        {
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom == null || kingdom.isRekt()) return;
            VassalRelationWindow.Open(kingdom.id);
        }

        private void OpenCourtWindow()
        {
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom == null || kingdom.isRekt()) return;
            CourtWindow.Open(kingdom.id);
        }

        private void ConfigureDiplomacyButton(GameObject pObject)
        {
            if (pObject == null) return;
            Button button = pObject.GetComponent<Button>();
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OpenDiplomacyWindow);
            button.interactable = true;
        }

        private void OpenDiplomacyWindow()
        {
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom?.data == null || kingdom.isRekt()) return;
            DiplomacyConversationWindow.Open(kingdom.id);
        }

        private void OpenInheritanceWindow()
        {
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom?.data == null || kingdom.isRekt()) return;
            InheritanceLawWindow.Open(kingdom.id);
        }

        // ───────────────────────── 刷新数据(每次开窗) ─────────────────────────

        private void RefreshPolicyBoxes(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom))
            {
                SetPolicyIcon(_policyStateIcon, "ui/icons/iconDiplomacy");
                SetPolicyIcon(_policyExecIcon, "ui/icons/iconKnowledge");
                SetPolicyIcon(_policyDecisionIcon, "ui/icons/iconPlotsList");
                if (_policyStateText != null)
                {
                    _policyStateText.text = PolicyStateButtonLabel(pKingdom);
                    _policyStateText.color = DirectKingdomTextColor(pKingdom);
                }
                if (_policyExecText != null)
                {
                    _policyExecText.text = AW_L10n.Text("aw_policy_research_short", "\u7814");
                    _policyExecText.color = Color.white;
                }
                if (_policyDecisionText != null)
                {
                    _policyDecisionText.text = AW_L10n.Text("aw_policy_decision_short", "\u4EE4");
                    _policyDecisionText.color = Color.white;
                }

                bool supported = KingdomPolicyService.CanUsePolicySystem(pKingdom);
                string title = supported
                    ? AW_L10n.Text("aw_policy_disabled", "\u56FD\u7B56\u672A\u542F\u7528")
                    : AW_L10n.Text("aw_policy_unsupported", "\u5F53\u524D\u7269\u79CD\u4E0D\u652F\u6301AW3\u56FD\u7B56");
                string desc = supported
                    ? AW_L10n.Text("aw_policy_click_to_enable", "\u70B9\u51FB\u8FDB\u5165\u56FD\u7B56\u7A97\u53E3\uFF0C\u53EF\u4EE5\u4E3AHuman\u56FD\u5BB6\u624B\u52A8\u542F\u7528\u3002")
                    : AW_L10n.Text("aw_policy_unsupported_desc", "\u76EE\u524DAW3\u56FD\u7B56\u53EA\u5BF9Xia\u9ED8\u8BA4\u542F\u7528\uFF0CHuman\u9700\u624B\u52A8\u5F00\u542F\u3002");
                SetPolicyTip(_policyStateTip, title, desc);
                SetPolicyTip(_policyExecTip, title, desc);
                SetPolicyTip(_policyDecisionTip, title, desc);
                return;
            }

            KingdomPolicyService.EnsureInitialized(pKingdom);
            RefreshPolicyButtonIcons(pKingdom);

            string classId = KingdomPolicyService.GetClassId(pKingdom);
            string className = AW_L10n.Text(KingdomPolicyService.GetClassLocaleKey(classId), ClassFallbackName(classId));
            if (_policyStateText != null)
            {
                _policyStateText.text = PolicyStateButtonLabel(pKingdom);
                _policyStateText.color = DirectKingdomTextColor(pKingdom);
            }

            string current = BuildCurrentPolicyText(pKingdom);
            if (_policyExecText != null)
            {
                _policyExecText.text = AW_L10n.Text("aw_policy_research_short", "\u7814");
                _policyExecText.color = Color.white;
            }

            string decision = BuildCurrentDecisionText(pKingdom);
            if (_policyDecisionText != null)
            {
                _policyDecisionText.text = AW_L10n.Text("aw_policy_decision_short", "\u4EE4");
                _policyDecisionText.color = Color.white;
            }

            string pointText = AW_L10n.Text("aw_policy_points_short", "\u653F") + ":" +
                               Mathf.FloorToInt(KingdomPolicyService.GetPoliticalPoints(pKingdom)) + "  " +
                               AW_L10n.Text("aw_tech_points_short", "\u6280") + ":" +
                               Mathf.FloorToInt(KingdomPolicyService.GetTechPoints(pKingdom));
            SetPolicyTip(_policyStateTip, AW_L10n.Text("aw_policy_class_state", "\u653F\u6CBB\u72B6\u6001"), className + "\n" + pointText);
            SetPolicyTip(_policyExecTip, AW_L10n.Text("aw_policy_current_research", "\u5F53\u524D\u7814\u53D1"), current + "\n" + pointText);
            SetPolicyTip(_policyDecisionTip, AW_L10n.Text("aw_policy_decisions", "\u5E38\u6001\u51B3\u7B56"), decision + "\n" + pointText);
        }

        private void RefreshVassalButton(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            if (_vassalStatusText != null)
            {
                _vassalStatusText.text = VassalService.GetStatusShort(pKingdom);
                _vassalStatusText.color = DirectKingdomTextColor(pKingdom);
            }
            SetPolicyTip(_vassalStatusTip, AW_L10n.Text("aw_vassal_relations", "\u9644\u5EB8\u5173\u7CFB"),
                VassalService.GetStatusTooltip(pKingdom));
        }

        private void RefreshXiaizationStatus(Kingdom pKingdom)
        {
            if (_xiaizationRow == null || pKingdom?.data == null) return;
            bool nativeXia = XiaizationService.IsNativePolicyKingdom(
                pKingdom);
            bool show = XiaizationStatusDisplayRules.ShouldShow(nativeXia);
            _xiaizationRow.SetActive(show);
            if (!show) return;
            int level = XiaizationService.GetLevel(pKingdom);
            if (_xiaizationText != null)
            {
                _xiaizationText.text = XiaizationStatusDisplayRules.Format(
                    AW_L10n.Text("aw_xiaization_status", "夏化"), level,
                    XiaizationService.GetLevelLabel(pKingdom));
                _xiaizationText.color = DirectKingdomTextColor(pKingdom);
            }
            SetPolicyTip(_xiaizationTip,
                AW_L10n.Text("aw_xiaization_level", "入夏等级"),
                XiaizationService.BuildTooltip(pKingdom));
        }

        private static string PolicyStateButtonLabel(Kingdom pKingdom)
        {
            string localeKey = KingdomPolicyProfileRules
                .ResolvePolicyStateLabelKey(
                    KingdomPolicyService.GetPolicyProfile(pKingdom));
            return AW_L10n.Text(localeKey, "\u653F");
        }

        private void RefreshCourtButton(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            bool policyEnabled = KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom);
            bool officialCourt = policyEnabled && CourtService.HasOfficialCourt(pKingdom);
            bool western = CourtProfileRegistry.For(pKingdom)?.Id ==
                           CourtProfileId.Western;
            string title = !policyEnabled
                ? AW_L10n.Text("aw_court_button_locked", "\u5B98\u573A\u672A\u542F\u7528")
                : western
                    ? officialCourt
                        ? AW_L10n.Text("aw_court_button_western_official",
                            "Western Court")
                        : AW_L10n.Text("aw_court_button_western_primitive",
                            "Household Council")
                    : officialCourt
                        ? AW_L10n.Text("aw_court_button_official", "\u767E\u5BB6\u5B98\u573A")
                        : AW_L10n.Text("aw_court_button_primitive", "\u539F\u59CB\u671D\u4F1A");

            if (_courtText != null)
            {
                _courtText.text = title;
                _courtText.color = policyEnabled ? Color.white : new Color(0.78f, 0.78f, 0.78f, 1f);
            }

            SetPolicyIcon(_courtIcon, officialCourt ? "ui/icons/iconDiplomacy" : "ui/icons/iconKingdomList");
            CourtSnapshot snapshot = CourtService.GetSnapshot(pKingdom);
            string desc;
            if (!policyEnabled)
            {
                desc = (KingdomPolicyService.CanUsePolicySystem(pKingdom)
                        ? AW_L10n.Text("aw_policy_disabled", "\u56FD\u7B56\u672A\u542F\u7528")
                        : AW_L10n.Text("aw_policy_unsupported", "\u5F53\u524D\u7269\u79CD\u4E0D\u652F\u6301AW3\u56FD\u7B56")) +
                       "\n" + AW_L10n.Text("aw_court_tooltip_cached",
                           "\u5B98\u573A\u6570\u636E\u6309\u4F4E\u9891\u7F13\u5B58\u5237\u65B0\uFF0C\u6253\u5F00\u7A97\u53E3\u4E0D\u4F1A\u5B9E\u65F6\u626B\u63CF\u5168\u56FD\u4EBA\u7269");
            }
            else
            {
                desc = AW_L10n.Text("aw_court_dominant_school", "\u4E3B\u6D41\u5B66\u6D3E") + ": " +
                       CourtSchoolName(snapshot.dominant_school) + "\n" +
                       AW_L10n.Text("aw_court_efficiency", "\u5B98\u573A\u6548\u7387") + ": " +
                       Mathf.FloorToInt(snapshot.efficiency);
            }

            SetPolicyTip(_courtTip, title, desc);
        }

        private void RefreshReservedButton()
        {
            string title = AW_L10n.Text("aw_diplomacy_window_title",
                "Diplomacy");
            if (_reservedText != null)
            {
                _reservedText.text = title;
                _reservedText.color = Color.white;
            }
            SetPolicyIcon(_reservedIcon, "ui/icons/iconDiplomacy");
            if (_reservedIcon != null)
                _reservedIcon.color = Color.white;
            SetPolicyTip(_reservedTip, title,
                AW_L10n.Text("aw_diplomacy_window_desc",
                    "Review diplomatic relations and event conversations."));
        }

        private void RefreshInheritanceButton(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            InheritanceLaw law = InheritanceLawService.GetEffectiveLaw(
                pKingdom);
            string title = law switch
            {
                InheritanceLaw.MilitaryAcclaim =>
                    AW_L10n.Text("aw_inheritance_law_military",
                        "\u519b\u4e2d\u62e5\u7acb"),
                InheritanceLaw.CivilAcclaim =>
                    AW_L10n.Text("aw_inheritance_law_civil",
                        "\u6587\u5b98\u62e5\u7acb"),
                _ => AW_L10n.Text("aw_inheritance_law_primogeniture",
                    "\u5ae1\u957f\u7ee7\u627f")
            };
            if (_inheritanceText != null)
            {
                _inheritanceText.text = title;
                _inheritanceText.color = Color.white;
            }
            SetPolicyIcon(_inheritanceIcon, "ui/Icons/iconKings");
            pKingdom.data.get(LineageKeys.INHERITANCE_CANDIDATE_MODE,
                out string mode, SuccessionMode.NONE);
            bool militaryGovernorate = VassalService.GetSubjectKind(pKingdom) ==
                                       VassalSubjectKind.MilitaryGovernorate;
            Actor heir = militaryGovernorate
                ? MilitaryGovernorateSuccessionService.
                    GetDesignatedSuccessorForReadModel(pKingdom)
                : HeirService.PeekStoredHeirForMinimap(pKingdom);
            string heirTitle = HeirTitleRules.DefaultTitleText(
                pKingdom, mode);
            string heirName = heir?.getName() ??
                              AW_L10n.Text("aw_inheritance_no_candidate",
                                  "\u6682\u65e0\u5019\u9009");
            SetPolicyTip(_inheritanceTip,
                AW_L10n.Text("aw_inheritance_window_title", "\u7ee7\u627f\u6cd5"),
                title + "\n" + heirTitle + ": " + heirName);
        }

        private static string BuildCurrentPolicyText(Kingdom pKingdom)
        {
            KingdomPolicyDef tech = KingdomPolicyService.GetDefinition(
                pKingdom, KingdomPolicyService.GetCurrent(pKingdom,
                    PolicyNodeKind.Tech));
            KingdomPolicyDef social = KingdomPolicyService.GetDefinition(
                pKingdom, KingdomPolicyService.GetCurrent(pKingdom,
                    PolicyNodeKind.Social));
            string techName = tech == null ? "" : AW_L10n.Text(tech.NameKey, tech.FallbackName);
            string socialName = social == null ? "" : AW_L10n.Text(social.NameKey, social.FallbackName);
            if (string.IsNullOrEmpty(techName) && string.IsNullOrEmpty(socialName))
                return AW_L10n.Text("aw_policy_idle", "\u5F85\u5B9A");
            if (string.IsNullOrEmpty(techName)) return socialName;
            if (string.IsNullOrEmpty(socialName)) return techName;
            return techName + "/" + socialName;
        }

        private static string BuildCurrentDecisionText(Kingdom pKingdom)
        {
            KingdomPolicyDef decision = KingdomPolicyService.GetDefinition(
                pKingdom, KingdomPolicyService.GetCurrent(pKingdom,
                    PolicyNodeKind.Decision));
            if (decision == null)
                return AW_L10n.Text("aw_policy_no_current_decision", "\u5F53\u524D\u6CA1\u6709\u51B3\u7B56");

            string name = AW_L10n.Text(decision.NameKey, decision.FallbackName);
            string target = KingdomPolicyService.BuildDecisionTargetLine(pKingdom);
            return string.IsNullOrEmpty(target) ? name : name + "\n" + target;
        }

        private void RefreshPolicyButtonIcons(Kingdom pKingdom)
        {
            string classId = KingdomPolicyService.GetClassId(pKingdom);
            SetPolicyIcon(_policyStateIcon, ClassIconPath(classId));
            SetPolicyIcon(_policyExecIcon, CurrentResearchIcon(pKingdom));
            SetPolicyIcon(_policyDecisionIcon, CurrentDecisionIcon(pKingdom));
        }

        private static string CurrentResearchIcon(Kingdom pKingdom)
        {
            KingdomPolicyDef tech = KingdomPolicyService.GetDefinition(
                pKingdom, KingdomPolicyService.GetCurrent(pKingdom,
                    PolicyNodeKind.Tech));
            if (tech != null && !string.IsNullOrEmpty(tech.IconPath)) return tech.IconPath;

            KingdomPolicyDef social = KingdomPolicyService.GetDefinition(
                pKingdom, KingdomPolicyService.GetCurrent(pKingdom,
                    PolicyNodeKind.Social));
            if (social != null && !string.IsNullOrEmpty(social.IconPath)) return social.IconPath;

            return "ui/icons/iconKnowledge";
        }

        private static string CurrentDecisionIcon(Kingdom pKingdom)
        {
            KingdomPolicyDef decision = KingdomPolicyService.GetDefinition(
                pKingdom, KingdomPolicyService.GetCurrent(pKingdom,
                    PolicyNodeKind.Decision));
            if (decision != null && !string.IsNullOrEmpty(decision.IconPath)) return decision.IconPath;
            return "ui/icons/iconPlotsList";
        }

        private static string ClassIconPath(string classId)
        {
            return KingdomPolicyService.GetClassIconPath(classId);
        }

        private static void SetPolicyIcon(Image pIcon, string pIconPath)
        {
            if (pIcon == null) return;
            Sprite sprite = SpriteTextureLoader.getSprite(pIconPath)
                            ?? SpriteTextureLoader.getSprite("ui/icons/iconKnowledge")
                            ?? SpriteTextureLoader.getSprite("ui/special/button");
            if (sprite == null) return;
            pIcon.sprite = sprite;
            pIcon.enabled = true;
            pIcon.preserveAspect = true;
        }

        private static string ClassFallbackName(string pClassId)
        {
            if (pClassId == KingdomPolicyDefs.ClassSlaveOwner) return "\u5974\u96B6\u5236";
            if (pClassId == KingdomPolicyDefs.ClassHalfAristocrat) return "\u534A\u8D35\u65CF\u5236";
            if (pClassId == KingdomPolicyDefs.ClassAristocrat) return "\u5C01\u5EFA\u8D35\u65CF";
            if (pClassId == KingdomPolicyDefs.ClassReform) return "\u6539\u9769\u5236";
            if (pClassId == KingdomPolicyDefs.ClassRepublic) return "\u5171\u548C\u653F\u4F53";
            if (pClassId == KingdomPolicyDefs.ClassRebel) return "\u519C\u6C11\u4E49\u519B";
            if (pClassId == KingdomPolicyDefs.ClassBandit) return "\u571F\u532A";
            return "\u90E8\u843D\u5236";
        }

        private static string CourtSchoolName(string pSchoolId)
        {
            switch (pSchoolId)
            {
                case CourtSchoolId.Ru: return AW_L10n.Text("aw_court_school_ru", "\u5112\u5BB6");
                case CourtSchoolId.Legalist: return AW_L10n.Text("aw_court_school_fa", "\u6CD5\u5BB6");
                case CourtSchoolId.Dao: return AW_L10n.Text("aw_court_school_dao", "\u9053\u5BB6");
                case CourtSchoolId.Mohist: return AW_L10n.Text("aw_court_school_mo", "\u58A8\u5BB6");
                case CourtSchoolId.Military: return AW_L10n.Text("aw_court_school_bing", "\u5175\u5BB6");
                case CourtSchoolId.Diplomat: return AW_L10n.Text("aw_court_school_zongheng", "\u7EB5\u6A2A\u5BB6");
                case CourtSchoolId.Agrarian: return AW_L10n.Text("aw_court_school_nong", "\u519C\u5BB6");
                case CourtSchoolId.YinYang: return AW_L10n.Text("aw_court_school_yinyang", "\u9634\u9633\u5BB6");
                case CourtSchoolId.Logician: return AW_L10n.Text("aw_court_school_ming", "\u540D\u5BB6");
                case CourtSchoolId.Medical: return AW_L10n.Text("aw_court_school_medical", "\u533B\u5BB6");
                case CourtSchoolId.Syncretist: return AW_L10n.Text("aw_court_school_syncretist", "\u6742\u5BB6");
                case CourtSchoolId.Merchant: return AW_L10n.Text("aw_court_school_merchant", "\u5546\u5BB6");
                case CourtSchoolId.Craftsman: return AW_L10n.Text("aw_court_school_craftsman", "\u5DE5\u5BB6");
                case CourtSchoolId.Historian: return AW_L10n.Text("aw_court_school_historian", "\u53F2\u5BB6");
                case CourtSchoolId.PrimitiveMinister: return AW_L10n.Text("aw_court_primitive_title", "\u539F\u59CB\u671D\u4F1A");
                default: return AW_L10n.Text("aw_policy_idle", "\u5F85\u5B9A");
            }
        }

        private static void SetPolicyTip(TipButton pTip, string pTitle, string pDesc)
        {
            if (pTip == null) return;
            pTip.enabled = true;
            pTip.type = AW_RawTooltip.TYPE;
            pTip.hoverAction = () =>
                Tooltip.show(pTip.gameObject, AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = pTitle ?? "", tip_description = pDesc ?? "" });
        }

        private void Refresh()
        {
            if (!_inited || _middle == null) return;
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom == null || kingdom.isRekt())
            {
                _middle.SetActive(false);
                if (_xiaizationRow != null)
                    _xiaizationRow.SetActive(false);
                return;
            }
            _middle.SetActive(true);
            RefreshXiaizationStatus(kingdom);

            // 年号
            if (_yearText != null)
            {
                string yearName = YearNameService.GetYearName(kingdom);
                Transform yearBox = _yearText.transform.parent;
                if (string.IsNullOrEmpty(yearName))
                {
                    if (yearBox != null) yearBox.gameObject.SetActive(false);
                }
                else
                {
                    if (yearBox != null) yearBox.gameObject.SetActive(true);
                    _yearText.text = yearName;
                    _yearText.color = DirectKingdomTextColor(kingdom);
                }
            }

            // 国王列(头像 + "国王"标签):有王整列显示,无王整列隐藏。
            RefreshPolicyBoxes(kingdom);
            RefreshCourtButton(kingdom);
            RefreshReservedButton();
            RefreshVassalButton(kingdom);
            RefreshInheritanceButton(kingdom);
            if (_kingCol != null && _kingAvatar != null)
            {
                bool hasKing = kingdom.hasKing();
                _kingCol.SetActive(hasKing);
                if (hasKing)
                {
                    _kingAvatar.show(kingdom.king);
                    Transform kingLabel = _kingCol.transform.Find("Label");
                    if (kingLabel != null)
                    {
                        bool republic = RepublicGovernmentService.IsRepublic(kingdom);
                        bool mandate = MandateService.IsRuntimeMandateKingdom(kingdom);
                        bool empireRank = KingdomTitleService.IsEmperor(kingdom);
                        bool militaryGovernorate = VassalService.GetSubjectKind(
                            kingdom) == VassalSubjectKind.MilitaryGovernorate;
                        bool ceremonialEmperor = empireRank || mandate;
                        string key = GovernmentTitleRules.RulerKey(republic);
                        string monarchyLabel = AW_L10n.Text("aw_label_king", "King");
                        string republicLabel = AW_L10n.Text(
                            GovernmentTitleRules.RepublicHeadKey, "Head of State");
                        string mandateFallback = AW_L10n.Text(
                            "aw_mandate_emperor", "Emperor");
                        string militaryGovernorLabel = AW_L10n.Text(
                            "aw_military_governorate_ruler", "General");
                        string livingAppellation =
                            (militaryGovernorate || ceremonialEmperor) && !republic
                            ? RulerAppellationService.GetFullLivingAppellation(kingdom)
                            : "";
                        string label = RulerAppellationRules.ResolveWindowRulerLabel(
                            militaryGovernorate, republic, empireRank, mandate,
                            livingAppellation, republicLabel, monarchyLabel,
                            mandateFallback, militaryGovernorLabel);
                        LocalizedText localized = kingLabel.GetComponent<LocalizedText>();
                        Text text = kingLabel.GetComponent<Text>();
                        if (militaryGovernorate || ceremonialEmperor && !republic)
                        {
                            if (localized != null) localized.enabled = false;
                            if (text != null) text.text = label;
                        }
                        else if (localized != null)
                        {
                            localized.enabled = true;
                            localized.setKeyAndUpdate(key);
                        }
                        else if (text != null) text.text = label;
                    }
                }
            }

            // 继承人列(头像 + "继承人"标签):**列容器始终保留占位**(SetActive(true)),
            //   使横排恒为 [国王34][中列108][继承人34] 三项对称,中列年号框落在窗口正中
            //   (根治"整排偏移不居中")。无继承人时只隐藏列内头像+标签,不整列 SetActive(false)
            //   —— 否则该列宽度从横排消失,MiddleCenter 使剩余两项整体偏移。
            //   国王列独立(左),继承人列独立(右),不会互相顶替。
            if (_heirCol != null && _heirAvatar != null)
            {
                _heirCol.SetActive(true);
                bool militaryGovernorate = VassalService.GetSubjectKind(
                    kingdom) == VassalSubjectKind.MilitaryGovernorate;
                Actor heir = militaryGovernorate
                    ? MilitaryGovernorateSuccessionService.
                        GetDesignatedSuccessorForReadModel(kingdom)
                    : HeirService.FindHeirReadOnly(kingdom);
                bool hasHeir = heir != null && !heir.isRekt();
                if (hasHeir) { _heirAvatar.gameObject.SetActive(true); _heirAvatar.show(heir); }
                else _heirAvatar.gameObject.SetActive(false);
                // 无继承人时隐藏"继承人"文字标签(留空头像位占位即可,不显文字)。
                Transform heirLabel = _heirCol.transform.Find("Label");
                if (heirLabel != null)
                {
                    heirLabel.gameObject.SetActive(hasHeir);
                    if (hasHeir)
                    {
                        string key = HeirTitleRules.TitleKey(kingdom);
                        LocalizedText localized = heirLabel.GetComponent<LocalizedText>();
                        if (localized != null) localized.setKeyAndUpdate(key);
                        else
                        {
                            Text text = heirLabel.GetComponent<Text>();
                            if (text != null) text.text = AW_L10n.Text(key, key);
                        }
                    }
                }
            }
        }

        private static Color DirectKingdomTextColor(Kingdom pKingdom)
        {
            ColorAsset color = KingdomFlagBuilder.ResolveColor(HistoryColors.FromKingdom(pKingdom),
                pKingdom?.data?.color_id ?? -1);
            return color != null ? color.getColorText() : Color.white;
        }

        private static void SetLayoutSize(Transform pTransform, float pWidth, float pHeight)
        {
            if (pTransform == null) return;
            RectTransform rect = pTransform.GetComponent<RectTransform>();
            if (rect != null) rect.sizeDelta = new Vector2(pWidth, pHeight);
            LayoutElement layout = pTransform.GetComponent<LayoutElement>();
            if (layout == null) return;
            layout.minWidth = pWidth;
            layout.preferredWidth = pWidth;
            layout.minHeight = pHeight;
            layout.preferredHeight = pHeight;
        }
    }
}
