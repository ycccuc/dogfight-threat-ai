namespace Dogfight.Ai
{
    /// <summary>
    /// 战场网格（可行走 / 障碍的二维布尔图）。
    ///
    /// 用途：BFS 巡逻与搜索、以及将来的威胁评估。这里只负责"地图长什么样"，
    /// 不含任何算法 —— 算法在 BfsPathfinder 里，这样两者可以分别单测。
    ///
    /// 坐标：格子 (0,0) 在左下角，格心世界坐标 = origin + (x+0.5, y+0.5) * cellSize。
    /// </summary>
    public sealed class GridMap
    {
        readonly bool[] _walkable;

        public int Width { get; }

        public int Height { get; }

        public float CellSize { get; }

        /// <summary>网格左下角的世界坐标。</summary>
        public Vec2 Origin { get; }

        public int CellCount => Width * Height;

        public GridMap(int width, int height, float cellSize, Vec2 origin, bool fillWalkable = true)
        {
            Width = width < 1 ? 1 : width;
            Height = height < 1 ? 1 : height;
            CellSize = cellSize > 1e-4f ? cellSize : 1f;
            Origin = origin;
            _walkable = new bool[Width * Height];
            if (fillWalkable) Fill(true);
        }

        public void Fill(bool walkable)
        {
            for (int i = 0; i < _walkable.Length; i++) _walkable[i] = walkable;
        }

        public bool IsInside(Vec2Int cell) =>
            cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

        /// <summary>越界一律视为不可行走 —— 这样寻路自然被关在地图内。</summary>
        public bool IsWalkable(Vec2Int cell) => IsInside(cell) && _walkable[IndexOf(cell)];

        public void SetWalkable(Vec2Int cell, bool walkable)
        {
            if (!IsInside(cell)) return;
            _walkable[IndexOf(cell)] = walkable;
        }

        public int IndexOf(Vec2Int cell) => cell.Y * Width + cell.X;

        public Vec2Int CellAt(int index) => new Vec2Int(index % Width, index / Width);

        public Vec2Int WorldToCell(Vec2 world)
        {
            float fx = (world.X - Origin.X) / CellSize;
            float fy = (world.Y - Origin.Y) / CellSize;
            return new Vec2Int((int)System.Math.Floor(fx), (int)System.Math.Floor(fy));
        }

        /// <summary>格心世界坐标。</summary>
        public Vec2 CellCenter(Vec2Int cell) =>
            new Vec2(Origin.X + (cell.X + 0.5f) * CellSize, Origin.Y + (cell.Y + 0.5f) * CellSize);

        /// <summary>整张地图的世界尺寸。</summary>
        public Vec2 WorldSize => new Vec2(Width * CellSize, Height * CellSize);

        /// <summary>把矩形区域一次性设为障碍（建地形时用）。</summary>
        public void SetRectWalkable(Vec2Int min, Vec2Int max, bool walkable)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    SetWalkable(new Vec2Int(x, y), walkable);
                }
            }
        }

        /// <summary>可行走格子总数（用于校验地形配置）。</summary>
        public int CountWalkable()
        {
            int n = 0;
            for (int i = 0; i < _walkable.Length; i++) if (_walkable[i]) n++;
            return n;
        }
    }
}
