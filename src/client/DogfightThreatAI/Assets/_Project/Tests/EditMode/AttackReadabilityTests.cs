using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class AttackReadabilityTests
    {
        [Test]
        public void AttackHasAnObservableWarningBeforeReachingStationaryPlayer()
        {
            var spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
            var player = FlightState.At(Vec2.Zero);
            var enemy = FlightState.At(new Vec2(4f, -2.5f), 1.5707963f);
            var world = new World(enemy, player);
            var combat = new ContactCombatModel();
            var ai = new AttackRunInputSource(world, 2, spec, combat);
            var model = FlightModel.From(spec);
            bool warned = false;
            for (int i = 0; i < 1500; i++)
            {
                world.Enemy = enemy;
                var input = ai.ReadInput(.02f);
                warned |= ai.Phase.ToString() == "Windup";
                var previous = enemy;
                model.Step(ref enemy, input, .02f);
                combat.Step(player, player, previous, enemy, .02f);
                if (Vec2.Distance(enemy.Position, player.Position) <= 1f)
                {
                    Assert.IsTrue(warned, "Contact must be preceded by an actual visible Windup, not an instantaneous homing attack.");
                    return;
                }
            }
            Assert.IsTrue(warned, "The enemy must actually prepare an attack, not avoid the requirement by never attacking.");
        }

        sealed class World : IWorldView
        {
            public FlightState Enemy;
            readonly FlightState _player;
            public World(FlightState enemy, FlightState player) { Enemy = enemy; _player = player; }
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(2, 2, Enemy)
                : new AircraftSnapshot(1, 1, _player, isPlayerControlled: true);
        }
    }
}
