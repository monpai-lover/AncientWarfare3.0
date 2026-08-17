using AncientWarfare3.core.court;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowEdgeView : MonoBehaviour
    {
        public CustomCourtEdge Edge { get; private set; }

        public void Bind(CustomCourtEdge edge, RectTransform from,
            RectTransform to)
        {
            Edge = edge;
            Image image = GetComponent<Image>();
            if (image != null)
                image.color = edge?.Kind == CustomCourtEdgeKind.Management
                    ? new Color(0.32f, 0.72f, 0.92f, 0.8f)
                    : new Color(0.9f, 0.62f, 0.25f, 0.8f);
            if (from == null || to == null) return;
            RectTransform rect = transform as RectTransform;
            Vector2 start = from.anchoredPosition;
            Vector2 end = to.anchoredPosition;
            Vector2 direction = end - start;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = start;
            rect.sizeDelta = new Vector2(direction.magnitude, 3f);
            rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
    }
}
