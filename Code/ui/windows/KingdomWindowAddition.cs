using System.Linq;
using AncientWarfare3.core.lineage;
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

        private bool _inited;
        private KingdomWindow _window;
        private GameObject _middle;
        private Text _yearText;
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
            var custom = root.BeginHoriGroup(new Vector2(200, 36), TextAnchor.MiddleCenter, 2, new RectOffset(0, 0, 2, 2));
            custom.name = MIDDLE_OBJ;
            _middle = custom.gameObject;
            // 插到 content_motto 兄弟位之后(新版 motto 在 content_motto 容器,非 AW2 的直接 MottoName)。
            Transform motto = content.Find("content_motto");
            if (motto != null) _middle.transform.SetSiblingIndex(motto.GetSiblingIndex() + 1);

            // 左:国王头像 + 下方"国王"标签(竖列)。
            _kingCol = BuildAvatarColumn(custom, avatarTemplate, "AW_KingAvatar", "aw_label_king", out _kingAvatar);
            custom.AddChild(_kingCol);

            // 中:竖排(年号框 + 一排两个国策占位框)。
            var middleBar = custom.BeginVertGroup(new Vector2(108, 36), TextAnchor.UpperCenter, 2, new RectOffset(0, 0, 0, 0));

            GameObject yearBox = BuildBox("Year", new Vector2(108, 16), out _yearText);
            middleBar.AddChild(yearBox);

            var policyRow = middleBar.BeginHoriGroup(new Vector2(108, 16), TextAnchor.MiddleCenter, 2, new RectOffset(0, 0, 0, 0));
            // TODO(国策系统):占位字换成「当前阶级状态」与「执行中国策」的真实显示。
            policyRow.AddChild(BuildPolicyBox("PolicyState", new Vector2(52, 16), "aw_policy_state_placeholder"));
            policyRow.AddChild(BuildPolicyBox("PolicyExec", new Vector2(52, 16), "aw_policy_exec_placeholder"));

            // 右:继承人头像 + 下方"继承人"标签(与国王对称)。show(heir) 自带点击→打开继承人窗;
            //    无继承人时 Refresh 里整列隐藏(不顶国王位 —— 用户报"继承人顶替了国王显示位")。
            _heirCol = BuildAvatarColumn(custom, avatarTemplate, "AW_HeirAvatar", "aw_label_heir", out _heirAvatar);
            custom.AddChild(_heirCol);
        }

        /// <summary>头像竖列:头像(克隆)在上 + 身份标签在下,整列作为 HoriGroup 的一个固定宽度子项。</summary>
        private GameObject BuildAvatarColumn(AutoHoriLayoutGroup pParentHori, UiUnitAvatarElement pTemplate,
            string pAvatarName, string pLabelKey, out UiUnitAvatarElement pAvatar)
        {
            var col = pParentHori.BeginVertGroup(new Vector2(34, 36), TextAnchor.UpperCenter, 1, new RectOffset(0, 0, 0, 0));
            col.name = pAvatarName + "_Col";

            pAvatar = CloneAvatar(pTemplate, col.gameObject, pAvatarName);
            col.AddChild(pAvatar.gameObject);

            // 标签(国王/继承人),本地化键 aw_label_king / aw_label_heir。
            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            var lrt = labelObj.GetComponent<RectTransform>();
            lrt.sizeDelta = new Vector2(34, 10);
            var le = labelObj.GetComponent<LayoutElement>();
            le.minWidth = 34; le.preferredWidth = 34; le.minHeight = 10; le.preferredHeight = 10;
            var lt = labelObj.GetComponent<Text>();
            lt.alignment = TextAnchor.MiddleCenter;
            lt.font = LocalizedTextManager.current_font;
            lt.fontSize = 8;
            lt.resizeTextForBestFit = true; lt.resizeTextMinSize = 1; lt.resizeTextMaxSize = 9;
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
            _yearText = middle.GetComponentsInChildren<Text>(true)
                              .FirstOrDefault(t => t.transform.parent != null && t.transform.parent.name == "Year");
            var avatars = middle.GetComponentsInChildren<UiUnitAvatarElement>(true);
            // 约定建立顺序:[0]=国王(AW_KingAvatar)、[1]=继承人(AW_HeirAvatar)。
            _kingAvatar = avatars.FirstOrDefault(a => a.name == "AW_KingAvatar") ?? (avatars.Length > 0 ? avatars[0] : null);
            _heirAvatar = avatars.FirstOrDefault(a => a.name == "AW_HeirAvatar") ?? (avatars.Length > 1 ? avatars[1] : null);
            // 竖列容器(头像名 + "_Col")。
            Transform kc = middle.transform.Find("AW_KingAvatar_Col");
            Transform hc = middle.transform.Find("AW_HeirAvatar_Col");
            _kingCol = kc != null ? kc.gameObject : (_kingAvatar != null ? _kingAvatar.transform.parent.gameObject : null);
            _heirCol = hc != null ? hc.gameObject : (_heirAvatar != null ? _heirAvatar.transform.parent.gameObject : null);
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
        private GameObject BuildPolicyBox(string name, Vector2 size, string localeKey)
        {
            var box = BuildBox(name, size, out Text text);
            var loc = text.gameObject.AddComponent<LocalizedText>();
            loc.setKeyAndUpdate(localeKey);
            text.color = new Color(1f, 1f, 1f, 0.45f);
            return box;
        }

        // ───────────────────────── 刷新数据(每次开窗) ─────────────────────────

        private void Refresh()
        {
            if (!_inited || _middle == null) return;
            Kingdom kingdom = _window != null ? _window.meta_object : null;
            if (kingdom == null || kingdom.isRekt()) { _middle.SetActive(false); return; }
            _middle.SetActive(true);

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
                    _yearText.color = kingdom.getColor().getColorText();
                }
            }

            // 国王列(头像 + "国王"标签):有王整列显示,无王整列隐藏。
            if (_kingCol != null && _kingAvatar != null)
            {
                bool hasKing = kingdom.hasKing();
                _kingCol.SetActive(hasKing);
                if (hasKing) _kingAvatar.show(kingdom.king);
            }

            // 继承人列(头像 + "继承人"标签):**列容器始终保留占位**(SetActive(true)),
            //   使横排恒为 [国王34][中列108][继承人34] 三项对称,中列年号框落在窗口正中
            //   (根治"整排偏移不居中")。无继承人时只隐藏列内头像+标签,不整列 SetActive(false)
            //   —— 否则该列宽度从横排消失,MiddleCenter 使剩余两项整体偏移。
            //   国王列独立(左),继承人列独立(右),不会互相顶替。
            if (_heirCol != null && _heirAvatar != null)
            {
                _heirCol.SetActive(true);
                Actor heir = HeirService.GetHeir(kingdom);
                bool hasHeir = heir != null && !heir.isRekt();
                if (hasHeir) { _heirAvatar.gameObject.SetActive(true); _heirAvatar.show(heir); }
                else _heirAvatar.gameObject.SetActive(false);
                // 无继承人时隐藏"继承人"文字标签(留空头像位占位即可,不显文字)。
                Transform heirLabel = _heirCol.transform.Find("Label");
                if (heirLabel != null) heirLabel.gameObject.SetActive(hasHeir);
            }
        }
    }
}
