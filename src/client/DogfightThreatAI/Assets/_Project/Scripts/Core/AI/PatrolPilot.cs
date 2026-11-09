using System;
using DogfightThreatAI.Core.AI.BehaviorTree;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.AI
{
    public enum PatrolBehavior
    {
        Patrol,
        Investigate,
        Search,
        Chase,
        Attack,
        Retreat,
        Return
    }

    /// <summary>由感知驱动的巡逻驾驶员，只输出飞行和开火意图。</summary>
    public sealed class PatrolPilot
    {
        const float BlindPursuitSeconds = 2.6f;
        readonly Bounds2D arena;
        readonly Vec2 center;
        readonly Vec2[] waypoints;
        readonly float leash;
        readonly float turnRate;
        readonly float cruise;
        readonly float pursuitSpeed;
        readonly WeaponSpec weapon;
        readonly IBehaviorNode<PatrolPilot> tree;
        readonly int searchSide;
        readonly PatrolPerception[] contacts;
        int targetIndex;

        FlightState self;
        Vec2 destination;
        Vec2 aim;
        Vec2 retreatPoint;
        float distance;
        float speed;
        float retreatTime;
        float retreatCooldown;
        float fireCooldown;
        bool returning;
        bool damaged;
        int health;
        int waypoint;
        int searchLeg;
        bool wasLost;

        public PatrolPerception Perception => contacts[targetIndex];
        public PatrolContact ContactFor(int playerIndex) => contacts[playerIndex].Contact;
        public PatrolBehavior Behavior { get; private set; }
        public bool WantsFire { get; private set; }
        public FlightMotionCommand Motion => FlightMotionCommand.SpeedOverride(speed, turnRate);

        public PatrolPilot(Bounds2D arena, Vec2 center, Vec2[] waypoints, int side,
            AircraftSpec aircraft, WeaponSpec weapon, float sightRange, float sightHalfAngle, int players = 1)
        {
            if (waypoints == null || waypoints.Length < 2)
            {
                throw new ArgumentException("At least two patrol points are required.", nameof(waypoints));
            }
            this.arena = arena;
            this.center = center;
            this.waypoints = (Vec2[])waypoints.Clone();
            this.weapon = weapon;
            searchSide = side < 0 ? -1 : 1;
            float patrolRadius = 0f;
            foreach (Vec2 point in waypoints)
            {
                patrolRadius = MathF.Max(patrolRadius, Vec2.Distance(center, point));
            }
            leash = patrolRadius + 48f;
            turnRate = MathF.Min(95f, aircraft.TurnRateDegPerSec);
            pursuitSpeed = MathF.Min(aircraft.MaxSpeed, 8.2f);
            cruise = MathF.Min(pursuitSpeed, 4.4f);
            contacts = new PatrolPerception[players];
            for (int i = 0; i < contacts.Length; i++)
            {
                contacts[i] = new PatrolPerception(sightRange, sightHalfAngle, 11f);
            }
            tree = new SelectorNode<PatrolPilot>(
                Branch(p => p.returning, p => p.Return()),
                Branch(p => p.ShouldRetreat(), p => p.Retreat()),
                Branch(p => p.HasTarget && p.CanShoot(), p => p.Attack()),
                Branch(p => p.HasTarget, p => p.Chase()),
                Branch(p => p.Perception.Contact == PatrolContact.Lost &&
                    p.Perception.TimeSinceSeen < BlindPursuitSeconds, p => p.FollowLastHeading()),
                Branch(p => p.Perception.Contact == PatrolContact.Lost, p => p.Search()),
                Branch(p => p.Perception.Contact == PatrolContact.Suspicious, p => p.Investigate()),
                new ActionNode<PatrolPilot>(p =>
                {
                    p.Patrol();
                    return NodeStatus.Running;
                }));
        }

        bool HasTarget => Perception.Contact == PatrolContact.Confirmed;

        static IBehaviorNode<PatrolPilot> Branch(Func<PatrolPilot, bool> condition,
            Action<PatrolPilot> action) =>
            new SequenceNode<PatrolPilot>(new ConditionNode<PatrolPilot>(condition),
                new ActionNode<PatrolPilot>(p =>
                {
                    action(p);
                    return NodeStatus.Running;
                }));

        public FlightInput Tick(in FlightState current, in FlightState target,
            int currentHealth, bool tookHit, float dt)
        {
            Perception.Observe(current, target, dt);
            return Plan(current, currentHealth, tookHit, dt);
        }

        public FlightInput TickTeam(in FlightState current, ReadOnlySpan<FlightState?> targets,
            int currentHealth, bool tookHit, float dt)
        {
            for (int i = 0; i < contacts.Length; i++)
            {
                contacts[i].Observe(current, targets[i], dt);
            }
            int next = targetIndex;
            for (int i = 0; i < contacts.Length; i++)
            {
                if (Priority(contacts[i].Contact) > Priority(contacts[next].Contact))
                {
                    next = i;
                }
            }
            if (next != targetIndex)
            {
                wasLost = false;
                targetIndex = next;
            }
            return Plan(current, currentHealth, tookHit, dt);
        }

        static int Priority(PatrolContact contact) => contact == PatrolContact.Confirmed ? 3 :
            contact == PatrolContact.Lost ? 2 : contact == PatrolContact.Suspicious ? 1 : 0;

        FlightInput Plan(in FlightState current, int currentHealth, bool tookHit, float dt)
        {
            self = current;
            health = currentHealth;
            damaged = tookHit;
            bool lost = Perception.Contact == PatrolContact.Lost;
            if (lost && !wasLost)
            {
                searchLeg = 0;
            }
            wasLost = lost;

            fireCooldown = MathF.Max(0f, fireCooldown - dt);
            retreatCooldown = MathF.Max(0f, retreatCooldown - dt);
            retreatTime = MathF.Max(0f, retreatTime - dt);
            WantsFire = false;
            float homeDistance = Vec2.Distance(center, self.Position);
            returning = homeDistance > (returning ? leash * .65f : leash);
            if (HasTarget)
            {
                var observation = Perception.LastSeen;
                distance = Vec2.Distance(self.Position, observation.Position);
                float lead = MathF.Min(.6f, distance / weapon.ProjectileSpeed);
                aim = observation.Position + observation.Velocity * lead * .78f;
            }

            tree.Tick(this);
            destination = arena.Clamp(destination);
            var delta = destination - self.Position;
            float error = delta.SqrMagnitude > .01f
                ? Vec2.NormalizeAngle(delta.AngleRad - self.Heading) : 0f;
            float steer = error / MathF.Max(.1f, turnRate * MathF.PI / 180f * .23f);
            return new FlightInput(1f, MathF.Max(-1f, MathF.Min(1f, steer)));
        }

        void Go(PatrolBehavior behavior, Vec2 point, float targetSpeed)
        {
            Behavior = behavior;
            destination = point;
            speed = targetSpeed;
        }

        void Patrol()
        {
            if (Vec2.Distance(self.Position, waypoints[waypoint]) <= 2.5f)
            {
                waypoint = (waypoint + 1) % waypoints.Length;
            }
            Go(PatrolBehavior.Patrol, waypoints[waypoint], cruise);
        }

        void Investigate()
        {
            Vec2 point = Perception.Clue;
            if (Vec2.Distance(point, self.Position) < 3f)
            {
                point += new Vec2(-self.Forward.Y, self.Forward.X) * (searchSide * 5f);
            }
            Go(PatrolBehavior.Investigate, point, cruise);
        }

        void Search()
        {
            var last = Perception.LastSeen;
            Vec2 predicted = LastKnownRouteEnd();
            Vec2 side = new Vec2(-last.Forward.Y, last.Forward.X) * searchSide;
            Vec2 point = SearchPoint(predicted, last.Forward, side);
            if (Vec2.Distance(self.Position, point) < 3f)
            {
                searchLeg = (searchLeg + 1) % 3;
                point = SearchPoint(predicted, last.Forward, side);
            }
            Go(PatrolBehavior.Search, point, cruise);
        }

        Vec2 LastKnownRouteEnd()
        {
            var last = Perception.LastSeen;
            Vec2 velocity = last.Velocity;
            return last.Position + (velocity.SqrMagnitude > 1f
                ? velocity.Normalized * MathF.Min(velocity.Magnitude, pursuitSpeed)
                : last.Forward * cruise) * BlindPursuitSeconds;
        }

        void FollowLastHeading() => Go(PatrolBehavior.Chase, LastKnownRouteEnd(), pursuitSpeed);

        Vec2 SearchPoint(Vec2 predicted, Vec2 forward, Vec2 side)
        {
            if (searchLeg == 1)
            {
                return predicted + side * 7f + forward * 6f;
            }
            return searchLeg == 2 ? predicted - side * 7f : predicted + forward * 3f;
        }

        bool CanShoot()
        {
            float aimError = MathF.Abs(Vec2.NormalizeAngle((aim - self.Position).AngleRad - self.Heading));
            return distance > 3f && distance < MathF.Min(19f, weapon.ProjectileSpeed * weapon.Lifetime)
                && aimError < .135f;
        }

        void Attack()
        {
            Go(PatrolBehavior.Attack, aim, MathF.Max(cruise, pursuitSpeed * .86f));
            WantsFire = fireCooldown <= 0f;
            if (WantsFire)
            {
                fireCooldown = searchSide < 0 ? 1.25f : 1.55f;
            }
        }

        void Chase() => Go(PatrolBehavior.Chase, aim, pursuitSpeed);
        void Return() => Go(PatrolBehavior.Return, center, pursuitSpeed);

        bool ShouldRetreat() => retreatTime > 0f ||
            (retreatCooldown <= 0f && ((HasTarget && distance < 3f) || (health <= 1 && damaged)));

        void Retreat()
        {
            if (retreatTime <= 0f)
            {
                Vec2 away = HasTarget ? (self.Position - Perception.LastSeen.Position).Normalized : self.Forward;
                if (away.SqrMagnitude < .01f)
                {
                    away = self.Forward;
                }
                var side = new Vec2(-away.Y, away.X) * searchSide;
                retreatPoint = self.Position + away * 9f + side * 5f;
                retreatTime = 1.9f;
                retreatCooldown = 7f;
            }
            Go(PatrolBehavior.Retreat, retreatPoint, pursuitSpeed);
        }
    }
}
