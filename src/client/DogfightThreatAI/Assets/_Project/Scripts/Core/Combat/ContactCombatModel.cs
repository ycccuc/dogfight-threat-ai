using System;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Combat
{
    /// <summary>
    /// 碰撞伤害的唯一权威：读取一个完整固定步的模拟轨迹，处理扫掠接触与无敌时间。
    /// 不依赖刚体回调，也不改写飞机的位置或速度。
    /// </summary>
    public sealed class ContactCombatModel : ICombatStatusView
    {
        readonly int _collisionDamage;
        readonly double _invulnerabilitySeconds;
        double _contactRadiusSquared;
        double _elapsedTime;
        double _invulnerabilityRemaining;
        int _highestAttackId;
        bool _attackConsumed;

        public CombatStatus Status { get; private set; }

        public ContactCombatModel(int maxHealth = 5, int collisionDamage = 1,
                                  float invulnerabilitySeconds = 2f, float contactRadius = 1f)
        {
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (collisionDamage <= 0) throw new ArgumentOutOfRangeException(nameof(collisionDamage));
            if (!IsFinite(invulnerabilitySeconds) || invulnerabilitySeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(invulnerabilitySeconds));
            if (!IsFinite(contactRadius) || contactRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(contactRadius));

            _collisionDamage = collisionDamage;
            _invulnerabilitySeconds = invulnerabilitySeconds;
            _contactRadiusSquared = (double)contactRadius * contactRadius;
            Status = new CombatStatus(maxHealth, maxHealth, 0, 0f, 0f);
        }

        public bool Step(in FlightState previousPlayer, in FlightState player,
                         in FlightState previousEnemy, in FlightState enemy, float deltaTime)
            => Step(previousPlayer, player, previousEnemy, enemy, deltaTime, AttackWindow.Inactive);

        public bool StepContact(in FlightState previousPlayer, in FlightState player,
                                in FlightState previousEnemy, in FlightState enemy, float deltaTime,
                                float combinedRadius)
            => StepContact(previousPlayer, player, previousEnemy, enemy, deltaTime,
                combinedRadius, _collisionDamage);

        /// <summary>Uses the current frozen action's damage without replacing health or protection state.</summary>
        public bool StepContact(in FlightState previousPlayer, in FlightState player,
                                in FlightState previousEnemy, in FlightState enemy, float deltaTime,
                                float combinedRadius, int collisionDamage)
        {
            if (collisionDamage <= 0 || !CanStep(previousPlayer, player, previousEnemy, enemy, deltaTime)) return false;
            if (!IsFinite(combinedRadius) || combinedRadius <= 0f)
                return Step(previousPlayer, player, previousEnemy, enemy, deltaTime);
            _contactRadiusSquared = (double)combinedRadius * combinedRadius;
            // Contact mode has no attack/phase permission: only geometry and the invulnerability clock matter.
            return Advance(previousPlayer, player, previousEnemy, enemy, deltaTime,
                0f, 1f, true, false, collisionDamage);
        }

        public bool Step(in FlightState previousPlayer, in FlightState player,
                         in FlightState previousEnemy, in FlightState enemy, float deltaTime,
                         in AttackWindow attack)
        {
            if (!CanStep(previousPlayer, player, previousEnemy, enemy, deltaTime)) return false;

            // 只有有效公开窗口才能登记新攻击；漏接、预警/恢复期或无效窗口均不会伤害。
            bool attackAllowed = IsValid(attack);
            if (attackAllowed && attack.AttackId > _highestAttackId)
            {
                _highestAttackId = attack.AttackId;
                _attackConsumed = false;
            }
            attackAllowed &= attack.AttackId == _highestAttackId && !_attackConsumed;

            return Advance(previousPlayer, player, previousEnemy, enemy, deltaTime,
                attack.StartFraction, attack.EndFraction, attackAllowed, true, _collisionDamage);
        }

        bool CanStep(in FlightState previousPlayer, in FlightState player,
                     in FlightState previousEnemy, in FlightState enemy, float deltaTime)
            => Status.IsPlayerAlive && IsFinite(deltaTime) && deltaTime > 0f &&
               IsFinite(previousPlayer) && IsFinite(player) && IsFinite(previousEnemy) && IsFinite(enemy);

        bool Advance(in FlightState previousPlayer, in FlightState player,
                     in FlightState previousEnemy, in FlightState enemy, float deltaTime,
                     float windowStart, float windowEnd, bool contactAllowed, bool consumeAttack,
                     int collisionDamage)
        {

            // 把公开伤害窗口与本步内无敌已结束的轨迹取交集，不能追溯早先的擦碰。
            double eligibleStart = System.Math.Max(windowStart, _invulnerabilityRemaining / deltaTime);
            double eligibleEnd = windowEnd;
            double hitFraction = 0d;
            bool hit = contactAllowed && eligibleStart <= eligibleEnd && TryFindContact(
                previousPlayer.Position, player.Position, previousEnemy.Position, enemy.Position,
                eligibleStart, eligibleEnd, out hitFraction);

            _elapsedTime += deltaTime;
            _invulnerabilityRemaining = System.Math.Max(0d, _invulnerabilityRemaining - deltaTime);
            int health = Status.PlayerHealth;
            int sequence = Status.HitSequence;
            if (hit)
            {
                if (consumeAttack) _attackConsumed = true;
                health = System.Math.Max(0, health - collisionDamage);
                sequence++;
                _invulnerabilityRemaining = System.Math.Max(0d,
                    _invulnerabilitySeconds - deltaTime * (1d - hitFraction));
            }

            Status = new CombatStatus(health, Status.MaxPlayerHealth, sequence,
                (float)_invulnerabilityRemaining, (float)System.Math.Min(float.MaxValue, _elapsedTime));
            return hit;
        }

        bool TryFindContact(Vec2 previousPlayer, Vec2 player, Vec2 previousEnemy, Vec2 enemy,
                            double eligibleStart, double eligibleEnd, out double contactFraction)
        {
            // 相对运动将“两条线段之间的同一时刻接触”变成一条线段到原点的距离。
            // 坐标差与平方都用 double，避免大但有限的 float 输入溢出。
            double x = (double)previousEnemy.X - previousPlayer.X;
            double y = (double)previousEnemy.Y - previousPlayer.Y;
            double dx = ((double)enemy.X - player.X) - x;
            double dy = ((double)enemy.Y - player.Y) - y;
            double startX = x + dx * eligibleStart;
            double startY = y + dy * eligibleStart;
            contactFraction = eligibleStart;
            if (startX * startX + startY * startY <= _contactRadiusSquared) return true;

            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0d) return false;
            double center = -(x * dx + y * dy) / lengthSquared;
            double closest = System.Math.Max(eligibleStart, System.Math.Min(eligibleEnd, center));
            double closestX = x + dx * closest;
            double closestY = y + dy * closest;
            if (closestX * closestX + closestY * closestY > _contactRadiusSquared) return false;

            double perpendicularX = x + dx * center;
            double perpendicularY = y + dy * center;
            double perpendicularSquared = perpendicularX * perpendicularX + perpendicularY * perpendicularY;
            double halfWidth = System.Math.Sqrt(System.Math.Max(0d,
                (_contactRadiusSquared - perpendicularSquared) / lengthSquared));
            contactFraction = System.Math.Max(eligibleStart, System.Math.Min(eligibleEnd, center - halfWidth));
            return true;
        }

        static bool IsValid(AttackWindow attack)
            => attack.IsActive && attack.AttackId > 0 && IsFinite(attack.StartFraction) &&
               IsFinite(attack.EndFraction) && attack.StartFraction >= 0f &&
               attack.StartFraction < attack.EndFraction && attack.EndFraction <= 1f;

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool IsFinite(Vec2 value) => IsFinite(value.X) && IsFinite(value.Y);
        static bool IsFinite(FlightState state)
            => IsFinite(state.Position) && IsFinite(state.Velocity) && IsFinite(state.Heading);
    }
}
