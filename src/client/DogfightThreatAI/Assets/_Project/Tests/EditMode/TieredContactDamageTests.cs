using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class TieredContactDamageTests
    {
        static FlightState At(float x) => FlightState.At(new Vec2(x, 0f));

        [Test]
        public void PerContactDamageChangesWithoutResettingHealthClockOrSequence()
        {
            var model = new ContactCombatModel(5, 1, .7f, 1f);
            var p = At(0f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .1f, 1f, 1));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            model.Step(p, p, At(5f), At(5f), .6f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .01f, 1f, 3));
            Assert.AreEqual(1, model.Status.PlayerHealth);
            Assert.AreEqual(2, model.Status.HitSequence);
            Assert.AreEqual(.71f, model.Status.ElapsedTime, .00001f);
        }

        [Test]
        public void HigherDamageCannotBypassOrRefreshExistingProtection()
        {
            var model = new ContactCombatModel(5, 1, .7f, 1f);
            var p = At(0f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .02f, 1f, 2));
            Assert.AreEqual(3, model.Status.PlayerHealth);
            Assert.IsFalse(model.StepContact(p, p, p, p, .1f, 1f, 3));
            Assert.AreEqual(3, model.Status.PlayerHealth);
            Assert.AreEqual(1, model.Status.HitSequence);
            Assert.AreEqual(.58f, model.Status.InvulnerabilityRemaining, .00001f);
            model.Step(p, p, At(5f), At(5f), .6f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .02f, 1f, 3));
            Assert.AreEqual(0, model.Status.PlayerHealth);
            Assert.AreEqual(2, model.Status.HitSequence);
        }

        [Test]
        public void LegacyContactKeepsConstructorDamageAfterExplicitDamageCall()
        {
            var model = new ContactCombatModel(9, 2, .7f, 1f);
            var p = At(0f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .02f, 1f));
            Assert.AreEqual(7, model.Status.PlayerHealth);
            model.Step(p, p, At(5f), At(5f), .7f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .02f, 1f, 1));
            Assert.AreEqual(6, model.Status.PlayerHealth);
            model.Step(p, p, At(5f), At(5f), .7f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .02f, 1f));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(3, model.Status.HitSequence);
        }

        [Test]
        public void LethalDamageClampsHealthAndCountsOneContact()
        {
            var model = new ContactCombatModel(5, 1, .7f, 1f);
            var p = At(0f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .02f, 1f, int.MaxValue));
            Assert.AreEqual(0, model.Status.PlayerHealth);
            Assert.AreEqual(1, model.Status.HitSequence);
            Assert.IsFalse(model.StepContact(p, p, p, p, .02f, 1f, 3));
            Assert.AreEqual(1, model.Status.HitSequence);
            Assert.AreEqual(.02f, model.Status.ElapsedTime, .00001f);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void InvalidDamageDoesNotAdvanceAnyCombatState(int damage)
        {
            var model = new ContactCombatModel(5, 1, .7f, 1f);
            var p = At(0f);
            Assert.IsFalse(model.StepContact(p, p, p, p, .02f, 1f, damage));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.AreEqual(0, model.Status.HitSequence);
            Assert.AreEqual(0f, model.Status.ElapsedTime);
            Assert.AreEqual(0f, model.Status.InvulnerabilityRemaining);
        }

        [Test]
        public void ExplicitDamageStillRequiresRealSweptContact()
        {
            var model = new ContactCombatModel(5, 1, .7f, 1f);
            var p = At(0f);
            Assert.IsFalse(model.StepContact(p, p, At(3f), At(3f), .02f, 1f, 3));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.IsTrue(model.StepContact(p, p, At(-1.2f), At(1.2f), .02f, 1f, 3));
            Assert.AreEqual(2, model.Status.PlayerHealth);
            Assert.AreEqual(1, model.Status.HitSequence);
        }

        [Test]
        public void InvalidRadiusStillAdvancesClockWithoutDamage()
        {
            var model = new ContactCombatModel(5, 1, .7f, 1f);
            var p = At(0f);
            Assert.IsFalse(model.StepContact(p, p, p, p, .02f, 0f, 3));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.AreEqual(.02f, model.Status.ElapsedTime, .00001f);
        }

        [Test]
        public void PendingDifficultyDoesNotChangeDamageInsideAnAttackOrRecovery()
        {
            var world = new World();
            var clock = new Clock();
            var spec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
            var ai = new PursuitChargeInputSource(world, 2, spec, clock, null, 1);
            ai.ReadInput(.02f);
            int firstDamage = ai.CollisionDamage;
            Assert.AreEqual(PursuitDifficultyProfile.ForLevel(1).CollisionDamage, firstDamage);
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase);
            ai.RequestDifficulty(4);
            clock.Now = .5f;
            ai.ReadInput(.02f);
            Assert.AreEqual(firstDamage, ai.CollisionDamage);
            clock.Now = .8f;
            ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            Assert.AreEqual(firstDamage, ai.CollisionDamage);
            clock.Now = 1.7f;
            ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Recover, ai.Phase);
            Assert.AreEqual(firstDamage, ai.CollisionDamage);
            clock.Now += ai.RecoverySeconds + .01f;
            ai.ReadInput(.02f);
            Assert.AreEqual(4, ai.Level);
            Assert.AreEqual(PursuitDifficultyProfile.ForLevel(4).CollisionDamage, ai.CollisionDamage);
        }

        sealed class World : IWorldView
        {
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(1, 1, FlightState.At(Vec2.Zero), true, true)
                : new AircraftSnapshot(2, 2, FlightState.At(new Vec2(-6f, 0f)));
        }

        sealed class Clock : ICombatStatusView
        {
            public float Now;
            public CombatStatus Status => new CombatStatus(5, 5, 0, 0f, Now);
        }
    }
}
