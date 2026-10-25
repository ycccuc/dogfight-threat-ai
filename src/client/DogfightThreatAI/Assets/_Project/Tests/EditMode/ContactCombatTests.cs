using System;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class ContactCombatTests
    {
        static FlightState At(float x, float y = 0f) => FlightState.At(new Vec2(x, y));
        static AttackWindow Active(int id = 1, float start = 0f, float end = 1f)
            => new AttackWindow(id, true, start, end);

        [Test]
        public void Contact_RemovesOneHealthAndStartsInvulnerability()
        {
            var model = new ContactCombatModel();
            var player = At(0f);
            var enemy = At(0.9f);

            Assert.IsTrue(model.Step(player, player, enemy, enemy, 0.02f, Active()));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(5, model.Status.MaxPlayerHealth);
            Assert.AreEqual(1, model.Status.HitSequence);
            Assert.AreEqual(1.98f, model.Status.InvulnerabilityRemaining, 1e-5f);
            Assert.IsTrue(model.Status.IsPlayerAlive);
        }

        [Test]
        public void SweptContact_DetectsFastAircraftThatCrossBetweenFrames()
        {
            var model = new ContactCombatModel();
            var player = At(0f);

            Assert.IsTrue(model.Step(player, player, At(-4f), At(4f), 0.02f, Active()));
            Assert.AreEqual(4, model.Status.PlayerHealth);
        }

        [Test]
        public void RelativeSweep_DetectsBothAircraftMovingThroughEachOther()
        {
            var model = new ContactCombatModel();

            Assert.IsTrue(model.Step(At(-3f), At(3f), At(3f), At(-3f), 0.02f, Active()));
            Assert.AreEqual(1, model.Status.HitSequence);
        }

        [Test]
        public void NearMiss_DoesNotDamageDespiteOverlappingAxisRanges()
        {
            var model = new ContactCombatModel();
            var player = At(0f);

            Assert.IsFalse(model.Step(player, player, At(-4f, 1.01f), At(4f, 1.01f), 0.02f, Active()));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.AreEqual(0, model.Status.HitSequence);
        }

        [Test]
        public void ExactTangent_IsAContact()
        {
            var model = new ContactCombatModel();
            var player = At(0f);

            Assert.IsTrue(model.Step(player, player, At(-4f, 1f), At(4f, 1f), 0.02f, Active()));
        }

        [Test]
        public void RepeatedOverlap_CannotDrainHealthInsideInvulnerabilityWindow()
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active()));

            for (int tick = 0; tick < 90; tick++)
                Assert.IsFalse(model.Step(state, state, state, state, 0.02f, Active(tick + 2)));

            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(1, model.Status.HitSequence);
            Assert.Greater(model.Status.InvulnerabilityRemaining, 0f);
        }

        [Test]
        public void ContactOnlyBeforeInvulnerabilityExpires_DoesNotRetroactivelyDamage()
        {
            var model = new ContactCombatModel(invulnerabilitySeconds: 1f);
            var player = At(0f);
            Assert.IsTrue(model.Step(player, player, player, player, 0.1f, Active()));
            // .9 seconds remain. The enemy is inside the radius only during the first .1 second.
            Assert.IsFalse(model.Step(player, player, At(0f), At(10f), 1f, Active()));

            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(0f, model.Status.InvulnerabilityRemaining);
        }

        [Test]
        public void ContactAfterInvulnerabilityExpiresWithinStep_DamagesOnce()
        {
            var model = new ContactCombatModel(invulnerabilitySeconds: 1f);
            var player = At(0f);
            Assert.IsTrue(model.Step(player, player, player, player, 0.1f, Active()));

            Assert.IsTrue(model.Step(player, player, At(10f), At(0f), 1f, Active(2)));
            Assert.AreEqual(3, model.Status.PlayerHealth);
            Assert.AreEqual(2, model.Status.HitSequence);
            Assert.AreEqual(0.9f, model.Status.InvulnerabilityRemaining, 1e-5f);
        }

        [Test]
        public void NewAttackStillOverlappingWhenInvulnerabilityEnds_CanDamageAgain()
        {
            var model = new ContactCombatModel(invulnerabilitySeconds: 1f);
            var player = At(0f);
            Assert.IsTrue(model.Step(player, player, player, player, 0.25f, Active()));

            Assert.IsTrue(model.Step(player, player, player, player, 1f, Active(2)));
            Assert.AreEqual(3, model.Status.PlayerHealth);
            Assert.AreEqual(0.75f, model.Status.InvulnerabilityRemaining, 1e-5f);
        }

        [Test]
        public void Death_ClampsHealthAndFreezesFurtherDamageAndBattleTime()
        {
            var model = new ContactCombatModel(maxHealth: 1, collisionDamage: 3);
            var state = At(0f);
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active()));
            var deathStatus = model.Status;
            Assert.AreEqual(0, deathStatus.PlayerHealth);
            Assert.IsFalse(deathStatus.IsPlayerAlive);

            Assert.IsFalse(model.Step(state, state, state, state, 5f, Active()));
            Assert.AreEqual(deathStatus.HitSequence, model.Status.HitSequence);
            Assert.AreEqual(deathStatus.ElapsedTime, model.Status.ElapsedTime);
        }

        [Test]
        public void Difficulty_RisesWithLiveBattleTimeAndCapsAfterOneMinute()
        {
            var model = new ContactCombatModel();
            var player = At(0f);
            var enemy = At(20f);
            Assert.AreEqual(0f, model.Status.Difficulty01);
            model.Step(player, player, enemy, enemy, 30f, Active());
            Assert.AreEqual(0.5f, model.Status.Difficulty01, 1e-5f);
            model.Step(player, player, enemy, enemy, 60f, Active());
            Assert.AreEqual(1f, model.Status.Difficulty01);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDeltaTime_DoesNotDamageOrAdvanceTime(float dt)
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            Assert.IsFalse(model.Step(state, state, state, state, dt, Active()));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.AreEqual(0f, model.Status.ElapsedTime);
        }

        [Test]
        public void NonFiniteFlightState_DoesNotDamageOrAdvanceTime()
        {
            var model = new ContactCombatModel();
            var player = At(0f);
            var enemy = At(0f);
            enemy.Velocity = new Vec2(float.NaN, 0f);
            Assert.IsFalse(model.Step(player, player, enemy, enemy, 0.02f, Active()));
            enemy = At(0f);
            enemy.Heading = float.PositiveInfinity;
            Assert.IsFalse(model.Step(player, player, enemy, enemy, 0.02f, Active()));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.AreEqual(0f, model.Status.ElapsedTime);
        }

        [Test]
        public void FiniteExtremeCoordinates_DoNotOverflowIntoFalseContact()
        {
            var model = new ContactCombatModel();
            var player = At(float.MaxValue);
            var enemy = At(-float.MaxValue);
            Assert.IsFalse(model.Step(player, player, enemy, enemy, 0.02f, Active()));
            Assert.AreEqual(5, model.Status.PlayerHealth);
        }

        [Test]
        public void MissingOrInactiveAttackWindow_AdvancesTimeWithoutContactDamage()
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            Assert.IsFalse(model.Step(state, state, state, state, 0.25f));
            Assert.IsFalse(model.Step(state, state, state, state, 0.25f, AttackWindow.Inactive));
            Assert.IsFalse(model.Step(state, state, state, state, 0.25f, new AttackWindow(50, false)));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.AreEqual(0, model.Status.HitSequence);
            Assert.AreEqual(0.75f, model.Status.ElapsedTime, 1e-5f);

            Assert.IsTrue(model.Step(state, state, state, state, 0.25f, Active()));
            Assert.IsFalse(model.Step(state, state, state, state, 0.25f));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(1.5f, model.Status.InvulnerabilityRemaining, 1e-5f);
        }

        [Test]
        public void SameAttack_CannotDamageAgainEvenAfterInvulnerabilityEnds()
        {
            var model = new ContactCombatModel(invulnerabilitySeconds: 0.1f);
            var state = At(0f);
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active()));
            for (int tick = 0; tick < 20; tick++)
                Assert.IsFalse(model.Step(state, state, state, state, 0.02f, Active()));

            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(0f, model.Status.InvulnerabilityRemaining);
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active(2)));
            Assert.AreEqual(3, model.Status.PlayerHealth);
        }

        [Test]
        public void MissedAttack_CanTryAgainOnLaterTickWithSameId()
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            var far = At(10f);
            Assert.IsFalse(model.Step(state, state, far, far, 0.02f, Active(4)));
            Assert.IsTrue(model.Step(state, state, At(-2f), At(2f), 0.02f, Active(4)));
            Assert.AreEqual(4, model.Status.PlayerHealth);
        }

        [Test]
        public void AttackBlockedByInvulnerability_IsNotConsumedUntilItActuallyHits()
        {
            var model = new ContactCombatModel(invulnerabilitySeconds: 0.1f);
            var state = At(0f);
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active()));
            Assert.IsFalse(model.Step(state, state, state, state, 0.02f, Active(2)));
            Assert.IsFalse(model.Step(state, state, state, state, 0.2f));
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active(2)));
            Assert.AreEqual(3, model.Status.PlayerHealth);
        }

        [Test]
        public void OlderAttackId_CannotBeReplayedAfterNewerAttackWasObserved()
        {
            var model = new ContactCombatModel(invulnerabilitySeconds: 0f);
            var state = At(0f);
            var far = At(10f);
            Assert.IsFalse(model.Step(state, state, far, far, 0.02f, Active(5)));
            Assert.IsFalse(model.Step(state, state, far, far, 0.02f, Active(6)));
            Assert.IsFalse(model.Step(state, state, state, state, 0.02f, Active(5)));
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active(6)));
            Assert.IsFalse(model.Step(state, state, state, state, 0.02f, AttackWindow.Inactive));
            Assert.IsFalse(model.Step(state, state, state, state, 0.02f, Active(5)));
            Assert.IsFalse(model.Step(state, state, state, state, 0.02f, Active(6)));
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active(7)));
            Assert.AreEqual(3, model.Status.PlayerHealth);
        }

        [Test]
        public void WindowStart_RejectsContactThatEndedBeforeAttackBegan()
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            Assert.IsFalse(model.Step(state, state, At(0f), At(10f), 1f, Active(start: 0.5f)));
            Assert.AreEqual(5, model.Status.PlayerHealth);
        }

        [Test]
        public void WindowEnd_RejectsContactThatBeganAfterAttackEnded()
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            Assert.IsFalse(model.Step(state, state, At(10f), At(0f), 1f, Active(end: 0.5f)));
            Assert.AreEqual(5, model.Status.PlayerHealth);
        }

        [Test]
        public void ContactInsidePartialWindow_UsesActualContactTimeForInvulnerability()
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            Assert.IsTrue(model.Step(state, state, At(-4f), At(4f), 1f,
                Active(start: 0.4f, end: 0.6f)));
            Assert.AreEqual(4, model.Status.PlayerHealth);
            Assert.AreEqual(1.4f, model.Status.InvulnerabilityRemaining, 1e-5f);
        }

        [Test]
        public void InvulnerabilityAndAttackWindow_MustOverlapAtContactTime()
        {
            var model = new ContactCombatModel(invulnerabilitySeconds: 1f);
            var state = At(0f);
            Assert.IsTrue(model.Step(state, state, state, state, 0.1f, Active()));
            // Immunity ends at fraction .9, after the active window has already closed.
            Assert.IsFalse(model.Step(state, state, state, state, 1f, Active(2, end: 0.8f)));
            Assert.AreEqual(4, model.Status.PlayerHealth);

            model = new ContactCombatModel(invulnerabilitySeconds: 1f);
            Assert.IsTrue(model.Step(state, state, state, state, 0.1f, Active()));
            Assert.IsTrue(model.Step(state, state, At(10f), At(0f), 1f,
                Active(2, start: 0.95f)));
            Assert.AreEqual(3, model.Status.PlayerHealth);
            Assert.AreEqual(0.95f, model.Status.InvulnerabilityRemaining, 1e-5f);
        }

        [TestCase(0, 0f, 1f)]
        [TestCase(-1, 0f, 1f)]
        [TestCase(1, -0.1f, 1f)]
        [TestCase(1, 0f, 1.1f)]
        [TestCase(1, 0.7f, 0.3f)]
        [TestCase(1, 0.5f, 0.5f)]
        [TestCase(1, float.NaN, 1f)]
        [TestCase(1, 0f, float.PositiveInfinity)]
        [TestCase(100, 0.8f, 0.2f)]
        public void InvalidWindow_CannotDamageAndDoesNotPoisonAttackIds(int id, float start, float end)
        {
            var model = new ContactCombatModel();
            var state = At(0f);
            Assert.IsFalse(model.Step(state, state, state, state, 0.25f, Active(id, start, end)));
            Assert.AreEqual(5, model.Status.PlayerHealth);
            Assert.AreEqual(0.25f, model.Status.ElapsedTime, 1e-5f);
            Assert.IsTrue(model.Step(state, state, state, state, 0.02f, Active()));
        }

        [Test]
        public void InvalidConfiguration_IsRejectedAtConstruction()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContactCombatModel(maxHealth: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContactCombatModel(collisionDamage: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContactCombatModel(invulnerabilitySeconds: -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContactCombatModel(invulnerabilitySeconds: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContactCombatModel(contactRadius: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContactCombatModel(contactRadius: float.PositiveInfinity));
        }
    }
}
