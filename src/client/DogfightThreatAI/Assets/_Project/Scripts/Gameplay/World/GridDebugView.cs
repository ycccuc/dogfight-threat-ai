using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.World
{
    /// <summary>
    /// 使用 Gizmos 绘制逻辑网格，用于在 Scene 视图中检查网格范围与对齐。
    /// 该组件仅负责可视化，不参与逻辑计算。
    /// 威胁热力图接入后，可在此基础上按格子显示威胁值。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GridDebugView : MonoBehaviour
    {
        [Header("网格配置（必须和逻辑网格一致）")]
        [Tooltip("世界尺寸。例如 400 × 300")]
        [SerializeField] Vector2 worldSize = new Vector2(400f, 300f);

        [Tooltip("单格边长。例如 4")]
        [SerializeField] float cellSize = 4f;

        [Header("绘制")]
        [Tooltip("Play 模式下也绘制（调试 AI 时建议保持开启）")]
        [SerializeField] bool drawInPlayMode = true;

        [Tooltip("每隔几条画一条粗线（例如 4 条 = 每 16 单位一条主线）")]
        [SerializeField, Min(1)] int majorEvery = 4;

        [SerializeField] Color lineColor = new Color(1f, 1f, 1f, 0.10f);
        [SerializeField] Color majorLineColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] Color borderColor = new Color(0.3f, 1f, 0.6f, 0.9f);

        Grid2D _grid;

        /// <summary>供其他系统读取（ThreatGrid 会复用同一份配置）。</summary>
        public Grid2D Grid => _grid;

        // 编辑网格配置后更新调试用逻辑网格。
        void OnValidate()
        {
            Rebuild();
        }

        // 根据场景配置初始化调试网格。
        void Awake()
        {
            Rebuild();
        }

        // 限制格子尺寸并重建居中的逻辑网格数据。
        void Rebuild()
        {
            if (cellSize <= 0f)
            {
                cellSize = 0.01f;
            }
            _grid = Grid2D.Centered(new Vec2(worldSize.x, worldSize.y), cellSize);
        }

        // 在 Scene 视图中绘制细格、主线和战场边界。
        void OnDrawGizmos()
        {
            if (Application.isPlaying && !drawInPlayMode)
            {
                return;
            }
            if (cellSize <= 0f)
            {
                return;
            }
            // OnDrawGizmos 可能在 Awake 之前被调用，所以这里也重建一次
            if (_grid.CellSize <= 0f || Mathf.Abs(_grid.CellSize - cellSize) > 1e-4f)
            {
                Rebuild();
            }
            if (_grid.CellCount <= 0)
            {
                return;
            }
            float halfW = _grid.Bounds.Width * 0.5f;
            float halfH = _grid.Bounds.Height * 0.5f;

            // 竖线
            for (int x = 0; x <= _grid.Width; x++)
            {
                float px = -halfW + x * cellSize;
                Gizmos.color = (x % majorEvery == 0) ? majorLineColor : lineColor;
                Gizmos.DrawLine(new Vector3(px, -halfH, 0f), new Vector3(px, halfH, 0f));
            }

            // 横线
            for (int y = 0; y <= _grid.Height; y++)
            {
                float py = -halfH + y * cellSize;
                Gizmos.color = (y % majorEvery == 0) ? majorLineColor : lineColor;
                Gizmos.DrawLine(new Vector3(-halfW, py, 0f), new Vector3(halfW, py, 0f));
            }

            // 战场边界（粗一圈，方便确认飞机不会飞出去）
            Gizmos.color = borderColor;
            var c0 = new Vector3(-halfW, -halfH, 0f);
            var c1 = new Vector3(halfW, -halfH, 0f);
            var c2 = new Vector3(halfW, halfH, 0f);
            var c3 = new Vector3(-halfW, halfH, 0f);
            Gizmos.DrawLine(c0, c1);
            Gizmos.DrawLine(c1, c2);
            Gizmos.DrawLine(c2, c3);
            Gizmos.DrawLine(c3, c0);
        }
    }
}
