using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Lobby;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class LobbySimulationTests
    {
        static LobbySimulation Create()
        {
            return new LobbySimulation(
            new AircraftSpec(0, 6, 12, 240, 2, 12, .15f), Bounds2D.Centered(new Vec2(5, 3)), Vec2.Zero, 0);
        }

        [Test]
        public void FirstSnapshotStartsAtSpawnAndIdleAircraftStaysStill()
        {
            var lobby = Create();
            for (int i = 0; i < 50; i++)
            {
                lobby.Tick(new FlightInput(-1, 0), .02f);
            }
            Assert.That(lobby.Current.Position, Is.EqualTo(Vec2.Zero));
            Assert.That(lobby.Previous.Position, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void ForwardIntentMovesAndBrakingReturnsToRest()
        {
            var lobby = Create();
            for (int i = 0; i < 30; i++)
            {
                lobby.Tick(new FlightInput(1, 0), .02f);
            }
            Assert.That(lobby.Current.Position.X, Is.GreaterThan(0));
            Assert.That(lobby.Previous.Position.X, Is.LessThan(lobby.Current.Position.X));
            for (int i = 0; i < 50; i++)
            {
                lobby.Tick(new FlightInput(-1, 0), .02f);
            }
            Assert.That(lobby.Current.Speed, Is.LessThan(.001f));
        }

        [Test]
        public void MovementRemainsInsideRoomAndTurningChangesDirection()
        {
            var lobby = Create();
            for (int i = 0; i < 500; i++)
            {
                lobby.Tick(new FlightInput(1, 0), .02f);
            }
            Assert.That(lobby.Current.Position.X, Is.LessThanOrEqualTo(5));
            for (int i = 0; i < 25; i++)
            {
                lobby.Tick(new FlightInput(1, 1), .02f);
            }
            Assert.That(MathF.Abs(lobby.Current.Heading), Is.GreaterThan(.1f));
            Assert.That(Bounds2D.Centered(new Vec2(5, 3)).Contains(lobby.Current.Position), Is.True);
        }

        [Test]
        public void DifferentLobbyInstancesHaveIndependentState()
        {
            var first = Create();
            var second = Create();
            first.Tick(new FlightInput(1, 0), .02f);
            Assert.That(first.Current.Position.X, Is.GreaterThan(0));
            Assert.That(second.Current.Position, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void InvalidStepDoesNotChangeState()
        {
            var lobby = Create();
            Assert.Throws<ArgumentOutOfRangeException>(() => lobby.Tick(FlightInput.Neutral, float.NaN));
            Assert.Throws<ArgumentException>(() => lobby.Tick(new FlightInput(float.PositiveInfinity, 0), .02f));
            Assert.That(lobby.Current.Position, Is.EqualTo(Vec2.Zero));
        }
    }
}
