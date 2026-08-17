using AncientWarfare3.core.court;
using UnityEngine;
using UnityEngine.UI;

namespace AncientWarfare3.ui.components
{
    public sealed class CourtWorkflowEdgeView : MonoBehaviour
    {
        public CustomCourtEdge Edge { get; private set; }

        public void Bind(CustomCourtEdge edge)
        {
            Edge = edge;
            Image image = GetComponent<Image>();
            if (image != null)
                image.color = edge?.Kind == CustomCourtEdgeKind.Management
                    ? new Color(0.32f, 0.72f, 0.92f, 0.8f)
                    : new Color(0.9f, 0.62f, 0.25f, 0.8f);
        }
    }
}
