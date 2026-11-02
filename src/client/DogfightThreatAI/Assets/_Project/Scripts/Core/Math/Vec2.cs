using System;

namespace DogfightThreatAI.Core.Math
{
    /// <summary>
    /// 二维向量。该类型不依赖 UnityEngine，
    /// 可在 Core、AI 与逻辑服务器中使用。
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 One = new Vec2(1f, 1f);
        public static readonly Vec2 Right = new Vec2(1f, 0f);
        public static readonly Vec2 Up = new Vec2(0f, 1f);

        public float SqrMagnitude => X * X + Y * Y;
        public float Magnitude => MathF.Sqrt(SqrMagnitude);

        /// <summary>单位化。零向量返回 Zero（不会返回 NaN）。</summary>
        public Vec2 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? new Vec2(X / m, Y / m) : Zero;
            }
        }

        /// <summary>机头角度（弧度），以 +X 轴为 0，逆时针为正。渲染时转成角度用。</summary>
        public float AngleRad => MathF.Atan2(Y, X);

        /// <summary>从弧度构造单位向量。</summary>
        public static Vec2 FromAngle(float radians)
        {
            return new Vec2(MathF.Cos(radians), MathF.Sin(radians));
        }

        /// <summary>按最大距离向目标靠拢。飞行模型用它产生"惯性滑行"手感。</summary>
        public static Vec2 MoveTowards(Vec2 current, Vec2 target, float maxDelta)
        {
            Vec2 d = target - current;
            float m = d.Magnitude;
            if (m <= maxDelta || m < 1e-6f)
            {
                return target;
            }
            return current + d * (maxDelta / m);
        }

        // 计算两个世界位置之间的直线距离。
        public static float Distance(Vec2 a, Vec2 b)
        {
            return (a - b).Magnitude;
        }

        // 计算向量点积，用于投影与同向程度判断。
        public static float Dot(Vec2 a, Vec2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        // 计算二维叉积的标量值，用于判断旋转侧别。
        public static float Cross(Vec2 a, Vec2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        /// <summary>把角度规整到 [-π, π)。渲染插值、威胁评估算"相对方位"时会用到。</summary>
        public static float NormalizeAngle(float radians)
        {
            const float twoPi = 6.28318530718f;
            radians %= twoPi;
            if (radians >= 3.14159265359f)
            {
                radians -= twoPi;
            }
            else if (radians < -3.14159265359f)
            {
                radians += twoPi;
            }
            return radians;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X + b.X, a.Y + b.Y);
        }

        public static Vec2 operator -(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X - b.X, a.Y - b.Y);
        }

        public static Vec2 operator -(Vec2 a)
        {
            return new Vec2(-a.X, -a.Y);
        }

        public static Vec2 operator *(Vec2 a, float s)
        {
            return new Vec2(a.X * s, a.Y * s);
        }

        public static Vec2 operator *(float s, Vec2 a)
        {
            return new Vec2(a.X * s, a.Y * s);
        }

        public static Vec2 operator /(Vec2 a, float s)
        {
            return new Vec2(a.X / s, a.Y / s);
        }

        public bool Equals(Vec2 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is Vec2 v && Equals(v);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ (Y.GetHashCode() << 2);
        }

        public override string ToString()
        {
            return $"({X:0.###}, {Y:0.###})";
        }
    }
}
