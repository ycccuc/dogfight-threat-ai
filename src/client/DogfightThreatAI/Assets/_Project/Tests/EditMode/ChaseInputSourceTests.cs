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
    public sealed class ChaseInputSourceTests
    {
        const float Dt = 0.02f;

        [Test]
        public void PlayerAhead_ProducesForwardInput()
        {
            var ai = NewAi(new FakeWorld(Self(), Player(2, 20f, 0f)));
            AssertInput(ai.ReadInput(Dt), 1f, 0f);
        }

        [TestCase(0f, 10f, 1f)]
        [TestCase(0f, -10f, -1f)]
        public void SteerSign_MatchesFlightModel(float x, float y, float expectedSign)
        {
            var ai = NewAi(new FakeWorld(Self(), Player(2, x, y)));
            var state = FlightState.At(Vec2.Zero);
            var input = ai.ReadInput(Dt);
            NewModel().Step(ref state, input, Dt);
            Assert.AreEqual(expectedSign, MathF.Sign(input.Steer));
            Assert.AreEqual(expectedSign, MathF.Sign(state.Heading));
        }

        [Test]
        public void SmallAngle_DoesNotOvershootInOneStep()
        {
            const float angle = 0.01f;
            var position = Vec2.FromAngle(angle) * 20f;
            var ai = NewAi(new FakeWorld(Self(), Player(2, position.X, position.Y)));
            var state = FlightState.At(Vec2.Zero);
            NewModel().Step(ref state, ai.ReadInput(Dt), Dt);
            Assert.AreEqual(angle, state.Heading, 1e-5f);
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void AcrossPiBoundary_TakesShortestTurn(float sign)
        {
            float heading = sign * (MathF.PI - 0.01f);
            Vec2 target = Vec2.FromAngle(-heading) * 20f;
            var ai = NewAi(new FakeWorld(Self(heading), Player(2, target.X, target.Y)));
            var input = ai.ReadInput(Dt);
            Assert.AreEqual(sign, MathF.Sign(input.Steer));
            Assert.Less(MathF.Abs(input.Steer), 1f);
        }

        [Test]
        public void AccumulatedHeading_StillFacesTargetWithoutFullTurn()
        {
            float heading = 2000f * MathF.PI + 0.3f;
            Vec2 target = Vec2.FromAngle(heading) * 20f;
            var input = NewAi(new FakeWorld(Self(heading), Player(2, target.X, target.Y))).ReadInput(Dt);
            Assert.AreEqual(0f, input.Steer, 1e-4f);
        }

        [Test]
        public void TargetSelection_IgnoresSelfFriendDeadAndNonPlayer()
        {
            var world = new FakeWorld(Self(),
                new AircraftSnapshot(3, 1, FlightState.At(new Vec2(1f, 0f)), isPlayerControlled: true),
                new AircraftSnapshot(4, 2, FlightState.At(new Vec2(2f, 0f)), isAlive: false, isPlayerControlled: true),
                new AircraftSnapshot(5, 2, FlightState.At(new Vec2(3f, 0f))),
                Player(6, 0f, 20f));
            AssertSteerSign(NewAi(world).ReadInput(Dt), 1f);
        }

        [Test]
        public void NearestEnemyPlayer_IsSelected()
        {
            var ai = NewAi(new FakeWorld(Self(), Player(2, 0f, 30f), Player(3, 0f, -10f)));
            AssertSteerSign(ai.ReadInput(Dt), -1f);
        }

        [Test]
        public void EqualDistance_UsesStableIdInsteadOfEnumerationOrder()
        {
            var first = NewAi(new FakeWorld(Self(), Player(9, 0f, -20f), Player(2, 0f, 20f)));
            var second = NewAi(new FakeWorld(Player(2, 0f, 20f), Player(9, 0f, -20f), Self()));
            var firstInput = first.ReadInput(Dt);
            var secondInput = second.ReadInput(Dt);
            AssertSteerSign(firstInput, 1f);
            AssertSteerSign(secondInput, 1f);
            AssertInput(secondInput, firstInput.Throttle, firstInput.Steer);
        }

        [Test]
        public void NoEnemyPlayer_BrakesRatherThanCoasts()
        {
            AssertInput(NewAi(new FakeWorld(Self())).ReadInput(Dt), -1f, 0f);
        }

        [Test]
        public void MissingOrDeadSelf_Brakes()
        {
            AssertInput(NewAi(new FakeWorld(Player(2, 10f, 0f))).ReadInput(Dt), -1f, 0f);
            var dead = new AircraftSnapshot(1, 1, FlightState.At(Vec2.Zero), isAlive: false);
            AssertInput(NewAi(new FakeWorld(dead, Player(2, 10f, 0f))).ReadInput(Dt), -1f, 0f);
        }

        [TestCase(0.5f)]
        public void AtConfiguredSpacing_Brakes(float distance)
        {
            var state = FlightState.At(Vec2.Zero);
            state.Velocity = new Vec2(5f, 0f);
            var ai = NewAi(new FakeWorld(new AircraftSnapshot(1, 1, state), Player(2, distance, 0f)));
            var input = ai.ReadInput(Dt);
            AssertInput(input, -1f, 0f);
            NewModel().Step(ref state, input, Dt);
            Assert.Less(state.Speed, 5f);
        }

        [Test]
        public void BehindTarget_TurnIsDeterministic()
        {
            var ai = NewAi(new FakeWorld(Self(), Player(2, -20f, 0f)));
            var first = ai.ReadInput(Dt);
            Assert.AreEqual(1f, MathF.Abs(first.Steer));
            for (int i = 0; i < 20; i++) AssertInput(ai.ReadInput(Dt), first.Throttle, first.Steer);
        }

        [Test]
        public void TargetMovementAndRemoval_AreReadOnEveryDecision()
        {
            var world = new FakeWorld(Self(), Player(2, 0f, 20f));
            var ai = NewAi(world);
            AssertSteerSign(ai.ReadInput(Dt), 1f);
            world.Aircraft[1] = Player(2, 0f, -20f);
            AssertSteerSign(ai.ReadInput(Dt), -1f);
            world.Aircraft = new[] { Self() };
            AssertInput(ai.ReadInput(Dt), -1f, 0f);
        }

        [TestCase(0f)]
        [TestCase(-0.02f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDeltaTime_ReturnsNeutral(float dt)
        {
            AssertInput(NewAi(new FakeWorld(Self(), Player(2, 20f, 0f))).ReadInput(dt), 0f, 0f);
        }

        [Test]
        public void InvalidCoordinates_AreIgnoredWithoutPoisoningInput()
        {
            var ai = NewAi(new FakeWorld(Self(), Player(2, float.NaN, 0f),
                Player(3, float.PositiveInfinity, 0f), Player(4, 0f, -20f)));
            AssertSteerSign(ai.ReadInput(Dt), -1f);
            var invalidSelf = new AircraftSnapshot(1, 1, FlightState.At(new Vec2(float.NaN, 0f)));
            AssertInput(NewAi(new FakeWorld(invalidSelf, Player(2, 10f, 0f))).ReadInput(Dt), -1f, 0f);
            AssertInput(NewAi(new FakeWorld(Self(float.NaN), Player(2, 10f, 0f))).ReadInput(Dt), -1f, 0f);
        }

        [Test]
        public void ExtremeFiniteCoordinates_StillSelectNearestPlayer()
        {
            var self = new AircraftSnapshot(1, 1, FlightState.At(new Vec2(-float.MaxValue, 0f)));
            var ai = NewAi(new FakeWorld(self, Player(2, float.MaxValue, 0f), Player(3, 0f, float.MaxValue)));
            AssertSteerSign(ai.ReadInput(Dt), 1f);
        }

        [Test]
        public void ConfiguredCruiseThrottleAndSpacing_AreUsed()
        {
            var ahead = new FakeWorld(Self(), Player(2, 20f, 0f));
            AssertInput(new ChaseInputSource(ahead, 1, Spec(), 0.4f, 1f).ReadInput(Dt), 0.4f, 0f);
            var side = new FakeWorld(Self(), Player(2, 0f, 2f));
            AssertSteerSign(new ChaseInputSource(side, 1, Spec(), 0.4f, 1f).ReadInput(Dt), 1f);
            AssertSteerSign(new ChaseInputSource(side, 1, Spec(), 0.4f, 3f).ReadInput(Dt), -1f);
        }

        [Test]
        public void Decisions_DoNotModifyWorldSnapshots()
        {
            var world = new FakeWorld(Self(0.3f), Player(2, 20f, 10f));
            var before = (AircraftSnapshot[])world.Aircraft.Clone();
            var ai = NewAi(world);
            for (int i = 0; i < 100; i++) ai.ReadInput(Dt);
            CollectionAssert.AreEqual(before, world.Aircraft);
        }

        [Test]
        public void SeededWorlds_AlwaysProduceFiniteLegalInputs()
        {
            var random = new Random(12345);
            for (int i = 0; i < 500; i++)
            {
                var world = new FakeWorld(Self((float)(random.NextDouble() * 20000 - 10000)),
                    Player(2, (float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 300 - 150)));
                var input = NewAi(world).ReadInput(Dt);
                Assert.IsFalse(float.IsNaN(input.Throttle) || float.IsInfinity(input.Throttle));
                Assert.IsFalse(float.IsNaN(input.Steer) || float.IsInfinity(input.Steer));
                Assert.That(input.Throttle, Is.InRange(-1f, 1f));
                Assert.That(input.Steer, Is.InRange(-1f, 1f));
            }
        }

        [TestCase(30f, 0f)]
        [TestCase(20f, 15f)]
        [TestCase(-20f, -10f)]
        public void RealFlightModel_ApproachesPlayer(float x, float y)
        {
            var target = Player(2, x, y);
            var world = new FakeWorld(Self(), target);
            var ai = NewAi(world);
            var model = NewModel();
            var state = world.Aircraft[0].Flight;
            float closest = Vec2.Distance(state.Position, target.Flight.Position);
            for (int tick = 0; tick < 600; tick++)
            {
                world.Aircraft[0] = new AircraftSnapshot(1, 1, state);
                model.Step(ref state, ai.ReadInput(Dt), Dt);
                closest = MathF.Min(closest, Vec2.Distance(state.Position, target.Flight.Position));
            }
            Assert.Less(closest, 1f, "The AI must actually approach through FlightModel, not teleport the state.");
        }

        [Test]
        public void IdenticalWorldSequence_ReplaysExactInputsAndFlightStates()
        {
            var a = new FakeWorld(Self(), Player(2, 30f, 10f));
            var b = new FakeWorld(Self(), Player(2, 30f, 10f));
            var aiA = NewAi(a);
            var aiB = NewAi(b);
            var stateA = a.Aircraft[0].Flight;
            var stateB = b.Aircraft[0].Flight;
            var modelA = NewModel();
            var modelB = NewModel();
            for (int tick = 0; tick < 400; tick++)
            {
                a.Aircraft[0] = new AircraftSnapshot(1, 1, stateA);
                b.Aircraft[0] = new AircraftSnapshot(1, 1, stateB);
                var player = Player(2, 30f + tick * Dt * 2f, 10f);
                a.Aircraft[1] = b.Aircraft[1] = player;
                var inputA = aiA.ReadInput(Dt);
                var inputB = aiB.ReadInput(Dt);
                AssertInput(inputB, inputA.Throttle, inputA.Steer);
                modelA.Step(ref stateA, inputA, Dt);
                modelB.Step(ref stateB, inputB, Dt);
                Assert.AreEqual(stateA, stateB);
            }
        }

        [Test]
        public void Constructor_RejectsNullWorld()
        {
            Assert.Throws<ArgumentNullException>(() => new ChaseInputSource(null, 1, Spec()));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_RejectsInvalidTurnRate(float rate)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(turnRate: rate)));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(1.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_RejectsInvalidThrottle(float throttle)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(), throttle));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_RejectsInvalidArrivalRadius(float radius)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(), 1f, radius));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidVelocity_DoesNotPoisonSpacingDecision(float value)
        {
            var invalidSelf = FlightState.At(Vec2.Zero);
            invalidSelf.Velocity = new Vec2(value, 0f);
            AssertInput(NewAi(new FakeWorld(new AircraftSnapshot(1, 1, invalidSelf), Player(2, 10f, 0f))).ReadInput(Dt), -1f, 0f);

            var invalidPlayer = FlightState.At(new Vec2(0f, 5f));
            invalidPlayer.Velocity = new Vec2(value, 0f);
            var world = new FakeWorld(Self(),
                new AircraftSnapshot(2, 2, invalidPlayer, isPlayerControlled: true), Player(3, 0f, -20f));
            AssertSteerSign(NewAi(world).ReadInput(Dt), -1f);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_RejectsInvalidMaxSpeed(float speed)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(maxSpeed: speed)));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_RejectsInvalidAcceleration(float acceleration)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(acceleration: acceleration)));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_RejectsInvalidMinSpeed(float speed)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(minSpeed: speed)));
        }

        [Test]
        public void Constructor_RejectsMinSpeedAboveMaxSpeed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(minSpeed: 9f, maxSpeed: 8f)));
        }

        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Constructor_RejectsInvalidDistanceTolerance(float tolerance)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChaseInputSource(new FakeWorld(), 1, Spec(), distanceTolerance: tolerance));
        }

        static ChaseInputSource NewAi(IWorldView world) => new ChaseInputSource(world, 1, Spec(), arrivalRadius: 0.5f);
        static AircraftSpec Spec(float minSpeed = 2f, float maxSpeed = 8f, float acceleration = 12f,
                                 float turnRate = 240f)
            => new AircraftSpec(minSpeed, maxSpeed, acceleration, turnRate, 0f, 6f, 0.35f);
        static FlightModel NewModel() => new FlightModel(2f, 8f, 12f, 240f, 0f);
        static AircraftSnapshot Self(float heading = 0f)
            => new AircraftSnapshot(1, 1, FlightState.At(Vec2.Zero, heading));
        static AircraftSnapshot Player(int id, float x, float y)
            => new AircraftSnapshot(id, 2, FlightState.At(new Vec2(x, y)), isPlayerControlled: true);

        static void AssertInput(FlightInput input, float throttle, float steer)
        {
            Assert.AreEqual(throttle, input.Throttle, 1e-5f);
            Assert.AreEqual(steer, input.Steer, 1e-5f);
        }

        static void AssertSteerSign(FlightInput input, float sign)
        {
            Assert.IsFalse(float.IsNaN(input.Throttle) || float.IsInfinity(input.Throttle));
            Assert.IsFalse(float.IsNaN(input.Steer) || float.IsInfinity(input.Steer));
            Assert.That(input.Throttle, Is.InRange(-1f, 1f));
            Assert.That(input.Steer, Is.InRange(-1f, 1f));
            Assert.AreEqual(sign, MathF.Sign(input.Steer));
        }

        sealed class FakeWorld : IWorldView
        {
            internal AircraftSnapshot[] Aircraft;
            public FakeWorld(params AircraftSnapshot[] aircraft) { Aircraft = aircraft; }
            public int AircraftCount => Aircraft.Length;
            public AircraftSnapshot GetAircraft(int index) => Aircraft[index];
        }
    }
}
