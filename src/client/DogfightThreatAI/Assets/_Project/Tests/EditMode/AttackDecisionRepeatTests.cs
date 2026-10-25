using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class AttackDecisionRepeatTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec EnemySpec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly AircraftSpec PlayerSpec = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);

        [Test]
        public void CornerAlternateApproach_RepeatedReadsDoNotReplaceAMoveOrderWithAnAimOrder()
        {
            // Exact state from deterministic boundary seed 202610245, offset 20, case 29.
            // Both aircraft start overlapped in the southwest corner, with zero velocity.
            // The complete player input sequence is brake (-1,0) on every fixed step.
            var player = FlightState.At(new Vec2(-200f, -150f), -1.0583016f);
            var enemy = FlightState.At(new Vec2(-200f, -150f), -2.029974f);
            var arena = Bounds2D.Centered(new Vec2(200f, 150f));
            var world = new World();
            var combat = new ContactCombatModel();
            var clock = new Clock(combat, 20f);
            var ai = new AttackRunInputSource(world, 2, EnemySpec, clock, arena, PlayerSpec);
            var enemyFlight = FlightModel.From(EnemySpec, arena);
            var playerFlight = FlightModel.From(PlayerSpec, arena);
            int reads = 0;
            for (int frame = 0; frame < 1800 && combat.Status.IsPlayerAlive; frame++)
            {
                world.Publish(enemy, player);
                var first = ai.ReadInput(Dt);
                var phase = ai.Phase;
                int id = ai.AttackId;
                var maneuver = ai.ManeuverPoint;
                var window = ai.AttackWindow;
                int previewCount = ai.PreviewPointCount;
                var previewEnd = previewCount > 0 ? ai.GetPreviewPoint(previewCount - 1) : Vec2.Zero;
                var second = ai.ReadInput(Dt);
                reads++;
                Assert.AreEqual(first.Throttle, second.Throttle, $"throttle changed on frame {frame}");
                Assert.AreEqual(first.Steer, second.Steer,
                    $"same clock/world returned move then aim on frame {frame}, elapsed={combat.Status.ElapsedTime:R}, phase={phase}, enemy={enemy.Position}");
                Assert.AreEqual(phase, ai.Phase);
                Assert.AreEqual(id, ai.AttackId);
                Assert.AreEqual(maneuver, ai.ManeuverPoint);
                Assert.AreEqual(window, ai.AttackWindow);
                Assert.AreEqual(previewCount, ai.PreviewPointCount);
                if (previewCount > 0) Assert.AreEqual(previewEnd, ai.GetPreviewPoint(previewCount - 1));
                var previousEnemy = enemy;
                var previousPlayer = player;
                enemyFlight.Step(ref enemy, first, Dt);
                playerFlight.Step(ref player, new FlightInput(-1f, 0f), Dt);
                combat.Step(previousPlayer, player, previousEnemy, enemy, Dt, ai.AttackWindow);
            }
            Assert.Greater(reads, 450, "The trajectory must exercise the second approach after the first hit.");
            Assert.AreEqual(0, combat.Status.PlayerHealth, "Repeated reads must not prevent the idle corner attack loop from completing.");
        }

        [TestCase(-200f, 150f, 1.099804f, -200f, 150f, 1.0269039f, 2.5870402f, 4.1449223f)]
        [TestCase(-200f, -150f, 2.9775403f, -200f, -146.91843f, -2.5994802f, 0f, 0f)]
        public void LevelFourCorner_RejectedApproachFindsAnotherLegalAngleAndCompletesTheAttackLoop(
            float px, float py, float playerHeading, float ex, float ey, float enemyHeading, float vx, float vy)
        {
            // Exact states from boundary seed 202610245, offset 60, cases 21 and 37.
            var player = FlightState.At(new Vec2(px, py), playerHeading);
            var enemy = FlightState.At(new Vec2(ex, ey), enemyHeading);
            enemy.Velocity = new Vec2(vx, vy);
            var arena = Bounds2D.Centered(new Vec2(200f, 150f));
            var world = new World();
            var combat = new ContactCombatModel();
            var ai = new AttackRunInputSource(world, 2, EnemySpec, new Clock(combat, 60f), arena, PlayerSpec);
            var enemyFlight = FlightModel.From(EnemySpec, arena);
            var playerFlight = FlightModel.From(PlayerSpec, arena);
            for (int frame = 0; frame < 6000 && combat.Status.IsPlayerAlive; frame++)
            {
                world.Publish(enemy, player);
                var input = ai.ReadInput(Dt);
                var previousEnemy = enemy;
                var previousPlayer = player;
                enemyFlight.Step(ref enemy, input, Dt);
                playerFlight.Step(ref player, new FlightInput(-1f, 0f), Dt);
                combat.Step(previousPlayer, player, previousEnemy, enemy, Dt, ai.AttackWindow);
            }
            Assert.AreEqual(0, combat.Status.PlayerHealth,
                $"A denied corner approach must select another usable angle, phase={ai.Phase}, enemy={enemy.Position}, waypoint={ai.ManeuverPoint}, attacks={ai.AttackId}");
            Assert.AreEqual(5, combat.Status.HitSequence);
        }

        sealed class Clock : ICombatStatusView
        {
            readonly ContactCombatModel _combat;
            readonly float _offset;
            public Clock(ContactCombatModel combat, float offset) { _combat = combat; _offset = offset; }
            public CombatStatus Status => new CombatStatus(_combat.Status.PlayerHealth, _combat.Status.MaxPlayerHealth,
                _combat.Status.HitSequence, _combat.Status.InvulnerabilityRemaining, _combat.Status.ElapsedTime + _offset);
        }
        sealed class World : IWorldView
        {
            AircraftSnapshot _enemy, _player;
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0 ? _enemy : _player;
            public void Publish(FlightState enemy, FlightState player)
            {
                _enemy = new AircraftSnapshot(2, 2, enemy);
                _player = new AircraftSnapshot(1, 1, player, isPlayerControlled: true);
            }
        }
    }
}
