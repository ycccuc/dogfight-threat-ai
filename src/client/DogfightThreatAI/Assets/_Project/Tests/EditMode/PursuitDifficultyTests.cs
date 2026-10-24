using System;
using System.Reflection;
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
    public sealed class PursuitDifficultyTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly Bounds2D Arena = Bounds2D.Centered(new Vec2(200f, 150f));

        [TestCase(20f, 2)]
        [TestCase(40f, 3)]
        [TestCase(60f, 4)]
        public void PursuitProgressesThroughFourDistinctLevels(float time, int expected)
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-20f, 0f)) };
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.ReadInput(Dt);
            clock.Now = time;
            ai.ReadInput(Dt);
            Assert.AreEqual(expected, Level(ai));
            Assert.AreEqual(AttackRunPhase.Pursuit, ai.Phase);
        }

        [Test]
        public void ACurrentAttackKeepsItsLevelUntilRecoveryHasFinished()
        {
            var world = new World();
            var clock = new Clock { Now = 19.8f };
            var ai = Create(world, clock);
            ai.ReadInput(Dt);
            clock.Now = 20.2f;
            ai.ReadInput(Dt);
            Assert.AreEqual(1, Level(ai));
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            clock.Now = 20.6f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            Assert.AreEqual(1, Level(ai));
            clock.Now = 21.5f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
            Assert.AreEqual(1, Level(ai));
            clock.Now = 22.91f;
            ai.ReadInput(Dt);
            Assert.AreEqual(2, Level(ai));
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
        }

        [TestCase(1, 1.4f)]
        [TestCase(2, .65f)]
        [TestCase(3, .6f)]
        [TestCase(4, .2f)]
        public void EachLevelHasItsOwnRecoveryButKeepsTheFullWarningAndStraightCharge(int level, float recovery)
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock, level);
            var flight = FlightModel.From(Spec, Arena);
            var input = ai.ReadInput(Dt);
            Assert.AreEqual(level, Level(ai));
            Assert.AreEqual(.8f, ai.PhaseRemaining, .0001f);
            Assert.AreEqual(1f, input.Throttle);
            var cruise = (IFlightCruiseSource)ai;
            var state = world.Enemy;
            flight.Step(ref state, input, Dt, ai.MotionCommand);
            Assert.AreEqual(ai.CruiseSpeed, state.Speed, .001f);
            clock.Now = .8f;
            var charge = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            Assert.AreEqual(0f, charge.Steer);
            Assert.AreEqual(5f, ai.SpeedMultiplier);
            Assert.AreEqual(0, ai.PreviewPointCount);
            clock.Now = 1.7f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
            Assert.AreEqual(recovery, ai.PhaseRemaining, .0001f);
        }

        [Test]
        public void InterceptionLeadsTheMovingTargetWhereTheBasicPursuerDoesNot()
        {
            var world = new World
            {
                Enemy = FlightState.At(new Vec2(-20f, 0f)),
                Player = new FlightState { Position = Vec2.Zero, Heading = MathF.PI * .5f, Velocity = new Vec2(0f, 9f) }
            };
            var clock = new Clock();
            var basic = Create(world, clock, 1);
            var interceptor = Create(world, clock, 2);
            basic.ReadInput(Dt);
            interceptor.ReadInput(Dt);
            Assert.Greater(interceptor.ManeuverPoint.Y, basic.ManeuverPoint.Y + .5f,
                "Level 2 must cut toward the observed travel route instead of copying the basic chase point.");
        }

        [Test]
        public void SideApproachChangesTheRouteWithoutWaitingForAPreparationPoint()
        {
            var world = new World
            {
                Enemy = FlightState.At(new Vec2(-20f, 0f)),
                Player = new FlightState { Position = Vec2.Zero, Heading = MathF.PI * .5f, Velocity = new Vec2(0f, 9f) }
            };
            var clock = new Clock();
            var interceptor = Create(world, clock, 2);
            var flanker = Create(world, clock, 3);
            interceptor.ReadInput(Dt);
            flanker.ReadInput(Dt);
            Assert.Greater(Vec2.Distance(interceptor.ManeuverPoint, flanker.ManeuverPoint), .5f);
            world.Enemy = FlightState.At(new Vec2(-6f, 0f));
            clock.Now = .1f;
            var input = flanker.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, flanker.Phase);
            Assert.AreEqual(.8f, flanker.PhaseRemaining, .0001f);
            Assert.AreEqual(1f, input.Throttle);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void EveryLevelStillDefeatsAStationaryPlayerWithRealFlightAndContact(int level)
        {
            var world = new World();
            var combat = new ContactCombatModel(invulnerabilitySeconds: .7f);
            var ai = Create(world, combat, level);
            var flight = FlightModel.From(Spec, Arena);
            for (int tick = 0; tick < 4000 && combat.Status.IsPlayerAlive; tick++)
            {
                var before = world.Enemy;
                var input = ai.ReadInput(Dt);
                flight.Step(ref world.Enemy, input, Dt, ai.MotionCommand);
                combat.StepContact(world.Player, world.Player, before, world.Enemy, Dt, 1f);
            }
            Assert.AreEqual(0, combat.Status.PlayerHealth, $"level {level} stopped attacking at {combat.Status.ElapsedTime}s / {ai.Phase}");
        }

        [Test]
        public void RequestedDifficultyWaitsForTheAttackBoundaryAndDoesNotResetItsWarning()
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.ReadInput(Dt);
            ai.RequestDifficulty(4);
            clock.Now = .4f;
            ai.ReadInput(Dt);
            Assert.AreEqual(1, ai.Level);
            Assert.AreEqual(.4f, ai.PhaseRemaining, .0001f);
            clock.Now = .8f;
            ai.ReadInput(Dt);
            Assert.AreEqual(1, ai.Level);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            clock.Now = 1.7f;
            ai.ReadInput(Dt);
            Assert.AreEqual(1.4f, ai.RecoverySeconds, .0001f);
            clock.Now = 3.11f;
            ai.ReadInput(Dt);
            Assert.AreEqual(4, ai.Level);
            Assert.AreEqual(.8f, ai.PhaseRemaining, .0001f);
            Assert.AreEqual(.2f, ai.RecoverySeconds, .0001f);
        }

        [Test]
        public void ObservationDoesNotExposeSamplesNewerThanTheCutoffOrRepeatedSameClockReads()
        {
            var memory = new PursuitObservationMemory();
            memory.Sample(FlightState.At(Vec2.Zero), 0f);
            memory.Sample(FlightState.At(new Vec2(100f, 0f)), 0f);
            memory.Sample(FlightState.At(new Vec2(10f, 0f)), .2f);
            Assert.IsTrue(memory.TryObserve(.3f, .18f, out var observed, out float at, out _, out _));
            Assert.AreEqual(0f, at);
            Assert.AreEqual(Vec2.Zero, observed.Position);
            Assert.IsTrue(memory.TryObserve(.4f, .18f, out observed, out at, out _, out _));
            Assert.AreEqual(.2f, at);
            Assert.AreEqual(new Vec2(10f, 0f), observed.Position);
        }

        [Test]
        public void FourthLevelLearnsOnlyAnAlreadyObservedConsistentTurnAndBoundsItsPrediction()
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-20f, 0f)) };
            var clock = new Clock();
            var ai = Create(world, clock, 4);
            Assert.AreEqual(0f, ai.Adaptation01);
            for (int tick = 0; tick <= 60; tick++)
            {
                float angle = tick * Dt * .8f;
                world.Player = new FlightState
                {
                    Position = new Vec2(MathF.Cos(angle), MathF.Sin(angle)) * 6f,
                    Velocity = new Vec2(-MathF.Sin(angle), MathF.Cos(angle)) * 4.8f,
                    Heading = angle + MathF.PI * .5f
                };
                clock.Now = tick * Dt;
                ai.ReadInput(Dt);
            }
            Assert.Greater(ai.Adaptation01, .6f);
            Assert.LessOrEqual(Vec2.Distance(ai.PredictionPoint, ai.ObservedTargetPosition), 3.501f);
            Vec2 offset = ai.PredictionPoint - ai.ObservedTargetPosition;
            Vec2 velocityDirection = new Vec2(-MathF.Sin(ai.ObservedAt * .8f), MathF.Cos(ai.ObservedAt * .8f));
            Assert.Greater(Vec2.Cross(velocityDirection, offset), .02f,
                "Learned curved motion must differ from a purely linear lead, using history that already happened.");
        }

        [Test]
        public void RepeatedReadsCannotManufactureObservationHistoryOrAdaptation()
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-20f, 0f)) };
            var clock = new Clock();
            var ai = Create(world, clock, 4);
            FlightInput first = ai.ReadInput(Dt);
            for (int read = 0; read < 200; read++)
            {
                FlightInput repeated = ai.ReadInput(Dt);
                Assert.AreEqual(first.Steer, repeated.Steer);
                Assert.AreEqual(0f, ai.Adaptation01);
                Assert.AreEqual(0f, ai.ObservedAt);
            }
        }

        static int Level(PursuitChargeInputSource source)
            => source.GetType().GetProperty("Level")?.GetValue(source) is int result ? result : 1;

        static PursuitChargeInputSource Create(World world, ICombatStatusView combat, int level = 0)
        {
            foreach (var constructor in typeof(PursuitChargeInputSource).GetConstructors())
            {
                if (constructor.GetParameters().Length == 6)
                    return (PursuitChargeInputSource)constructor.Invoke(new object[] { world, 2, Spec, combat, Arena, level });
            }
            return new PursuitChargeInputSource(world, 2, Spec, combat, Arena);
        }

        sealed class World : IWorldView
        {
            public FlightState Enemy = FlightState.At(new Vec2(-6f, 0f));
            public FlightState Player = FlightState.At(Vec2.Zero, MathF.PI * .5f);
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(2, 2, Enemy)
                : new AircraftSnapshot(1, 1, Player, true, true);
        }

        sealed class Clock : ICombatStatusView
        {
            public float Now;
            public CombatStatus Status => new CombatStatus(5, 5, 0, 0f, Now);
        }
    }
}
