using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class ContactVolumeCombatTests
    {
        static FlightState At(float x, float y = 0f) => FlightState.At(new Vec2(x, y));
        static ContactCombatModel Model() => new ContactCombatModel(invulnerabilitySeconds: .7f);

        [Test]
        public void TouchNeedsNoAttackWindowAndGrantsPointSevenSecondsProtection()
        {
            var model = Model();
            Assert.IsTrue(model.StepContact(At(0f), At(0f), At(.9f), At(.9f), .02f, 1f));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(1, model.Status.HitSequence);
            Assert.AreEqual(.68f, model.Status.InvulnerabilityRemaining, .00001f);
        }

        [Test]
        public void PersistentOverlapCannotDamageAgainUntilProtectionActuallyExpires()
        {
            var model = Model();
            var point = At(0f);
            Assert.IsTrue(model.StepContact(point, point, point, point, .02f, 1f));
            for (int i = 0; i < 33; i++)
                Assert.IsFalse(model.StepContact(point, point, point, point, .02f, 1f));
            Assert.IsFalse(model.StepContact(point, point, point, point, .01f, 1f));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.IsTrue(model.StepContact(point, point, point, point, .02f, 1f));
            Assert.AreEqual(3, model.Status.PlayerHealth);
        }

        [TestCase(.99f, true)]
        [TestCase(1f, true)]
        [TestCase(1.01f, false)]
        public void FortyFiveSpeedGlancingPassUsesSweepInsteadOfOnlyEndpointOverlap(float y, bool expected)
        {
            var model = Model();
            Assert.Greater(Vec2.Distance(At(0f).Position, At(-.45f, y).Position), 1f);
            Assert.Greater(Vec2.Distance(At(0f).Position, At(.45f, y).Position), 1f);
            Assert.AreEqual(expected, model.StepContact(At(0f), At(0f), At(-.45f, y), At(.45f, y), .02f, 1f));
            Assert.AreEqual(expected ? 4 : 5, model.Status.PlayerHealth);
        }

        [Test]
        public void DamageUsesCurrentCombinedColliderRadius()
        {
            var model = Model();
            Assert.IsFalse(model.StepContact(At(0f), At(0f), At(.9f), At(.9f), .02f, .8f));
            Assert.AreEqual(.02f, model.Status.ElapsedTime, .00001f);
            Assert.IsTrue(model.StepContact(At(0f), At(0f), At(.9f), At(.9f), .02f, 1f));
        }

        [Test]
        public void ContactOnlyBeforeProtectionEndsCannotDamageRetroactively()
        {
            var model = Model();
            var p = At(0f);
            Assert.IsTrue(model.StepContact(p, p, p, p, .1f, 1f));
            Assert.IsFalse(model.StepContact(p, p, p, At(10f), .8f, 1f));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(0f, model.Status.InvulnerabilityRemaining);
        }

        [Test]
        public void RepeatedContactsEventuallyKillAtExactlyFiveDamageEvents()
        {
            var model = Model();
            var p = At(0f);
            for (int i = 0; i < 200 && model.Status.IsPlayerAlive; i++)
                model.StepContact(p, p, p, p, .02f, 1f);
            Assert.AreEqual(0, model.Status.PlayerHealth);
            Assert.AreEqual(5, model.Status.HitSequence);
            Assert.IsFalse(model.StepContact(p, p, p, p, .02f, 1f));
        }
    }
}
