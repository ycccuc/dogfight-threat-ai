using System;

namespace Dogfight.Ai
{
    /// <summary>
    /// 整数网格坐标（BFS / 巡逻航线用）。
    /// 与 Vec2 同理：不用 UnityEngine.Vector2Int，保持这一层零 Unity 依赖。
    /// </summary>
    // 同 Vec2：不加 [Serializable]，readonly 字段 Unity 序列化不了。
    public readonly struct Vec2Int : IEquatable<Vec2Int>
    {
        public readonly int X;
        public readonly int Y;

        public Vec2Int(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2Int Zero = new Vec2Int(0, 0);

        /// <summary>四邻域方向（上/右/下/左）。BFS 默认用它。</summary>
        public static readonly Vec2Int[] Neighbors4 =
        {
            new Vec2Int(0, 1),
            new Vec2Int(1, 0),
            new Vec2Int(0, -1),
            new Vec2Int(-1, 0),
        };

        /// <summary>八邻域方向（含对角）。注意对角代价不是 1，用之前想清楚。</summary>
        public static readonly Vec2Int[] Neighbors8 =
        {
            new Vec2Int(0, 1), new Vec2Int(1, 1), new Vec2Int(1, 0), new Vec2Int(1, -1),
            new Vec2Int(0, -1), new Vec2Int(-1, -1), new Vec2Int(-1, 0), new Vec2Int(-1, 1),
        };

        public static Vec2Int operator +(Vec2Int a, Vec2Int b) => new Vec2Int(a.X + b.X, a.Y + b.Y);

        public static Vec2Int operator -(Vec2Int a, Vec2Int b) => new Vec2Int(a.X - b.X, a.Y - b.Y);

        public static bool operator ==(Vec2Int a, Vec2Int b) => a.X == b.X && a.Y == b.Y;

        public static bool operator !=(Vec2Int a, Vec2Int b) => !(a == b);

        /// <summary>曼哈顿距离（四邻域下的真实步数下界）。</summary>
        public int ManhattanTo(Vec2Int other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        /// <summary>切比雪夫距离（八邻域下的步数下界）。</summary>
        public int ChebyshevTo(Vec2Int other) =>
            Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

        /// <summary>格心在世界坐标里的位置（格边长 = cellSize）。</summary>
        public Vec2 ToWorld(float cellSize) =>
            new Vec2((X + 0.5f) * cellSize, (Y + 0.5f) * cellSize);

        public bool Equals(Vec2Int other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is Vec2Int other && Equals(other);

        public override int GetHashCode() => unchecked(X * 73856093) ^ (Y * 19349663);

        public override string ToString() => "[" + X + "," + Y + "]";
    }
}
