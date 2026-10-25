using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.AI.BehaviorTree;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class GunnerBehaviorTests
    {
        static GunnerProfile Profile(bool astar = true, float delay = .1f) => new GunnerProfile(astar, .1f, .5f, .15f, delay, 10, 220, 3, 7);
        static GunnerPilot Pilot(bool astar = true) => new GunnerPilot(Grid2D.Centered(new Vec2(200, 150), 4),
            new WeaponSpec("cannon", .45f, 46, 1.5f, .09f, .85f), Profile(astar));
        [Test] public void ReactiveSelectorLetsEmergencyPreemptRunningAction()
        {
            int normalCalls = 0, emergencyCalls = 0;
            var tree = new SelectorNode<bool>(new SequenceNode<bool>(new ConditionNode<bool>(c => c),
                new ActionNode<bool>(c => { emergencyCalls++; return NodeStatus.Success; })),
                new ActionNode<bool>(c => { normalCalls++; return NodeStatus.Running; }));
            Assert.That(tree.Tick(false), Is.EqualTo(NodeStatus.Running));
            Assert.That(tree.Tick(true), Is.EqualTo(NodeStatus.Success));
            Assert.That(normalCalls, Is.EqualTo(1)); Assert.That(emergencyCalls, Is.EqualTo(1));
        }
        [Test] public void SequenceStopsOnFailureAndRunning()
        {
            int calls = 0;
            var sequence = new SequenceNode<int>(new ConditionNode<int>(c => c > 0),
                new ActionNode<int>(c => NodeStatus.Running), new ActionNode<int>(c => { calls++; return NodeStatus.Success; }));
            Assert.That(sequence.Tick(0), Is.EqualTo(NodeStatus.Failure));
            Assert.That(sequence.Tick(1), Is.EqualTo(NodeStatus.Running)); Assert.That(calls, Is.Zero);
        }
        [TestCase(false)] [TestCase(true)]
        public void DistantApproachActuallyUsesSelectedPathfindingAlgorithm(bool astar)
        {
            var pilot = Pilot(astar);
            pilot.Tick(FlightState.At(Vec2.Zero), FlightState.At(new Vec2(80, 20)), true, .02f);
            Assert.That(pilot.SearchCount, Is.EqualTo(1)); Assert.That(pilot.LastSearchUsedAStar, Is.EqualTo(astar));
            Assert.That(pilot.WantsFire, Is.False);
        }
        [Test] public void AlignedTargetRequiresReactionThenRequestsFire()
        {
            var pilot = Pilot(); var self = FlightState.At(Vec2.Zero); var target = FlightState.At(new Vec2(10, 0));
            pilot.Tick(self, target, true, .02f); Assert.That(pilot.WantsFire, Is.False);
            bool fired = false;
            for (int i = 0; i < 10; i++) { pilot.Tick(self, target, true, .02f); fired |= pilot.WantsFire; }
            Assert.That(fired, Is.True); Assert.That(pilot.Behavior, Is.EqualTo("瞄准开火"));
        }
        [Test] public void CloseTargetTriggersRetreatAndDeadTargetStopsFire()
        {
            var pilot = Pilot(); var self = FlightState.At(Vec2.Zero);
            pilot.Tick(self, FlightState.At(new Vec2(1, 0)), true, .02f);
            Assert.That(pilot.Behavior, Is.EqualTo("拉开距离")); Assert.That(pilot.WantsFire, Is.False);
            pilot.Tick(self, default, false, .02f); Assert.That(pilot.Behavior, Is.EqualTo("结束")); Assert.That(pilot.WantsFire, Is.False);
        }
        [Test] public void ChangingDifficultyResetsObservationAndReaction()
        {
            var pilot = Pilot(); var self = FlightState.At(Vec2.Zero); var target = FlightState.At(new Vec2(10, 0));
            pilot.Tick(self, target, true, .3f); Assert.That(pilot.WantsFire, Is.True);
            pilot.SetProfile(Profile(delay: .5f)); pilot.Tick(self, target, true, .02f);
            Assert.That(pilot.WantsFire, Is.False);
        }
        [Test] public void ShootingDisciplineWaitsBetweenShotsWithoutChangingWeaponCooldown()
        {
            var pilot = Pilot();
            pilot.SetProfile(new GunnerProfile(true, .1f, .5f, .15f, .1f, 10, 220, 3, 7, .8f));
            var self = FlightState.At(Vec2.Zero); var target = FlightState.At(new Vec2(10, 0));
            int shots = 0;
            for (int i = 0; i < 100; i++) { pilot.Tick(self, target, true, .02f); if (pilot.WantsFire) shots++; }
            Assert.That(shots, Is.EqualTo(3));
        }
        [Test] public void FollowingTargetAtEngagementDistanceMatchesItsSpeed()
        {
            var pilot = Pilot(); var target = FlightState.At(new Vec2(7, 0)); target.Velocity = new Vec2(9, 0);
            pilot.Tick(FlightState.At(Vec2.Zero), target, true, .02f);
            Assert.That(pilot.MotionCommand.MaxSpeed, Is.EqualTo(9).Within(.001f));
        }
    }
}
