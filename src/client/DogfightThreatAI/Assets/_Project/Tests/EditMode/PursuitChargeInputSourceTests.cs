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
    public sealed class PursuitChargeInputSourceTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly Bounds2D Arena = Bounds2D.Centered(new Vec2(200f, 150f));

        [Test]
        public void OutsideRangeAlwaysChasesCurrentPositionEvenWhenOffScreen()
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-20f, 0f), 0f) };
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.CanStartVisibleAttack = false;
            FlightInput first = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Pursuit, ai.Phase);
            Assert.AreEqual(1f, first.Throttle);
            world.Player = FlightState.At(new Vec2(-20f, 20f));
            clock.Now = Dt;
            ai.ReadInput(Dt); // A new visible sample is recorded, but the basic observer has not reacted yet.
            clock.Now = .4f;
            FlightInput changed = ai.ReadInput(Dt);
            Assert.Greater(changed.Steer, .9f);
            Assert.AreEqual(1f, changed.Throttle);
            Assert.AreEqual(0, ai.AttackId);
        }

        [Test]
        public void EnteringRangeStartsFullMovingAimWithoutAnyPreparationPoint()
        {
            var world = new World();
            var ai = Create(world, new Clock());
            FlightInput input = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Assert.AreEqual(1, ai.AttackId);
            Assert.AreEqual(.8f, ai.PhaseRemaining, .0001f);
            Assert.Greater(input.Throttle, 0f);
            Assert.IsFalse(ai.AttackWindow.IsActive);
            Assert.AreEqual(2, ai.PreviewPointCount);
        }

        [Test]
        public void PredictionTracksTheMovingPlayerWhileTheWarningShowsTheNoseWithoutRestartingItsTimer()
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.ReadInput(Dt);
            Vec2 before = ai.PredictionPoint;
            world.Player = FlightState.At(new Vec2(0f, 4f));
            clock.Now = .3f;
            ai.ReadInput(Dt);
            Assert.AreEqual(before, ai.PredictionPoint, "A basic observer must not read the newly changed target before its observation delay.");
            clock.Now = .48f;
            FlightInput input = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Assert.Greater(Vec2.Distance(before, ai.PredictionPoint), .1f);
            Assert.Greater(Vec2.Dot((ai.PredictionPoint - world.Enemy.Position).Normalized,
                (world.Player.Position - world.Enemy.Position).Normalized), .999f);
            Assert.AreEqual(world.Enemy.Forward, ai.AimDirection);
            Assert.That(input.Steer, Is.InRange(-1f, 1f));
            Assert.Greater(input.Throttle, 0f);
            Assert.AreEqual(.32f, ai.PhaseRemaining, .0001f);
            Assert.AreEqual(1, ai.AttackId);
            Assert.IsFalse(ai.AttackWindow.IsActive);
        }

        [Test]
        public void TimerFinishesWhenTargetMovesOrVisibilityChangesAndKeepsTheCommittedHeading()
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.ReadInput(Dt);
            clock.Now = .6f;
            ai.CanStartVisibleAttack = false;
            world.Player = FlightState.At(new Vec2(2f, 4f));
            world.Enemy = FlightState.At(new Vec2(-4f, 0f), .4f);
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Assert.IsFalse(ai.AttackWindow.IsActive);
            float promisedHeading = ai.LockedHeading;
            clock.Now = .8f;
            FlightInput charge = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            Assert.AreEqual(promisedHeading, ai.LockedHeading, .0001f);
            Assert.AreEqual(.9f, ai.PhaseRemaining, .0001f);
            Assert.AreEqual(1f, charge.Throttle);
            Assert.AreEqual(0f, charge.Steer);
            Assert.IsTrue(ai.AttackWindow.IsActive);
            Vec2 locked = ai.LockedDirection;
            world.Player = FlightState.At(new Vec2(-10f, -10f));
            clock.Now = .9f;
            Assert.AreEqual(0f, ai.ReadInput(Dt).Steer);
            Assert.AreEqual(locked, ai.LockedDirection);
        }

        [Test]
        public void OffScreenRangeEntryKeepsChasingAndStartsFullAimOnceVisible()
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.CanStartVisibleAttack = false;
            Assert.AreEqual(1f, ai.ReadInput(Dt).Throttle);
            Assert.AreEqual(AttackRunPhase.Pursuit, ai.Phase);
            clock.Now = 10f;
            ai.CanStartVisibleAttack = true;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Assert.AreEqual(.8f, ai.PhaseRemaining, .0001f);
        }

        [Test]
        public void RecoveryEndsOnTimeAndInvulnerabilityAllowsOnlyACompleteNewWarning()
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.ReadInput(Dt);
            clock.Now = .8f;
            ai.ReadInput(Dt);
            clock.Now = 1f;
            clock.Hits = 1;
            clock.Invulnerable = 2f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
            Assert.IsFalse(ai.AttackWindow.IsActive);
            clock.Now = 1f + ai.RecoverySeconds + .01f;
            clock.Invulnerable = .2f;
            Assert.AreEqual(1f, ai.ReadInput(Dt).Throttle);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Assert.AreEqual(.8f, ai.PhaseRemaining, .0001f);
            Assert.AreEqual(2, ai.AttackId);
            Assert.IsFalse(ai.AttackWindow.IsActive);
            clock.Now += .4f;
            clock.Invulnerable = .1f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Assert.AreEqual(.4f, ai.PhaseRemaining, .0001f);
            Assert.IsFalse(ai.AttackWindow.IsActive);
            clock.Now += .4f;
            clock.Invulnerable = 0f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
        }

        [TestCase(0f, 0f)]
        [TestCase(199.5f, 149.5f)]
        public void ActualFlightAndCombatRepeatAgainstStoppedPlayerUntilFiveHits(float x, float y)
        {
            var world = new World { Player = FlightState.At(new Vec2(x, y)), Enemy = FlightState.At(new Vec2(x - 6f, y - 2f), .3f) };
            var combat = new ContactCombatModel(invulnerabilitySeconds: .7f);
            var ai = Create(world, combat);
            var flight = FlightModel.From(Spec, Arena);
            for (int tick = 0; tick < 5000 && combat.Status.IsPlayerAlive; tick++)
            {
                FlightState previous = world.Enemy;
                FlightInput input = ai.ReadInput(Dt);
                Assert.That(input.Throttle, Is.InRange(-1f, 1f));
                Assert.That(input.Steer, Is.InRange(-1f, 1f));
                if (ai.Phase != AttackRunPhase.Charge) Assert.IsFalse(ai.AttackWindow.IsActive);
                flight.Step(ref world.Enemy, input, Dt, ai.MotionCommand);
                combat.StepContact(world.Player, world.Player, previous, world.Enemy, Dt, 1f);
            }
            Assert.AreEqual(0, combat.Status.PlayerHealth, $"stationary target not defeated; time={combat.Status.ElapsedTime}, phase={ai.Phase}, enemy={world.Enemy.Position}");
            Assert.AreEqual(5, combat.Status.HitSequence);
        }

        [Test]
        public void RepeatedReadsDoNotAdvanceAimAndInvalidTargetClearsDamageAndPreview()
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock);
            FlightInput original = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Vec2 end = ai.GetPreviewPoint(ai.PreviewPointCount - 1);
            for (int i = 0; i < 10; i++)
            {
                FlightInput repeated = ai.ReadInput(Dt);
                Assert.AreEqual(original.Throttle, repeated.Throttle);
                Assert.AreEqual(original.Steer, repeated.Steer);
                Assert.AreEqual(1, ai.AttackId);
                Assert.AreEqual(.8f, ai.PhaseRemaining, .0001f);
                Assert.AreEqual(end, ai.GetPreviewPoint(ai.PreviewPointCount - 1));
            }
            clock.Now = .8f;
            ai.ReadInput(Dt);
            Assert.IsTrue(ai.AttackWindow.IsActive);
            world.Alive = false;
            Assert.AreEqual(-1f, ai.ReadInput(Dt).Throttle);
            Assert.AreEqual(AttackRunPhase.NoTarget, ai.Phase);
            Assert.IsFalse(ai.AttackWindow.IsActive);
            Assert.AreEqual(0, ai.PreviewPointCount);
        }

        [Test]
        public void ChargeHasNoPreviewAndRequestsItsExplicitSpeedAbility()
        {
            var world = new World();
            var clock = new Clock();
            var ai = Create(world, clock);
            ai.ReadInput(Dt);
            clock.Now = .8f;
            FlightInput input = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            Assert.AreEqual(0, ai.PreviewPointCount);
            Assert.AreEqual(5f, ai.SpeedMultiplier);
            var predicted = world.Enemy;
            FlightModel.From(Spec, Arena).Step(ref predicted, input, Dt, ai.SpeedMultiplier);
            Assert.Greater(predicted.Speed, 40f);
        }

        static PursuitChargeInputSource Create(World world, ICombatStatusView combat)
            => new PursuitChargeInputSource(world, 2, Spec, combat, Arena);

        sealed class World : IWorldView
        {
            public FlightState Enemy = FlightState.At(new Vec2(-6f, 0f), 0f);
            public FlightState Player = FlightState.At(Vec2.Zero);
            public bool Alive = true;
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(2, 2, Enemy)
                : new AircraftSnapshot(1, 1, Player, Alive, true);
        }

        sealed class Clock : ICombatStatusView
        {
            public float Now, Invulnerable;
            public int Hits;
            public CombatStatus Status => new CombatStatus(5, 5, Hits, Invulnerable, Now);
        }
    }
}
