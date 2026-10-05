using System;

namespace Dogfight.Ai
{
    /// <summary>
    /// 数学工具。因为 Dogfight.Ai 不引用 UnityEngine，所以拿不到 Mathf，这里是自己的一份。
    /// 只放这一层真正用得到的函数，不追求完整 —— 缺什么再加，避免变成"重写一遍 Mathf"。
    /// </summary>
    public static class MathUtil
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;
        public const float Epsilon = 1e-6f;

        public static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);

        public static float Clamp01(float value) => Clamp(value, 0f, 1f);

        public static int Clamp(int value, int min, int max) =>
            value < min ? min : (value > max ? max : value);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;

        /// <summary>与 Mathf.MoveTowards 同语义：每步最多移动 maxDelta。</summary>
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float diff = target - current;
            if (Math.Abs(diff) <= maxDelta) return target;
            return current + Math.Sign(diff) * maxDelta;
        }

        public static float Abs(float value) => value < 0f ? -value : value;

        public static float Min(float a, float b) => a < b ? a : b;

        public static float Max(float a, float b) => a > b ? a : b;

        public static float Sqrt(float value) => (float)Math.Sqrt(value);

        /// <summary>把角度规整到 [0, 2π)。</summary>
        public static float NormalizeAngle(float radians)
        {
            float r = radians % TwoPi;
            return r < 0f ? r + TwoPi : r;
        }

        /// <summary>把角度规整到 (-π, π]。</summary>
        public static float NormalizeAngleSigned(float radians)
        {
            float r = NormalizeAngle(radians);
            return r > Pi ? r - TwoPi : r;
        }

        public static bool Approximately(float a, float b, float tolerance = 1e-4f) =>
            Abs(a - b) <= tolerance;
    }
}
