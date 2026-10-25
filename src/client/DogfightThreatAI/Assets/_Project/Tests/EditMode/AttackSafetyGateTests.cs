using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class AttackSafetyGateTests
    {
        static readonly AircraftSpec Enemy = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly AircraftSpec Player = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);

        [TestCase(0f)]
        [TestCase(-.5585993f)]
        [TestCase(1.5707963f)]
        public void StationaryPlayer_WithATimedTurnOrStraightEscape_DoesNotBlockAllAttacks(float heading)
        {
            var gate = new AttackSafetyGate(Enemy, Player, null);
            var enemy = FlightState.At(new Vec2(4f, -2.5f), 2.5829933f);
            var player = FlightState.At(Vec2.Zero, heading);
            Assert.IsTrue(gate.CanStart(enemy, player, enemy.Heading), "Sustained full circles are not the only legal dodge: permit a short turn followed by straight thrust.");
        }

        [Test]
        public void Gate_DoesNotClaimEscapeWhenTheWholeArenaFitsInsideTheContactRadius()
        {
            var arena = Bounds2D.Centered(new Vec2(.2f, .2f));
            var gate = new AttackSafetyGate(Enemy, Player, arena);
            var enemy = FlightState.At(new Vec2(.1f, 0f), 3.1415927f);
            Assert.IsFalse(gate.CanStart(enemy, FlightState.At(Vec2.Zero), enemy.Heading));
        }

        [Test]
        public void Gate_RejectsAnAttackWhenThePlayerCannotPhysicallyClearItsPath()
        {
            var slowPlayer = new AircraftSpec(0f, .1f, .1f, 240f, .1f, 8f, .35f);
            var gate = new AttackSafetyGate(Enemy, slowPlayer, null);
            var enemy = FlightState.At(new Vec2(4f, -2.5f), 2.5829933f);
            Assert.IsFalse(gate.CanStart(enemy, FlightState.At(Vec2.Zero), enemy.Heading));
        }
    }
}
