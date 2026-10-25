using UnityEngine;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Menu
{
    /// <summary>Native UI mesh: no transient texture references when saving the authored scene.</summary>
    [AddComponentMenu("Dogfight/UI/Menu Shade")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuShade : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            const int columns = 24;
            for (int i = 0; i <= columns; i++)
            {
                float t = (float)i / columns;
                float alpha = Mathf.Lerp(.02f, .72f, Mathf.SmoothStep(0, 1, (t - .35f) / .65f));
                var tint = new Color(.018f, .028f, .031f, alpha);
                float x = Mathf.Lerp(rect.xMin, rect.xMax, t);
                vh.AddVert(new Vector3(x, rect.yMin), tint, Vector2.zero);
                vh.AddVert(new Vector3(x, rect.yMax), tint, Vector2.one);
                if (i == 0) continue;
                int v = (i - 1) * 2;
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v + 2, v + 1, v + 3);
            }
        }
    }
}
