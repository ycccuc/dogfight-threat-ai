using UnityEngine;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Battle
{
    /// <summary>一颗机炮弹的矢量剪影，可替换为美术资产；不持有武器逻辑。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CannonIcon : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2[] outline = { new Vector2(-.24f, -.5f), new Vector2(.24f, -.5f),
                new Vector2(.24f, -.38f), new Vector2(.16f, -.34f), new Vector2(.16f, .24f),
                new Vector2(.10f, .4f), new Vector2(0, .5f), new Vector2(-.1f, .4f),
                new Vector2(-.16f, .24f), new Vector2(-.16f, -.34f), new Vector2(-.24f, -.38f) };
            var rect = rectTransform.rect;
            vh.AddVert(rect.center, color, Vector2.zero);
            foreach (var p in outline) vh.AddVert(rect.center + Vector2.Scale(p, rect.size), color, Vector2.zero);
            for (int i = 0; i < outline.Length; i++) vh.AddTriangle(0, i + 1, (i + 1) % outline.Length + 1);
        }
    }
}
