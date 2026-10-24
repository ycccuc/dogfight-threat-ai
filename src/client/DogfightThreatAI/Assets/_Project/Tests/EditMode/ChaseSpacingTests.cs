using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class ChaseSpacingTests
    {
        const float Dt = 0.02f;
        const int TailSteps = 100;

        [TestCase(0f)]
        [TestCase(9f)]
        public void StationaryPlayer_ChaserBrakesEarlyAndSettlesAtTwoUnits(float initialSpeed)
        {
            var state = FlightState.At(Vec2.Zero);
            state.Velocity = new Vec2(initialSpeed, 0f);
            var result = RunStationary(DefaultSpec(), state, new Vec2(15f, 0f), 700);

            Assert.Greater(result.MinimumDistance, 1.2f,
                "Braking must preserve separation throughout the approach, not just at the last frame.");
            AssertStableStationarySpacing(result);
        }

        [Test]
        public void SlowBrakingAircraft_UsesItsActualBrakingCapability()
        {
            var spec = new AircraftSpec(2f, 9f, 4f, 240f, 0.1f, 8f, 0.35f);
            var state = FlightState.At(Vec2.Zero);
            state.Velocity = new Vec2(9f, 0f);
            // At acceleration 4, 24 units leave more than the physical stopping distance.
            var result = RunStationary(spec, state, new Vec2(24f, 0f), 1100);

            Assert.Greater(result.MinimumDistance, 1.2f);
            AssertStableStationarySpacing(result);
        }

        [Test]
        public void TurningApproach_AccountsForRealLateralDrift()
        {
            var result = RunStationary(DefaultSpec(), FlightState.At(Vec2.Zero), new Vec2(12f, 8f), 850);

            Assert.Greater(result.MinimumDistance, 1.2f);
            AssertStableStationarySpacing(result);
        }

        [Test]
        public void FullSpeedPlayerBrakes_ChaserRetainsSeparation()
        {
            var spec = DefaultSpec();
            var player = FlightState.At(new Vec2(3f, 0f));
            player.Velocity = new Vec2(9f, 0f);
            var chaser = FlightState.At(Vec2.Zero);
            chaser.Velocity = new Vec2(9f, 0f);
            var world = new FakeWorld(chaser, player);
            var source = new ChaseInputSource(world, 1, spec);
            var chaserModel = FlightModel.From(spec);
            var playerModel = FlightModel.From(spec);
            var result = NewSummary();

            for (int tick = 0; tick < 750; tick++)
            {
                world.Publish(chaser, player);
                var input = source.ReadInput(Dt);
                AssertLegal(input);
                // Both aircraft obey the same real acceleration limit; the player does not teleport to rest.
                playerModel.Step(ref player, new FlightInput(tick < 100 ? 1f : -1f, 0f), Dt);
                chaserModel.Step(ref chaser, input, Dt);
                Observe(ref result, chaser, player.Position, tick >= 750 - TailSteps);
            }

            Assert.Greater(result.MinimumDistance, 1.2f);
            AssertStableStationarySpacing(result);
        }

        [TestCase(0.6f)]
        [TestCase(1f)]
        public void SlowPlayerBelowChaserMinSpeed_IsFollowedWithoutClosingTheGap(float speed)
        {
            var chaserSpec = DefaultSpec();
            // This test player has no minimum-speed floor, while the chaser still has MinSpeed=2.
            var playerSpec = new AircraftSpec(0f, 9f, 18f, 240f, 0f, 8f, 0.35f);
            var player = FlightState.At(new Vec2(10f, 0f));
            player.Velocity = new Vec2(speed, 0f);
            var chaser = FlightState.At(Vec2.Zero);
            var world = new FakeWorld(chaser, player);
            var source = new ChaseInputSource(world, 1, chaserSpec);
            var chaserModel = FlightModel.From(chaserSpec);
            var playerModel = FlightModel.From(playerSpec);
            var result = NewSummary();

            for (int tick = 0; tick < 1000; tick++)
            {
                world.Publish(chaser, player);
                var input = source.ReadInput(Dt);
                AssertLegal(input);
                playerModel.Step(ref player, new FlightInput(speed / playerSpec.MaxSpeed, 0f), Dt);
                chaserModel.Step(ref chaser, input, Dt);
                Observe(ref result, chaser, player.Position, tick >= 1000 - TailSteps);
            }

            Assert.Greater(result.MinimumDistance, 1.2f);
            Assert.That(result.TailMinimumDistance, Is.GreaterThanOrEqualTo(1.7f));
            Assert.That(result.TailMaximumDistance, Is.LessThanOrEqualTo(2.4f));
            Assert.That(chaser.Speed, Is.InRange(0.15f, 1.4f),
                "The chaser must actually follow the slow player, rather than remaining parked.");
            Assert.Greater(chaser.Position.X, 10f);
        }

        [TestCase(1f)]
        [TestCase(0f)]
        public void InitiallyTooCloseOrCoincident_ActivelyRestoresSeparation(float initialDistance)
        {
            var state = FlightState.At(new Vec2(initialDistance, 0f), MathF.PI);
            var result = RunStationary(DefaultSpec(), state, Vec2.Zero, 900);

            // There is no minimum-distance guarantee before recovery: the scene starts too close by design.
            AssertStableStationarySpacing(result);
            Assert.Greater(Vec2.Distance(result.FinalState.Position, state.Position), 0.6f,
                "Braking in place is not sufficient when the initial separation is already wrong.");
        }

        [Test]
        public void IdenticalMovingWorldSequence_ReplaysSpacingInputsAndStatesExactly()
        {
            var spec = DefaultSpec();
            var a = FlightState.At(Vec2.Zero);
            var b = a;
            var player = FlightState.At(new Vec2(10f, 5f));
            var worldA = new FakeWorld(a, player);
            var worldB = new FakeWorld(b, player);
            var sourceA = new ChaseInputSource(worldA, 1, spec);
            var sourceB = new ChaseInputSource(worldB, 1, spec);
            var modelA = FlightModel.From(spec);
            var modelB = FlightModel.From(spec);
            var playerModel = FlightModel.From(spec);

            for (int tick = 0; tick < 650; tick++)
            {
                worldA.Publish(a, player);
                worldB.Publish(b, player);
                var inputA = sourceA.ReadInput(Dt);
                var inputB = sourceB.ReadInput(Dt);
                AssertLegal(inputA);
                Assert.AreEqual(inputA.Throttle, inputB.Throttle);
                Assert.AreEqual(inputA.Steer, inputB.Steer);
                modelA.Step(ref a, inputA, Dt);
                modelB.Step(ref b, inputB, Dt);
                Assert.AreEqual(a, b);
                var playerInput = tick < 150 ? new FlightInput(0.5f, 0f) :
                    tick < 200 ? new FlightInput(0.5f, 0.7f) : new FlightInput(-1f, 0f);
                playerModel.Step(ref player, playerInput, Dt);
            }
        }

        [TestCase(0f)]
        [TestCase(1f)]
        public void FullSpeedPlayerAlreadyTooClose_ChaserFallsBackToSafeSpacing(float chaserOffset)
        {
            var spec = DefaultSpec();
            var player = FlightState.At(Vec2.Zero);
            player.Velocity = new Vec2(9f, 0f);
            var chaser = FlightState.At(new Vec2(chaserOffset, 0f));
            chaser.Velocity = new Vec2(9f, 0f);
            var world = new FakeWorld(chaser, player);
            var ai = new ChaseInputSource(world, 1, spec);
            var playerModel = FlightModel.From(spec);
            var chaserModel = FlightModel.From(spec);
            var result = NewSummary();
            for (int tick = 0; tick < 900; tick++)
            {
                world.Publish(chaser, player);
                var input = ai.ReadInput(Dt);
                AssertLegal(input);
                playerModel.Step(ref player, new FlightInput(1f, 0f), Dt);
                chaserModel.Step(ref chaser, input, Dt);
                Observe(ref result, chaser, player.Position, tick >= 900 - TailSteps);
            }
            Assert.That(result.TailMinimumDistance, Is.GreaterThanOrEqualTo(1.7f));
            Assert.That(result.TailMaximumDistance, Is.LessThanOrEqualTo(2.4f));
            Assert.Less(chaser.Position.X, player.Position.X,
                "A speed-limited chaser should fall behind instead of trying to outrun a full-speed player forever.");
        }

        [Test]
        public void SpacingDecision_DoesNotModifyThePublishedWorld()
        {
            var state = FlightState.At(Vec2.Zero, 0.3f);
            state.Velocity = new Vec2(8f, 1f);
            var player = FlightState.At(new Vec2(5f, 3f), 0.7f);
            player.Velocity = new Vec2(0.4f, 0.6f);
            var world = new FakeWorld(state, player);
            var before = (AircraftSnapshot[])world.Aircraft.Clone();
            var source = new ChaseInputSource(world, 1, DefaultSpec());

            for (int i = 0; i < 100; i++) AssertLegal(source.ReadInput(Dt));

            CollectionAssert.AreEqual(before, world.Aircraft,
                "Any prediction must advance local state copies, never the published aircraft state.");
        }

        static SimulationSummary RunStationary(AircraftSpec spec, FlightState state, Vec2 targetPosition, int steps)
        {
            var player = FlightState.At(targetPosition);
            var world = new FakeWorld(state, player);
            var source = new ChaseInputSource(world, 1, spec);
            var model = FlightModel.From(spec);
            var result = NewSummary();

            for (int tick = 0; tick < steps; tick++)
            {
                world.Publish(state, player);
                var input = source.ReadInput(Dt);
                AssertLegal(input);
                model.Step(ref state, input, Dt);
                Observe(ref result, state, targetPosition, tick >= steps - TailSteps);
            }
            return result;
        }

        static void Observe(ref SimulationSummary summary, FlightState state, Vec2 targetPosition, bool inTail)
        {
            float distance = Vec2.Distance(state.Position, targetPosition);
            Assert.IsFalse(float.IsNaN(distance) || float.IsInfinity(distance));
            summary.MinimumDistance = MathF.Min(summary.MinimumDistance, distance);
            if (inTail)
            {
                summary.TailMinimumDistance = MathF.Min(summary.TailMinimumDistance, distance);
                summary.TailMaximumDistance = MathF.Max(summary.TailMaximumDistance, distance);
                summary.TailMaximumSpeed = MathF.Max(summary.TailMaximumSpeed, state.Speed);
            }
            summary.FinalState = state;
        }

        static void AssertStableStationarySpacing(SimulationSummary result)
        {
            Assert.That(result.TailMinimumDistance, Is.GreaterThanOrEqualTo(1.7f),
                "The last two seconds must remain outside the player's occupied space.");
            Assert.That(result.TailMaximumDistance, Is.LessThanOrEqualTo(2.4f),
                "The chaser should settle near the configured two-unit spacing.");
            Assert.Less(result.TailMaximumSpeed, 0.2f,
                "A stationary target should result in a stable stop, not an orbit that occasionally passes the desired distance.");
        }

        static void AssertLegal(FlightInput input)
        {
            Assert.IsFalse(float.IsNaN(input.Throttle) || float.IsInfinity(input.Throttle));
            Assert.IsFalse(float.IsNaN(input.Steer) || float.IsInfinity(input.Steer));
            Assert.That(input.Throttle, Is.InRange(-1f, 1f));
            Assert.That(input.Steer, Is.InRange(-1f, 1f));
        }

        static AircraftSpec DefaultSpec() => new AircraftSpec(2f, 9f, 18f, 240f, 0.1f, 8f, 0.35f);
        static SimulationSummary NewSummary() => new SimulationSummary
        {
            MinimumDistance = float.PositiveInfinity,
            TailMinimumDistance = float.PositiveInfinity
        };

        struct SimulationSummary
        {
            public float MinimumDistance;
            public float TailMinimumDistance;
            public float TailMaximumDistance;
            public float TailMaximumSpeed;
            public FlightState FinalState;
        }

        sealed class FakeWorld : IWorldView
        {
            internal readonly AircraftSnapshot[] Aircraft = new AircraftSnapshot[2];
            public FakeWorld(FlightState self, FlightState player) { Publish(self, player); }
            public int AircraftCount => Aircraft.Length;
            public AircraftSnapshot GetAircraft(int index) => Aircraft[index];
            public void Publish(FlightState self, FlightState player)
            {
                Aircraft[0] = new AircraftSnapshot(1, 1, self);
                Aircraft[1] = new AircraftSnapshot(2, 2, player, isPlayerControlled: true);
            }
        }
    }
}
