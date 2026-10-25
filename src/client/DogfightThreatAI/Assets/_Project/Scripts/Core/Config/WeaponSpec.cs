using System;

namespace DogfightThreatAI.Core.Config
{
    /// <summary>不可变武器配置；双方共用同一套规则。</summary>
    public readonly struct WeaponSpec
    {
        public readonly string Id;
        public readonly float ShotInterval, ProjectileSpeed, Lifetime, Radius, MuzzleOffset;
        public WeaponSpec(string id, float shotInterval, float projectileSpeed, float lifetime, float radius, float muzzleOffset)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException(nameof(id));
            if (!Positive(shotInterval) || !Positive(projectileSpeed) || !Positive(lifetime) ||
                !Positive(radius) || !Positive(muzzleOffset)) throw new ArgumentOutOfRangeException(nameof(shotInterval));
            Id = id; ShotInterval = shotInterval; ProjectileSpeed = projectileSpeed;
            Lifetime = lifetime; Radius = radius; MuzzleOffset = muzzleOffset;
        }
        static bool Positive(float value) => value > 0f && !float.IsInfinity(value);
    }
}
