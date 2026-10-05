namespace Dogfight.Ai
{
    /// <summary>
    /// 飞行动力学的**纯计算**部分。
    ///
    /// 这里只负责"给定输入和当前状态，应该产生多大的力 / 角速度"，
    /// 具体怎么把它作用到 Rigidbody2D 上，由 Gameplay 层的 PlaneAgent 决定。
    /// 这样拆的好处：手感公式可以单测（例如"低速时转向能力必须下降"），
    /// 而不需要开 Unity、也不需要 Play 模式。
    ///
    /// 角度约定：一律用**弧度**，逆时针为正，0 = +X 方向。
    /// 朝向用单位向量 Vec2 表示，不用角度 —— 免得把"精灵机头朝上"这种表现层细节漏进本层。
    /// </summary>
    public static class FlightModel
    {
        /// <summary>
        /// 推力（沿机头方向的力）。
        /// throttle &gt; 0 加速，&lt; 0 反推（空中刹车），范围会被夹到 [-1, 1]。
        /// </summary>
        public static Vec2 ThrustForce(in FlightSpec spec, Vec2 heading, float throttle)
        {
            float t = MathUtil.Clamp(throttle, -1f, 1f);
            Vec2 dir = heading.Normalized;
            return dir * (spec.Thrust * t);
        }

        /// <summary>把速度截断到极速以内（只削超出上限的部分，保留方向）。</summary>
        public static Vec2 ClampVelocity(in FlightSpec spec, Vec2 velocity) =>
            velocity.ClampedTo(spec.MaxSpeed);

        /// <summary>
        /// 转向能力系数（0~1）：速度低于失速速度时按比例衰减。
        /// 这是"飞机"区别于"平移的小人"的关键一笔 —— 停住就该转不动。
        /// </summary>
        public static float TurnAuthority(in FlightSpec spec, float speed)
        {
            float stall = spec.StallSpeed > 1e-4f ? spec.StallSpeed : 1e-4f;
            float t = MathUtil.Clamp01(speed / stall);
            return MathUtil.Lerp(1f - spec.StallPenalty, 1f, t);
        }

        /// <summary>
        /// 目标角速度（弧度/秒，逆时针为正）。
        /// turnInput: +1 = 左转（逆时针），-1 = 右转（顺时针）。
        /// </summary>
        public static float TargetAngularSpeed(in FlightSpec spec, float turnInput, float speed)
        {
            float input = MathUtil.Clamp(turnInput, -1f, 1f);
            return input * spec.TurnRate * TurnAuthority(spec, speed);
        }

        /// <summary>朝目标角速度逼近一步（产生转向惯性）。</summary>
        public static float StepAngularSpeed(in FlightSpec spec, float currentAngularSpeed, float targetAngularSpeed, float dt) =>
            MathUtil.MoveTowards(currentAngularSpeed, targetAngularSpeed, spec.TurnAccel * dt);

        /// <summary>按角速度把机头方向转一步，返回单位向量。</summary>
        public static Vec2 StepHeading(Vec2 heading, float angularSpeed, float dt)
        {
            Vec2 rotated = heading.Rotated(angularSpeed * dt);
            return rotated.SqrMagnitude > 1e-8f ? rotated.Normalized : heading;
        }

        /// <summary>
        /// 极速下的最小转弯半径（单位）= v / ω。用于调参时判断"能不能绕回来"。
        /// 角速度为 0 时返回 float.PositiveInfinity。
        /// </summary>
        public static float TurnRadius(float speed, float angularSpeed)
        {
            float w = MathUtil.Abs(angularSpeed);
            return w < 1e-4f ? float.PositiveInfinity : speed / w;
        }
    }
}
