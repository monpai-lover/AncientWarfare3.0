using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    /// <summary>姓族总览:列出所有姓(每行可点进氏支列表)。NML 列表窗,自带滚动+对象池。</summary>
    internal class LineageOverviewWindow : AbstractListWindow<LineageOverviewWindow, SurnameOverview>
    {
        public static void Open()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.OVERVIEW);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.OVERVIEW,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init()
        {
            // 使用原版列表窗尺寸。
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        private void Refresh()
        {
            ClearList();
            var list = LineageQuery.GetSurnameOverview();
            foreach (var s in list)
            {
                AddItemToList(s);
            }
        }

        protected override AbstractListWindowItem<SurnameOverview> CreateItemPrefab()
        {
            var obj = new GameObject("LineageListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<LineageListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
