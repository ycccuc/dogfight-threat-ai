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
    public sealed class HardenedPressureTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec PlayerSpec = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);
        static readonly AircraftSpec EnemySpec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);

        [TestCase(1, 1f, 8.7f)] [TestCase(1, -1f, 8.5f)]
        [TestCase(2, 1f, 10.9f)] [TestCase(2, -1f, 9.2f)]
        [TestCase(3, 1f, 12f)] [TestCase(3, -1f, 12f)]
        [TestCase(4, 1f, 4.5f)] [TestCase(4, -1f, 4.5f)]
        public void EveryTierDeliversThreeRealContactsWithinItsStrengthenedPressureBudget(int level, float turn, float limit)
        {
            var survival = new List<float>();
            foreach (float distance in new[] { 12f, 18f, 24f })
            foreach (float heading in new[] { 0f, MathF.PI * .5f })
            foreach (float side in new[] { -1f, 1f })
            {
                var world = new World
                {
                    Player = new FlightState { Heading = heading, Velocity = Vec2.FromAngle(heading) * 9f },
                    Enemy = new FlightState { Position = new Vec2(-distance, side * 2f), Velocity = new Vec2(9f, 0f) }
                };
                var combat = new ContactCombatModel(5, 1, .7f, 1f);
                var ai = new PursuitChargeInputSource(world, 2, EnemySpec, combat, null, level);
                var playerFlight = FlightModel.From(PlayerSpec);
                var enemyFlight = FlightModel.From(EnemySpec);
                for (int tick = 0; tick < 3000 && combat.Status.IsPlayerAlive && combat.Status.HitSequence < 3; tick++)
                {
                    var previousPlayer = world.Player;
                    var previousEnemy = world.Enemy;
                    world.Time = combat.Status.ElapsedTime;
                    Vec2 relative = world.Enemy.Position - world.Player.Position;
                    ai.CanStartVisibleAttack = MathF.Abs(relative.X) <= 8.15f && MathF.Abs(relative.Y) <= 4.25f;
                    var input = ai.ReadInput(Dt);
                    playerFlight.Step(ref world.Player, new FlightInput(1f, turn), Dt,
                        FlightMotionCommand.Ordinary(9f, 240f));
                    enemyFlight.Step(ref world.Enemy, input, Dt, ai.MotionCommand);
                    combat.StepContact(previousPlayer, world.Player, previousEnemy, world.Enemy, Dt, 1f, ai.CollisionDamage);
                }
                Assert.AreEqual(level, ai.Level);
                Assert.AreEqual(3, combat.Status.HitSequence, "each normal five-HP starting state must receive three real contacts within the observation period");
                TestContext.WriteLine($"L{level} turn={turn} d={distance} heading={heading} side={side}: seconds={combat.Status.ElapsedTime} hp={combat.Status.PlayerHealth} contacts={combat.Status.HitSequence}");
                survival.Add(combat.Status.ElapsedTime);
            }
            survival.Sort();
            float median = (survival[5] + survival[6]) * .5f;
            Assert.LessOrEqual(median, limit, "the one-damage adjustment must preserve the strengthened enemy's first-three-contact tempo; full five-HP survival is measured separately");
        }

        sealed class World : IWorldView
        {
            public FlightState Player, Enemy;
            public float Time;
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(1, 1, Player, true, true, VisiblePropulsionCue.None, true, Time)
                : new AircraftSnapshot(2, 2, Enemy);
        }
    }
}
