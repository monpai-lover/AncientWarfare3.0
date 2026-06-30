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

        private UiUnitAvatarElement _avatar;       // 活人头像(原版控件,带旗帜/点击)
        private Image _deadPortrait;               // 死者静态画像(纯数据合成 + 灰度,不引用活对象)
        private Button _avatarButton;
        private TipButton _avatarTip;
        private Text _nameText;
        private Button _upButton;          // 上溯(到父)
        private Button _downButton;        // 下溯(到子)
        private Button _toggleButton;      // 展开/折叠(大树)
        private Text _toggleText;
        private GameObject _branchBadge;   // 称王分封"建立分支X氏"提示徽标(点击跳新支大树)
        private Text _branchBadgeText;
        private Button _branchBadgeButton;

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

            // 死者静态画像 Image(纯数据合成 + 灰度,与活人头像同位置叠放,按节点死活切换显隐)。
            var deadObj = new GameObject("DeadPortrait", typeof(RectTransform), typeof(Image));
            deadObj.transform.SetParent(holder.transform, false);
            var drt = deadObj.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.5f, 0.5f); drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f); drt.anchoredPosition = Vector2.zero;
            drt.sizeDelta = new Vector2(AVATAR, AVATAR);
            _deadPortrait = deadObj.GetComponent<Image>();
            _deadPortrait.preserveAspect = true;
            _deadPortrait.raycastTarget = false;
            deadObj.SetActive(false);

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
            BuildBranchBadge();
        }

        /// <summary>称王分封徽标:名字下方一行"建支:X氏"(青色),点击跳转新支大树。默认隐藏,Bind 按数据显隐。</summary>
        private void BuildBranchBadge()
        {
            var obj = new GameObject("BranchBadge", typeof(RectTransform), typeof(Text), typeof(Button));
            obj.transform.SetParent(transform, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f); rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(NODE_W + 20, 12);
            rect.anchoredPosition = new Vector2(0, -(AVATAR + NAME_GAP + 13)); // 名字下方再一行
            _branchBadgeText = obj.GetComponent<Text>();
            _branchBadgeText.font = LocalizedTextManager.current_font;
            _branchBadgeText.fontSize = 8;
            _branchBadgeText.alignment = TextAnchor.UpperCenter;
            _branchBadgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _branchBadgeText.color = new Color(0.4f, 0.85f, 0.95f, 1f); // 青色链接感
            _branchBadgeButton = obj.GetComponent<Button>();
            _branchBadge = obj;
            obj.SetActive(false);
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

            // 称王分封:该节点开了新氏支 → 显示"建支:X氏",点击跳转新支大树。
            BindBranchBadge(pNode);
        }

        /// <summary>绑定"建立分支X氏"徽标:节点有 founded_branch_shi_id 时显示并可点击跳新支大树;否则隐藏。</summary>
        private void BindBranchBadge(FamilyTreeNode pNode)
        {
            if (_branchBadge == null) return;
            _branchBadgeButton.onClick.RemoveAllListeners();

            long branchShi = pNode.founded_branch_shi_id;
            if (branchShi < 0)
            {
                _branchBadge.SetActive(false);
                return;
            }

            // 取新支氏名(查 ShiBranch 档案);取不到则用占位。
            var info = LineageQuery.GetShiBranchInfo(branchShi);
            string clanName = info != null && !string.IsNullOrEmpty(info.clan_name) ? info.clan_name : "新支";
            _branchBadgeText.text = "▸建支:" + clanName + "氏";
            _branchBadge.SetActive(true);
            _branchBadgeButton.onClick.AddListener(() =>
                AncientWarfare3.ui.windows.FamilyTreeWindow.OpenBigTree(branchShi));
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

        // ── 头像渲染 ──
        //   活人:原版 UiUnitAvatarElement.show(actor)(带边框/旗帜/点击,实时,avatarLoader 启用)。
        //   死者:**同一个 _avatar 外壳**(边框+旗帜与活人一致),画像用存档 sex/head/skin 数据经
        //        ActorAvatarData.setData(**pIsStopIdleAnimation=true** 强制静态)复现 → load 走 showStatic
        //        立即定格首帧,**不引用活 Actor、不走动画 Update**(再 avatarLoader.enabled=false 硬停双保险,
        //        根治旧的 _unit_sprites[idx] 越界崩),整体头像本体置灰。
        private void RenderAvatar(FamilyTreeNode pNode)
        {
            if (_avatar == null) return;
            if (_deadPortrait != null) _deadPortrait.gameObject.SetActive(false); // 弃用独立死像,统一走 _avatar 外壳
            _avatar.gameObject.SetActive(true);

            Actor live = World.world?.units?.get(pNode.id);
            // 必须 isAlive():死者在世界里可能仍有 actor 但 !isRekt(),走 show(live) 会触发 loader 的 showDied()
            // 渲染 _died_sprite(墓碑/僵尸占位)。真死者一律走下方静态重建分支。
            bool alive = live != null && !live.isRekt() && live.isAlive();

            if (alive)
            {
                _avatar.enabled = true;                                 // 恢复控件刷新(死者分支会关掉)
                if (_avatar.avatarLoader != null) _avatar.avatarLoader.enabled = true; // 恢复活人动画
                _avatar.show(live);
                FixClanBannerColor(live);
                return;
            }

            // 死者:**完全脱离 UiUnitAvatarElement**(它强耦合 live Actor:OnDisable 清 _actor、
            //   IRefreshElement 刷新与 tooltip 都走 _actor;死者 getActor()==null → 框架刷新时画像消失/出错,
            //   这正是用户报的"死人画像 getActor 返回 null"根因)。改用独立 _deadPortrait Image 直接显示
            //   我们用存档 phenotype 自行合成的**静态 Xia 上色 sprite**,不依赖任何 live actor。
            _avatar.avatarLoader.enabled = false;                  // 停 loader Update
            _avatar.enabled = false;                               // 停控件自身刷新(IRefreshElement),杜绝 null-actor 刷新
            HideAvatarBody();                                      // 关掉原版控件的单位贴图/底图标(留 _deadPortrait 接管)

            try
            {
                Sprite dead = BuildDeadSprite(pNode);
                if (dead != null && _deadPortrait != null)
                {
                    _deadPortrait.gameObject.SetActive(true);
                    _deadPortrait.sprite = dead;
                    _deadPortrait.color = DeadTint;                // 整体灰度(死者)
                }
                LoadDeadKingdomBanner(pNode);                      // 国旗帜:活国实时 / 亡国存档色(仍用 _avatar 的 banner 子物体)
                if (_avatar.clanBanner != null)                    // 死人无 clan 对象 → 隐藏氏族旗帜
                    _avatar.clanBanner.gameObject.SetActive(false);
            }
            catch { /* 合成失败:留空头像 + 灰名,不崩 */ }
        }

        /// <summary>关掉原版控件的单位贴图/底图标本体(保留 kingdomBanner 子树供死者旗帜用),让 _deadPortrait 接管画像。</summary>
        private void HideAvatarBody()
        {
            if (_avatar == null) return;
            var loader = _avatar.avatarLoader;
            if (loader != null)
            {
                if (loader._actor_image != null) loader._actor_image.enabled = false;
                if (loader._item_image != null) loader._item_image.enabled = false;
            }
            if (_avatar.unit_type_bg != null) _avatar.unit_type_bg.gameObject.SetActive(false);
            if (_avatar._tile_graphics_1 != null) _avatar._tile_graphics_1.enabled = false;
            if (_avatar._tile_graphics_2 != null) _avatar._tile_graphics_2.enabled = false;
        }

        /// <summary>用存档数据(sex/head/phenotype)合成一张**静态上色 Xia sprite**,不引用 live Actor。
        /// 走 getContainerForUI(取 Xia 逐帧贴图容器)→ walking 首帧 → getColoredSprite(按存档 phenotype 上色)。</summary>
        private static Sprite BuildDeadSprite(FamilyTreeNode pNode)
        {
            var data = BuildDeadAvatarData(pNode);
            if (data == null || data.asset == null) return null;
            if (data.asset.has_override_sprite) return data.asset.get_override_sprite(null);

            var container = DynamicActorSpriteCreatorUI.getContainerForUI(
                data.asset, data.is_adult, data.getTextureAsset(),
                data.mutation_skin_asset, data.is_egg, data.egg_asset);
            if (container?.walking?.frames == null || container.walking.frames.Length == 0) return null;

            Sprite baseFrame = container.walking.frames[0];
            return data.getColoredSprite(baseFrame, container); // 内部用 data.phenotype_* 上 Xia 真实肤色
        }

        /// <summary>用死者存档数据(sex/head id)构造 ActorAvatarData(无 live Actor)。
        /// pIsStopIdleAnimation=true → avatarLoader.load 走 showStatic 定格静态首帧,不触发动画 Update 越界。</summary>
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
                pNode.id,                              // actor id(头部随机种子;有 head id 时不用)
                pNode.head >= 0 ? pNode.head : 0,      // head id
                null,                                  // sprite_head(loader 填)
                pNode.phenotype_index,                 // phenotype_index(生前真实肤色,0 会被上成僵尸绿)
                pNode.phenotype_shade,                 // phenotype_skin_shade
                color,                                 // kingdom color
                false,                                 // is_egg
                false, false, false,                   // king / warrior / wise
                null,                                  // egg_asset
                true,                                  // is_adult(死者按成年画)
                false,                                 // is_lying
                false,                                 // is_touching_liquid
                false,                                 // is_inside_boat
                false,                                 // is_hovering
                false,                                 // is_immovable
                false,                                 // is_unconscious
                true,                                  // ⚠ is_stop_idle_animation = true → 强制 showStatic 静态首帧
                null,                                  // item renderer
                (int)pNode.id,                         // hash
                null, null);                           // statuses
            return data;
        }

        /// <summary>死者国旗帜:活国 kingdomBanner.load(kingdom)(含配色);亡国用存档 kingdom_color 手工染色;无则隐藏。</summary>
        private void LoadDeadKingdomBanner(FamilyTreeNode pNode)
        {
            if (_avatar.kingdomBanner == null) return;
            Kingdom k = (pNode.kingdom_id >= 0) ? World.world?.kingdoms?.get(pNode.kingdom_id) : null;
            if (k != null && !k.isRekt())
            {
                _avatar.kingdomBanner.gameObject.SetActive(true);
                _avatar.kingdomBanner.load(k);
            }
            else if (!string.IsNullOrEmpty(pNode.kingdom_color))
            {
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

        /// <summary>只把头像本体(unit 贴图/底图标/tile)置灰,跳过 kingdomBanner/clanBanner 子树(保旗帜配色)。</summary>
        private void TintAvatarBodyOnly(Color pColor)
        {
            if (_avatar == null) return;
            Transform kb = _avatar.kingdomBanner != null ? _avatar.kingdomBanner.transform : null;
            Transform cb = _avatar.clanBanner != null ? _avatar.clanBanner.transform : null;
            foreach (var img in _avatar.GetComponentsInChildren<Image>(true))
            {
                if (IsUnder(img.transform, kb) || IsUnder(img.transform, cb)) continue;
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

        /// <summary>节点 tooltip 正文:多行丰富信息(身份/性别 · 氏族 · 国/城 · 生卒 · 代)。空字段省略该行。</summary>
        private static string BuildActorTip(FamilyTreeNode pNode)
        {
            var sb = new System.Text.StringBuilder();

            // 行1:身份 · 性别 · 状态(在世/已故)
            string identity = IdentityLabel(pNode.status);
            string sex = pNode.sex == 0 ? "男" : "女";
            string alive = pNode.is_alive ? "在世" : "已故";
            string line1 = JoinNonEmpty(" · ", identity, sex, alive);
            if (line1.Length > 0) sb.AppendLine(line1);

            // 行2:氏(clan_name)
            if (!string.IsNullOrEmpty(pNode.clan_name))
                sb.AppendLine("氏:" + pNode.clan_name);

            // 行3:国 · 城
            string kc = JoinNonEmpty("  ",
                string.IsNullOrEmpty(pNode.kingdom_name) ? "" : "国:" + pNode.kingdom_name,
                string.IsNullOrEmpty(pNode.city_name) ? "" : "居:" + pNode.city_name);
            if (kc.Length > 0) sb.AppendLine(kc);

            // 行4:生卒
            string birth = pNode.birth_time > 0 ? Date.getYear(pNode.birth_time) + "年" : "?";
            string death = pNode.is_alive ? "—" : (pNode.death_time > 0 ? Date.getYear(pNode.death_time) + "年" : "?");
            sb.AppendLine("生:" + birth + "   卒:" + death);

            // 行5:距贵族代数(noble_distance:本人贵族=0)
            if (pNode.noble_distance >= 0 && pNode.noble_distance < 99)
                sb.Append(pNode.noble_distance == 0 ? "贵族本支" : ("距贵族 " + pNode.noble_distance + " 代"));

            return sb.ToString().TrimEnd('\n', '\r');
        }

        private static string JoinNonEmpty(string pSep, params string[] pParts)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var s in pParts) if (!string.IsNullOrEmpty(s)) parts.Add(s);
            return string.Join(pSep, parts.ToArray());
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
            // 返回完整身份词(调用方不再 +"族",否则"平民"会变"平族")。
            if (pStatus == LineageStatus.NOBLE) return "贵族";
            if (pStatus == LineageStatus.COMMON) return "平民";
            if (pStatus == LineageStatus.SLAVE) return "奴隶";
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
