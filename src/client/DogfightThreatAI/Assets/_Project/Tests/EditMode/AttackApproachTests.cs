using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class AttackApproachTests
    {
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static AircraftSnapshot Target(Vec2 position, float heading, float speed = 3f, int id = 1)
        {
            var state = FlightState.At(position, heading);
            state.Velocity = Vec2.FromAngle(heading) * speed;
            return new AircraftSnapshot(id, 1, state, true, true);
        }

        [TestCase(40f)]
        [TestCase(60f)]
        public void AdvancedApproach_DoesNotSpinItsWorldAnchorWithEveryObservedTurn(float clock)
        {
            var selector = new TacticalPositionSelector(Spec);
            var self = FlightState.At(new Vec2(-8f, 0f));
            var profile = AiDifficultyProfile.ForTime(clock);
            var first = selector.Select(self, Target(Vec2.Zero, 0f), profile, null, clock);
            for (int i = 1; i <= 3; i++)
            {
                var next = selector.Select(self, Target(new Vec2(.15f * i, .1f * i), i * .6f), profile, null, clock + i * .25f);
                Assert.Less(Vec2.Distance(first.Position, next.Position), .001f,
                    "A committed approach is a world-space destination; a new movement-axis angle must not move it away every quarter second.");
            }
        }

        [TestCase(40f)]
        [TestCase(60f)]
        public void AdvancedApproach_WithoutProgressFallsBackToAnExecutableDirectIntercept(float clock)
        {
            var selector = new TacticalPositionSelector(Spec);
            var self = FlightState.At(new Vec2(-8f, 0f));
            var target = Target(Vec2.Zero, 0f);
            var profile = AiDifficultyProfile.ForTime(clock);
            selector.Select(self, target, profile, null, clock);
            TacticalPlan last = default;
            for (int i = 1; i <= 12; i++)
                last = selector.Select(self, target, profile, null, clock + i * .25f);
            Assert.AreEqual(AttackTactic.Intercept, last.Tactic,
                "Repeatedly timing out at the same flank destination must change the approach, not restart the same plan forever.");
            Assert.Less(MathF.Abs(last.Position.Y), .001f);
        }

        [Test]
        public void ACommittedApproachIsDiscardedWhenTheObservedTargetLeavesItsRegion()
        {
            var selector = new TacticalPositionSelector(Spec);
            var self = FlightState.At(new Vec2(-8f, 0f));
            var profile = AiDifficultyProfile.ForTime(40f);
            var first = selector.Select(self, Target(Vec2.Zero, 0f), profile, null, 40f);
            var next = selector.Select(self, Target(new Vec2(20f, 0f), 0f), profile, null, 40.25f);
            Assert.Greater(Vec2.Distance(first.Position, next.Position), 10f);
        }

        static AircraftSnapshot Circle(float time, int id = 1)
        {
            const float radius = 1.4f, omega = 4f;
            float angle = omega * time;
            var position = Vec2.FromAngle(angle) * radius;
            var velocity = new Vec2(-MathF.Sin(angle), MathF.Cos(angle)) * (radius * omega);
            var state = FlightState.At(position, angle + MathF.PI * .5f);
            state.Velocity = velocity;
            return new AircraftSnapshot(id, 1, state, true, true);
        }

        [Test]
        public void Forecast_UsesObservedCurvatureRatherThanProjectingACircleAlongItsTangent()
        {
            var forecast = new TargetMotionForecast();
            for (int i = 0; i <= 5; i++) forecast.Sample(Circle(i * .1f), i * .1f);
            var observed = Circle(.5f);
            Vec2 future = forecast.Predict(observed, 1.1f);
            Vec2 actual = Circle(1.6f).Flight.Position;
            Assert.Less(Vec2.Distance(future, actual), .03f);
            Assert.Greater(Vec2.Distance(observed.Flight.Position + observed.Flight.Velocity * 1.1f, actual), 4f);
        }

        [Test]
        public void Forecast_RepeatedTimestampCannotReplaceTheObservedTurnHistory()
        {
            var forecast = new TargetMotionForecast();
            forecast.Sample(Circle(0f), 0f);
            forecast.Sample(Circle(.1f), .1f);
            float rate = forecast.TurnRateRadiansPerSecond;
            forecast.Sample(Target(Vec2.Zero, -1f, 9f), .1f);
            Assert.AreEqual(rate, forecast.TurnRateRadiansPerSecond);
            Assert.AreEqual(4f, rate, .001f);
        }

        [Test]
        public void Forecast_TargetChangeAndClockRewindDiscardTheOldCurve()
        {
            var forecast = new TargetMotionForecast();
            forecast.Sample(Circle(0f), 0f);
            forecast.Sample(Circle(.1f), .1f);
            var changed = Target(Vec2.Zero, 0f, 3f, 99);
            forecast.Sample(changed, .2f);
            Assert.AreEqual(new Vec2(3f, 0f), forecast.Predict(changed, 1f));
            forecast.Sample(Circle(.3f), .3f);
            forecast.Sample(Circle(.4f), .4f);
            forecast.Sample(Circle(0f), 0f);
            Assert.AreEqual(0f, forecast.TurnRateRadiansPerSecond);
            forecast.Reset();
            Assert.AreEqual(0f, forecast.TurnRateRadiansPerSecond);
        }

        [Test]
        public void Forecast_LimitsItsHorizonAndKeepsPredictionsInsideTheArena()
        {
            var forecast = new TargetMotionForecast();
            var observed = Target(new Vec2(8f, 0f), 0f, 3f);
            forecast.Sample(observed, 0f);
            Assert.AreEqual(new Vec2(14f, 0f), forecast.Predict(observed, 99f));
            Assert.AreEqual(observed.Flight.Position, forecast.Predict(observed, float.NaN));
            Assert.AreEqual(new Vec2(10f, 0f), forecast.Predict(observed, 1f, Bounds2D.Centered(new Vec2(10, 7))));
        }

        [Test]
        public void AimRefreshKeepsTheCommittedWaypointButUsesTheNewDelayedObservation()
        {
            var selector = new TacticalPositionSelector(Spec);
            var original = new TacticalPlan(new Vec2(-5f, -2f), AttackTactic.Intercept, Vec2.Zero);
            var observed = Target(new Vec2(2f, 1f), 0f, 3f);
            var refreshed = selector.UpdateAim(original, observed, AiDifficultyProfile.ForTime(40f), null, 40f, 39.8f);
            Assert.AreEqual(original.Position, refreshed.Position);
            Assert.AreEqual(original.Tactic, refreshed.Tactic);
            Assert.Greater(refreshed.AimPosition.X, observed.Flight.Position.X + 3f);
            Assert.AreEqual(observed.Flight.Position.Y, refreshed.AimPosition.Y);
        }

        [Test]
        public void StationaryAimRemainsAtTheObservedTargetAndNeverAtThePreparationWaypoint()
        {
            var selector = new TacticalPositionSelector(Spec);
            var observed = Target(new Vec2(3f, 2f), 0f, 0f);
            var plan = selector.Select(FlightState.At(new Vec2(-8f, 0f)), observed,
                AiDifficultyProfile.ForTime(60f), null, 60f, 59.8f);
            Assert.AreEqual(observed.Flight.Position, plan.AimPosition);
            Assert.Greater(Vec2.Distance(plan.Position, plan.AimPosition), 4f);
        }

        [Test]
        public void AimRefreshUsesTheActualObservationAgeAndObservedCurvature()
        {
            var selector = new TacticalPositionSelector(Spec);
            var profile = AiDifficultyProfile.ForTime(40f);
            var plan = new TacticalPlan(new Vec2(-5f, 0f), AttackTactic.Intercept);
            selector.UpdateAim(plan, Circle(0f), profile, null, 40.2f, 40f);
            selector.UpdateAim(plan, Circle(.1f), profile, null, 40.3f, 40.1f);
            var aim = selector.UpdateAim(plan, Circle(.1f), profile, null, 40.35f, 40.1f);
            Assert.Less(Vec2.Distance(Circle(1.4f).Flight.Position, aim.AimPosition), .03f,
                "Prediction includes .25 seconds observation age plus 1.05 seconds planned travel, without recording a repeated observation as fresh.");
            Assert.AreEqual(plan.Position, aim.Position);
        }

        [Test]
        public void ActualImpactEstimateChangesTheCurvedAimWithoutMovingTheApproach()
        {
            var selector = new TacticalPositionSelector(Spec);
            var profile = AiDifficultyProfile.ForTime(40f);
            var plan = new TacticalPlan(new Vec2(-5f, 0f), AttackTactic.Intercept);
            selector.UpdateAim(plan, Circle(0f), profile, null, 40.2f, 40f);
            selector.UpdateAim(plan, Circle(.1f), profile, null, 40.3f, 40.1f);
            var early = selector.UpdateAim(plan, Circle(.1f), profile, null, 40.3f, 40.1f, .8f);
            var late = selector.UpdateAim(plan, Circle(.1f), profile, null, 40.3f, 40.1f, 1.6f);
            Assert.Less(Vec2.Distance(Circle(1.1f).Flight.Position, early.AimPosition), .03f);
            Assert.Less(Vec2.Distance(Circle(1.9f).Flight.Position, late.AimPosition), .03f);
            Assert.Greater(Vec2.Distance(early.AimPosition, late.AimPosition), 2f);
            Assert.AreEqual(plan.Position, early.Position);
            Assert.AreEqual(plan.Position, late.Position);
        }

        [Test]
        public void ImpactEstimateIsBoundedAndDoesNotDisplaceAStationaryTarget()
        {
            var selector = new TacticalPositionSelector(Spec);
            var profile = AiDifficultyProfile.ForTime(60f);
            var plan = new TacticalPlan(new Vec2(-5f, 0f), AttackTactic.Intercept);
            var moving = Target(Vec2.Zero, 0f, 3f);
            var early = selector.UpdateAim(plan, moving, profile, null, 60f, 60f, 0f);
            var late = selector.UpdateAim(plan, moving, profile, null, 60f, 60f, 99f);
            Assert.Less(Vec2.Distance(new Vec2(2.4f, 0f), early.AimPosition), .00001f);
            Assert.Less(Vec2.Distance(new Vec2(5.1f, 0f), late.AimPosition), .00001f);
            var stopped = Target(new Vec2(2f, 3f), 0f, 0f);
            early = selector.UpdateAim(plan, stopped, profile, null, 60.1f, 60.1f, .8f);
            late = selector.UpdateAim(plan, stopped, profile, null, 60.1f, 60.1f, 1.7f);
            Assert.AreEqual(stopped.Flight.Position, early.AimPosition);
            Assert.AreEqual(stopped.Flight.Position, late.AimPosition);
        }
    }
}
