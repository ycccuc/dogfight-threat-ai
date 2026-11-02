using System;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.World
{
    /// <summary>
    /// 二维逻辑网格，用于威胁评估与 A* 寻路。
    /// 提供世界坐标与格子坐标互转、行优先索引以及 4/8 邻居查询。
    /// 该类型仅表示逻辑数据，不负责渲染；格子线由 GridDebugView 绘制。
    /// 该类型不引用 UnityEngine，客户端与逻辑服务器使用同一份实现。
    /// </summary>
    [Serializable]
    public readonly struct Grid2D
    {
        /// <summary>网格覆盖的世界范围（连续坐标）。</summary>
        public readonly Bounds2D Bounds;

        /// <summary>单格边长（世界单位）。例如 4 表示每格 4×4。</summary>
        public readonly float CellSize;

        public Grid2D(Bounds2D bounds, float cellSize)
        {
            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize 必须 > 0");
            }
            Bounds = bounds;
            CellSize = cellSize;
        }

        /// <summary>以原点为中心、给定世界尺寸构造。 size = (400,300)、cell = 4 → 100×75 格。</summary>
        public static Grid2D Centered(Vec2 worldSize, float cellSize)
        {
            return new Grid2D(Bounds2D.Centered(worldSize * 0.5f), cellSize);
        }

        /// <summary>
        /// 横向格数（向上取整，保证覆盖整个 Bounds）。
        /// ⚠️ 这里必须写 System.Math：本项目有命名空间 DogfightThreatAI.Core.Math，
        ///    它会遮蔽 System.Math，直接写 Math.Max 会报 CS0234。
        /// </summary>
        public int Width => System.Math.Max(1, (int)MathF.Ceiling(Bounds.Width / CellSize));

        /// <summary>纵向格数。</summary>
        public int Height => System.Math.Max(1, (int)MathF.Ceiling(Bounds.Height / CellSize));

        /// <summary>总格数。400×300 / 4 = 100×75 = 7500。</summary>
        public int CellCount => Width * Height;

        // ───────────────────────── 坐标换算 ─────────────────────────

        /// <summary>
        /// 世界坐标 → 格子坐标（向下取整）。
        /// 越界时返回 false，并把 x/y 设成 -1（调用方必须检查）。
        /// </summary>
        /// <remarks>
        /// 为什么用 floor 而不是 (int) 强转：
        ///   (int)(-0.5f) = 0  —— 负数向 0 截断，格子全错
        ///   MathF.Floor(-0.5f) = -1  —— 正确
        /// 这里先把坐标平移到以 Bounds.Min 为原点，再除，天然规避该问题。
        /// </remarks>
        public bool TryWorldToCell(Vec2 world, out int cellX, out int cellY)
        {
            cellX = (int)MathF.Floor((world.X - Bounds.Min.X) / CellSize);
            cellY = (int)MathF.Floor((world.Y - Bounds.Min.Y) / CellSize);
            if (InRange(cellX, cellY))
            {
                return true;
            }
            cellX = -1;
            cellY = -1;
            return false;
        }

        /// <summary>世界坐标 → 格子坐标，越界则夹到最近的合法格（AI 常用）。</summary>
        public void WorldToCellClamped(Vec2 world, out int cellX, out int cellY)
        {
            cellX = ClampInt((int)MathF.Floor((world.X - Bounds.Min.X) / CellSize), 0, Width - 1);
            cellY = ClampInt((int)MathF.Floor((world.Y - Bounds.Min.Y) / CellSize), 0, Height - 1);
        }

        /// <summary>格子坐标 → 该格中心的世界坐标（AI 的目标点一般用格中心，不要用格角）。</summary>
        public Vec2 CellCenter(int cellX, int cellY)
        {
            return new Vec2(Bounds.Min.X + (cellX + 0.5f) * CellSize,
                    Bounds.Min.Y + (cellY + 0.5f) * CellSize);
        }

        /// <summary>格子坐标 → 该格覆盖的世界矩形。</summary>
        public Bounds2D CellBounds(int cellX, int cellY)
        {
            Vec2 min = new Vec2(Bounds.Min.X + cellX * CellSize, Bounds.Min.Y + cellY * CellSize);
            return new Bounds2D(min, new Vec2(min.X + CellSize, min.Y + CellSize));
        }

        // 判断格子坐标是否处于网格有效范围内。
        public bool InRange(int cellX, int cellY)
        {
            return cellX >= 0 && cellX < Width && cellY >= 0 && cellY < Height;
        }

        // ───────────────────────── 索引换算（行优先）─────────────────────────

        /// <summary>
        /// (x, y) → 一维索引。行优先：index = y * Width + x。
        /// 同一行在内存里连续，扫行/扫邻居时缓存友好。
        /// </summary>
        public int ToIndex(int cellX, int cellY)
        {
            return cellY * Width + cellX;
        }

        // 把一维格索引转换为二维格坐标。
        public void FromIndex(int index, out int cellX, out int cellY)
        {
            cellY = index / Width;
            cellX = index - cellY * Width;
        }

        /// <summary>越界返回 false（避免数组越界）。</summary>
        public bool TryToIndex(Vec2 world, out int index)
        {
            if (TryWorldToCell(world, out int x, out int y))
            {
                index = ToIndex(x, y);
                return true;
            }
            index = -1;
            return false;
        }

        // ───────────────────────── 邻居 ─────────────────────────

        /// <summary>四邻居偏移（上下左右）。</summary>
        public static readonly (int dx, int dy)[] Neighbors4 =
        {
            ( 0,  1), ( 1,  0), ( 0, -1), (-1,  0)
        };

        /// <summary>八邻居偏移（含对角）。A* 常用这个。</summary>
        public static readonly (int dx, int dy)[] Neighbors8 =
        {
            ( 0,  1), ( 1,  1), ( 1,  0), ( 1, -1),
            ( 0, -1), (-1, -1), (-1,  0), (-1,  1)
        };

        /// <summary>枚举四邻居的合法格子（不分配内存，用 for 循环调用）。</summary>
        public int NeighborCount4 => 4;

        // 读取四邻域中的指定相邻格，并检查边界。
        public bool TryNeighbor4(int cellX, int cellY, int dir, out int nx, out int ny)
        {
            var (dx, dy) = Neighbors4[dir];
            nx = cellX + dx;
            ny = cellY + dy;
            return InRange(nx, ny);
        }

        // 读取八邻域中的指定相邻格，并检查边界。
        public bool TryNeighbor8(int cellX, int cellY, int dir, out int nx, out int ny)
        {
            var (dx, dy) = Neighbors8[dir];
            nx = cellX + dx;
            ny = cellY + dy;
            return InRange(nx, ny);
        }

        static int ClampInt(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public override string ToString()
        {
            return $"Grid2D {Width}×{Height} cell={CellSize} bounds={Bounds}";
        }
    }
}
