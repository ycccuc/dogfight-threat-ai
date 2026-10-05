namespace Dogfight.Ai
{
    /// <summary>
    /// 命中与伤害的**纯公式**。全静态、无状态、无 Unity 依赖 —— 因此可以直接单测。
    ///
    /// 把这些公式独立出来不只是为了好看：数值平衡是改得最频繁的地方，
    /// 有了单测就能保证"改了衰减曲线不会顺手把护甲公式改坏"。
    /// </summary>
    public static class DamageMath
    {
        /// <summary>
        /// 距离衰减：距离 0 时全额，距离 ≥ range 时为 0，中间线性插值。
        /// range ≤ 0 视为无衰减。
        /// </summary>
        public static float WithFalloff(float baseDamage, float range, float distance)
        {
            if (range <= 0f) return baseDamage;
            if (distance <= 0f) return baseDamage;
            float t = MathUtil.Clamp01(1f - distance / range);
            return baseDamage * t;
        }

        /// <summary>是否在射程内（含边界）。</summary>
        public static bool InRange(float range, float distance) => distance >= 0f && distance <= range;

        /// <summary>护甲减免：armor 为 0~1 的比例，夹紧后按比例减伤。</summary>
        public static float ThroughArmor(float damage, float armor) =>
            damage * (1f - MathUtil.Clamp01(armor));

        /// <summary>
        /// 一发子弹在给定距离上的最终伤害。
        /// 射程外返回 0（而不是负数），这样调用方不需要额外判断。
        /// </summary>
        public static float ForShot(in WeaponSpec weapon, float distance, float armor = 0f)
        {
            if (distance < 0f || distance > weapon.Range) return 0f;
            float afterFalloff = WithFalloff(weapon.Damage, weapon.Range, distance);
            return ThroughArmor(afterFalloff * weapon.DamageMultiplier, armor);
        }

        /// <summary>一轮齐射（含多弹丸）在给定距离上的总伤害。</summary>
        public static float ForVolley(in WeaponSpec weapon, float distance, float armor = 0f) =>
            ForShot(weapon, distance, armor) * weapon.PelletsPerShot;

        /// <summary>
        /// 打掉 hp 需要几发。伤害为 0（射程外）时返回 int.MaxValue。
        /// 用于平衡性估算与单测断言。
        /// </summary>
        public static int ShotsToKill(in WeaponSpec weapon, float hp, float distance, float armor = 0f)
        {
            float perShot = ForShot(weapon, distance, armor);
            if (perShot <= 0f) return int.MaxValue;
            if (hp <= 0f) return 0;
            return (int)System.Math.Ceiling(hp / perShot);
        }

        /// <summary>
        /// 追踪转向：把 currentDir 朝 toTarget 转，单帧最多转 maxTurnRadians。
        /// 返回新的单位方向（目标方向为零向量时原样返回）。
        /// </summary>
        public static Vec2 SteerTowards(Vec2 currentDir, Vec2 toTarget, float maxTurnRadians)
        {
            if (toTarget.SqrMagnitude < 1e-8f) return currentDir.Normalized;
            float current = currentDir.AngleRadians;
            float desired = toTarget.AngleRadians;
            float delta = Vec2.DeltaAngle(current, desired);
            float step = MathUtil.Clamp(delta, -maxTurnRadians, maxTurnRadians);
            return Vec2.FromAngle(current + step);
        }

        /// <summary>
        /// 提前量瞄准：目标以 targetVelocity 匀速运动，弹速为 projectileSpeed 时，
        /// 应该朝哪个方向开火。弹速 ≤ 0（射线检测）或无法解出时返回目标当前位置方向。
        /// </summary>
        public static Vec2 LeadAim(Vec2 shooterPos, Vec2 targetPos, Vec2 targetVelocity, float projectileSpeed)
        {
            Vec2 toTarget = targetPos - shooterPos;
            if (projectileSpeed <= 0f) return toTarget.Normalized;

            float a = Vec2.Dot(targetVelocity, targetVelocity) - projectileSpeed * projectileSpeed;
            float b = 2f * Vec2.Dot(toTarget, targetVelocity);
            float c = Vec2.Dot(toTarget, toTarget);

            if (MathUtil.Abs(a) < 1e-6f)
            {
                // 退化为一次方程
                if (MathUtil.Abs(b) < 1e-6f) return toTarget.Normalized;
                float t0 = -c / b;
                return t0 > 0f ? (toTarget + targetVelocity * t0).Normalized : toTarget.Normalized;
            }

            float disc = b * b - 4f * a * c;
            if (disc < 0f) return toTarget.Normalized;

            float sq = MathUtil.Sqrt(disc);
            float t1 = (-b + sq) / (2f * a);
            float t2 = (-b - sq) / (2f * a);
            float t = float.MaxValue;
            if (t1 > 0f) t = t1;
            if (t2 > 0f && t2 < t) t = t2;
            if (t == float.MaxValue) return toTarget.Normalized;

            return (toTarget + targetVelocity * t).Normalized;
        }
    }
}
