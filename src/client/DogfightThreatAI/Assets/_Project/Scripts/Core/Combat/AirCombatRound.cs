using System;
using System.Collections.Generic;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Combat
{
    public readonly struct AircraftCombatFrame
    {
        public readonly FlightState Previous;
        public readonly FlightState Current;
        public readonly float Radius;
        public readonly bool Fire;

        public AircraftCombatFrame(FlightState previous, FlightState current, float radius, bool fire)
        {
            Previous = previous;
            Current = current;
            Radius = radius;
            Fire = fire;
        }
    }

    public readonly struct AircraftCollision
    {
        public readonly int FirstSeat;
        public readonly int SecondSeat;
        public readonly Vec2 Position;

        public AircraftCollision(int firstSeat, int secondSeat, Vec2 position)
        {
            FirstSeat = firstSeat;
            SecondSeat = secondSeat;
            Position = position;
        }
    }

    /// <summary>一场空战的武器和生命规则。席位从 1 开始，生命为零的飞机不再参与结算。</summary>
    public sealed class AirCombatRound
    {
        readonly WeaponSpec weapon;
        readonly float protection;
        readonly int[] health;
        readonly int[] factions;
        readonly float[] cooldown;
        readonly float[] invulnerability;
        readonly Vec2[] impactDirections;
        readonly bool[,] overlapping;
        readonly List<AircraftCollision> collisions = new List<AircraftCollision>();
        readonly List<ProjectileState> shots;
        readonly List<ProjectileState> fired;
        readonly List<int> removed;
        int nextShotId;

        public IReadOnlyList<ProjectileState> Fired => fired;
        public IReadOnlyList<int> Removed => removed;
        public IReadOnlyList<ProjectileState> Projectiles => shots;
        public IReadOnlyList<AircraftCollision> Collisions => collisions;
        public int ProjectileCapacity { get; }
        public int AliveCount { get; private set; }

        public AirCombatRound(WeaponSpec weapon, int seats, int maxHealth, float protection)
            : this(weapon, seats, maxHealth, protection, null, null)
        {
        }

        // 空战任务可为每个席位配置初始生命与阵营。自由对战沿用等生命、无队伍规则。
        public AirCombatRound(WeaponSpec weapon, int seats, int maxHealth, float protection,
            int[] initialHealth, int[] factions, bool aircraftCollisions = false)
        {
            if (seats < 2 || maxHealth < 1 || protection < 0f || !Finite(protection) ||
                weapon.ShotInterval <= 0f)
            {
                throw new ArgumentException("Invalid round configuration.");
            }

            this.weapon = weapon;
            this.protection = protection;
            health = new int[seats];
            cooldown = new float[seats];
            invulnerability = new float[seats];
            impactDirections = new Vec2[seats];
            if ((initialHealth != null && initialHealth.Length != seats) ||
                (factions != null && factions.Length != seats))
            {
                throw new ArgumentException("Seat settings do not match seat count.");
            }
            this.factions = factions != null ? (int[])factions.Clone() : null;
            overlapping = aircraftCollisions ? new bool[seats, seats] : null;
            for (int i = 0; i < seats; i++)
            {
                health[i] = initialHealth != null ? initialHealth[i] : maxHealth;
                if (health[i] < 1)
                {
                    throw new ArgumentException("Initial health must be positive.", nameof(initialHealth));
                }
            }
            AliveCount = seats;

            ProjectileCapacity = checked(seats * ((int)MathF.Ceiling(weapon.Lifetime / weapon.ShotInterval) + 2));
            shots = new List<ProjectileState>(ProjectileCapacity);
            fired = new List<ProjectileState>(seats);
            removed = new List<int>(ProjectileCapacity);
        }

        public int Health(int seat) => health[seat - 1];
        public float Invulnerability(int seat) => invulnerability[seat - 1];
        public float Cooldown01(int seat) => cooldown[seat - 1] / weapon.ShotInterval;
        public Vec2 ImpactDirection(int seat) => impactDirections[seat - 1];

        public void Forfeit(int seat)
        {
            if (health[seat - 1] > 0)
            {
                health[seat - 1] = 0;
                AliveCount--;
            }
        }

        // 淘汰或超时都使用剩余生命值决定名次。并列第一时为平局。
        public int WinnerByHealth()
        {
            int winner = 0;
            int highest = 0;
            for (int i = 0; i < health.Length; i++)
            {
                if (health[i] > highest)
                {
                    winner = i + 1;
                    highest = health[i];
                }
                else if (health[i] == highest)
                {
                    winner = 0;
                }
            }
            return winner;
        }

        public void Tick(float dt, AircraftCombatFrame[] aircraft)
        {
            if (dt <= 0f || !Finite(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt));
            }
            if (aircraft == null || aircraft.Length != health.Length)
            {
                throw new ArgumentException("One frame per seat is required.", nameof(aircraft));
            }

            fired.Clear();
            removed.Clear();
            collisions.Clear();
            Array.Clear(impactDirections, 0, impactDirections.Length);
            for (int i = 0; i < health.Length; i++)
            {
                cooldown[i] = MathF.Max(0, cooldown[i] - dt);
                invulnerability[i] = MathF.Max(0, invulnerability[i] - dt);
            }

            if (overlapping != null)
            {
                ResolveAircraftCollisions(aircraft);
            }

            // 命中判定使用飞机和弹丸的相对运动，避免高速弹丸穿透。
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var shot = shots[i];
                float travel = MathF.Min(dt, shot.Remaining);
                Vec2 start = shot.Position;
                shot.PreviousPosition = start;
                shot.Position += shot.Velocity * travel;
                shot.Remaining -= dt;
                int victim = FindVictim(shot.OwnerId, start, shot.Position, travel / dt, aircraft);
                if (victim >= 0 && invulnerability[victim] <= 0f)
                {
                    Damage(victim, -shot.Velocity.Normalized);
                }

                if (victim >= 0 || shot.Remaining <= 0f)
                {
                    removed.Add(shot.Id);
                    shots.RemoveAt(i);
                }
                else
                {
                    shots[i] = shot;
                }
            }

            if (factions == null && AliveCount <= 1)
            {
                return;
            }
            for (int i = 0; i < health.Length; i++)
            {
                if (health[i] > 0 && aircraft[i].Fire && cooldown[i] <= .00001f && shots.Count < ProjectileCapacity)
                {
                    var shot = ProjectileBallistics.Launch(weapon, ++nextShotId, i + 1, aircraft[i].Current);
                    shots.Add(shot);
                    fired.Add(shot);
                    cooldown[i] = weapon.ShotInterval;
                }
            }
        }

        void ResolveAircraftCollisions(AircraftCombatFrame[] aircraft)
        {
            for (int first = 0; first < aircraft.Length - 1; first++)
            {
                for (int second = first + 1; second < aircraft.Length; second++)
                {
                    if (health[first] == 0 || health[second] == 0 ||
                        (factions != null && factions[first] == factions[second]))
                    {
                        continue;
                    }

                    var a = aircraft[first];
                    var b = aircraft[second];
                    Vec2 previous = a.Previous.Position - b.Previous.Position;
                    Vec2 current = a.Current.Position - b.Current.Position;
                    float radius = a.Radius + b.Radius;
                    bool newContact = !overlapping[first, second];
                    overlapping[first, second] = current.SqrMagnitude <= radius * radius;
                    if (!newContact || invulnerability[first] > 0f || invulnerability[second] > 0f ||
                        !ProjectileBallistics.TryContact(previous, current, radius, out float fraction))
                    {
                        continue;
                    }

                    Vec2 pointA = a.Previous.Position + (a.Current.Position - a.Previous.Position) * fraction;
                    Vec2 pointB = b.Previous.Position + (b.Current.Position - b.Previous.Position) * fraction;
                    Vec2 direction = (pointB - pointA).Normalized;
                    Damage(first, direction);
                    Damage(second, -direction);
                    collisions.Add(new AircraftCollision(first + 1, second + 1, (pointA + pointB) * .5f));
                }
            }
        }

        void Damage(int index, Vec2 direction)
        {
            health[index]--;
            impactDirections[index] = direction;
            invulnerability[index] = protection;
            if (health[index] == 0)
            {
                AliveCount--;
            }
        }

        int FindVictim(int owner, Vec2 start, Vec2 end, float stepFraction, AircraftCombatFrame[] aircraft)
        {
            int victim = -1;
            float firstContact = float.PositiveInfinity;
            for (int i = 0; i < health.Length; i++)
            {
                if (i + 1 == owner || health[i] <= 0 ||
                    (factions != null && factions[i] == factions[owner - 1]))
                {
                    continue;
                }
                var frame = aircraft[i];
                Vec2 previous = frame.Previous.Position;
                Vec2 targetEnd = previous + (frame.Current.Position - previous) * stepFraction;
                if (ProjectileBallistics.TryContact(start - previous, end - targetEnd,
                    frame.Radius + weapon.Radius, out float contact) && contact < firstContact)
                {
                    firstContact = contact;
                    victim = i;
                }
            }
            return victim;
        }

        static bool Finite(float value) => !float.IsInfinity(value) && !float.IsNaN(value);
    }
}
