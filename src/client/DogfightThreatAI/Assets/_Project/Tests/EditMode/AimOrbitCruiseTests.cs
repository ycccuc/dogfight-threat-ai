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
    public sealed class AimOrbitCruiseTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);

        [Test]
        public void AimingFromRestImmediatelyMovesAtNormalCruiseSpeed()
        {
            var world = new World();
            var ai = new PursuitChargeInputSource(world, 2, Spec, new Clock());
            var input = ai.ReadInput(Dt);
            var cruise = (object)ai as IFlightCruiseSource;
            var state = world.Enemy;
            FlightModel.From(Spec).Step(ref state, input, Dt, ai.SpeedMultiplier, cruise != null && cruise.MaintainCruiseSpeed);
            Assert.That(state.Speed, Is.InRange(8.9f, 9.01f), "An enemy born inside aiming range must move, rather than spend the warning accelerating from rest.");
        }

        [Test]
        public void PoweredCruiseDoesNotStallDuringAReverseTurn()
        {
            var flight = FlightModel.From(Spec);
            var state = new FlightState { Heading = 0f, Velocity = new Vec2(-9f, 0f) };
            for (int tick = 0; tick < 40; tick++)
            {
                flight.Step(ref state, new FlightInput(1f, 1f), Dt, 1f, true);
                Assert.That(state.Speed, Is.InRange(8.9f, 9.01f), $"Powered aiming stalled at tick {tick}.");
            }
        }

        [Test]
        public void OrdinaryFlightStillAcceleratesNormallyWithoutPoweredCruise()
        {
            var flight = FlightModel.From(Spec);
            var ordinary = FlightState.At(Vec2.Zero);
            var cruise = ordinary;
            flight.Step(ref ordinary, new FlightInput(1f, 0f), Dt);
            flight.Step(ref cruise, new FlightInput(1f, 0f), Dt, 1f, true);
            Assert.Less(ordinary.Speed, 1f);
            Assert.Greater(cruise.Speed, 8.9f);
        }

        [Test]
        public void EarlyAimGuidesTowardThePlayersRearSideInsteadOfTheCenter()
        {
            var world = new World();
            var ai = new PursuitChargeInputSource(world, 2, Spec, new Clock());
            ai.ReadInput(Dt);
            var offset = ai.ManeuverPoint - world.Player.Position;
            Assert.Less(Vec2.Dot(offset, world.Player.Forward), -2f);
            Assert.Greater(System.Math.Abs(offset.X), 1f);
            Assert.Greater(Vec2.Dot(ai.AimDirection, (world.Player.Position - world.Enemy.Position).Normalized), .999f,
                "The yellow aim still follows the player, even when the flight guidance goes around the rear side.");
        }

        [Test]
        public void OrbitSideDoesNotFlipWhenTheTargetOrEnemyCrossesIt()
        {
            var world = new World();
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock);
            ai.ReadInput(Dt);
            float firstSide = ai.ManeuverPoint.X - world.Player.Position.X;
            world.Player = FlightState.At(new Vec2(1f, 1f), System.MathF.PI * .5f);
            world.Enemy = FlightState.At(new Vec2(6f, 0f));
            clock.Now = .2f;
            ai.ReadInput(Dt);
            float laterSide = ai.ManeuverPoint.X - world.Player.Position.X;
            Assert.Greater(firstSide * laterSide, 1f, "An aim run must not zig-zag between sides as positions cross.");
            Assert.AreEqual(.6f, ai.PhaseRemaining, .0001f);
            Assert.AreEqual(1, ai.AttackId);
        }

        [Test]
        public void OrbitGivesWayToDirectAimBeforeTheOriginalDeadline()
        {
            var world = new World();
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock);
            ai.ReadInput(Dt);
            Assert.Greater(Vec2.Distance(ai.ManeuverPoint, world.Player.Position), 2f);
            clock.Now = .4f;
            ai.ReadInput(Dt);
            Assert.Less(Vec2.Distance(ai.ManeuverPoint, world.Player.Position), .001f);
            Assert.AreEqual(.4f, ai.PhaseRemaining, .0001f);
            clock.Now = .8f;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            Assert.AreEqual(1, ai.AttackId);
        }

        [Test]
        public void CruiseCapabilityIsOnlyActiveDuringMovingAim()
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-20f, 0f)) };
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock);
            var cruise = (object)ai as IFlightCruiseSource;
            Assert.IsNotNull(cruise, "The AI must explicitly request its normal-speed flight ability.");
            ai.ReadInput(Dt);
            Assert.IsFalse(cruise.MaintainCruiseSpeed);
            world.Enemy = FlightState.At(new Vec2(-6f, 0f));
            ai.ReadInput(Dt);
            Assert.IsTrue(cruise.MaintainCruiseSpeed);
            clock.Now = .8f;
            ai.ReadInput(Dt);
            Assert.IsFalse(cruise.MaintainCruiseSpeed);
            Assert.AreEqual(5f, ai.SpeedMultiplier);
        }

        sealed class World : IWorldView
        {
            public FlightState Enemy = FlightState.At(new Vec2(-6f, 0f));
            public FlightState Player = FlightState.At(Vec2.Zero, System.MathF.PI * .5f);
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
