using System.Collections;
using AncientWarfare3.core.lineage;
using AncientWarfare3.ui.items;
using NeoModLoader.api;
using UnityEngine;

namespace AncientWarfare3.ui.windows
{
    internal class VassalRelationWindow : AbstractListWindow<VassalRelationWindow, VassalRelationInfo>
    {
        private static long _kingdomId = -1;

        public static void Open(long pKingdomId)
        {
            _kingdomId = pKingdomId;
            if (Instance == null) CreateAndInit(AW_LineageWindowIds.VASSAL_RELATIONS);
            AW_LineageWindowIds.SafeShow(AW_LineageWindowIds.VASSAL_RELATIONS,
                () => { if (Instance != null) Instance.Refresh(); });
        }

        public static void OpenKingdomRenameFlow(long pKingdomId)
        {
            Kingdom kingdom = World.world?.kingdoms?.get(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt()) return;
            MetaType.Kingdom.getAsset().selectAndInspect(kingdom);
            if (Instance != null)
                Instance.StartCoroutine(FocusKingdomNameInput());
        }

        private static IEnumerator FocusKingdomNameInput()
        {
            yield return null;
            yield return null;
            KingdomWindow window = Object.FindObjectOfType<KingdomWindow>();
            NameInput input = window?.transform.FindRecursive(
                "NameInputElement")?.GetComponent<NameInput>();
            input?.inputField?.ActivateInputField();
        }

        protected override void Init()
        {
        }

        public override void OnNormalEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            ClearList();
            Kingdom kingdom = World.world?.kingdoms?.get(_kingdomId);
            if (kingdom?.data == null || kingdom.isRekt()) return;

            foreach (VassalRelationInfo row in VassalService.GetRelationView(kingdom))
                AddItemToList(row);
        }

        protected override AbstractListWindowItem<VassalRelationInfo> CreateItemPrefab()
        {
            var obj = new GameObject("VassalRelationListItem");
            obj.transform.SetParent(ContentTransform, false);
            var item = obj.AddComponent<VassalRelationListItem>();
            obj.SetActive(false);
            return item;
        }
    }
}
