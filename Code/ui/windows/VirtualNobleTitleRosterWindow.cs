using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.windows
{
    internal sealed class VirtualNobleTitleRosterWindow : AbstractWindow<VirtualNobleTitleRosterWindow>
    {
        private static long _kingdomId = -1L;
        private RectTransform _root;
        private RectTransform _content;
        private Text _header;
        private readonly List<GameObject> _rows = new List<GameObject>();

        internal static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.VIRTUAL_TITLES);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.VIRTUAL_TITLES,
                () => Instance?.Refresh());
        }

        protected override void Init()
        {
            EnsureUi();
            ApplyLayout();
        }

        public override void OnNormalEnable() => Refresh();

        private void Refresh()
        {
            EnsureUi();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (_header != null)
                _header.text = kingdom?.data != null
                    ? kingdom.name + " - " + AW_L10n.Text("aw_virtual_titles", "Title Holders")
                    : AW_L10n.Text("aw_virtual_titles", "Title Holders");
            ClearRows();
            if (kingdom?.data == null || kingdom.isRekt()) return;

            var rows = new List<Tuple<long, string, Actor>>();
            try
            {
                foreach (Actor actor in kingdom.units)
                {
                    if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                    string title = NobleRankService.GetDisplayTitle(actor);
                    if (!string.IsNullOrWhiteSpace(title))
                        rows.Add(Tuple.Create(actor.data.id, title, actor));
                }
            }
            catch { }
            foreach (VirtualNobleTitleSnapshot title in
                     VirtualNobleTitleService.GetActiveForKingdom(kingdom.id))
            {
                Actor actor = World.world?.units?.get(title.ActorId);
                if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
                rows.Add(Tuple.Create(actor.data.id, title.Text, actor));
            }

            foreach (Tuple<long, string, Actor> row in rows
                .OrderBy(p => p.Item2, StringComparer.Ordinal)
                .ThenBy(p => p.Item1))
                AddRow(row.Item2, row.Item3);
            LayoutRows();
        }

        private void EnsureUi()
        {
            if (_root != null || ContentTransform == null) return;
            var rootObject = new GameObject("VirtualNobleTitleRosterRoot", typeof(RectTransform));
            rootObject.transform.SetParent(ContentTransform, false);
            _root = rootObject.GetComponent<RectTransform>();
            _header = CreateText(_root, "Header", 12, TextAnchor.MiddleCenter,
                new Color(1f, 0.84f, 0.42f, 1f));
            var contentObject = new GameObject("Rows", typeof(RectTransform));
            contentObject.transform.SetParent(_root, false);
            _content = contentObject.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 1f);
        }

        private void AddRow(string pTitle, Actor pActor)
        {
            var row = new GameObject("TitleHolder", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(_content, false);
            AW_UIStyle.ApplyListRow(row.GetComponent<Image>(), 0.95f);
            Button button = row.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                if (pActor?.data != null && !pActor.isRekt())
                    ActionLibrary.openUnitWindow(pActor);
            });
            Text text = CreateText(row.transform, "Text", 10, TextAnchor.MiddleLeft, Color.white);
            text.text = pTitle + "  -  " + (pActor?.getName() ??
                AW_L10n.Text("aw_unknown_actor", "Unknown actor"));
            text.rectTransform.offsetMin = new Vector2(8f, 0f);
            text.rectTransform.offsetMax = new Vector2(-8f, 0f);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 25f);
            _rows.Add(row);
        }

        private void ClearRows()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            _rows.Clear();
        }

        private void LayoutRows()
        {
            if (_content == null) return;
            float y = 0f;
            for (int i = 0; i < _rows.Count; i++)
            {
                RectTransform row = _rows[i]?.GetComponent<RectTransform>();
                if (row == null) continue;
                row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
                row.pivot = new Vector2(0f, 1f);
                row.anchoredPosition = new Vector2(0f, -y);
                y += 29f;
            }
            _content.sizeDelta = new Vector2(320f, Math.Max(30f, y));
        }

        private void ApplyLayout()
        {
            if (_root == null) return;
            RectTransform bg = BackgroundTransform?.GetComponent<RectTransform>();
            if (bg != null) bg.sizeDelta = new Vector2(380f, 330f);
            ScrollWindow window = GetComponent<ScrollWindow>();
            if (window?.titleText != null)
                window.titleText.text = AW_L10n.Text("aw_virtual_titles", "Title Holders");
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(338f, 270f);
            SetRect(_header, 8f, 4f, 322f, 25f);
            SetRect(_content, 8f, 34f, 322f, 230f);
        }

        private static Text CreateText(Transform parent, string name, int size,
            TextAnchor anchor, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            Text text = obj.GetComponent<Text>();
            text.font = LocalizedTextManager.current_font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void SetRect(Component pComponent, float x, float y,
            float width, float height)
        {
            RectTransform rect = pComponent?.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
