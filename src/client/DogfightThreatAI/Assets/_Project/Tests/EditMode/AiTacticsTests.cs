using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class AiTacticsTests
    {
        static readonly AircraftSpec Spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static AircraftSnapshot Target(float x, float y = 0f, float vx = 0f, float vy = 0f, int id = 2)
        {
            var state = FlightState.At(new Vec2(x, y));
            state.Velocity = new Vec2(vx, vy);
            return new AircraftSnapshot(id, 2, state, isPlayerControlled: true);
        }

        [TestCase(0f, 1, .35f, 0f, 7.5f, 1.8f)]
        [TestCase(19.999f, 1, .35f, 0f, 7.5f, 1.8f)]
        [TestCase(20f, 2, .25f, .3f, 7.8f, 1.6f)]
        [TestCase(40f, 3, .2f, .4f, 8f, 1.4f)]
        [TestCase(60f, 4, .15f, .45f, 8f, 1.2f)]
        [TestCase(600f, 4, .15f, .45f, 8f, 1.2f)]
        public void Difficulty_UsesTheApprovedTimeBands(float time, int level, float delay, float lead, float cruise, float recovery)
        {
            var profile = AiDifficultyProfile.ForTime(time);
            Assert.AreEqual(level, profile.Level);
            Assert.AreEqual(delay, profile.ObservationDelay, 1e-6f);
            Assert.AreEqual(lead, profile.PredictionLead, 1e-6f);
            Assert.AreEqual(cruise, profile.CruiseSpeed, 1e-6f);
            Assert.AreEqual(recovery, profile.RecoverySeconds, 1e-6f);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.NegativeInfinity)]
        public void Difficulty_InvalidEarlyTimeUsesFirstBand(float time)
            => Assert.AreEqual(1, AiDifficultyProfile.ForTime(time).Level);

        [Test]
        public void Memory_DoesNotRevealASampleBeforeItsDelayHasElapsed()
        {
            var memory = new PerceptionMemory();
            memory.Sample(Target(0), 0f);
            memory.Sample(Target(10), .1f);
            memory.Sample(Target(20), .2f);
            Assert.IsFalse(memory.TryObserve(.2f, .35f, out _, out _));
            Assert.IsTrue(memory.TryObserve(.46f, .35f, out var observed, out float time));
            Assert.AreEqual(.1f, time, 1e-6f);
            Assert.AreEqual(10f, observed.Flight.Position.X);
        }

        [Test]
        public void Memory_UsesTenHertzSamplesWithoutReplacingASameTickSnapshot()
        {
            var memory = new PerceptionMemory();
            memory.Sample(Target(1), 0f);
            memory.Sample(Target(100), 0f);
            memory.Sample(Target(200), .08f);
            Assert.IsTrue(memory.TryObserve(.09f, 0f, out var first, out float firstTime));
            Assert.AreEqual(1f, first.Flight.Position.X);
            Assert.AreEqual(0f, firstTime);
            memory.Sample(Target(2), .1f);
            Assert.IsTrue(memory.TryObserve(.11f, 0f, out var second, out float secondTime));
            Assert.AreEqual(2f, second.Flight.Position.X);
            Assert.AreEqual(.1f, secondTime, 1e-6f);
        }

        [Test]
        public void Memory_KeepsTheActualTimestampWhenASampleArrivesLate()
        {
            var memory = new PerceptionMemory();
            memory.Sample(Target(1), 0f);
            memory.Sample(Target(2), .13f);
            Assert.IsTrue(memory.TryObserve(.12f, 0f, out var earlier, out float at));
            Assert.AreEqual(1f, earlier.Flight.Position.X);
            Assert.AreEqual(0f, at);
            Assert.IsTrue(memory.TryObserve(.13f, 0f, out _, out at));
            Assert.AreEqual(.13f, at, 1e-6f);
        }

        [Test]
        public void Memory_DoesNotExtrapolateAFreshVelocityOrPositionIntoAnOlderObservation()
        {
            var memory = new PerceptionMemory();
            memory.Sample(Target(1, vx: 2), 0f);
            memory.Sample(Target(100, vx: -9), .1f);
            Assert.IsTrue(memory.TryObserve(.35f, .35f, out var observed, out _));
            Assert.AreEqual(1f, observed.Flight.Position.X);
            Assert.AreEqual(2f, observed.Flight.Velocity.X);
        }

        [Test]
        public void Memory_ResetClockRewindAndTargetChangeDiscardOldHistory()
        {
            var memory = new PerceptionMemory();
            memory.Sample(Target(9), 5f);
            memory.Sample(Target(1), 0f);
            Assert.IsTrue(memory.TryObserve(1f, .35f, out var rewind, out _));
            Assert.AreEqual(1f, rewind.Flight.Position.X);
            memory.Sample(Target(3, id: 3), .1f);
            Assert.IsFalse(memory.TryObserve(.1f, .35f, out _, out _));
            Assert.IsTrue(memory.TryObserve(.5f, .35f, out var changed, out _));
            Assert.AreEqual(3, changed.EntityId);
            memory.Reset();
            Assert.IsFalse(memory.TryObserve(99f, 0f, out _, out _));
        }

        [Test]
        public void Memory_RejectsInvalidTimesAndNonfiniteSnapshots()
        {
            var memory = new PerceptionMemory();
            memory.Sample(Target(float.NaN), 0f);
            memory.Sample(Target(1), float.NaN);
            Assert.IsFalse(memory.TryObserve(1f, 0f, out _, out _));
            memory.Sample(Target(2), 0f);
            Assert.IsFalse(memory.TryObserve(float.NaN, 0f, out _, out _));
            Assert.IsFalse(memory.TryObserve(1f, -.1f, out _, out _));
            Assert.IsTrue(memory.TryObserve(1f, 0f, out var valid, out _));
            Assert.AreEqual(2f, valid.Flight.Position.X);
        }

        [Test]
        public void Memory_RetainsTheApprovedMaximumDelayAcrossLongBattles()
        {
            var memory = new PerceptionMemory();
            for (int i = 0; i <= 1000; i++) memory.Sample(Target(i), i * .1f);
            Assert.IsTrue(memory.TryObserve(100f, .35f, out var observed, out float at));
            Assert.LessOrEqual(at, 99.65f);
            Assert.GreaterOrEqual(at, 99.5f);
            Assert.AreEqual(at * 10f, observed.Flight.Position.X, .001f);
        }

        [Test]
        public void LevelOne_PreparesFivePointOneUnitsFromTheObservedTarget()
        {
            var selector = new TacticalPositionSelector(Spec);
            var plan = selector.Select(FlightState.At(new Vec2(-15, 0)), Target(0), AiDifficultyProfile.ForTime(0), null, 0);
            Assert.AreEqual(AttackTactic.Pursuit, plan.Tactic);
            Assert.AreEqual(-5.1f, plan.Position.X, .001f);
            Assert.AreEqual(0f, plan.Position.Y, .001f);
        }

        [Test]
        public void LevelTwo_UsesOnlyTheSuppliedObservedVelocityForItsLimitedLead()
        {
            var self = FlightState.At(new Vec2(-15, 0));
            var selector = new TacticalPositionSelector(Spec);
            var target = Target(0, vx: 4);
            var plan = selector.Select(self, target, AiDifficultyProfile.ForTime(20), null, 20);
            Assert.AreEqual(AttackTactic.Intercept, plan.Tactic);
            Assert.AreEqual(-3.9f, plan.Position.X, .001f);
            Assert.AreEqual(0f, plan.Position.Y, .001f);
        }

        [Test]
        public void HigherLevels_SelectLateralPreparationPositions()
        {
            var selector = new TacticalPositionSelector(Spec);
            var target = Target(0, vx: 3);
            var profile = AiDifficultyProfile.ForTime(40);
            var plan = selector.Select(FlightState.At(new Vec2(-15, 0)), target, profile, null, 40);
            Assert.That(plan.Tactic, Is.EqualTo(AttackTactic.FlankLeft).Or.EqualTo(AttackTactic.FlankRight));
            Assert.AreEqual(5.1f, Vec2.Distance(plan.Position, target.Flight.Position + target.Flight.Velocity * profile.PredictionLead), .001f);
            Assert.Greater(MathF.Abs(plan.Position.Y), 3f);
        }

        [Test]
        public void FlankChoice_IsStableAcrossRepeatedReadsAndTinyMotion()
        {
            var selector = new TacticalPositionSelector(Spec);
            var profile = AiDifficultyProfile.ForTime(40);
            var self = FlightState.At(new Vec2(-15, 0));
            var first = selector.Select(self, Target(0), profile, null, 40);
            Assert.That(first.Tactic, Is.EqualTo(AttackTactic.FlankLeft).Or.EqualTo(AttackTactic.FlankRight));
            for (int i = 0; i < 30; i++)
            {
                self.Position.Y = (i & 1) == 0 ? -.001f : .001f;
                var next = selector.Select(self, Target(0), profile, null, 40 + i * .02f);
                Assert.AreEqual(first.Tactic, next.Tactic);
            }
        }

        [Test]
        public void TacticalSelection_KeepsItsPreparationPointAndEscapeSpaceInsideTheArena()
        {
            var bounds = Bounds2D.Centered(new Vec2(10, 7));
            var selector = new TacticalPositionSelector(Spec);
            var target = Target(8, 5);
            var plan = selector.Select(FlightState.At(new Vec2(-6, 0)), target, AiDifficultyProfile.ForTime(40), bounds, 40);
            Assert.IsTrue(bounds.Contains(plan.Position));
            Assert.Greater(Vec2.Distance(plan.Position, target.Flight.Position), 3f);
            Assert.AreEqual(5.1f, Vec2.Distance(plan.Position, target.Flight.Position), .001f);
        }

        [Test]
        public void PreparationRoute_DoesNotCutThroughTheObservedPlayer()
        {
            var self = FlightState.At(new Vec2(-12, 0));
            var target = Target(0);
            var selector = new TacticalPositionSelector(Spec);
            var plan = selector.Select(self, target, AiDifficultyProfile.ForTime(40), null, 40);
            Assert.That(plan.Tactic, Is.EqualTo(AttackTactic.FlankLeft).Or.EqualTo(AttackTactic.FlankRight));
            var delta = plan.Position - self.Position;
            float fraction = MathF.Max(0f, MathF.Min(1f, Vec2.Dot(target.Flight.Position - self.Position, delta) / delta.SqrMagnitude));
            Assert.Greater(Vec2.Distance(self.Position + delta * fraction, target.Flight.Position), 1.5f);
        }

        [Test]
        public void LevelFour_RecentFourDodgesCanChangeTheNextPlanWithoutMutatingTheOldPlan()
        {
            var self = FlightState.At(new Vec2(-15, 0));
            var target = Target(0, vx: 3);
            var selector = new TacticalPositionSelector(Spec);
            var profile = AiDifficultyProfile.ForTime(60);
            for (int i = 0; i < 4; i++) selector.RecordDodge(1f);
            var first = selector.Select(self, target, profile, null, 60);
            Assert.AreEqual(AttackTactic.FlankLeft, first.Tactic);
            var saved = first.Position;
            for (int i = 0; i < 4; i++) selector.RecordDodge(-1f);
            var second = selector.Select(self, target, profile, null, 61);
            Assert.AreEqual(AttackTactic.FlankRight, second.Tactic);
            Assert.AreEqual(saved, first.Position);
        }

        [Test]
        public void EarlierLevels_DoNotReadTheRecordedDodgeHabit()
        {
            var self = FlightState.At(new Vec2(-15, 0));
            var target = Target(0);
            var a = new TacticalPositionSelector(Spec);
            var b = new TacticalPositionSelector(Spec);
            for (int i = 0; i < 4; i++) { a.RecordDodge(1f); b.RecordDodge(-1f); }
            var pa = a.Select(self, target, AiDifficultyProfile.ForTime(40), null, 40);
            var pb = b.Select(self, target, AiDifficultyProfile.ForTime(40), null, 40);
            Assert.That(pa.Tactic, Is.EqualTo(AttackTactic.FlankLeft).Or.EqualTo(AttackTactic.FlankRight));
            Assert.AreEqual(pa.Tactic, pb.Tactic);
            Assert.AreEqual(pa.Position, pb.Position);
        }

        [Test]
        public void LevelThree_DodgeRecordsDoNotEvenChangeItsSideHysteresis()
        {
            var self = FlightState.At(new Vec2(-15, .001f));
            var target = Target(0);
            var selector = new TacticalPositionSelector(Spec);
            var profile = AiDifficultyProfile.ForTime(40);
            var first = selector.Select(self, target, profile, null, 40);
            self.Position.Y = -.001f;
            selector.RecordDodge(-1f);
            var next = selector.Select(self, target, profile, null, 40.02f);
            Assert.AreEqual(first.Tactic, next.Tactic);
        }

        [Test]
        public void FiniteExtremeObservedPositionAndVelocity_DoNotProduceAnInfinitePlan()
        {
            var self = FlightState.At(new Vec2(-float.MaxValue, 0));
            var target = Target(float.MaxValue, vx: float.MaxValue);
            var selector = new TacticalPositionSelector(Spec);
            var plan = selector.Select(self, target, AiDifficultyProfile.ForTime(20), null, 20);
            Assert.IsFalse(float.IsNaN(plan.Position.X) || float.IsInfinity(plan.Position.X));
            Assert.IsFalse(float.IsNaN(plan.Position.Y) || float.IsInfinity(plan.Position.Y));
        }

        [TestCase(-1f, -1f)]
        [TestCase(-1f, 1f)]
        [TestCase(1f, -1f)]
        [TestCase(1f, 1f)]
        public void CornerPreparation_UsesTheRealObservedCenterWithoutMovingItInward(float xSign, float ySign)
        {
            var bounds = Bounds2D.Centered(new Vec2(200, 150));
            var target = Target(200 * xSign, 150 * ySign);
            var self = FlightState.At(new Vec2(190 * xSign, 140 * ySign));
            var selector = new TacticalPositionSelector(Spec);
            var plan = selector.Select(self, target, AiDifficultyProfile.ForTime(40), bounds, 40);
            Assert.IsTrue(bounds.Contains(plan.Position));
            Assert.AreEqual(5.1f, Vec2.Distance(plan.Position, target.Flight.Position), .002f,
                "The candidate margin must not shift the observed target and invalidate the 4.5..5.5 attack preparation band.");
        }
    }
}
