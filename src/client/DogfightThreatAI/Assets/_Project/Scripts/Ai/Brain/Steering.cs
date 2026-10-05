namespace Dogfight.Ai
{
    /// <summary>
    /// 把"想去哪"翻译成"推杆多少"。纯函数，所以能单测。
    ///
    /// 注意这里产出的是**输入**（-1~1），不是直接改姿态 ——
    /// AI 和玩家走的是同一条通路（IPlaneInput），所以 AI 也受失速、转向惯性、
    /// 速度上限的约束。AI 不会作弊，这一点对"胜率统计"的可信度是必要的。
    /// </summary>
    public static class Steering
    {
        /// <summary>
        /// 转向输入：+1 = 左转（逆时针），-1 = 右转。
        /// 角误差在 ±fullDeflectionRadians 内按比例给舵，超过就满舵。
        /// </summary>
        public static float TurnInputFor(Vec2 heading, Vec2 desiredDirection, float fullDeflectionRadians)
        {
            Vec2 desired = desiredDirection.Normalized;
            if (desired.SqrMagnitude < 0.5f) return 0f;

            Vec2 current = heading.Normalized;
            if (current.SqrMagnitude < 0.5f) return 0f;

            float error = Vec2.DeltaAngle(current.AngleRadians, desired.AngleRadians);
            float scale = fullDeflectionRadians < 1e-3f ? 1e-3f : fullDeflectionRadians;
            return MathUtil.Clamp(error / scale, -1f, 1f);
        }

        /// <summary>
        /// 到达式油门：远时给巡航油门，接近路点时收油，避免在路点附近来回冲过头。
        /// </summary>
        public static float ArriveThrottle(float distance, float arriveRadius, float cruiseThrottle)
        {
            float radius = arriveRadius < 0.05f ? 0.05f : arriveRadius;
            if (distance <= radius) return 0f;

            // 从 radius 到 radius*4 之间线性收油
            float slowBand = radius * 4f;
            if (distance >= slowBand) return cruiseThrottle;

            float t = (distance - radius) / (slowBand - radius);
            return cruiseThrottle * MathUtil.Clamp01(t);
        }

        /// <summary>
        /// 逃逸方向：背离威胁点。
        /// 位置重合时（贴脸）退化为"当前机头的侧向"，避免零向量导致 AI 呆住。
        /// </summary>
        public static Vec2 FleeDirection(Vec2 position, Vec2 threatPosition, Vec2 currentHeading)
        {
            Vec2 away = position - threatPosition;
            if (away.SqrMagnitude > 1e-4f) return away.Normalized;

            Vec2 heading = currentHeading.Normalized;
            if (heading.SqrMagnitude < 0.5f) return Vec2.Up;

            // 侧转 90°：向左或向右都行，取固定一侧保证可复现
            return heading.Rotated(MathUtil.Pi * 0.5f);
        }

        /// <summary>
        /// 追击/攻击时的瞄准方向：对高速目标取提前量，对射线武器直指当前位置。
        /// 只做一层封装，方便单测"该不该提前"。
        /// </summary>
        public static Vec2 AimDirection(Vec2 shooterPosition, in AgentSnapshot target, in WeaponSpec weapon)
        {
            float projectileSpeed = weapon.Kind == WeaponKind.Cannon ? 0f : weapon.ProjectileSpeed;
            return DamageMath.LeadAim(shooterPosition, target.Position, target.Velocity, projectileSpeed);
        }
    }
}
