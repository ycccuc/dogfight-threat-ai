using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class ChargeTelegraphTests
    {
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);

        [TestCase(1)]
        [TestCase(4)]
        public void WarningShowsTheActualAircraftAxisInsteadOfAnUnrelatedTargetLine(int level)
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-6f, -1f), 1.1f) };
            var ai = new PursuitChargeInputSource(world, 2, Spec, new Clock(), null, level);
            ai.ReadInput(.02f);
            Vec2 axis = (ai.GetPreviewPoint(1) - ai.GetPreviewPoint(0)).Normalized;
            Assert.Greater(Vec2.Dot(axis, world.Enemy.Forward), .99999f,
                "The warning must describe the aircraft's real attack axis, not the separately predicted target.");
            Assert.AreEqual(14f, Vec2.Distance(ai.GetPreviewPoint(0), ai.GetPreviewPoint(1)), .0001f);
        }

        [TestCase(.01f)]
        [TestCase(.02f)]
        [TestCase(.0333f)]
        public void FinalWarningCommitsBeforeChargeAndMovingTargetCannotRedirectTheAttack(float dt)
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-6f, -1f), .6f) };
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock, null, 4);
            var flight = FlightModel.From(Spec);
            bool announced = false;
            Vec2 promised = Vec2.Zero, origin = Vec2.Zero;
            for (int tick = 0; tick < 100; tick++)
            {
                clock.Now = tick * dt;
                var input = ai.ReadInput(dt);
                if (ai.Phase == AttackRunPhase.Windup && ai.IsAimLocked)
                {
                    if (!announced)
                    {
                        announced = true;
                        promised = ai.LockedDirection;
                        origin = ai.GetPreviewPoint(0);
                        world.Player = FlightState.At(new Vec2(-6f, -10f));
                    }
                    Assert.Greater(Vec2.Dot(promised, ai.AimDirection), .99999f);
                    Assert.Less(Vec2.Distance(origin, ai.GetPreviewPoint(0)), .0001f,
                        "A late target reversal cannot slide the announced launch lane.");
                }
                if (ai.Phase == AttackRunPhase.Charge)
                {
                    Assert.IsTrue(announced);
                    float firstChargeStep = MathF.Ceiling(.8f / dt - .0001f) * dt;
                    Assert.AreEqual(firstChargeStep, clock.Now, .0001f);
                    Assert.AreEqual(0f, input.Steer);
                    Assert.Less(Vec2.Distance(origin, world.Enemy.Position), .001f);
                    Vec2 start = world.Enemy.Position;
                    flight.Step(ref world.Enemy, input, dt, ai.MotionCommand);
                    Assert.Greater(Vec2.Dot((world.Enemy.Position - start).Normalized, promised), .99999f);
                    Assert.AreEqual(45f, world.Enemy.Speed, .0001f);
                    return;
                }
                flight.Step(ref world.Enemy, input, dt, ai.MotionCommand);
            }
            Assert.Fail("The full .8-second warning must end in its promised charge.");
        }

        [Test]
        public void CommitmentAnnouncesItsFutureLaunchLaneWhileTheAircraftFinishesBanking()
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-6f, -1f), .6f) };
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock, null, 4);
            ai.ReadInput(.02f);
            clock.Now = PursuitChargeInputSource.AimSeconds - PursuitChargeInputSource.AimCommitSeconds;
            ai.ReadInput(.02f);
            Assert.IsTrue(ai.IsAimLocked);
            Assert.Greater(Vec2.Distance(ai.GetPreviewPoint(0), world.Enemy.Position), .1f,
                "The committed line must name the future launch lane, allowing the aircraft to bank into it without moving the promise.");
            Assert.Greater(ai.MotionCommand.TurnRateDegPerSec, 0f,
                "Committing an attack direction must not remove the physical bank needed to enter its lane.");
        }

        sealed class Clock : ICombatStatusView
        {
            public float Now;
            public CombatStatus Status => new CombatStatus(5, 5, 0, 0f, Now);
        }

        sealed class World : IWorldView
        {
            public FlightState Enemy;
            public FlightState Player = FlightState.At(Vec2.Zero);
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(2, 2, Enemy)
                : new AircraftSnapshot(1, 1, Player, true, true);
        }
    }
}
