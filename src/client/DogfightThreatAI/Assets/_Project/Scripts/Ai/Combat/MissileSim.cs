namespace Dogfight.Ai
{
    /// <summary>一发导弹的运行状态（值类型，便于批量对战里放进扁平数组）。</summary>
    public struct MissileState
    {
        public Vec2 Position;
        public Vec2 Velocity;

        /// <summary>已飞行时间（秒）。</summary>
        public float Age;

        /// <summary>已飞行距离（世界单位），用来判超程。</summary>
        public float DistanceTravelled;

        /// <summary>锁定目标 id；-1 表示无目标（直飞）。</summary>
        public int TargetId;

        public bool Alive;

        public Vec2 Direction => Velocity.Normalized;

        public static MissileState Launch(Vec2 position, Vec2 direction, float speed, int targetId)
        {
            Vec2 dir = direction.Normalized;
            return new MissileState
            {
                Position = position,
                Velocity = dir * (speed > 0f ? speed : 0f),
                Age = 0f,
                DistanceTravelled = 0f,
                TargetId = targetId,
                Alive = true,
            };
        }
    }

    public enum MissileStepOutcome
    {
        Flying = 0,
        Hit = 1,
        Expired = 2,
    }

    /// <summary>
    /// 导弹飞行与追踪的**纯模拟**。
    ///
    /// 与机炮（射线检测、瞬时命中）不同，导弹有飞行时间、会追踪、有寿命与射程限制，
    /// 所以它是"每帧推进一点"的东西。把它放在 Ai 层意味着：
    ///   - 转向限制、近炸引信、超程失效这些规则全部可单测
    ///   - 批量对战里几百发导弹的推进不依赖引擎物理，快进是安全的
    ///
    /// Unity 那边只负责把 Position/Direction 画出来。
    /// </summary>
    public static class MissileSim
    {
        /// <summary>默认寿命（秒）。发射后这么久没命中就自毁。</summary>
        public const float DefaultLifetimeSeconds = 5f;

        /// <summary>近炸引信半径（世界单位）。导弹不必正中目标，擦过也算。</summary>
        public const float DefaultFuseRadius = 0.45f;

        /// <summary>
        /// 推进一步。
        /// 顺序很重要：**先转向、再前进、最后判定命中** ——
        /// 命中判定用的是这一帧走过的线段，所以高速导弹不会"穿过"目标而不触发。
        /// </summary>
        /// <param name="targetRadius">目标碰撞半径，与引信半径相加得到实际判定阈值</param>
        public static MissileStepOutcome Step(
            ref MissileState missile,
            in WeaponSpec spec,
            Vec2 targetPosition,
            bool targetAlive,
            float targetRadius,
            float dt,
            float fuseRadius = DefaultFuseRadius,
            float lifetimeSeconds = DefaultLifetimeSeconds)
        {
            if (!missile.Alive) return MissileStepOutcome.Expired;

            missile.Age += dt;
            if (missile.Age >= lifetimeSeconds)
            {
                missile.Alive = false;
                return MissileStepOutcome.Expired;
            }

            float speed = spec.ProjectileSpeed > 0f ? spec.ProjectileSpeed : missile.Velocity.Magnitude;

            // ── 1. 转向：受最大转向速率限制（所以导弹可以被甩掉）──
            Vec2 direction = missile.Direction;
            if (targetAlive && spec.TurnRate > 0f)
            {
                Vec2 toTarget = targetPosition - missile.Position;
                direction = DamageMath.SteerTowards(direction, toTarget, spec.TurnRate * dt);
            }

            missile.Velocity = direction * speed;

            // ── 2. 前进 ──
            Vec2 previousPosition = missile.Position;
            missile.Position = missile.Position + missile.Velocity * dt;
            missile.DistanceTravelled += speed * dt;

            // ── 3. 命中：用本帧线段做近炸判定，避免高速穿透 ──
            if (targetAlive)
            {
                float threshold = fuseRadius + (targetRadius > 0f ? targetRadius : 0f);
                float distance = HitGeometry.DistancePointToSegment(targetPosition, previousPosition, missile.Position);
                if (distance <= threshold)
                {
                    missile.Position = HitGeometry.ClosestPointOnSegment(targetPosition, previousPosition, missile.Position);
                    missile.Alive = false;
                    return MissileStepOutcome.Hit;
                }
            }

            // ── 4. 超程自毁 ──
            if (spec.Range > 0f && missile.DistanceTravelled >= spec.Range)
            {
                missile.Alive = false;
                return MissileStepOutcome.Expired;
            }

            return MissileStepOutcome.Flying;
        }
    }
}
