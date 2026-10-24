using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Balance regressions from the actual two-key circle exploit.
    /// Count real collision/near-collision passes, not merely announced attacks.
    /// </summary>
    public sealed class MovingAttackGrowthTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec EnemySpec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly AircraftSpec PlayerSpec = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);
        static readonly Bounds2D Arena = Bounds2D.Centered(new Vec2(200f, 150f));

        [TestCase(0f)]
        [TestCase(20f)]
        [TestCase(40f)]
        [TestCase(60f)]
        public void UnchangedTwoKeyCircleStillReceivesMeaningfulAttacks(float startingClock)
        {
            var result = Simulate(startingClock, 60f, false);
            Assert.GreaterOrEqual(result.Warnings, 2,
                "A held W+A must not stop the enemy organizing an attack for an entire round. " + result);
            Assert.GreaterOrEqual(result.ClosePasses, 1,
                "Starting far-away attacks that never approach the circle is not a fix. " + result);
            Assert.GreaterOrEqual(result.Hits, 1,
                "An unchanging two-key policy must eventually need a deliberate dodge. " + result);
        }

        [TestCase(40f)]
        [TestCase(60f)]
        public void AdvancedEnemyDoesNotSpendFifteenSecondsOnlyRepositioning(float startingClock)
        {
            var result = Simulate(startingClock, 60f, false);
            Assert.LessOrEqual(result.LongestWarningGap, 15f,
                "Fifteen seconds is already several normal attack cycles for this nearby circling target. " + result);
        }

        [TestCase(0f)]
        [TestCase(20f)]
        [TestCase(40f)]
        [TestCase(60f)]
        public void PublicWarningStillAllowsPointThreeSecondReactionAtEveryLevel(float startingClock)
        {
            var result = Simulate(startingClock, 45f, true);
            Assert.GreaterOrEqual(result.Warnings, 2,
                "A dodge pass requires multiple actual attacks, not perpetual refusal to engage. " + result);
            Assert.AreEqual(0, result.Hits,
                "The player uses only the public warning and legal W+A/D, with a full .30 second reaction delay. " + result);
        }

        static Result Simulate(float startingClock, float duration, bool reactToWarning)
        {
            var enemy = FlightState.At(new Vec2(4f, -2.5f), MathF.PI * .5f);
            var player = FlightState.At(Vec2.Zero, MathF.PI * .5f);
            var world = new TestWorld();
            var combat = new ContactCombatModel();
            var clock = new Clock(combat, startingClock);
            var ai = new AttackRunInputSource(world, 2, EnemySpec, clock, Arena, PlayerSpec);
            var enemyFlight = FlightModel.From(EnemySpec, Arena);
            var playerFlight = FlightModel.From(PlayerSpec, Arena);
            var result = new Result();
            var closeAttackIds = new HashSet<int>();
            int lastWarningId = 0;
            float lastWarning = 0f, responseStarted = -100f;
            Vec2 escapeDirection = Vec2.Zero;
            for (int frame = 0; frame < (int)(duration / Dt) && combat.Status.IsPlayerAlive; frame++)
            {
                float time = combat.Status.ElapsedTime;
                world.Publish(enemy, player);
                FlightInput enemyInput = ai.ReadInput(Dt);
                Assert.That(enemyInput.Throttle, Is.InRange(-1f, 1f));
                Assert.That(enemyInput.Steer, Is.InRange(-1f, 1f));
                if (ai.Phase == AttackRunPhase.Windup && ai.AttackId != lastWarningId)
                {
                    lastWarningId = ai.AttackId;
                    result.Warnings++;
                    result.LongestWarningGap = MathF.Max(result.LongestWarningGap, time - lastWarning);
                    lastWarning = responseStarted = time;
                    var normal = new Vec2(-ai.LockedDirection.Y, ai.LockedDirection.X);
                    escapeDirection = Vec2.Dot(normal, player.Forward) >= 0f ? normal : -normal;
                    if (!Arena.Contains(player.Position + escapeDirection * 12f)) escapeDirection = -escapeDirection;
                }

                FlightInput playerInput = new FlightInput(1f, 1f);
                if (reactToWarning)
                {
                    float sinceWarning = time - responseStarted;
                    bool active = ai.Phase == AttackRunPhase.Windup || ai.Phase == AttackRunPhase.Charge;
                    playerInput = new FlightInput(-1f, 0f);
                    if (lastWarningId > 0 && active && sinceWarning >= .30f)
                    {
                        float angle = MathF.Atan2(Vec2.Cross(player.Forward, escapeDirection), Vec2.Dot(player.Forward, escapeDirection));
                        float steer = sinceWarning < .70f
                            ? MathF.Max(-1f, MathF.Min(1f, angle / (240f * MathF.PI / 180f * Dt))) : 0f;
                        playerInput = new FlightInput(1f, steer);
                    }
                }

                var previousEnemy = enemy;
                var previousPlayer = player;
                enemyFlight.Step(ref enemy, enemyInput, Dt);
                playerFlight.Step(ref player, playerInput, Dt);
                if (ai.AttackWindow.IsActive && RelativeSegmentDistance(previousEnemy.Position, enemy.Position,
                    previousPlayer.Position, player.Position) <= 1.5d) closeAttackIds.Add(ai.AttackId);
                combat.Step(previousPlayer, player, previousEnemy, enemy, Dt, ai.AttackWindow);
                result.Hits = combat.Status.HitSequence;
            }
            result.Elapsed = combat.Status.ElapsedTime;
            result.LongestWarningGap = MathF.Max(result.LongestWarningGap, result.Elapsed - lastWarning);
            result.ClosePasses = closeAttackIds.Count;
            return result;
        }

        static double RelativeSegmentDistance(Vec2 enemyBefore, Vec2 enemyAfter, Vec2 playerBefore, Vec2 playerAfter)
        {
            double x = (double)enemyBefore.X - playerBefore.X, y = (double)enemyBefore.Y - playerBefore.Y;
            double dx = ((double)enemyAfter.X - playerAfter.X) - x;
            double dy = ((double)enemyAfter.Y - playerAfter.Y) - y;
            double denominator = dx * dx + dy * dy;
            double t = denominator > 1e-12d
                ? System.Math.Max(0d, System.Math.Min(1d, -(x * dx + y * dy) / denominator)) : 0d;
            return System.Math.Sqrt((x + dx * t) * (x + dx * t) + (y + dy * t) * (y + dy * t));
        }

        sealed class Result
        {
            public int Warnings, ClosePasses, Hits;
            public float Elapsed, LongestWarningGap;
            public override string ToString()
                => $"warnings={Warnings}, closePasses={ClosePasses}, hits={Hits}, elapsed={Elapsed:F2}, longestGap={LongestWarningGap:F2}";
        }

        sealed class Clock : ICombatStatusView
        {
            readonly ContactCombatModel _model;
            readonly float _offset;
            public Clock(ContactCombatModel model, float offset) { _model = model; _offset = offset; }
            public CombatStatus Status => new CombatStatus(_model.Status.PlayerHealth, _model.Status.MaxPlayerHealth,
                _model.Status.HitSequence, _model.Status.InvulnerabilityRemaining, _model.Status.ElapsedTime + _offset);
        }
        sealed class TestWorld : IWorldView
        {
            AircraftSnapshot _enemy, _player;
            public int AircraftCount => 2;
            public void Publish(FlightState enemy, FlightState player)
            {
                _enemy = new AircraftSnapshot(2, 2, enemy);
                _player = new AircraftSnapshot(1, 1, player, true, true);
            }
            public AircraftSnapshot GetAircraft(int index) => index == 0 ? _enemy : _player;
        }
    }
}
