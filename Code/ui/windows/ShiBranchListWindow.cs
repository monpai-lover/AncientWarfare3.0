using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    /// <summary>某姓下的氏支列表(每行可点进该氏支大树)。上下文=当前姓名,OpenFor 设置。</summary>
    internal class ShiBranchListWindow : AbstractListWindow<ShiBranchListWindow, ShiBranchInfo>
    {
        private static string _contextFamilyName = "";

        public static void OpenFor(string pFamilyName)
        {
            _contextFamilyName = pFamilyName ?? "";
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.SHI_LIST);
            ScrollWindow.showWindow(AW_LineageWindowIds.SHI_LIST);
            if (Instance != null && Instance.gameObject.activeInHierarchy) Instance.Refresh();
        }

        protected override void Init() { }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            ClearList();
            if (string.IsNullOrEmpty(_contextFamilyName)) return;
            var list = LineageQuery.GetShiBranches(_contextFamilyName);
            foreach (var s in list) AddItemToList(s);
        }

        protected override AbstractListWindowItem<ShiBranchInfo> CreateItemPrefab()
        {
            var obj = new GameObject("ShiBranchListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<ShiBranchListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
