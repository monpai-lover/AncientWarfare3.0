using System;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     家族树/氏族大树节点。节点 = 完整单位头像框(克隆原版 UiUnitAvatarElement:框+单位贴图+底图标+
    ///     右上国家旗帜)+ 下方名字(国家色)+ 可选 上/下溯箭头 + 展开/折叠按钮。
    ///     - 活人:头像 load(actor)(自带 banner/旗帜);死人:手工 ActorAvatarData + load(data) 还原,整体灰度。
    ///     - 名字色 = 所属国家色(活国实时 / 亡国存档 hex)。
    ///     - 点击行为由外部回调决定(大树点→开小树;小树点→inspect),本视图只转发。
    /// </summary>
    internal class FamilyTreeNodeView : MonoBehaviour
    {
        private const int AVATAR = 30;     // 头像框边长
        private const int NODE_W = 70;     // 节点宽
        private const int NODE_H = 64;     // 节点高(头像+名字,留足名字空间不与头像重叠)
        private const int NAME_GAP = 8;    // 名字与头像底的间隙(头像旗帜会向下外溢,需多留)

        private static UiUnitAvatarElement _avatarPrefab; // 懒克隆自 unit 窗的 avatar element

        private UiUnitAvatarElement _avatar;
        private Button _avatarButton;
        private TipButton _avatarTip;
        private Text _nameText;
        private Button _upButton;          // 上溯(到父)
        private Button _downButton;        // 下溯(到子)
        private Button _toggleButton;      // 展开/折叠(大树)
        private Text _toggleText;

        private static readonly Color DeadTint = new Color(0.6f, 0.6f, 0.6f, 1f);

        public static FamilyTreeNodeView Create(Transform pParent)
        {
            var obj = new GameObject("FamilyTreeNodeView", typeof(RectTransform));
            obj.transform.SetParent(pParent, false);
            obj.GetComponent<RectTransform>().sizeDelta = new Vector2(NODE_W, NODE_H);
            var view = obj.AddComponent<FamilyTreeNodeView>();
            view.BuildUi();
            return view;
        }

        private void BuildUi()
        {
            // 头像框(克隆原版 avatar element;失败则退化为带框 Image)
            var holder = new GameObject("Avatar", typeof(RectTransform));
            holder.transform.SetParent(transform, false);
            var arect = holder.GetComponent<RectTransform>();
            arect.anchorMin = new Vector2(0.5f, 1f); arect.anchorMax = new Vector2(0.5f, 1f);
            arect.pivot = new Vector2(0.5f, 1f);
            arect.sizeDelta = new Vector2(AVATAR, AVATAR);
            arect.anchoredPosition = Vector2.zero;

            UiUnitAvatarElement prefab = GetAvatarPrefab();
            if (prefab != null)
            {
                _avatar = Instantiate(prefab, holder.transform);
                var rt = _avatar.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
                _avatar.gameObject.SetActive(true);
            }

            // 点击 + tooltip 挂在 holder(覆盖整个头像区)
            var img = holder.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0); // 透明,仅作点击区
            _avatarButton = holder.AddComponent<Button>();
            _avatarTip = holder.AddComponent<TipButton>();

            // 名字(图标下方)
            var nameObj = new GameObject("Name", typeof(RectTransform), typeof(Text));
            nameObj.transform.SetParent(transform, false);
            var nrect = nameObj.GetComponent<RectTransform>();
            nrect.anchorMin = new Vector2(0.5f, 1f); nrect.anchorMax = new Vector2(0.5f, 1f);
            nrect.pivot = new Vector2(0.5f, 1f);
            nrect.sizeDelta = new Vector2(NODE_W + 20, 14);
            nrect.anchoredPosition = new Vector2(0, -(AVATAR + NAME_GAP)); // 往下挪,避开头像与外溢旗帜
            _nameText = nameObj.GetComponent<Text>();
            _nameText.font = LocalizedTextManager.current_font;
            _nameText.fontSize = 9;
            _nameText.alignment = TextAnchor.UpperCenter;
            _nameText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _upButton = MakeArrow("Up", new Vector2(0.5f, 1f), new Vector2(0, 2), "▲");
            _downButton = MakeArrow("Down", new Vector2(0.5f, 0f), new Vector2(0, -2), "▼");
            _toggleButton = MakeToggle();
        }

        private Button MakeArrow(string pName, Vector2 pAnchor, Vector2 pOffset, string pGlyph)
        {
            var obj = new GameObject(pName, typeof(RectTransform), typeof(Text), typeof(Button));
            obj.transform.SetParent(transform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = pAnchor; rect.anchorMax = pAnchor; rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(14, 12); rect.anchoredPosition = pOffset;
            var t = obj.GetComponent<Text>();
            t.font = LocalizedTextManager.current_font; t.fontSize = 10;
            t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.text = pGlyph;
            obj.SetActive(false);
            return obj.GetComponent<Button>();
        }

        private Button MakeToggle()
        {
            var obj = new GameObject("Toggle", typeof(RectTransform), typeof(Text), typeof(Button));
            obj.transform.SetParent(transform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(12, 12); rect.anchoredPosition = new Vector2(2, 0);
            _toggleText = obj.GetComponent<Text>();
            _toggleText.font = LocalizedTextManager.current_font; _toggleText.fontSize = 12;
            _toggleText.alignment = TextAnchor.MiddleCenter; _toggleText.color = Color.white;
            obj.SetActive(false);
            return obj.GetComponent<Button>();
        }

        /// <summary>
        ///     绑定节点。pOnNode=点头像(大树→开小树/小树→inspect,由外部决定);
        ///     pOnToggle=展开折叠(大树,null 则不显示);pOnUp/pOnDown=小树上下溯(null 则不显示箭头)。
        /// </summary>
        public void Bind(FamilyTreeNode pNode, Action<long> pOnNode, Action pOnToggle,
            bool pHasChildren, bool pExpanded, Action pOnUp, Action pOnDown)
        {
            RenderAvatar(pNode);

            string sex = pNode.sex == 0 ? "♂" : "♀";
            _nameText.text = pNode.display_name + sex;
            _nameText.color = ResolveNameColor(pNode);

            long id = pNode.id;
            _avatarButton.onClick.RemoveAllListeners();
            _avatarButton.onClick.AddListener(() => pOnNode?.Invoke(id));
            SetTip(_avatarTip, gameObject, pNode.display_name, BuildActorTip(pNode));

            // 上/下溯箭头(小树用)
            SetArrow(_upButton, pOnUp);
            SetArrow(_downButton, pOnDown);

            // 展开/折叠(大树用)
            _toggleButton.onClick.RemoveAllListeners();
            if (pHasChildren && pOnToggle != null)
            {
                _toggleText.text = pExpanded ? "−" : "+";
                _toggleButton.gameObject.SetActive(true);
                _toggleButton.onClick.AddListener(() => pOnToggle.Invoke());
            }
            else
            {
                _toggleButton.gameObject.SetActive(false);
            }
        }

        private void SetArrow(Button pBtn, Action pAction)
        {
            pBtn.onClick.RemoveAllListeners();
            if (pAction != null)
            {
                pBtn.gameObject.SetActive(true);
                pBtn.onClick.AddListener(() => pAction.Invoke());
            }
            else
            {
                pBtn.gameObject.SetActive(false);
            }
        }

        // ── 头像渲染:活人 show(actor)(自带国家/氏族旗帜配色);死人手工 ActorAvatarData + 国家旗帜还原 + 头像灰度 ──
        private void RenderAvatar(FamilyTreeNode pNode)
        {
            if (_avatar == null) return;

            Actor live = World.world?.units?.get(pNode.id);
            if (live != null && !live.isRekt())
            {
                // 活人:show 内部 avatarLoader.load + kingdomBanner.load(国家色) + clanBanner.load(氏族色)。
                // **不要再 tint** —— 旧 bug:SetAvatarTint 遍历所有子 Image,把刚 load 的旗帜配色冲成白色。
                _avatar.show(live);
                // clan 旗帜"有时白":原版 ClanBanner 图标用 getColorBanner(),部分颜色的 banner 色近白
                // (colors_general.json 里有 #FFFEF9 等)→ 随机分到就发白。统一用饱和主色重染,保证永远有色。
                FixClanBannerColor(live);
                return;
            }

            // 死者:手工 ActorAvatarData 还原贴图 + 还原国家旗帜 + 仅头像本体置灰(旗帜保留配色)。
            try
            {
                var data = BuildDeadAvatarData(pNode);
                if (data != null) _avatar.avatarLoader.load(data, false);

                LoadDeadKingdomBanner(pNode);     // 国家旗帜:活国实时 load / 亡国用存档色手工还原
                if (_avatar.clanBanner != null)   // 氏族旗帜:死人无 clan 对象(未存 clan_id),隐藏
                    _avatar.clanBanner.gameObject.SetActive(false);

                TintAvatarBodyOnly(DeadTint);     // 只灰头像本体,跳过 banner 子树(保旗帜色)
            }
            catch
            {
                TintAvatarBodyOnly(DeadTint);
            }
        }

        /// <summary>
        ///     修 clan 旗帜"有时白":原版 ClanBanner 图标(part_icon)用 getColorBanner(),
        ///     而 colors_general.json 里部分颜色的 banner 色近白(#FFFEF9 等)→ 氏族随机分到就发白。
        ///     这里在 show 之后,把 clanBanner 的背景+图标都重染成氏族**饱和主色**(getColorMainSecond),
        ///     保证旗帜永远有明显颜色。part_background/part_icon 是 BannerGeneric protected 字段(publicized 可访问)。
        /// </summary>
        private void FixClanBannerColor(Actor pActor)
        {
            var cb = _avatar.clanBanner;
            if (cb == null || !cb.gameObject.activeSelf) return;
            var clan = pActor.clan;
            if (clan == null) return;

            Color main = clan.getColor().getColorMainSecond();
            if (cb.part_background != null)
                cb.part_background.color = new Color(main.r, main.g, main.b, cb.part_background.color.a);
            if (cb.part_icon != null)
                cb.part_icon.color = new Color(main.r, main.g, main.b, cb.part_icon.color.a);
        }

        /// <summary>死者国家旗帜还原:活国 kingdomBanner.load(kingdom)(含配色);亡国用存档 kingdom_color 手工染色。</summary>
        private void LoadDeadKingdomBanner(FamilyTreeNode pNode)
        {
            if (_avatar.kingdomBanner == null) return;

            Kingdom k = (pNode.kingdom_id >= 0) ? World.world?.kingdoms?.get(pNode.kingdom_id) : null;
            if (k != null && !k.isRekt())
            {
                _avatar.kingdomBanner.gameObject.SetActive(true);
                _avatar.kingdomBanner.load(k); // 含国家配色
            }
            else if (!string.IsNullOrEmpty(pNode.kingdom_color))
            {
                // 亡国:旗帜底图保留 prefab 自带,只把背景/图标染回存档国家色(近似还原)。
                _avatar.kingdomBanner.gameObject.SetActive(true);
                Color c = Toolbox.makeColor(pNode.kingdom_color);
                foreach (var img in _avatar.kingdomBanner.GetComponentsInChildren<Image>(true))
                    img.color = new Color(c.r, c.g, c.b, img.color.a);
            }
            else
            {
                _avatar.kingdomBanner.gameObject.SetActive(false);
            }
        }

        /// <summary>仅把头像本体(unit 贴图、底图标、tile)置灰,**跳过 kingdomBanner/clanBanner 子树**,
        /// 否则会把旗帜的国家/氏族配色冲掉。</summary>
        private void TintAvatarBodyOnly(Color pColor)
        {
            if (_avatar == null) return;
            Transform kb = _avatar.kingdomBanner != null ? _avatar.kingdomBanner.transform : null;
            Transform cb = _avatar.clanBanner != null ? _avatar.clanBanner.transform : null;
            foreach (var img in _avatar.GetComponentsInChildren<Image>(true))
            {
                if (IsUnder(img.transform, kb) || IsUnder(img.transform, cb)) continue; // 跳过旗帜
                img.color = new Color(pColor.r, pColor.g, pColor.b, img.color.a);
            }
        }

        private static bool IsUnder(Transform pNode, Transform pAncestor)
        {
            if (pAncestor == null) return false;
            for (Transform t = pNode; t != null; t = t.parent)
                if (t == pAncestor) return true;
            return false;
        }

        /// <summary>用存档字段手工构造死者 ActorAvatarData(无 live Actor)。</summary>
        private static ActorAvatarData BuildDeadAvatarData(FamilyTreeNode pNode)
        {
            ActorAsset xia = AssetManager.actor_library.get(LineageService.XIA_ASSET_ID);
            if (xia == null) return null;

            ColorAsset color = null;
            if (!string.IsNullOrEmpty(pNode.kingdom_color))
                color = ColorAsset.getExistingColorAsset(pNode.kingdom_color);

            var data = new ActorAvatarData();
            data.setData(
                xia,                                   // asset
                null,                                  // mutation
                pNode.sex == 0 ? ActorSex.Male : ActorSex.Female,
                pNode.id,                              // actor id
                pNode.head >= 0 ? pNode.head : 0,      // head id
                null,                                  // sprite_head(loader 填)
                0,                                     // phenotype_index
                0,                                     // phenotype_skin_shade
                color,                                 // kingdom color
                false,                                 // is_egg
                false, false, false,                   // king/warrior/wise
                null,                                  // egg_asset
                true,                                  // is_adult(死者按成年画)
                false, false, false, false, false, false, false, // 各状态
                null,                                  // item renderer
                (int)pNode.id,                         // hash
                null, null);                           // statuses
            return data;
        }

        private static Color ResolveNameColor(FamilyTreeNode pNode)
        {
            Color baseColor = Color.white;
            Kingdom k = (pNode.kingdom_id >= 0) ? World.world?.kingdoms?.get(pNode.kingdom_id) : null;
            if (k != null && !k.isRekt())
                baseColor = k.getColor().getColorText();
            else if (!string.IsNullOrEmpty(pNode.kingdom_color))
                baseColor = Toolbox.makeColor(pNode.kingdom_color);

            if (!pNode.is_alive) baseColor *= DeadTint;
            return baseColor;
        }

        private static string BuildActorTip(FamilyTreeNode pNode)
        {
            string birth = pNode.birth_time > 0 ? Date.getYear(pNode.birth_time).ToString() : "?";
            string life = pNode.is_alive
                ? "(" + birth + "- )"
                : "(" + birth + "-" + (pNode.death_time > 0 ? Date.getYear(pNode.death_time).ToString() : "?") + ")";
            string identity = IdentityLabel(pNode.status);
            string kn = string.IsNullOrEmpty(pNode.kingdom_name) ? "" : ("  " + pNode.kingdom_name);
            return pNode.display_name + " " + life + " " + identity + kn;
        }

        private static void SetTip(TipButton pTip, GameObject pOwner, string pTitle, string pDesc)
        {
            if (pTip == null) return;
            pTip.hoverAction = null;
            if (string.IsNullOrEmpty(pTitle) && string.IsNullOrEmpty(pDesc)) { pTip.enabled = false; return; }
            pTip.enabled = true;
            // 用自定义 "aw_raw" type:人名/生卒等动态文本原样显示,不经 .Localize() → 不刷 missing text。
            pTip.type = AncientWarfare3.ui.AW_RawTooltip.TYPE;
            string title = pTitle ?? "", desc = pDesc ?? "";
            pTip.hoverAction = () =>
                Tooltip.show(pOwner, AncientWarfare3.ui.AW_RawTooltip.TYPE,
                    new TooltipData { tip_name = title, tip_description = desc });
        }

        private static string IdentityLabel(string pStatus)
        {
            if (pStatus == LineageStatus.NOBLE) return "贵";
            if (pStatus == LineageStatus.COMMON) return "平";
            if (pStatus == LineageStatus.SLAVE) return "奴";
            return "";
        }

        /// <summary>
        ///     懒取 UiUnitAvatarElement prefab。原版 unit meta 用 `Resources.Load&lt;UiUnitAvatarElement&gt;("ui/UnitAvatarElement")`
        ///     (MetaCustomizationLibrary.cs:405-408)→ 直接 Resources 取 prefab,Instantiate 即用,比克隆窗口稳。
        /// </summary>
        private static UiUnitAvatarElement GetAvatarPrefab()
        {
            if (_avatarPrefab != null) return _avatarPrefab;
            try
            {
                _avatarPrefab = Resources.Load<UiUnitAvatarElement>("ui/UnitAvatarElement");
            }
            catch
            {
                _avatarPrefab = null;
            }
            return _avatarPrefab;
        }
    }
}
