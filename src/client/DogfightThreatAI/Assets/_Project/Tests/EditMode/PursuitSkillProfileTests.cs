using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class PursuitSkillProfileTests
    {
        [Test]
        public void VisibleCueUsesTheSameDelayedCutoffAsMotionAndInvisibleSamplesDoNotReplaceIt()
        {
            var m=new PursuitObservationMemory();
            m.Sample(new AircraftSnapshot(1,1,FlightState.At(Vec2.Zero),true,true,VisiblePropulsionCue.None,true,0f),0f);
            m.Sample(new AircraftSnapshot(1,1,FlightState.At(new Vec2(2f,0f)),true,true,VisiblePropulsionCue.Overheat,true,.2f),.2f);
            Assert.IsTrue(m.TryObserveCue(.3f,.18f,out var cue,out var at)); Assert.AreEqual(VisiblePropulsionCue.None,cue); Assert.AreEqual(0f,at);
            m.Sample(new AircraftSnapshot(1,1,FlightState.At(new Vec2(99f,0f)),true,true,VisiblePropulsionCue.Dash,false,.4f),.4f);
            Assert.IsTrue(m.TryObserve(.6f,.18f,out var state,out _,out _,out _)); Assert.AreEqual(new Vec2(2f,0f),state.Position);
            Assert.IsTrue(m.TryObserveCue(.6f,.18f,out cue,out at)); Assert.AreEqual(VisiblePropulsionCue.Overheat,cue); Assert.AreEqual(.2f,at);
        }

        [Test]
        public void ThreeRoutePlannerHasBoundedWorkAndKeepsItsSideUntilItsHoldExpires()
        {
            var planner=new PursuitSteeringPlanner();
            var self=new FlightState{Position=new Vec2(-18f,0f),Velocity=new Vec2(12f,0f)};
            var target=new FlightState{Position=Vec2.Zero,Velocity=new Vec2(0f,9f),Heading=1.57f};
            var p=PursuitDifficultyProfile.ForLevel(3);
            Vec2 first=planner.Plan(self,target,target.Position+target.Velocity*.4f,p,0f,0f,0f);
            int version=planner.PlanVersion; float side=planner.SelectedSide;
            Assert.AreEqual(3,planner.CandidateCount); Assert.LessOrEqual(planner.SimulationStepsPerCandidate,16);
            Assert.AreEqual(first,planner.Plan(self,target,new Vec2(9f,9f),p,0f,0f,.05f)); Assert.AreEqual(version,planner.PlanVersion);
            planner.Plan(self,target,new Vec2(9f,9f),p,0f,0f,.12f);
            Assert.Greater(planner.PlanVersion,version); Assert.AreEqual(side,planner.SelectedSide);
        }

        [Test]
        public void FourthLevelCanUseOnlyRecordedVisibleEvasionAndFallsBackWithoutHistory()
        {
            var p=PursuitDifficultyProfile.ForLevel(4);
            var self=new FlightState{Position=new Vec2(-18f,0f),Velocity=new Vec2(13.5f,0f)};
            var target=new FlightState{Velocity=new Vec2(0f,9f)};
            var a=new PursuitSteeringPlanner(); var b=new PursuitSteeringPlanner();
            Assert.AreEqual(a.Plan(self,target,Vec2.Up,p,0f,0f,0f),b.Plan(self,target,Vec2.Up,p,0f,0f,0f));
            a.RecordVisibleEvasion(1f); b.RecordVisibleEvasion(-1f);
            a.Plan(self,target,Vec2.Up,p,0f,0f,2f); b.Plan(self,target,Vec2.Up,p,0f,0f,2f);
            Assert.AreNotEqual(a.SelectedSide,b.SelectedSide);
        }

        [Test]
        public void VisibleOverheatCanSpendFourthLevelBudgetOnlyAfterItPassesTheBrakingCheck()
        {
            var self=new FlightState{Velocity=new Vec2(13.5f,0f)};
            var target=new FlightState{Position=new Vec2(18f,0f),Velocity=new Vec2(7.2f,0f)};
            var p=PursuitDifficultyProfile.ForLevel(4);
            Assert.AreEqual(0f,PursuitSteeringPlanner.SafeBoostDuration(self,target,target.Position,p,8f));
            Assert.GreaterOrEqual(PursuitSteeringPlanner.SafeBoostDuration(self,target,target.Position,p,8f,null,true),.2f);
            target.Position=new Vec2(9f,0f);
            Assert.AreEqual(0f,PursuitSteeringPlanner.SafeBoostDuration(self,target,target.Position,p,8f,null,true));
        }

        [Test]
        public void OffscreenVerticalApproachChoosesAWideVisibleEntryWithoutAnArrivalGate()
        {
            var planner=new PursuitSteeringPlanner();
            var self=new FlightState{Position=new Vec2(0f,14f),Heading=-1.57f};
            Vec2 goal=planner.Plan(self,FlightState.At(Vec2.Zero),Vec2.Zero,PursuitDifficultyProfile.ForLevel(4),0f,0f,0f,false);
            Assert.AreEqual(7.8f,System.Math.Abs(goal.X),.001f); Assert.AreEqual(0f,goal.Y,.001f);
            Assert.AreEqual(3,planner.CandidateCount); Assert.LessOrEqual(planner.SimulationStepsPerCandidate,16);
        }

        [TestCase(0f,.3f,.1f)] [TestCase(9f,0f,-.4f)]
        public void NarrowCameraApproachUsesItsVisibleCornerAndGivesStoppedTargetsTurningRoom(float speed,float widthOffset,float heightOffset)
        {
            var planner=new PursuitSteeringPlanner();
            var self=new FlightState{Position=new Vec2(0f,14f),Heading=-1.57f};
            var target=new FlightState{Position=Vec2.Zero,Velocity=new Vec2(speed,0f)};
            Vec2 goal=planner.Plan(self,target,Vec2.Zero,PursuitDifficultyProfile.ForLevel(4),0f,0f,0f,false,5f*4f/3f-1f);
            Assert.AreEqual(5f*4f/3f-1f+widthOffset,System.Math.Abs(goal.X),.001f);
            self.Position=new Vec2(10f,12f);
            goal=planner.Plan(self,target,Vec2.Zero,PursuitDifficultyProfile.ForLevel(4),0f,0f,.12f,false,5f*4f/3f-1f);
            Assert.AreEqual(new Vec2(5f*4f/3f-1f+widthOffset,4f+heightOffset),goal,"the narrow entry remains active outside the old six-unit switch boundary");
        }
    }
}
