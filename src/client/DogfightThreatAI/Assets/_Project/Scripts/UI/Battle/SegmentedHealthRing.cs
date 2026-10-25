using UnityEngine;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Battle
{
    /// <summary>只绘制五段环，不计算伤害。外半径 5、内半径 3，即 0.6 比例。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SegmentedHealthRing : MaskableGraphic
    {
        int _health = 5;
        public int FilledSegments => _health;
        public void SetHealth(int health)
        {
            int next = Mathf.Clamp(health, 0, 5);
            if (next == _health) return;
            _health = next; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            float outer = Mathf.Min(rect.width, rect.height) * .5f, inner = outer * .6f;
            for (int segment = 0; segment < 5; segment++)
            {
                Color tint = segment < _health ? color : new Color(color.r, color.g, color.b, color.a * .18f);
                if (_health <= 1 && segment < _health) tint = new Color(.82f, .37f, .29f, color.a);
                for (int step = 0; step < 24; step++)
                {
                    float a = (90 - segment * 72 - 3 - step * 66f / 24) * Mathf.Deg2Rad;
                    float b = (90 - segment * 72 - 3 - (step + 1) * 66f / 24) * Mathf.Deg2Rad;
                    int index = vh.currentVertCount;
                    Add(vh, rect.center, a, outer, tint); Add(vh, rect.center, b, outer, tint);
                    Add(vh, rect.center, b, inner, tint); Add(vh, rect.center, a, inner, tint);
                    vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
                }
            }
        }
        static void Add(VertexHelper vh, Vector2 center, float angle, float radius, Color tint)
            => vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
    }
}
