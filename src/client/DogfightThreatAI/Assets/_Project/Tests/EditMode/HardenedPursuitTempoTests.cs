using System;
using System.Reflection;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class HardenedPursuitTempoTests
    {
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void StraightRecoveryDoesNotOvershootItsEscapePointAndTurnBackBeforeItsDeadline(int level)
        {
            float speed = PursuitDifficultyProfile.ForLevel(level).CruiseSpeed;
            var world = new World
            {
                Player = FlightState.At(new Vec2(-4f, 0f)),
                Enemy = new FlightState { Velocity = new Vec2(speed, 0f) }
            };
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock, null, level);
            var flight = FlightModel.From(Spec);
            ai.ReadInput(.02f);
            clock.Hits = 1;
            clock.Now = .02f;
            ai.ReadInput(.02f);
            float deadline = clock.Now + ai.RecoverySeconds;
            for (; clock.Now < deadline - .0001f; clock.Now += .02f)
            {
                var input = ai.ReadInput(.02f);
                Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
                flight.Step(ref world.Enemy, input, .02f, ai.MotionCommand);
                Assert.Greater(Vec2.Dot(world.Enemy.Forward, new Vec2(1f, 0f)), .9f,
                    "a forward recovery must keep separating instead of crossing a too-near fixed goal and banking back into the protected player");
            }
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void ProtectedPlayerCanReceiveAFullNewWarningWithoutRemovingProtection(int level)
        {
            var world = new World();
            var clock = new Clock { Protection = .6f };
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock, null, level);
            ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase, "protection should not make the enemy idle before its next full warning");
            Assert.AreEqual(.8f, ai.PhaseRemaining, .0001f);
            Assert.AreEqual(.6f, clock.Status.InvulnerabilityRemaining, "AI cannot change the combat protection clock");
            clock.Now = .78f;
            ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            clock.Now = .8f;
            ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void LowerTiersCanReenterUsingActuallyObservedCurvature(int level)
        {
            var world = new World { Enemy = FlightState.At(new Vec2(-20f, 0f)) };
            var clock = new Clock();
            var ai = new PursuitChargeInputSource(world, 2, Spec, clock, null, level);
            for (int tick = 0; tick <= 70; tick++)
            {
                float angle = tick * .02f * 2f;
                world.Player = new FlightState
                {
                    Position = Vec2.FromAngle(angle) * 4.5f,
                    Heading = angle + MathF.PI * .5f,
                    Velocity = Vec2.FromAngle(angle + MathF.PI * .5f) * 9f
                };
                clock.Now = tick * .02f;
                ai.ReadInput(.02f);
            }
            clock.Hits = 1;
            ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
            var path = (PursuitReentryPath)typeof(PursuitChargeInputSource)
                .GetField("_reentryPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ai);
            Assert.IsTrue(path.Active, "all strengthened tiers must use reliable observed turning history instead of blindly following a circle");
        }

        sealed class World : IWorldView
        {
            public FlightState Player = FlightState.At(Vec2.Zero);
            public FlightState Enemy = FlightState.At(new Vec2(-6f, 0f));
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(1, 1, Player, true, true)
                : new AircraftSnapshot(2, 2, Enemy);
        }

        sealed class Clock : ICombatStatusView
        {
            public float Now, Protection;
            public int Hits;
            public CombatStatus Status => new CombatStatus(5, 5, Hits, Protection, Now);
        }
    }
}
