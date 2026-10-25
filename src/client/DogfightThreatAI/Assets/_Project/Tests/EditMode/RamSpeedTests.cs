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
    public sealed class RamSpeedTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);

        [Test]
        public void AimingKeepsTheSameFullThrottleAsPursuit()
        {
            var world = new World();
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock);
            FlightInput input = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            Assert.AreEqual(1f, input.Throttle, "Tracking aim must not reduce the enemy's normal speed.");
            Assert.AreEqual(1f, ai.SpeedMultiplier);
        }

        [Test]
        public void ChargeRequestsFiveTimesNormalSpeedWithoutDrawingAChargePreview()
        {
            var ai = Charge(out _, out _);
            Assert.AreEqual(5f, ai.SpeedMultiplier);
            Assert.AreEqual(0, ai.PreviewPointCount, "Only the live yellow aim line remains; charge has no red marker.");
        }

        [Test]
        public void RealChargeStepTravelsAtFiveTimesTheNormalFlightSpeed()
        {
            var ai = Charge(out var world, out _);
            var flight = FlightModel.From(Spec);
            var normal = world.Enemy;
            for (int i = 0; i < 100; i++) flight.Step(ref normal, new FlightInput(1f, 0f), Dt);
            var charging = world.Enemy;
            Vec2 start = charging.Position;
            FlightInput input = ai.ReadInput(Dt);
            flight.Step(ref charging, input, Dt, ai.SpeedMultiplier);
            Assert.That(charging.Speed, Is.EqualTo(normal.Speed * 5f).Within(.001f),
                "The actual integrator must make a 45-unit/s dash; a property or throttle above one is insufficient.");
            Assert.That(Vec2.Distance(charging.Position, start), Is.EqualTo(charging.Speed * Dt).Within(.0001f));
            Assert.Greater(charging.Speed, 40f);
        }

        [Test]
        public void DashIsStraightAlongTheCommittedHeadingDespiteInheritedSidewaysVelocity()
        {
            var ai = Charge(out var world, out _);
            var state = world.Enemy;
            state.Velocity = new Vec2(2f, 5f);
            FlightModel.From(Spec).Step(ref state, ai.ReadInput(Dt), Dt, ai.SpeedMultiplier);
            Assert.AreEqual(0f, state.Velocity.Y, .00001f);
            Assert.Greater(state.Velocity.X, 40f);
        }

        [Test]
        public void EndOfChargeReturnsToNormalSpeedOnTheFirstRecoveryStep()
        {
            var ai = Charge(out var world, out var clock);
            var state = world.Enemy;
            var flight = FlightModel.From(Spec);
            flight.Step(ref state, ai.ReadInput(Dt), Dt, ai.SpeedMultiplier);
            Assert.Greater(state.Speed, 40f);
            world.Enemy = state;
            clock.Now = 1.7f;
            FlightInput recovery = ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
            Assert.AreEqual(1f, ai.SpeedMultiplier);
            flight.Step(ref state, recovery, Dt, ai.SpeedMultiplier);
            Assert.LessOrEqual(state.Speed, Spec.MaxSpeed + .001f,
                "The temporary dash must not leave the recovering enemy travelling at charge speed.");
        }

        [Test]
        public void LegacyFlightRetainsAccelerationWhileExplicitAbilityModeClampsItsNormalLimit()
        {
            var flight = FlightModel.From(Spec);
            var normal = FlightState.At(Vec2.Zero);
            flight.Step(ref normal, new FlightInput(1f, 0f), Dt);
            Assert.Less(normal.Speed, 1f);
            var legacyOverspeed = new FlightState { Position = Vec2.Zero, Heading = 0f, Velocity = new Vec2(45f, 0f) };
            var abilityOverspeed = legacyOverspeed;
            flight.Step(ref legacyOverspeed, new FlightInput(1f, 0f), Dt);
            flight.Step(ref abilityOverspeed, new FlightInput(1f, 0f), Dt, 1f);
            Assert.Greater(legacyOverspeed.Speed, 40f, "The existing three-argument integrator is a preserved contract.");
            Assert.LessOrEqual(abilityOverspeed.Speed, 9f);
        }

        [Test]
        public void LosingTheTargetCancelsTheTemporarySpeedAbility()
        {
            var ai = Charge(out var world, out _);
            Assert.AreEqual(5f, ai.SpeedMultiplier);
            world.Alive = false;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.NoTarget, ai.Phase);
            Assert.AreEqual(1f, ai.SpeedMultiplier);
        }

        static PursuitChargeInputSource Charge(out World world, out Clock clock)
        {
            world = new World();
            clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock);
            ai.ReadInput(Dt);
            clock.Now = .8f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            return ai;
        }

        sealed class World : IWorldView
        {
            public FlightState Enemy = FlightState.At(new Vec2(-6f, 0f));
            public bool Alive = true;
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(2, 2, Enemy)
                : new AircraftSnapshot(1, 1, FlightState.At(Vec2.Zero), Alive, true);
        }

        sealed class Clock : ICombatStatusView
        {
            public float Now;
            public CombatStatus Status => new CombatStatus(5, 5, 0, 0f, Now);
        }
    }
}
