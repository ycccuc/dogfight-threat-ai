using System;

namespace Dogfight.Ai
{
    /// <summary>
    /// 二维向量。
    ///
    /// 为什么不用 UnityEngine.Vector2：
    ///   Dogfight.Ai 程序集的 asmdef 打开了 noEngineReferences（不引用 UnityEngine），
    ///   所以这一层拿不到 Vector2/Mathf。代价是要自己写这个结构体；
    ///   收益是**编译器强制**保证这一层不依赖 Unity ——
    ///   于是单元测试不需要进 Play 模式，批量对战不需要渲染，
    ///   将来逻辑服务器（无头进程）也能直接复用同一份代码。
    ///
    /// 角度约定与 Unity 一致：0 弧度 = +X 方向，逆时针为正。
    /// </summary>
    // 刻意不加 [Serializable]：Unity 不序列化 readonly 字段，加了只会让人误以为
    // 能在 Inspector 里存。Vec2 是运行期数据，不是配置数据。
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 Right = new Vec2(1f, 0f);
        public static readonly Vec2 Up = new Vec2(0f, 1f);

        public float SqrMagnitude => X * X + Y * Y;

        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);

        public Vec2 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? new Vec2(X / m, Y / m) : Zero;
            }
        }

        /// <summary>弧度制朝向（0 = +X，逆时针为正）。零向量返回 0。</summary>
        public float AngleRadians
        {
            get
            {
                if (SqrMagnitude < 1e-12f) return 0f;
                return (float)Math.Atan2(Y, X);
            }
        }

        public static Vec2 FromAngle(float radians) =>
            new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians));

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);

        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);

        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);

        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);

        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>二维叉积（返回标量）：判断左右转方向用。</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;

        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrMagnitude;

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * MathUtil.Clamp01(t);

        /// <summary>把长度限制到 maxLength 以内，方向不变。</summary>
        public Vec2 ClampedTo(float maxLength)
        {
            float sq = SqrMagnitude;
            if (sq <= maxLength * maxLength) return this;
            float m = (float)Math.Sqrt(sq);
            return new Vec2(X / m * maxLength, Y / m * maxLength);
        }

        /// <summary>逆时针旋转 radians 弧度。</summary>
        public Vec2 Rotated(float radians)
        {
            float c = (float)Math.Cos(radians);
            float s = (float)Math.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        /// <summary>两个角度之间的最短夹角，范围 (-π, π]。</summary>
        public static float DeltaAngle(float fromRadians, float toRadians)
        {
            float d = (toRadians - fromRadians) % MathUtil.TwoPi;
            if (d > MathUtil.Pi) d -= MathUtil.TwoPi;
            if (d < -MathUtil.Pi) d += MathUtil.TwoPi;
            return d;
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => unchecked(X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() => "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ")";
    }
}
