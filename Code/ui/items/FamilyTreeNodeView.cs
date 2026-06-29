using System;
using AncientWarfare3.core.lineage;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.items
{
    /// <summary>
    ///     家族树/氏族大树共用的节点视图。显示:[关系] 名 性别 (生-卒) 身份。
    ///     死者整体灰调。有子节点时显示展开/折叠按钮(懒加载)。
    /// </summary>
    internal class FamilyTreeNodeView : MonoBehaviour
    {
        private Text _label;
        private Button _nodeButton;
        private Button _toggleButton;
        private Text _toggleText;

        private static readonly Color AliveColor = Color.white;
        private static readonly Color DeadColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        public static FamilyTreeNodeView Create(Transform pParent)
        {
            var obj = new GameObject("FamilyTreeNodeView", typeof(RectTransform));
            obj.transform.SetParent(pParent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(240, 22);
            var le = obj.AddComponent<LayoutElement>();
            le.minHeight = 22;
            le.preferredHeight = 22;
            var hl = obj.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = false;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.spacing = 4;

            var view = obj.AddComponent<FamilyTreeNodeView>();
            view.BuildUi();
            return view;
        }

        private void BuildUi()
        {
            // 展开/折叠按钮
            var toggleObj = new GameObject("Toggle", typeof(RectTransform), typeof(Text), typeof(Button));
            toggleObj.transform.SetParent(transform, false);
            toggleObj.GetComponent<RectTransform>().sizeDelta = new Vector2(16, 22);
            _toggleText = toggleObj.GetComponent<Text>();
            _toggleText.font = LocalizedTextManager.current_font;
            _toggleText.fontSize = 12;
            _toggleText.alignment = TextAnchor.MiddleCenter;
            _toggleText.color = Color.white;
            _toggleButton = toggleObj.GetComponent<Button>();

            // 主标签(点击=以此人重新居中/查家族树)
            var labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Button));
            labelObj.transform.SetParent(transform, false);
            labelObj.GetComponent<RectTransform>().sizeDelta = new Vector2(220, 22);
            _label = labelObj.GetComponent<Text>();
            _label.font = LocalizedTextManager.current_font;
            _label.fontSize = 11;
            _label.alignment = TextAnchor.MiddleLeft;
            _nodeButton = labelObj.GetComponent<Button>();
        }

        public void Bind(FamilyTreeNode pNode, string pRelationLabel, Action<long> pOnClick,
            Action pOnToggle, bool pHasChildren, bool pExpanded)
        {
            string birth = pNode.birth_time > 0 ? Date.getYear(pNode.birth_time).ToString() : "?";
            string death = pNode.is_alive ? "" : (pNode.death_time > 0 ? Date.getYear(pNode.death_time).ToString() : "?");
            string life = pNode.is_alive ? "(" + birth + "- )" : "(" + birth + "-" + death + ")";
            string sex = pNode.sex == 0 ? "♂" : "♀";
            string identity = IdentityLabel(pNode.status);
            string prefix = string.IsNullOrEmpty(pRelationLabel) ? "" : "[" + pRelationLabel + "] ";

            _label.text = prefix + pNode.display_name + " " + sex + " " + life + " " + identity;
            _label.color = pNode.is_alive ? AliveColor : DeadColor;

            _nodeButton.onClick.RemoveAllListeners();
            long id = pNode.id;
            _nodeButton.onClick.AddListener(() => pOnClick?.Invoke(id));

            _toggleButton.onClick.RemoveAllListeners();
            if (pHasChildren && pOnToggle != null)
            {
                _toggleText.text = pExpanded ? "−" : "+";
                _toggleButton.gameObject.SetActive(true);
                _toggleButton.onClick.AddListener(() => pOnToggle.Invoke());
            }
            else
            {
                _toggleText.text = "";
                _toggleButton.gameObject.SetActive(false);
            }
        }

        private static string IdentityLabel(string pStatus)
        {
            if (pStatus == LineageStatus.NOBLE) return "贵";
            if (pStatus == LineageStatus.COMMON) return "平";
            if (pStatus == LineageStatus.SLAVE) return "奴";
            return "";
        }
    }
}
