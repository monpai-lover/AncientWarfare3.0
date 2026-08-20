using AncientWarfare3.core.court;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowEdgeView : MonoBehaviour
    {
        private RectTransform _from;
        private RectTransform _to;
        private Image _line;
        private Text _directionArrow;
        private Text _label;

        public CustomCourtEdge Edge { get; private set; }

        public void Bind(CustomCourtEdge edge, RectTransform from,
            RectTransform to)
        {
            Edge = edge;
            _from = from;
            _to = to;
            _line = GetComponent<Image>();
            EnsureDecorations();
            ApplyStyle();
            UpdateGeometry();
        }

        private void LateUpdate()
        {
            UpdateGeometry();
        }

        private void EnsureDecorations()
        {
            if (_directionArrow == null)
            {
                GameObject marker = new GameObject("DirectionArrow",
                    typeof(RectTransform), typeof(Text));
                marker.transform.SetParent(transform, false);
                _directionArrow = marker.GetComponent<Text>();
                _directionArrow.font = LocalizedTextManager.current_font;
                _directionArrow.fontSize = 12;
                _directionArrow.fontStyle = FontStyle.Bold;
                _directionArrow.alignment = TextAnchor.MiddleCenter;
                _directionArrow.horizontalOverflow = HorizontalWrapMode.Overflow;
                _directionArrow.verticalOverflow = VerticalWrapMode.Overflow;
                _directionArrow.text = "▶";
                _directionArrow.raycastTarget = false;
                RectTransform markerRect = _directionArrow.rectTransform;
                markerRect.anchorMin = markerRect.anchorMax =
                    new Vector2(1f, 0.5f);
                markerRect.pivot = new Vector2(0.5f, 0.5f);
                markerRect.anchoredPosition = Vector2.zero;
                markerRect.sizeDelta = new Vector2(16f, 16f);
            }

            if (_label == null)
            {
                GameObject labelRoot = new GameObject("RelationshipLabel",
                    typeof(RectTransform), typeof(Image));
                labelRoot.transform.SetParent(transform, false);
                RectTransform labelRect =
                    labelRoot.GetComponent<RectTransform>();
                labelRect.anchorMin = labelRect.anchorMax =
                    new Vector2(0f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.sizeDelta = new Vector2(34f, 16f);
                Image background = labelRoot.GetComponent<Image>();
                background.color = new Color(0.04f, 0.035f, 0.025f, 0.94f);
                background.raycastTarget = false;

                GameObject textObject = new GameObject("Text",
                    typeof(RectTransform), typeof(Text));
                textObject.transform.SetParent(labelRoot.transform, false);
                _label = textObject.GetComponent<Text>();
                _label.font = LocalizedTextManager.current_font;
                _label.fontSize = 8;
                _label.alignment = TextAnchor.MiddleCenter;
                _label.raycastTarget = false;
                RectTransform textRect = _label.rectTransform;
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(2f, 1f);
                textRect.offsetMax = new Vector2(-2f, -1f);
            }
        }

        private void ApplyStyle()
        {
            bool management = Edge?.Kind == CustomCourtEdgeKind.Management;
            Color color = management
                ? new Color(0.18f, 0.86f, 1f, 0.94f)
                : new Color(1f, 0.58f, 0.12f, 0.94f);
            if (_line != null) _line.color = color;
            if (_directionArrow != null) _directionArrow.color = color;
            if (_label != null)
            {
                _label.color = color;
                _label.text = management
                    ? AW_L10n.Text("aw_custom_court_edge_management_short",
                        "Manage")
                    : AW_L10n.Text(
                        "aw_custom_court_edge_prerequisite_short", "Requires");
            }
        }

        private void UpdateGeometry()
        {
            RectTransform rect = transform as RectTransform;
            if (rect == null || _from == null || _to == null) return;

            Vector2 fromCenter = CenterOf(_from);
            Vector2 toCenter = CenterOf(_to);
            Vector2 direction = toCenter - fromCenter;
            float distance = direction.magnitude;
            if (distance < 0.01f)
            {
                rect.sizeDelta = Vector2.zero;
                return;
            }

            Vector2 normalized = direction / distance;
            Vector2 start = fromCenter + normalized * BoundaryDistance(_from,
                normalized);
            Vector2 end = toCenter - normalized * BoundaryDistance(_to,
                -normalized);
            direction = end - start;
            distance = Mathf.Max(1f, direction.magnitude);
            float angle = Mathf.Atan2(direction.y, direction.x) *
                          Mathf.Rad2Deg;

            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = start;
            rect.sizeDelta = new Vector2(distance,
                Edge?.Kind == CustomCourtEdgeKind.Management ? 4f : 2f);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);

            if (_label != null)
            {
                RectTransform labelRect = _label.transform.parent as
                    RectTransform;
                labelRect.anchoredPosition = new Vector2(distance * 0.5f, 0f);
                labelRect.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
        }

        private static Vector2 CenterOf(RectTransform rect)
        {
            Vector2 size = rect.rect.size;
            return rect.anchoredPosition + new Vector2(
                (0.5f - rect.pivot.x) * size.x,
                (0.5f - rect.pivot.y) * size.y);
        }

        private static float BoundaryDistance(RectTransform rect,
            Vector2 direction)
        {
            Vector2 half = rect.rect.size * 0.5f;
            float x = Mathf.Abs(direction.x) > 0.0001f
                ? half.x / Mathf.Abs(direction.x)
                : float.PositiveInfinity;
            float y = Mathf.Abs(direction.y) > 0.0001f
                ? half.y / Mathf.Abs(direction.y)
                : float.PositiveInfinity;
            return Mathf.Min(x, y);
        }
    }
}
