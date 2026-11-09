using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Combat
{
    public enum MissionOutcome
    {
        Active,
        Victory,
        Defeat
    }

    public readonly struct PatrolSector
    {
        public readonly Vec2 Center;
        public readonly Vec2 HalfSize;
        public readonly int EnemyCount;

        public PatrolSector(Vec2 center, Vec2 halfSize, int enemyCount)
        {
            if (halfSize.X < 4f || halfSize.Y < 4f || enemyCount < 1 || enemyCount > 4)
            {
                throw new ArgumentException("Patrol needs a usable sector and one to four aircraft.");
            }
            Center = center;
            HalfSize = halfSize;
            EnemyCount = enemyCount;
        }
    }

    /// <summary>独立关卡实例。调用方提供玩家输入后的飞行快照，任务负责敌机、机炮与结算。</summary>
    public sealed class PatrolMission
    {
        public const int PlayerSeat = 1;
        const float EnemyRadius = .42f;
        const float HearingRange = 14f;
        public const float PickupRadius = 1.4f;

        readonly PatrolSector[] sectors;
        readonly int[] sectorIndices;
        readonly PatrolPilot[] pilots;
        readonly FlightModel enemyFlight;
        readonly AircraftCombatFrame[] frames;
        readonly int[] healthBefore;
        readonly bool[] tookHit;
        readonly Vec2[] pickups;
        readonly int[] collectedBy;
        readonly FlightState?[] targets;

        public AirCombatRound Combat { get; }
        public MissionOutcome Outcome { get; private set; }
        public float Elapsed { get; private set; }
        public int EnemyCount => pilots.Length;
        public int SectorCount => sectors.Length;
        public int InitialPlayerHealth { get; }
        public int CollectedCount { get; private set; }
        public int PlayerCount => targets.Length;
        public int PlayersRemaining
        {
            get
            {
                int alive = 0;
                for (int i = 1; i <= PlayerCount; i++)
                {
                    if (Combat.Health(i) > 0)
                    {
                        alive++;
                    }
                }
                return alive;
            }
        }
        public int EnemiesRemaining => Combat.AliveCount - PlayersRemaining;

        public PatrolMission(Bounds2D arena, WeaponSpec weapon, AircraftSpec enemyAircraft,
            PatrolSector[] sectors, int playerHealth, int enemyHealth, float sightRange, float sightHalfAngle,
            Vec2[] pickups = null, int playerCount = 1)
        {
            if (sectors == null || sectors.Length == 0)
            {
                throw new ArgumentException("At least one patrol sector is required.", nameof(sectors));
            }
            if (playerCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(playerCount));
            }
            targets = new FlightState?[playerCount];
            this.sectors = (PatrolSector[])sectors.Clone();
            this.pickups = pickups != null ? (Vec2[])pickups.Clone() : Array.Empty<Vec2>();
            collectedBy = new int[this.pickups.Length];
            InitialPlayerHealth = playerHealth;
            int enemies = 0;
            foreach (var sector in this.sectors)
            {
                if (sector.EnemyCount < 1 || !arena.Contains(sector.Center - sector.HalfSize) ||
                    !arena.Contains(sector.Center + sector.HalfSize))
                {
                    throw new ArgumentException("Patrol sector must be inside the mission bounds.", nameof(sectors));
                }
                enemies += sector.EnemyCount;
            }

            pilots = new PatrolPilot[enemies];
            sectorIndices = new int[enemies];
            frames = new AircraftCombatFrame[enemies + playerCount];
            healthBefore = new int[enemies];
            tookHit = new bool[enemies];
            enemyFlight = FlightModel.From(enemyAircraft, arena);
            int[] factions = new int[frames.Length];
            int[] health = new int[frames.Length];
            for (int i = 0; i < playerCount; i++)
            {
                health[i] = playerHealth;
            }
            int index = 0;
            for (int sectorIndex = 0; sectorIndex < this.sectors.Length; sectorIndex++)
            {
                var sector = this.sectors[sectorIndex];
                for (int member = 0; member < sector.EnemyCount; member++, index++)
                {
                    Vec2[] route = MakeRoute(sector, member);
                    var spawn = FlightState.At(route[0], (route[1] - route[0]).AngleRad);
                    frames[index + playerCount] = new AircraftCombatFrame(spawn, spawn, EnemyRadius, false);
                    pilots[index] = new PatrolPilot(arena, sector.Center, route,
                        member % 2 == 0 ? -1 : 1, enemyAircraft, weapon, sightRange, sightHalfAngle, playerCount);
                    sectorIndices[index] = sectorIndex;
                    factions[index + playerCount] = 1;
                    health[index + playerCount] = enemyHealth;
                }
            }
            Combat = new AirCombatRound(weapon, frames.Length, playerHealth, .75f, health, factions,
                aircraftCollisions: true);
        }

        static Vec2[] MakeRoute(in PatrolSector sector, int member)
        {
            const int points = 8;
            var route = new Vec2[points];
            float radius = member % 2 == 0 ? .78f : .57f;
            for (int i = 0; i < route.Length; i++)
            {
                float angle = 2f * MathF.PI * (i / (float)points + member / (float)sector.EnemyCount);
                route[i] = sector.Center + new Vec2(MathF.Cos(angle) * sector.HalfSize.X,
                    MathF.Sin(angle) * sector.HalfSize.Y) * radius;
            }
            return route;
        }

        public AircraftCombatFrame EnemyFrame(int index) => frames[index + PlayerCount];
        public int EnemyHealth(int index) => Combat.Health(index + PlayerCount + 1);
        public PatrolBehavior EnemyBehavior(int index) => pilots[index].Behavior;
        public PatrolContact EnemyContact(int index) => pilots[index].Perception.Contact;
        public PatrolContact EnemyContactFor(int index, int seat) => pilots[index].ContactFor(seat - 1);
        public bool IsCollected(int index) => CollectedBy(index) != 0;
        public int CollectedBy(int index) => collectedBy[index];

        public int SectorRemaining(int index)
        {
            int count = 0;
            for (int i = 0; i < EnemyCount; i++)
            {
                if (sectorIndices[i] == index && EnemyHealth(i) > 0)
                {
                    count++;
                }
            }
            return count;
        }

        public void Tick(float dt, in AircraftCombatFrame player)
        {
            if (PlayerCount != 1)
            {
                throw new InvalidOperationException("Use TickTeam for a squad mission.");
            }
            frames[0] = player;
            Advance(dt);
        }

        public void TickTeam(float dt, ReadOnlySpan<AircraftCombatFrame> players)
        {
            if (players.Length != PlayerCount)
            {
                throw new ArgumentException("One frame per player seat is required.", nameof(players));
            }
            players.CopyTo(frames.AsSpan(0, PlayerCount));
            Advance(dt);
        }

        void Advance(float dt)
        {
            if (Outcome != MissionOutcome.Active)
            {
                return;
            }
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt));
            }

            for (int i = 0; i < PlayerCount; i++)
            {
                targets[i] = Combat.Health(i + 1) > 0 ? frames[i].Current : (FlightState?)null;
            }
            Elapsed += dt;
            for (int i = 0; i < EnemyCount; i++)
            {
                int hp = EnemyHealth(i);
                healthBefore[i] = hp;
                if (hp <= 0)
                {
                    continue;
                }

                var pilot = pilots[i];
                int frame = i + PlayerCount;
                var previous = frames[frame].Current;
                var next = previous;
                var input = pilot.TickTeam(previous, targets, hp, tookHit[i], dt);
                enemyFlight.Step(ref next, input, dt, pilot.Motion);
                frames[frame] = new AircraftCombatFrame(previous, next, EnemyRadius, pilot.WantsFire);
            }

            Combat.Tick(dt, frames);
            for (int i = 0; i < PlayerCount; i++)
            {
                if (Combat.Health(i + 1) > 0)
                {
                    CollectAlongRoute(frames[i], i + 1);
                }
            }
            for (int i = 0; i < EnemyCount; i++)
            {
                tookHit[i] = EnemyHealth(i) < healthBefore[i];
                if (tookHit[i] && EnemyHealth(i) > 0)
                {
                    // 遭到攻击只获知来袭方向附近的线索，远距离受击不会暴露玩家坐标。
                    Vec2 impact = EnemyFrame(i).Current.Position;
                    Vec2 direction = Combat.ImpactDirection(i + PlayerCount + 1);
                    pilots[i].Perception.Hear(impact + direction * 6f);
                }
            }
            foreach (var shot in Combat.Fired)
            {
                if (shot.OwnerId > PlayerCount)
                {
                    continue;
                }
                for (int i = 0; i < EnemyCount; i++)
                {
                    if (EnemyHealth(i) > 0 &&
                        (EnemyFrame(i).Current.Position - shot.Position).SqrMagnitude <= HearingRange * HearingRange)
                    {
                        pilots[i].Perception.Hear(shot.Position);
                    }
                }
            }

            if (PlayersRemaining == 0)
            {
                Outcome = MissionOutcome.Defeat;
            }
            else if (EnemiesRemaining == 0)
            {
                Outcome = MissionOutcome.Victory;
            }
        }

        void CollectAlongRoute(in AircraftCombatFrame player, int seat)
        {
            for (int i = 0; i < pickups.Length; i++)
            {
                if (collectedBy[i] == 0 && ProjectileBallistics.TryContact(player.Previous.Position - pickups[i],
                    player.Current.Position - pickups[i], PickupRadius + player.Radius, out _))
                {
                    collectedBy[i] = seat;
                    CollectedCount++;
                }
            }
        }
    }
}
