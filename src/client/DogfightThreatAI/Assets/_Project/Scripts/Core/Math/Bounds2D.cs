using System;

namespace DogfightThreatAI.Core.Math
{
    /// <summary>
    /// 二维矩形边界，表示战场范围。
    /// 边界判定属于模拟层，客户端与服务器使用同一份数据。
    /// 该类型不依赖 Unity，可用于 EditMode 测试与网络快照。
    /// </summary>
    [Serializable]
    public struct Bounds2D
    {
        public Vec2 Min;
        public Vec2 Max;

        public Bounds2D(Vec2 min, Vec2 max)
        {
            Min = new Vec2(MathF.Min(min.X, max.X), MathF.Min(min.Y, max.Y));
            Max = new Vec2(MathF.Max(min.X, max.X), MathF.Max(min.Y, max.Y));
        }

        /// <summary>以原点为中心、给定半尺寸的矩形。</summary>
        public static Bounds2D Centered(Vec2 halfSize)
        {
            return new Bounds2D(halfSize * -1f, halfSize);
        }

        public float Width => Max.X - Min.X;
        public float Height => Max.Y - Min.Y;
        public Vec2 Center => (Min + Max) * 0.5f;

        // 把世界位置限制到矩形边界内。
        public Vec2 Clamp(Vec2 p)
        {
            return new Vec2(MathF.Max(Min.X, MathF.Min(p.X, Max.X)),
                    MathF.Max(Min.Y, MathF.Min(p.Y, Max.Y)));
        }

        // 判断世界位置是否落在当前矩形范围内。
        public bool Contains(Vec2 p)
        {
            return p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;
        }

        public override string ToString()
        {
            return $"[{Min} → {Max}]";
        }
    }
}
