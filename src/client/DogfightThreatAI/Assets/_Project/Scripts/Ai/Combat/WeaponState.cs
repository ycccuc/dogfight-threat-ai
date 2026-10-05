namespace Dogfight.Ai
{
    /// <summary>
    /// 一把武器的**开火节奏状态**（冷却）。
    ///
    /// 为什么单独拎出来：冷却逻辑看着简单，但"冷却中点按要排队"、
    /// "HUD 要画冷却进度"这类需求会让它反复被改。做成纯结构体之后，
    /// 它的行为能被单测钉死，HUD 只要读 CooldownProgress 就行。
    ///
    /// 时间用"对局已进行的秒数"（BtContext.ElapsedSeconds 或 MatchLoop.ElapsedSeconds），
    /// 不用 Time.time —— 这样批量对战快进时也是对的。
    /// </summary>
    public struct WeaponState
    {
        float _nextFireAt;

        /// <summary>下一次可开火的时间点（对局秒数）。0 表示一开火就能打。</summary>
        public float NextFireAt => _nextFireAt;

        public bool CanFire(float now) => now >= _nextFireAt;

        /// <summary>开火并进入冷却。</summary>
        public void MarkFired(float now, in WeaponSpec spec)
        {
            float cooldown = spec.Cooldown > 0f ? spec.Cooldown : 0f;
            _nextFireAt = now + cooldown;
        }

        public float CooldownRemaining(float now)
        {
            float remaining = _nextFireAt - now;
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>冷却进度 0~1（1 = 已就绪）。HUD 画进度条用它。</summary>
        public float CooldownProgress(float now, in WeaponSpec spec)
        {
            if (spec.Cooldown <= 0f) return 1f;
            float remaining = CooldownRemaining(now);
            if (remaining <= 0f) return 1f;
            return MathUtil.Clamp01(1f - remaining / spec.Cooldown);
        }

        /// <summary>
        /// 尝试开火：能开就进入冷却并返回 true。
        /// "冷却中点按排队"这类需求应该在调用方攒意图，而不是塞进这里 ——
        /// 这个结构体只回答"现在能不能打"。
        /// </summary>
        public bool TryFire(float now, in WeaponSpec spec)
        {
            if (!CanFire(now)) return false;
            MarkFired(now, spec);
            return true;
        }

        public void Reset() => _nextFireAt = 0f;

        /// <summary>存档 / 回放用。</summary>
        public void SetNextFireAt(float value) => _nextFireAt = value;
    }

    /// <summary>
    /// 开火解算：从瞄准点算出射向、加散布、算枪口位置。
    /// 纯函数 —— 同样的方向 + 同样的随机数状态 → 同样的弹道（批量对战可复现）。
    /// </summary>
    public static class Gunnery
    {
        /// <summary>由"开火者位置 → 瞄准点"求单位射向。瞄准点与自身重合时返回 fallback。</summary>
        public static Vec2 AimDirection(Vec2 muzzle, Vec2 aimPoint, Vec2 fallback)
        {
            Vec2 toAim = aimPoint - muzzle;
            if (toAim.SqrMagnitude < 1e-8f) return fallback.Normalized;
            return toAim.Normalized;
        }

        /// <summary>
        /// 在射向上叠加随机散布。spreadRadians 是**半角**（0 = 绝对精准）。
        /// 用 DeterministicRng 而不是 UnityEngine.Random，保证同种子可复现。
        /// </summary>
        public static Vec2 ApplySpread(Vec2 direction, float spreadRadians, ref DeterministicRng rng)
        {
            Vec2 dir = direction.Normalized;
            if (spreadRadians <= 1e-6f) return dir;

            float offset = rng.Range(-spreadRadians, spreadRadians);
            return dir.Rotated(offset).Normalized;
        }

        /// <summary>枪口位置：从机体中心沿射向前移一点，避免子弹从机身正中冒出来。</summary>
        public static Vec2 MuzzlePosition(Vec2 center, Vec2 direction, float muzzleOffset) =>
            center + direction.Normalized * muzzleOffset;

        /// <summary>
        /// 判断一次开火能打到什么（机炮的完整解算，一步到位）。
        /// 调用方只需提供世界快照与随机数状态。
        /// </summary>
        public static RayHit ResolveShot(
            Vec2 muzzle,
            Vec2 direction,
            in WeaponSpec spec,
            CircleTarget[] targets,
            int count,
            int excludeTeam,
            int shooterId,
            ref DeterministicRng rng)
        {
            Vec2 spread = ApplySpread(direction, spec.SpreadRadians, ref rng);
            return HitGeometry.RaycastNearest(muzzle, spread, spec.Range, targets, count, excludeTeam, shooterId);
        }
    }
}
