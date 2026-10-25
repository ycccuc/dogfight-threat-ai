using System;
using System.Collections.Generic;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Combat
{
    /// <summary>
    /// 仅持有武器、弹丸和生命状态。输入是飞行快照及开火意图，输出是快照和事件。
    /// 弹丸在相对运动空间扫掠检测，不依赖 Unity 碰撞，不以飞机接触扣血。
    /// </summary>
    public sealed class ProjectileCombatSimulation : ICombatStatusView
    {
        readonly WeaponSpec _weapon;
        readonly int _maxHealth;
        readonly float _protection;
        readonly List<ProjectileState> _projectiles;
        readonly List<WeaponEvent> _events;
        readonly int[] _health = new int[2];
        readonly float[] _cooldown = new float[2], _invulnerability = new float[2];
        readonly int _capacity;
        int _nextId, _hits;
        float _elapsed;
        public CombatStatus Status => new CombatStatus(_health[0], _maxHealth, _hits, _invulnerability[0], _elapsed);
        public int EnemyHealth => _health[1];
        public bool IsFinished => _health[0] <= 0 || _health[1] <= 0;
        public IReadOnlyList<ProjectileState> Projectiles => _projectiles;
        public IReadOnlyList<WeaponEvent> Events => _events;
        public float PlayerCooldown01 => _cooldown[0] / _weapon.ShotInterval;

        public ProjectileCombatSimulation(WeaponSpec weapon, int maxHealth, float invulnerabilitySeconds)
        {
            if (maxHealth < 1 || invulnerabilitySeconds < 0 || float.IsNaN(invulnerabilitySeconds) ||
                float.IsInfinity(invulnerabilitySeconds)) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (weapon.ShotInterval <= 0f) throw new ArgumentException("WeaponSpec must be initialized.", nameof(weapon));
            _weapon = weapon; _maxHealth = maxHealth; _protection = invulnerabilitySeconds;
            _health[0] = _health[1] = maxHealth;
            _capacity = checked(2 * ((int)MathF.Ceiling(weapon.Lifetime / weapon.ShotInterval) + 2));
            _projectiles = new List<ProjectileState>(_capacity);
            _events = new List<WeaponEvent>(_capacity * 2 + 4);
        }

        public void Tick(float dt, in FlightState playerPrevious, in FlightState player,
            in FlightState enemyPrevious, in FlightState enemy, float playerRadius, float enemyRadius,
            bool playerFire, bool enemyFire)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
            _events.Clear();
            if (IsFinished) return;
            _elapsed += dt;
            for (int i = 0; i < 2; i++)
            {
                _cooldown[i] = MathF.Max(0, _cooldown[i] - dt);
                _invulnerability[i] = MathF.Max(0, _invulnerability[i] - dt);
            }
            // 已有弹丸先推进；本步发射的弹丸从本步末的位置出生，下步才参与移动。
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var shot = _projectiles[i];
                float travelTime = MathF.Min(dt, shot.Remaining);
                shot.PreviousPosition = shot.Position;
                shot.Position += shot.Velocity * travelTime;
                shot.Remaining -= dt;
                int victim = shot.OwnerId == 1 ? 1 : 0;
                var previous = victim == 0 ? playerPrevious : enemyPrevious;
                var current = victim == 0 ? player : enemy;
                Vec2 targetEnd = previous.Position + (current.Position - previous.Position) * (travelTime / dt);
                float radius = (victim == 0 ? playerRadius : enemyRadius) + _weapon.Radius;
                bool hit = SweptHit(shot.PreviousPosition - previous.Position, shot.Position - targetEnd, radius);
                if (hit)
                {
                    // 保护期间仍消耗弹丸，不能积攒多发并在保护结束时重复命中。
                    if (_health[victim] > 0 && _invulnerability[victim] <= 0)
                    {
                        _health[victim]--;
                        _invulnerability[victim] = _protection;
                        if (victim == 0) _hits++;
                        _events.Add(new WeaponEvent(WeaponEventKind.Hit, victim + 1, current.Position));
                        if (_health[victim] == 0) _events.Add(new WeaponEvent(WeaponEventKind.Destroyed, victim + 1, current.Position));
                    }
                    _projectiles.RemoveAt(i);
                }
                else if (shot.Remaining <= 0) _projectiles.RemoveAt(i);
                else _projectiles[i] = shot;
            }
            if (IsFinished) { _projectiles.Clear(); return; }
            Fire(0, player, playerFire);
            Fire(1, enemy, enemyFire);
        }

        void Fire(int index, in FlightState aircraft, bool intent)
        {
            if (!intent || _cooldown[index] > .00001f || _projectiles.Count >= _capacity) return;
            _cooldown[index] = _weapon.ShotInterval;
            Vec2 position = aircraft.Position + aircraft.Forward * _weapon.MuzzleOffset;
            _projectiles.Add(new ProjectileState { Id = ++_nextId, OwnerId = index + 1,
                Position = position, PreviousPosition = position,
                Velocity = aircraft.Forward * _weapon.ProjectileSpeed, Remaining = _weapon.Lifetime });
            _events.Add(new WeaponEvent(WeaponEventKind.Fired, index + 1, position));
        }

        static bool SweptHit(Vec2 from, Vec2 to, float radius)
        {
            Vec2 delta = to - from;
            float t = delta.SqrMagnitude > .000001f ? -Vec2.Dot(from, delta) / delta.SqrMagnitude : 0f;
            t = MathF.Max(0, MathF.Min(1, t));
            return (from + delta * t).SqrMagnitude <= radius * radius;
        }
    }
}
