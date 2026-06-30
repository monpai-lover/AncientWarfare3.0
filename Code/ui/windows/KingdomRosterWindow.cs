using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    /// <summary>
    ///     全王国列表窗:列出世界上所有王国(含已亡国),每行 = 旗帜 + 国名(国家色)+ 亡国标记。
    ///     点击一行 → 该国朝代分段历史(HistoryListWindow)。亡国旗帜走存档重建,规避空引用。
    ///     入口在自定义姓族 tab(AW_LineageTab)。
    /// </summary>
    internal class KingdomRosterWindow : AbstractListWindow<KingdomRosterWindow, KingdomArchiveInfo>
    {
        public static void Open()
        {
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.KINGDOM_ROSTER);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.KINGDOM_ROSTER,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        protected override void Init() { }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            ClearList();
            var list = HistoryQuery.GetAllKingdoms();
            foreach (var k in list) AddItemToList(k);
        }

        protected override AbstractListWindowItem<KingdomArchiveInfo> CreateItemPrefab()
        {
            var obj = new GameObject("KingdomRosterListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<KingdomRosterListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
