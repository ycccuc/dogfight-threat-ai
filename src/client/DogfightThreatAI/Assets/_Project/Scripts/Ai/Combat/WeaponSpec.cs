namespace Dogfight.Ai
{
    public enum WeaponKind
    {
        /// <summary>机炮：射线检测（hitscan），即时命中，无飞行时间。</summary>
        Cannon = 0,

        /// <summary>导弹：有飞行时间，会追踪目标，转向能力有限。</summary>
        Missile = 1,
    }

    /// <summary>
    /// 武器参数（纯数据）。由 Gameplay 层的 WeaponConfigSO 烘焙而来。
    /// 同样不加 [Serializable]，理由见 FlightSpec。
    /// </summary>
    public readonly struct WeaponSpec
    {
        public readonly WeaponKind Kind;

        /// <summary>单发基础伤害（距离 0 时）。</summary>
        public readonly float Damage;

        /// <summary>有效射程（单位）。超出后伤害线性衰减到 0，也是射线检测的最远距离。</summary>
        public readonly float Range;

        /// <summary>冷却时间（秒）。</summary>
        public readonly float Cooldown;

        /// <summary>弹速（单位/秒）。机炮是射线检测，此值被忽略。</summary>
        public readonly float ProjectileSpeed;

        /// <summary>导弹最大转向速率（弧度/秒）。非追踪武器忽略。</summary>
        public readonly float TurnRate;

        /// <summary>散布半角（弧度）。0 = 绝对精准。</summary>
        public readonly float SpreadRadians;

        /// <summary>单次开火发数（霰弹式机炮 &gt; 1）。</summary>
        public readonly int PelletsPerShot;

        /// <summary>全局伤害倍率（用于难度/道具加成）。</summary>
        public readonly float DamageMultiplier;

        public WeaponSpec(
            WeaponKind kind,
            float damage,
            float range,
            float cooldown,
            float projectileSpeed,
            float turnRate,
            float spreadRadians,
            int pelletsPerShot,
            float damageMultiplier)
        {
            Kind = kind;
            Damage = damage;
            Range = range;
            Cooldown = cooldown;
            ProjectileSpeed = projectileSpeed;
            TurnRate = turnRate;
            SpreadRadians = spreadRadians < 0f ? 0f : spreadRadians;
            PelletsPerShot = pelletsPerShot < 1 ? 1 : pelletsPerShot;
            DamageMultiplier = damageMultiplier <= 0f ? 1f : damageMultiplier;
        }

        /// <summary>机炮默认值：射程 12、冷却 0.15s、无散布。</summary>
        public static WeaponSpec DefaultCannon =>
            new WeaponSpec(
                kind: WeaponKind.Cannon,
                damage: 10f,
                range: 12f,
                cooldown: 0.15f,
                projectileSpeed: 0f,
                turnRate: 0f,
                spreadRadians: 0f,
                pelletsPerShot: 1,
                damageMultiplier: 1f);

        /// <summary>导弹默认值：射程 18、冷却 1.2s、弹速 8、可追踪。</summary>
        public static WeaponSpec DefaultMissile =>
            new WeaponSpec(
                kind: WeaponKind.Missile,
                damage: 35f,
                range: 18f,
                cooldown: 1.2f,
                projectileSpeed: 8f,
                turnRate: 120f * MathUtil.Deg2Rad,
                spreadRadians: 0f,
                pelletsPerShot: 1,
                damageMultiplier: 1f);

        /// <summary>每秒理论输出（DPS），用于平衡性对比与单测。</summary>
        public float TheoreticalDps => Cooldown <= 0f ? 0f : Damage * DamageMultiplier * PelletsPerShot / Cooldown;

        public override string ToString() =>
            Kind + "(dmg=" + Damage + ", range=" + Range + ", cd=" + Cooldown + "s)";
    }
}
