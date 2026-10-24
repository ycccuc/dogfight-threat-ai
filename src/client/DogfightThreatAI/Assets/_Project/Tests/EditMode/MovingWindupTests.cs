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
    public sealed class MovingWindupTests
    {
        const float Dt = .02f;
        [Test]
        public void LevelFourUsesInvulnerableWaitToPrepareItsNextApproachWithoutStartingAnotherAttack()
        {
            var spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
            var world = new World { Enemy = FlightState.At(new Vec2(4f, -2.5f), 1.5707963f) };
            var combat = new Clock { Model = new ContactCombatModel(), Offset = 60f };
            var ai = new AttackRunInputSource(world, 2, spec, combat);
            var model = FlightModel.From(spec);
            for (int i = 0; i < 2000 && combat.Status.HitSequence == 0; i++) Tick(world, ai, model, combat);
            Assert.AreEqual(1, combat.Status.HitSequence);
            int attack = ai.AttackId;
            while (combat.Status.InvulnerabilityRemaining > .12f)
            {
                Tick(world, ai, model, combat);
                Assert.AreEqual(attack, ai.AttackId, "No new warning may start while the player remains invulnerable.");
                Assert.IsFalse(ai.AttackWindow.IsActive);
            }
            Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
            float targetHeading = System.MathF.Atan2(-world.Enemy.Position.Y, -world.Enemy.Position.X);
            float error = System.MathF.Abs(Vec2.NormalizeAngle(targetHeading - world.Enemy.Heading));
            Assert.Less(error, 1f, "The last level should prepare the next approach during the existing safe recovery wait.");
        }

        [TestCase(0f)]
        [TestCase(20f)]
        [TestCase(40f)]
        [TestCase(60f)]
        public void WarningKeepsMovingAlongTheLockedCourseAndCannotDamage(float offset)
        {
            var spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
            var world = new World { Enemy = FlightState.At(new Vec2(4f, -2.5f), 1.5707963f) };
            var combat = new Clock { Model = new ContactCombatModel(), Offset = offset };
            var ai = new AttackRunInputSource(world, 2, spec, combat);
            var model = FlightModel.From(spec);
            for (int i = 0; i < 2000 && ai.Phase != AttackRunPhase.Windup; i++) Tick(world, ai, model, combat);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase, "Moving preview cannot pass by refusing every attack.");
            Vec2 origin = world.Enemy.Position;
            float heading = ai.LockedHeading;
            int frames = 1;
            for (int i = 0; i < 38; i++)
            {
                Tick(world, ai, model, combat);
                Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
                Assert.AreEqual(heading, ai.LockedHeading);
                Assert.IsFalse(ai.AttackWindow.IsActive);
                Assert.AreEqual(5, combat.Status.PlayerHealth);
                frames++;
            }
            Assert.Greater(Vec2.Distance(origin, world.Enemy.Position), 1.1f,
                "The aircraft must visibly advance during the .8-second aim, not brake to a stationary marker.");
            Assert.AreEqual(39, frames);
        }

        static void Tick(World world, AttackRunInputSource ai, FlightModel model, Clock clock)
        {
            var before = world.Enemy;
            FlightInput input = ai.ReadInput(Dt);
            model.Step(ref world.Enemy, input, Dt);
            var player = FlightState.At(Vec2.Zero);
            clock.Model.Step(player, player, before, world.Enemy, Dt, ai.AttackWindow);
        }
        sealed class Clock : ICombatStatusView
        {
            public ContactCombatModel Model;
            public float Offset;
            public CombatStatus Status => new CombatStatus(Model.Status.PlayerHealth, 5, Model.Status.HitSequence,
                Model.Status.InvulnerabilityRemaining, Model.Status.ElapsedTime + Offset);
        }
        sealed class World : IWorldView
        {
            public FlightState Enemy;
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0 ? new AircraftSnapshot(2, 2, Enemy) :
                new AircraftSnapshot(1, 1, FlightState.At(Vec2.Zero), true, true);
        }
    }
}
