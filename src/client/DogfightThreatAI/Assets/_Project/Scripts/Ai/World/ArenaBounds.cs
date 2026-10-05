namespace Dogfight.Ai
{
    /// <summary>
    /// 战场矩形边界 + **软边界回推**。
    ///
    /// 为什么不做成硬墙：俯视空战里撞墙急停手感很差，而且会让 AI 的追击路线在角落打死结。
    /// 软边界的做法是——进入靠边的 margin 带之后，施加一个朝场内的力，越靠边越强；
    /// 于是飞机会被"推"回来，而不是被"挡住"。
    ///
    /// 这是纯计算（无 Unity 依赖），所以能单测：边界力必须在场内为零、方向朝内、越界越强。
    /// </summary>
    public readonly struct ArenaBounds
    {
        public readonly float MinX;

        public readonly float MinY;

        public readonly float MaxX;

        public readonly float MaxY;

        /// <summary>靠边的回推带宽度（世界单位）。0 表示只做硬夹紧、没有回推力。</summary>
        public readonly float SoftMargin;

        public ArenaBounds(float minX, float minY, float maxX, float maxY, float softMargin)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX < minX ? minX : maxX;
            MaxY = maxY < minY ? minY : maxY;
            SoftMargin = softMargin < 0f ? 0f : softMargin;
        }

        public static readonly ArenaBounds Unbounded =
            new ArenaBounds(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity, 0f);

        /// <summary>由中心与尺寸构造。size 是整条边的长度。</summary>
        public static ArenaBounds FromSize(Vec2 center, Vec2 size, float softMargin)
        {
            float halfX = MathUtil.Abs(size.X) * 0.5f;
            float halfY = MathUtil.Abs(size.Y) * 0.5f;
            return new ArenaBounds(center.X - halfX, center.Y - halfY, center.X + halfX, center.Y + halfY, softMargin);
        }

        public Vec2 Center => new Vec2((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f);

        public Vec2 Size => new Vec2(MaxX - MinX, MaxY - MinY);

        public bool IsUnbounded =>
            float.IsInfinity(MinX) || float.IsInfinity(MaxX) || float.IsInfinity(MinY) || float.IsInfinity(MaxY);

        public bool Contains(Vec2 point) =>
            point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

        /// <summary>硬夹紧到边界内（兜底用，防止极端情况下真的飞出去）。</summary>
        public Vec2 Clamp(Vec2 point) =>
            new Vec2(MathUtil.Clamp(point.X, MinX, MaxX), MathUtil.Clamp(point.Y, MinY, MaxY));

        /// <summary>
        /// 边界回推力（向量，指向场内）。
        /// 每一轴独立计算：进入 margin 带之后按"深入程度"线性给出 0~1 的权重，
        /// 已经越界时权重 &gt; 1（越出越多，拉回越猛）。strength 是权重 1 时对应的力大小。
        /// </summary>
        public Vec2 BoundaryForce(Vec2 position, float strength)
        {
            if (IsUnbounded || SoftMargin <= 1e-4f) return Vec2.Zero;

            float margin = SoftMargin;
            float fx = 0f;
            float fy = 0f;

            // 左边界：位置越靠左，越需要往右推
            if (position.X < MinX + margin) fx += (MinX + margin - position.X) / margin;
            // 右边界
            if (position.X > MaxX - margin) fx -= (position.X - (MaxX - margin)) / margin;
            // 下边界
            if (position.Y < MinY + margin) fy += (MinY + margin - position.Y) / margin;
            // 上边界
            if (position.Y > MaxY - margin) fy -= (position.Y - (MaxY - margin)) / margin;

            return new Vec2(fx, fy) * strength;
        }

        /// <summary>离最近边界还有多远（场内为正）。用于 HUD 预警与 AI 的"别贴边"判断。</summary>
        public float DistanceToNearestEdge(Vec2 position)
        {
            if (IsUnbounded) return float.PositiveInfinity;
            float left = position.X - MinX;
            float right = MaxX - position.X;
            float bottom = position.Y - MinY;
            float top = MaxY - position.Y;
            float min = MathUtil.Min(MathUtil.Min(left, right), MathUtil.Min(bottom, top));
            return min;
        }

        /// <summary>位置是否已进入软边界带（HUD 可以用来闪红）。</summary>
        public bool IsNearEdge(Vec2 position) =>
            !IsUnbounded && SoftMargin > 0f && DistanceToNearestEdge(position) < SoftMargin;

        public override string ToString() =>
            "Arena[" + MinX.ToString("0.#") + "," + MinY.ToString("0.#") + " → " +
            MaxX.ToString("0.#") + "," + MaxY.ToString("0.#") + " margin=" + SoftMargin.ToString("0.#") + "]";
    }
}
