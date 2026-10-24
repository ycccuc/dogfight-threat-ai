using System;
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
    public sealed class PursuitBoostTests
    {
        const float Dt = .02f;
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly Bounds2D Arena = Bounds2D.Centered(new Vec2(200f, 150f));

        [TestCase(1,16f,420f,24f,.45f,4.2f)]
        [TestCase(2,17f,450f,28f,.5f,3.5f)]
        [TestCase(3,18f,480f,32f,.6f,2.8f)]
        [TestCase(4,18f,520f,36f,.65f,2.2f)]
        public void ProfilesDefineTheActualAircraftPerformance(int level,float speed,float turn,float boost,float duration,float cooldown)
        {
            var p=PursuitDifficultyProfile.ForLevel(level);
            Assert.AreEqual(1,p.CollisionDamage,"Every production difficulty uses the requested one-point contact damage.");
            Assert.AreEqual(speed,p.CruiseSpeed); Assert.AreEqual(turn,p.TurnRateDegPerSec);
            Assert.AreEqual(boost,p.BoostSpeed); Assert.AreEqual(duration,p.BoostDuration); Assert.AreEqual(cooldown,p.BoostCooldown);
        }

        [Test]
        public void BoostPaysOnceAndCancelNeverRefundsOrClearsCooldown()
        {
            var b=new PursuitBoostModel(); var p=PursuitDifficultyProfile.ForLevel(4);
            Assert.IsTrue(b.TryStart(0f,.4f,p)); Assert.AreEqual(30f,b.Energy);
            b.Cancel(.1f); Assert.AreEqual(30f,b.Energy); Assert.IsFalse(b.IsBoosting);
            b.Advance(.89f); Assert.AreEqual(30f,b.Energy,.001f);
            b.Advance(1.1f); Assert.AreEqual(33f,b.Energy,.001f); Assert.AreEqual(1.1f,b.CooldownRemaining,.001f);
            Assert.IsFalse(b.TryStart(1.1f,.4f,p)); b.Advance(2.2f);
            Assert.IsTrue(b.TryStart(2.2f,.4f,p)); Assert.Less(b.Energy,30f);
        }

        [Test]
        public void RepeatedReadsAtOneTimeCannotRegenerateOrConsumeExtraEnergy()
        {
            var b=new PursuitBoostModel(); var p=PursuitDifficultyProfile.ForLevel(1);
            b.TryStart(0f,.2f,p);
            for(int i=0;i<100;i++) b.Advance(.1f);
            Assert.AreEqual(30f,b.Energy); Assert.AreEqual(.1f,b.Remaining,.0001f);
            b.Advance(5f); Assert.AreEqual(60f,b.Energy); Assert.IsFalse(b.IsBoosting);
        }

        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidGameTimeCannotStartOrPoisonAPaidBoost(float time)
        {
            var b=new PursuitBoostModel(); var p=PursuitDifficultyProfile.ForLevel(1);
            Assert.IsFalse(b.TryStart(time,.2f,p)); Assert.AreEqual(60f,b.Energy);
            Assert.IsTrue(b.TryStart(0f,.2f,p)); b.Cancel(time);
            Assert.IsTrue(b.IsBoosting); b.Advance(.1f); Assert.AreEqual(.1f,b.Remaining,.0001f);
        }

        [Test]
        public void RealPursuitBoostClosesAFortyUnitGapAndUsesNinetyDegreeTurnLimit()
        {
            var w=new World(); w.Player.Position=new Vec2(40f,0f);
            var c=new Clock(); var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,1);
            var flight=FlightModel.From(Spec,Arena); float min=40f; bool boosted=false;
            for(int i=0;i<100;i++)
            {
                c.Now=i*Dt; var input=ai.ReadInput(Dt);
                if(ai.IsPursuitBoosting) { boosted=true; Assert.AreEqual(90f,ai.MotionCommand.TurnRateDegPerSec); }
                flight.Step(ref w.Enemy,input,Dt,ai.MotionCommand); w.Player.Position+=w.Player.Velocity*Dt;
                min=MathF.Min(min,Vec2.Distance(w.Enemy.Position,w.Player.Position));
            }
            Assert.IsTrue(boosted); Assert.Less(min,35f); Assert.Less(ai.BoostEnergy,60f);
        }

        [TestCase(1)] [TestCase(4)]
        public void PursuitBoostNinetyDegreeLimitChangesTheActualAircraftHeading(int level)
        {
            var w=new World(); w.Player.Position=new Vec2(40f,5f);
            var ai=new PursuitChargeInputSource(w,2,Spec,new Clock(),Arena,level);
            FlightInput input=ai.ReadInput(Dt); Assert.IsTrue(ai.IsPursuitBoosting);
            FlightModel.From(Spec,Arena).Step(ref w.Enemy,input,Dt,ai.MotionCommand);
            Assert.AreEqual(90f*.0174532925f*Dt,System.Math.Abs(w.Enemy.Heading),.00001f);
        }

        [Test]
        public void SuddenRangeEntryCancelsBoostAndKeepsEntryForwardSpeedForOneUnresetWarning()
        {
            var w=new World(); w.Player.Position=new Vec2(40f,0f);
            var c=new Clock(); var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,1);
            ai.ReadInput(Dt); Assert.IsTrue(ai.IsPursuitBoosting);
            w.Enemy.Velocity=new Vec2(16f,0f); w.Player.Position=w.Enemy.Position+new Vec2(7f,0f);
            c.Now=.1f; ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Windup,ai.Phase); Assert.IsFalse(ai.IsPursuitBoosting);
            Assert.AreEqual(16f,ai.MotionCommand.MaxSpeed); Assert.AreEqual(420f,ai.MotionCommand.TurnRateDegPerSec);
            Assert.AreEqual(30f,ai.BoostEnergy); Assert.AreEqual(.8f,ai.PhaseRemaining,.0001f);
            c.Now=.5f; ai.ReadInput(Dt); Assert.AreEqual(.4f,ai.PhaseRemaining,.0001f); Assert.AreEqual(1,ai.AttackId);
            c.Now=.9f; ai.ReadInput(Dt); Assert.AreEqual(AttackRunPhase.Charge,ai.Phase); Assert.AreEqual(45f,ai.MotionCommand.MaxSpeed);
        }

        [Test]
        public void UpgradeWaitsForBoostAndCoastBoundaryWithoutResettingBudget()
        {
            var w=new World(); w.Player.Position=new Vec2(40f,0f);
            var c=new Clock(); var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,1);
            ai.ReadInput(Dt); ai.RequestDifficulty(4); c.Now=.1f; ai.ReadInput(Dt); Assert.AreEqual(1,ai.Level);
            w.Enemy.Velocity=new Vec2(20f,0f); c.Now=.5f; ai.ReadInput(Dt); Assert.IsTrue(ai.IsBoostCoasting); Assert.AreEqual(1,ai.Level);
            w.Enemy.Velocity=new Vec2(16f,0f); c.Now=.6f; ai.ReadInput(Dt); Assert.AreEqual(4,ai.Level); Assert.AreEqual(30f,ai.BoostEnergy);
        }

        [Test]
        public void RepeatedContactCannotExtendRecoveryOrMoveItsEscapePoint()
        {
            var w=new World(); var c=new Clock(); var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,1);
            ai.ReadInput(Dt); c.Hits=1; c.Now=.1f; ai.ReadInput(Dt); Vec2 escape=ai.ManeuverPoint;
            c.Hits=2; c.Now=.2f; w.Enemy.Position=new Vec2(40f,20f); ai.ReadInput(Dt);
            Assert.AreEqual(escape,ai.ManeuverPoint); Assert.AreEqual(1.3f,ai.PhaseRemaining,.0001f);
            c.Hits=3; c.Now=.4f; ai.ReadInput(Dt); Assert.AreEqual(1.1f,ai.PhaseRemaining,.0001f);
            c.Now=1.51f; ai.ReadInput(Dt); Assert.AreEqual(AttackRunPhase.Pursuit,ai.Phase);
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void ChargeKeepsAbsoluteFortyFiveAndRecoveryImmediatelyReturnsToItsCruise(int level)
        {
            var w=new World(); w.Player.Position=new Vec2(7f,0f); var c=new Clock();
            var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,level); var flight=FlightModel.From(Spec,Arena);
            ai.ReadInput(Dt); c.Now=.8f; var input=ai.ReadInput(Dt); flight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
            Assert.AreEqual(45f,w.Enemy.Speed,.001f);
            c.Now=1.7f; input=ai.ReadInput(Dt); flight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
            Assert.AreEqual(AttackRunPhase.Recover,ai.Phase); Assert.LessOrEqual(w.Enemy.Speed,ai.CruiseSpeed+.01f);
        }

        [Test]
        public void UnsafeShortGapDoesNotBoostAndDoesNotStopFollowing()
        {
            var w=new World(); w.Player.Position=new Vec2(9f,0f); w.Player.Velocity=Vec2.Zero;
            var ai=new PursuitChargeInputSource(w,2,Spec,new Clock(),Arena,4); var input=ai.ReadInput(Dt);
            Assert.IsFalse(ai.IsPursuitBoosting); Assert.AreEqual(1f,input.Throttle); Assert.AreEqual(60f,ai.BoostEnergy);
        }

        [TestCase(1,9.5f,7.8f,0f)]
        [TestCase(4,13.5f,7.8f,0f)]
        [TestCase(4,24f,7.8f,-9f)]
        public void MovingWarningAvoidsAnActiveBodyCollisionWhenThereIsReactionSpace(int level,float speed,float distance,float playerVelocity)
        {
            var w=new World(); w.Enemy.Velocity=new Vec2(speed,0f); w.Player.Position=new Vec2(distance,0f); w.Player.Velocity=new Vec2(playerVelocity,0f);
            var c=new Clock(); var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,level); var flight=FlightModel.From(Spec,Arena);
            var profile=PursuitDifficultyProfile.ForLevel(level);
            float capturedSpeed=MathF.Min(profile.BoostSpeed,MathF.Max(profile.CruiseSpeed,speed));
            for(int tick=0;tick<40;tick++)
            {
                c.Now=tick*Dt; var input=ai.ReadInput(Dt); Assert.AreEqual(AttackRunPhase.Windup,ai.Phase);
                Assert.Greater(input.Throttle,0f); Assert.AreEqual(capturedSpeed,ai.MotionCommand.MaxSpeed,.0001f);
                flight.Step(ref w.Enemy,input,Dt,ai.MotionCommand); w.Player.Position+=w.Player.Velocity*Dt;
                Assert.Greater(Vec2.Distance(w.Enemy.Position,w.Player.Position),1f,$"contact before warning at {c.Now}");
            }
            c.Now=.8f; ai.ReadInput(Dt); Assert.AreEqual(AttackRunPhase.Charge,ai.Phase);
        }

        [TestCase(15f,15f,100f)] [TestCase(18f,12f,100f)] [TestCase(21f,14f,100f)]
        [TestCase(23f,8f,100f)] [TestCase(24f,16f,100f)] [TestCase(18f,8f,60f)]
        public void DurationPredictionIncludesExistingLateralMomentumAndBraking(float x,float y,float lateral)
        {
            var self=new FlightState { Velocity=new Vec2(13.5f,lateral) };
            var target=FlightState.At(new Vec2(x,y)); var p=PursuitDifficultyProfile.ForLevel(4);
            var model=FlightModel.From(Spec,Arena);
            float duration=PursuitSteeringPlanner.SafeBoostDuration(self,target,target.Position,p,8f,model);
            if(duration==0f) return;
            for(float time=0f;time<duration+(p.BoostSpeed-p.CruiseSpeed)/30f+.025f;time+=.025f)
            {
                float turn=Vec2.NormalizeAngle((target.Position-self.Position).AngleRad-self.Heading)/(90f*.0174532925f*.025f);
                var command=FlightMotionCommand.FiniteAcceleration(time<duration?p.BoostSpeed:p.CruiseSpeed,40f,30f,90f,40f,0f);
                model.Step(ref self,new FlightInput(1f,turn),.025f,command);
                Assert.Greater(Vec2.Distance(self.Position,target.Position),8f,"a granted duration must stay outside attack range until braking completes");
            }
        }

        [TestCase(3,0f)] [TestCase(4,0f)] [TestCase(4,1f)] [TestCase(4,-1f)]
        public void HigherLevelsDefeatStoppingAndFixedCircleTargetsInOpenSpace(int level,float steer)
        {
            var w=new World(); w.Enemy.Position=new Vec2(-12f,-2f); w.Player.Position=Vec2.Zero;
            var combat=new ContactCombatModel(5,1,.7f,1); var ai=new PursuitChargeInputSource(w,2,Spec,combat,null,level);
            var eFlight=FlightModel.From(Spec); var pFlight=FlightModel.From(new AircraftSpec(2f,9f,18f,240f,.1f,8f,.35f));
            AttackRunPhase previous=AttackRunPhase.NoTarget;
            for(int tick=0;tick<3000&&combat.Status.IsPlayerAlive;tick++)
            {
                FlightState previousEnemy=w.Enemy,previousPlayer=w.Player;
                Vec2 relative=w.Enemy.Position-w.Player.Position;
                ai.CanStartVisibleAttack=MathF.Abs(relative.X)<=8.15f&&MathF.Abs(relative.Y)<=4.25f;
                FlightInput input=ai.ReadInput(Dt);
                if(ai.Phase!=previous)
                {
                    TestContext.WriteLine($"{combat.Status.ElapsedTime:0.00} {ai.Phase} pos={w.Enemy.Position} p={w.Player.Position} head={w.Enemy.Heading:0.00} targetHeading={(w.Player.Position-w.Enemy.Position).AngleRad:0.00} hp={combat.Status.PlayerHealth}");
                    previous=ai.Phase;
                }
                eFlight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
                pFlight.Step(ref w.Player,new FlightInput(steer==0f?-1f:1f,steer),Dt,FlightMotionCommand.Ordinary(9f,240f));
                combat.StepContact(previousPlayer,w.Player,previousEnemy,w.Enemy,Dt,1f);
            }
            Assert.AreEqual(0,combat.Status.PlayerHealth,$"L{level} steer{steer} misses a deterministic target");
        }

        [Test]
        public void FreshVisibleOverheatChangesBudgetChoiceAndAnUnseenOldCueExpires()
        {
            var freshWorld=new World { Cue=VisiblePropulsionCue.Overheat };
            freshWorld.Player.Velocity=new Vec2(7.2f,0f); freshWorld.Enemy.Velocity=new Vec2(13.5f,0f);
            var clock=new Clock(); var fresh=new PursuitChargeInputSource(freshWorld,2,Spec,clock,Arena,4);
            fresh.ReadInput(Dt); Assert.IsFalse(fresh.IsPursuitBoosting,"cue waits for the observation cutoff");
            clock.Now=.1f; fresh.ReadInput(Dt); Assert.IsTrue(fresh.IsPursuitBoosting);

            var plainWorld=new World(); plainWorld.Player.Velocity=new Vec2(7.2f,0f); plainWorld.Enemy.Velocity=new Vec2(13.5f,0f);
            var plain=new PursuitChargeInputSource(plainWorld,2,Spec,clock,Arena,4); plain.ReadInput(Dt); Assert.IsFalse(plain.IsPursuitBoosting);

            var staleWorld=new World { Cue=VisiblePropulsionCue.Overheat };
            staleWorld.Player.Velocity=new Vec2(7.2f,0f); staleWorld.Enemy.Heading=3.14159f;
            var staleClock=new Clock(); var stale=new PursuitChargeInputSource(staleWorld,2,Spec,staleClock,Arena,4);
            stale.ReadInput(Dt); staleClock.Now=.1f; stale.ReadInput(Dt);
            staleWorld.Visible=false; staleWorld.Enemy.Heading=0f; staleClock.Now=1.2f; stale.ReadInput(Dt);
            Assert.IsFalse(stale.IsPursuitBoosting,"an invisible Overheat cue cannot remain a permanent pressure signal");
            Assert.AreEqual(.1f,stale.ObservedAt,.0001f);
        }

        [Test]
        public void ACommittedChargeEndsEarlyOnlyAfterTheAircraftActuallyPassesItsOriginalTargetRay()
        {
            var w=new World(); w.Player.Position=new Vec2(7f,0f); var c=new Clock();
            var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,4);
            ai.ReadInput(Dt); c.Now=.8f; ai.ReadInput(Dt); float locked=ai.LockedHeading;
            w.Enemy.Position=new Vec2(12f,0f); c.Now=.9f; ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Recover,ai.Phase); Assert.AreEqual(.2f,ai.PhaseRemaining,.001f);
            Assert.AreEqual(locked,ai.LockedHeading); Assert.IsFalse(ai.IsPursuitBoosting);
        }

        [TestCase(false)] [TestCase(true)]
        public void OriginalBattleSpawnAndAPlayerWhoStopsAfterTurningRemainAttackable(bool moveThenStop)
        {
            var w=new World { Player=FlightState.At(new Vec2(-.06097406f,0f),1.5707963f), Enemy=FlightState.At(new Vec2(3.9390259f,-2.5f),1.5707963f) };
            var combat=new ContactCombatModel(5,1,.7f,1f); var ai=new PursuitChargeInputSource(w,2,Spec,combat,Arena);
            var eFlight=FlightModel.From(Spec,Arena); var pFlight=FlightModel.From(new AircraftSpec(2f,9f,18f,240f,.1f,8f,.35f),Arena);
            for(int tick=0;tick<6000&&combat.Status.IsPlayerAlive;tick++)
            {
                var oldE=w.Enemy; var oldP=w.Player; var relative=w.Enemy.Position-w.Player.Position;
                ai.CanStartVisibleAttack=System.Math.Abs(relative.X)<=8.15f&&System.Math.Abs(relative.Y)<=4.25f;
                var input=ai.ReadInput(Dt); eFlight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
                float throttle=moveThenStop&&tick<100?1f:tick<140?-1f:0f;
                float steer=moveThenStop&&tick>=80&&tick<100?.5f:0f;
                pFlight.Step(ref w.Player,new FlightInput(throttle,steer),Dt,FlightMotionCommand.Ordinary(9f,240f));
                combat.StepContact(oldP,w.Player,oldE,w.Enemy,Dt,1f);
            }
            Assert.AreEqual(0,combat.Status.PlayerHealth,$"original spawn stops attacking: move={moveThenStop}, time={combat.Status.ElapsedTime}, pos={w.Enemy.Position}, target={w.Player.Position}, phase={ai.Phase}");
        }

        [Test]
        public void PlayerDeathBrakesAChargeFromFortyFiveToRestWithinOneSecondWithoutAnInstantClip()
        {
            var w=new World(); w.Player.Position=new Vec2(7f,0f); var c=new Clock();
            var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,4); var flight=FlightModel.From(Spec,Arena);
            ai.ReadInput(Dt); c.Now=.8f;
            var input=ai.ReadInput(Dt); flight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
            Assert.AreEqual(AttackRunPhase.Charge,ai.Phase); Assert.AreEqual(45f,w.Enemy.Speed,.001f);
            c.Health=0;
            input=ai.ReadInput(Dt); flight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
            Assert.AreEqual(AttackRunPhase.NoTarget,ai.Phase); Assert.That(w.Enemy.Speed,Is.InRange(40f,44.99f));
            Assert.IsFalse(ai.AttackWindow.IsActive); Assert.AreEqual(0,ai.PreviewPointCount);
            // The combat clock freezes on death; motion still receives fixed steps to stop safely.
            for(int tick=1;tick<50;tick++)
            {
                input=ai.ReadInput(Dt); flight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
            }
            Assert.Less(w.Enemy.Speed,.1f);
        }

        [TestCase(false)] [TestCase(true)]
        public void FourByThreeCameraAllowsAnotherHitAfterThePlayerMovesTurnsAndStops(bool moveThenStop)
        {
            const float halfWidth=5f*4f/3f-1f;
            var w=new World { Player=FlightState.At(new Vec2(-.06097406f,0f),1.5707963f), Enemy=FlightState.At(new Vec2(3.9390259f,-2.5f),1.5707963f) };
            var combat=new ContactCombatModel(5,1,.7f,1f); var ai=new PursuitChargeInputSource(w,2,Spec,combat,Arena) { VisibleApproachHalfWidth=halfWidth };
            var eFlight=FlightModel.From(Spec,Arena); var pFlight=FlightModel.From(new AircraftSpec(2f,9f,18f,240f,.1f,8f,.35f),Arena);
            int hitsWhenStopped=0;
            AttackRunPhase lastPhase=AttackRunPhase.NoTarget;
            for(int tick=0;tick<(moveThenStop?3141:6010)&&combat.Status.IsPlayerAlive;tick++)
            {
                var oldE=w.Enemy; var oldP=w.Player; var relative=w.Enemy.Position-w.Player.Position;
                ai.CanStartVisibleAttack=System.Math.Abs(relative.X)<=halfWidth+.4f&&System.Math.Abs(relative.Y)<=4.25f;
                var input=ai.ReadInput(Dt); eFlight.Step(ref w.Enemy,input,Dt,ai.MotionCommand);
                if(ai.Phase!=lastPhase) { TestContext.WriteLine($"{combat.Status.ElapsedTime} L{ai.Level} {ai.Phase} e={w.Enemy.Position} p={w.Player.Position} h={w.Enemy.Heading} goal={ai.ManeuverPoint}"); lastPhase=ai.Phase; }
                // Unity's freshly loaded scene receives one neutral fixed step before the test presses W.
                float throttle=!moveThenStop||tick==0?0f:tick<101?1f:tick<141?-1f:0f;
                float steer=moveThenStop&&tick>=81&&tick<101?.5f:0f;
                pFlight.Step(ref w.Player,new FlightInput(throttle,steer),Dt,FlightMotionCommand.Ordinary(9f,240f));
                combat.StepContact(oldP,w.Player,oldE,w.Enemy,Dt,1f);
                if(tick==140) hitsWhenStopped=combat.Status.HitSequence;
            }
            if(moveThenStop) Assert.Greater(combat.Status.HitSequence,hitsWhenStopped,"a narrow camera cannot trap pursuit outside its horizontal visibility gate");
            else Assert.AreEqual(0,combat.Status.PlayerHealth,"stationary target must not remain in a periodic miss cycle");
        }

        [Test]
        public void FinalWarningControlUsesTheActualFixedStepForASmallHeadingCorrection()
        {
            var self=new FlightState { Position=Vec2.Zero,Velocity=new Vec2(9.5f,0f) };
            var target=FlightState.At(new Vec2(7.8f,7.8f*System.MathF.Tan(System.MathF.PI/180f)));
            float steer=PursuitSteeringPlanner.SafeWindupSteer(self,target,9.5f,190f,Dt,0f,1f);
            FlightModel.From(Spec).Step(ref self,new FlightInput(1f,steer),Dt,FlightMotionCommand.SpeedOverride(9.5f,190f));
            float error=System.MathF.Abs(Vec2.NormalizeAngle((target.Position-self.Position).AngleRad-self.Heading));
            Assert.Less(error,.1f*System.MathF.PI/180f,"the predicted first control must not become a sixteen-times-larger real turn");
        }

        [TestCase(false,12f)] [TestCase(true,4f)]
        public void FourthLevelUsesTheVisibleAttackResultAndCruiseTravelToChooseRecoverySpace(bool hit,float escapeDistance)
        {
            var w=new World(); w.Player.Position=new Vec2(7f,0f); var c=new Clock();
            var ai=new PursuitChargeInputSource(w,2,Spec,c,Arena,4); ai.ReadInput(Dt);
            c.Now=.8f; ai.ReadInput(Dt); Assert.AreEqual(AttackRunPhase.Charge,ai.Phase);
            w.Enemy.Position=new Vec2(12f,0f); c.Now=1f; if(hit) c.Hits++;
            ai.ReadInput(Dt);
            Assert.AreEqual(AttackRunPhase.Recover,ai.Phase); Assert.AreEqual(.2f,ai.PhaseRemaining,.001f);
            float expectedDistance=MathF.Max(escapeDistance,ai.CruiseSpeed*ai.RecoverySeconds+2f);
            Assert.AreEqual(expectedDistance,Vec2.Distance(w.Enemy.Position,ai.ManeuverPoint),.001f);
        }

        sealed class World:IWorldView
        {
            public FlightState Enemy=new FlightState{Position=Vec2.Zero,Velocity=new Vec2(9f,0f)};
            public FlightState Player=new FlightState{Position=new Vec2(18f,0f),Velocity=new Vec2(9f,0f)};
            public VisiblePropulsionCue Cue;
            public bool Visible=true;
            public int AircraftCount=>2;
            public AircraftSnapshot GetAircraft(int index)=>index==0?new AircraftSnapshot(2,2,Enemy):new AircraftSnapshot(1,1,Player,true,true,Cue,Visible);
        }
        sealed class Clock:ICombatStatusView { public float Now; public int Hits; public int Health=5; public CombatStatus Status=>new CombatStatus(Health,5,Hits,0f,Now); }
    }
}
