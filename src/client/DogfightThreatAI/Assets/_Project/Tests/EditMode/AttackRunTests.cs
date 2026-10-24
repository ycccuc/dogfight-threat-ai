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
    public sealed class AttackRunTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec EnemySpec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly AircraftSpec PlayerSpec = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);
        static readonly Bounds2D Arena = Bounds2D.Centered(new Vec2(200f, 150f));

        [TestCase(0f, 0f, 4f, -2.5f)]
        [TestCase(0f, 0f, 2.2f, 0f)]
        [TestCase(0f, 0f, 0f, 0f)]
        [TestCase(200f, 150f, 194f, 147f)]
        [TestCase(-200f, -150f, -194f, -147f)]
        [TestCase(200f, 0f, 198f, 4f)]
        public void IdlePlayerReceivesFiveSeparatedWarnedAttacks(float px, float py, float ex, float ey)
        {
            var run = new Run(new Vec2(ex, ey), new Vec2(px, py));
            int warnedId = 0, lastHitId = 0, previousHits = 0;
            float lastHitTime = -100f;
            bool separated = true;
            for (int i = 0; i < 6000 && run.Combat.Status.IsPlayerAlive; i++)
            {
                run.Tick(FlightInput.Neutral);
                if (run.Ai.Phase == AttackRunPhase.Windup)
                {
                    Assert.IsTrue(separated, "Rearming requires an actual separation after the last hit.");
                    warnedId = run.Ai.AttackId;
                }
                if (Vec2.Distance(run.Enemy.Position, run.Player.Position) >= 3.5f) separated = true;
                if (run.Combat.Status.HitSequence > previousHits)
                {
                    Assert.AreEqual(AttackRunPhase.Charge, run.Ai.Phase);
                    Assert.AreEqual(warnedId, run.Ai.AttackId);
                    Assert.Greater(run.Ai.AttackId, lastHitId);
                    Assert.GreaterOrEqual(run.Combat.Status.ElapsedTime - lastHitTime, 2f);
                    lastHitTime = run.Combat.Status.ElapsedTime;
                    lastHitId = run.Ai.AttackId;
                    previousHits++;
                    separated = false;
                }
            }
            Assert.AreEqual(0, run.Combat.Status.PlayerHealth,
                $"Idle player remains unharmed: phase={run.Ai.Phase}, enemy={run.Enemy.Position}, goal={run.Ai.ManeuverPoint}, level={run.Ai.Level}, hits={previousHits}");
            Assert.AreEqual(5, previousHits);
        }

        [Test]
        public void WindupLastsPointEightSecondsAndHeadingStaysLockedWhenPlayerMoves()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            run.UntilWarning();
            float started = run.Combat.Status.ElapsedTime - Dt;
            float heading = run.Ai.LockedHeading;
            int id = run.Ai.AttackId;
            int warnings = 1, charges = 0;
            for (int i = 0; i < 90; i++)
            {
                run.Tick(new FlightInput(1f, i < 30 ? 1f : -1f));
                if (run.Ai.Phase == AttackRunPhase.Recover) break;
                Assert.AreEqual(id, run.Ai.AttackId);
                Assert.AreEqual(heading, run.Ai.LockedHeading);
                if (run.Ai.Phase == AttackRunPhase.Windup)
                {
                    warnings++;
                    Assert.IsFalse(run.Ai.AttackWindow.IsActive);
                    Assert.GreaterOrEqual(run.Ai.PreviewPointCount, 40);
                }
                if (run.Ai.Phase == AttackRunPhase.Charge)
                {
                    charges++;
                    Assert.AreEqual(0f, run.LastInput.Steer);
                    Assert.IsTrue(run.Ai.AttackWindow.IsActive);
                    Assert.GreaterOrEqual(run.Combat.Status.ElapsedTime - Dt - started, .8f - .001f);
                }
            }
            Assert.AreEqual(40, warnings, ".8 seconds at 50Hz must provide 40 warning frames.");
            Assert.Greater(charges, 0);
            Assert.LessOrEqual(charges, 45);
        }

        [TestCase(0f)]
        [TestCase(20f)]
        [TestCase(40f)]
        [TestCase(60f)]
        public void EachLevelStillThreatensIdlePlayerAndAllowsAReaction(float timeOffset)
        {
            var idle = new Run(new Vec2(4f, -2.5f), Vec2.Zero, timeOffset);
            for (int i = 0; i < 1800 && idle.Combat.Status.HitSequence == 0; i++) idle.Tick(FlightInput.Neutral);
            Assert.Greater(idle.Combat.Status.HitSequence, 0, "Fairness cannot pass by never attacking.");
            bool escaped = false;
            // Responses depend only on the public warning; no future target or private waypoint is read.
            foreach (float turn in new[] { -1f, 1f })
            {
                var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero, timeOffset);
                run.UntilWarning();
                for (int frame = 1; frame <= 90; frame++)
                {
                    FlightInput input = frame < 15 ? FlightInput.Neutral :
                        new FlightInput(1f, frame < 35 ? turn : 0f);
                    run.Tick(input);
                    if (run.Ai.Phase == AttackRunPhase.Recover) break;
                }
                escaped |= run.Combat.Status.HitSequence == 0;
            }
            Assert.IsTrue(escaped, "A response beginning 0.30s after the warning must escape.");
        }

        [Test]
        public void LevelChangesByClockButFreezesDuringCommittedAttackAndRecovery()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero, 19f);
            run.UntilWarning();
            Assert.AreEqual(1, run.Ai.Level);
            while (run.Ai.Phase == AttackRunPhase.Windup || run.Ai.Phase == AttackRunPhase.Charge)
            {
                run.Tick(FlightInput.Neutral);
                Assert.AreEqual(1, run.Ai.Level);
            }
            float minimum = run.Combat.Status.ElapsedTime + 1.8f - Dt;
            while (run.Combat.Status.ElapsedTime < minimum - .04f)
            {
                run.Tick(FlightInput.Neutral);
                Assert.AreEqual(1, run.Ai.Level);
            }
            for (int i = 0; i < 400 && run.Ai.Level == 1; i++) run.Tick(FlightInput.Neutral);
            Assert.AreEqual(2, run.Ai.Level);
        }

        [Test]
        public void VisibilityOnlyBlocksNewWindupAndClockStillLevelsUp()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            run.Ai.CanStartVisibleAttack = false;
            for (int i = 0; i < 3200; i++)
            {
                run.Tick(FlightInput.Neutral);
                Assert.IsFalse(run.Ai.AttackWindow.IsActive);
                Assert.AreEqual(0, run.Ai.AttackId);
            }
            Assert.AreEqual(4, run.Ai.Level);
            run.Ai.CanStartVisibleAttack = true;
            run.UntilWarning();
            int id = run.Ai.AttackId;
            run.Ai.CanStartVisibleAttack = false;
            for (int i = 0; i < 45 && run.Ai.Phase != AttackRunPhase.Charge; i++) run.Tick(FlightInput.Neutral);
            Assert.AreEqual(AttackRunPhase.Charge, run.Ai.Phase);
            Assert.AreEqual(id, run.Ai.AttackId);
        }

        [TestCase(0f)]
        [TestCase(.65f)]
        [TestCase(-.65f)]
        public void MovingThenStoppedPlayerIsAttackedAgain(float steer)
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            for (int i = 0; i < 250; i++) run.Tick(new FlightInput(1f, steer));
            for (int i = 0; i < 100; i++) run.Tick(new FlightInput(-1f, 0f));
            int before = run.Combat.Status.HitSequence;
            for (int i = 0; i < 2500 && run.Combat.Status.HitSequence == before; i++) run.Tick(FlightInput.Neutral);
            Assert.Greater(run.Combat.Status.HitSequence, before);
        }

        [Test]
        public void PressuringEnemyDoesNotFreezeGrowthAndStoppingRestoresAttack()
        {
            var run = new Run(new Vec2(2f, 0f), Vec2.Zero);
            for (int i = 0; i < 1300 && run.Combat.Status.IsPlayerAlive; i++)
            {
                Vec2 direction = run.Enemy.Position - run.Player.Position;
                float heading = MathF.Atan2(direction.Y, direction.X);
                float angle = Vec2.NormalizeAngle(heading - run.Player.Heading);
                run.Tick(new FlightInput(1f, MathF.Max(-1f, MathF.Min(1f, angle / (240f * MathF.PI / 180f * Dt)))));
            }
            Assert.GreaterOrEqual(run.Ai.Level, 2);
            for (int i = 0; i < 100; i++) run.Tick(new FlightInput(-1f, 0f));
            int hits = run.Combat.Status.HitSequence;
            for (int i = 0; i < 3000 && run.Combat.Status.HitSequence == hits; i++) run.Tick(FlightInput.Neutral);
            Assert.Greater(run.Combat.Status.HitSequence, hits, "After pressure stops the enemy must regain an attack.");
        }

        [Test]
        public void PreviewMatchesTheActualLockedFlightModelIncludingMovingWindup()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            run.UntilWarning();
            FlightState predicted = run.World.Enemy;
            var model = FlightModel.From(EnemySpec, Arena);
            int count = run.Ai.PreviewPointCount;
            Assert.That(count, Is.InRange(85, 87));
            for (int i = 1; i < count; i++)
            {
                model.Step(ref predicted, i <= 40 ? new FlightInput(run.Ai.WarningSpeed / EnemySpec.MaxSpeed, 0f) : new FlightInput(1f, 0f), Dt);
                Assert.Less(Vec2.Distance(predicted.Position, run.Ai.GetPreviewPoint(i)), .001f);
            }
        }

        [Test]
        public void RepeatedReadsDoNotMutateClockWorldOrInputs()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            run.UntilWarning();
            run.World.Enemy = run.Enemy;
            var before = run.World.GetAircraft(0);
            float time = run.Clock.Status.ElapsedTime;
            var expected = run.Ai.ReadInput(Dt);
            float remaining = run.Ai.PhaseRemaining;
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(expected, run.Ai.ReadInput(Dt));
                Assert.AreEqual(remaining, run.Ai.PhaseRemaining);
            }
            Assert.AreEqual(time, run.Clock.Status.ElapsedTime);
            Assert.AreEqual(before, run.World.GetAircraft(0));
        }

        [Test]
        public void TargetLostAndReacquiredRequiresNewObservationAndWarning()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            run.UntilWarning();
            run.World.Alive = false;
            Assert.AreEqual(new FlightInput(-1f, 0f), run.Ai.ReadInput(Dt));
            Assert.AreEqual(AttackRunPhase.NoTarget, run.Ai.Phase);
            Assert.IsFalse(run.Ai.AttackWindow.IsActive);
            run.World.Alive = true;
            AssertLegal(run.Ai.ReadInput(Dt));
            Assert.AreEqual(AttackRunPhase.Recover, run.Ai.Phase);
            Assert.IsFalse(run.Ai.AttackWindow.IsActive);
            run.UntilWarning();
        }

        [Test]
        public void LosingTargetDuringWarningKeepsRecoveryWhenItReturns()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            run.UntilWarning();
            float cancelled = run.Clock.Status.ElapsedTime;
            int attack = run.Ai.AttackId;
            run.World.Alive = false;
            run.Tick(FlightInput.Neutral);
            run.World.Alive = true;
            for (int i = 0; i < 80; i++)
            {
                run.Tick(FlightInput.Neutral);
                Assert.AreEqual(attack, run.Ai.AttackId,
                    $"Cancelled warning cannot restart before recovery: {run.Clock.Status.ElapsedTime - cancelled:F2}s");
                Assert.IsFalse(run.Ai.AttackWindow.IsActive);
            }
            run.UntilWarning();
            Assert.GreaterOrEqual(run.Clock.Status.ElapsedTime - cancelled, 1.8f);
        }

        [Test]
        public void FiniteExtremeSnapshotsAndInvalidTimeNeverProduceInvalidInput()
        {
            var run = new Run(new Vec2(-float.MaxValue, 0f), new Vec2(float.MaxValue, 0f));
            for (int i = 0; i < 60; i++)
            {
                run.Clock.Offset = i * .02f;
                AssertLegal(run.Ai.ReadInput(Dt));
            }
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
            {
                run.Clock.Offset = invalid;
                Assert.AreEqual(new FlightInput(-1f, 0f), run.Ai.ReadInput(Dt));
                Assert.IsFalse(run.Ai.AttackWindow.IsActive);
            }
        }

        [Test]
        public void SameInputsReplayExactly()
        {
            CollectionAssert.AreEqual(Replay(), Replay());
        }
        static List<string> Replay()
        {
            var run = new Run(new Vec2(4f, -2.5f), Vec2.Zero);
            var result = new List<string>();
            for (int i = 0; i < 3500 && run.Combat.Status.IsPlayerAlive; i++)
            {
                run.Tick(i < 250 ? new FlightInput(1f, .65f) : new FlightInput(-1f, 0f));
                result.Add($"{run.Enemy.Position.X:R}/{run.Enemy.Position.Y:R}/{run.Enemy.Heading:R}/{run.Ai.Phase}/{run.Ai.Level}/{run.Combat.Status.PlayerHealth}");
            }
            return result;
        }
        static void AssertLegal(FlightInput input)
        {
            Assert.That(input.Throttle, Is.InRange(-1f, 1f));
            Assert.That(input.Steer, Is.InRange(-1f, 1f));
        }
        sealed class ClockView : ICombatStatusView
        {
            public ContactCombatModel Model;
            public float Offset;
            public CombatStatus Status => new CombatStatus(Model.Status.PlayerHealth, Model.Status.MaxPlayerHealth,
                Model.Status.HitSequence, Model.Status.InvulnerabilityRemaining, Model.Status.ElapsedTime + Offset);
        }
        sealed class TestWorld : IWorldView
        {
            public FlightState Enemy, Player;
            public bool Alive = true;
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(2, 2, Enemy)
                : new AircraftSnapshot(1, 1, Player, Alive, true);
        }
        sealed class Run
        {
            public FlightState Enemy, Player;
            public readonly TestWorld World = new TestWorld();
            public readonly ContactCombatModel Combat = new ContactCombatModel();
            public readonly ClockView Clock;
            public readonly AttackRunInputSource Ai;
            public FlightInput LastInput;
            readonly FlightModel _enemy = FlightModel.From(EnemySpec, Arena);
            readonly FlightModel _player = FlightModel.From(PlayerSpec, Arena);
            public Run(Vec2 enemy, Vec2 player, float offset = 0f)
            {
                Enemy = FlightState.At(enemy, MathF.PI * .5f);
                Player = FlightState.At(player);
                World.Enemy = Enemy; World.Player = Player;
                Clock = new ClockView { Model = Combat, Offset = offset };
                Ai = new AttackRunInputSource(World, 2, EnemySpec, Clock, Arena, PlayerSpec);
            }
            public void Tick(FlightInput playerInput)
            {
                World.Enemy = Enemy; World.Player = Player;
                LastInput = Ai.ReadInput(Dt);
                AssertLegal(LastInput);
                var beforeEnemy = Enemy; var beforePlayer = Player;
                _enemy.Step(ref Enemy, LastInput, Dt);
                _player.Step(ref Player, playerInput, Dt);
                Combat.Step(beforePlayer, Player, beforeEnemy, Enemy, Dt, Ai.AttackWindow);
            }
            public void UntilWarning()
            {
                for (int i = 0; i < 2500; i++)
                {
                    Tick(FlightInput.Neutral);
                    if (Ai.Phase == AttackRunPhase.Windup) return;
                }
                Assert.Fail($"No warning: {Ai.Phase}, enemy={Enemy.Position}, goal={Ai.ManeuverPoint}, level={Ai.Level}");
            }
        }
    }
}
