using System.Reflection;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Gameplay.InputSource;
using NUnit.Framework;
using UnityEngine;

namespace DogfightThreatAI.Tests.PlayMode
{
    public sealed class PlayerBoostInputTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _object;
        KeyboardInputSource _input;
        PlayerBoostModel Boost => (PlayerBoostModel)typeof(KeyboardInputSource).GetField("_boost", Private).GetValue(_input);

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("BufferedBoostInputFixture");
            _object.SetActive(false);
            _input = _object.AddComponent<KeyboardInputSource>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_object);

        [Test]
        public void SpaceTapBetweenFixedStepsIsConsumedExactlyOnce()
        {
            Capture(true, false);
            Capture(false, false);
            Consume(.02f);
            Assert.IsTrue(Boost.IsDashing);
            Assert.AreEqual(35f, Boost.Energy);
            Consume(.2f);
            Assert.IsFalse(Boost.IsDashing);
            Assert.AreEqual(35f, Boost.Energy, .0001f);
        }

        [Test]
        public void HeldSpaceNeverRepeatsAfterCooldown()
        {
            Capture(true, false);
            Consume(.02f);
            Capture(true, false);
            Consume(2f);
            Assert.IsFalse(Boost.IsDashing);
            Assert.GreaterOrEqual(Boost.Energy, 35f);
        }

        [Test]
        public void AShiftTapBetweenFixedStepsStillReceivesOnePaidThrustStep()
        {
            Capture(false, true);
            Capture(false, false);
            Consume(.02f);
            Assert.IsTrue(Boost.IsSustaining);
            Assert.IsFalse(Boost.IsDashing);
            Assert.AreEqual(59.2f, Boost.Energy, .0001f);
            Consume(.02f);
            Assert.IsFalse(Boost.IsBoosting);
        }

        [Test]
        public void FocusLossDropsQueuedEdgesAndRequiresRelease()
        {
            Capture(true, true);
            Invoke("OnApplicationFocus", false);
            Invoke("OnApplicationFocus", true);
            Capture(true, true);
            Consume(.02f);
            Assert.AreEqual(60f, Boost.Energy);
            Assert.IsFalse(Boost.IsBoosting);
            Capture(false, false);
            Capture(true, false);
            Consume(.02f);
            Assert.IsTrue(Boost.IsDashing);
        }

        [Test]
        public void FocusLossCancelsPaidDashWithoutRefund()
        {
            Capture(true, false);
            Consume(.02f);
            Invoke("OnApplicationFocus", false);
            Assert.IsFalse(Boost.IsBoosting);
            Assert.AreEqual(35f, Boost.Energy);
            Assert.AreEqual(1.18f, Boost.DashCooldownRemaining, .0001f);
        }

        [Test]
        public void BrakeDropsPendingSpaceAndLocksHeldAbilityKeys()
        {
            Capture(true, true);
            Consume(.02f, true, true);
            Assert.AreEqual(60f, Boost.Energy);
            Assert.IsFalse(Boost.IsBoosting);
            Consume(.02f);
            Assert.IsFalse(Boost.IsBoosting);
            Capture(false, false);
            Capture(false, true);
            Consume(.02f);
            Assert.IsTrue(Boost.IsSustaining);
        }

        [Test]
        public void DeathDropsUnconsumedEdgesAndLocksUntilRelease()
        {
            Capture(true, true);
            Consume(.02f, false);
            Consume(.02f);
            Assert.AreEqual(60f, Boost.Energy);
            Assert.IsFalse(Boost.IsBoosting);
            Capture(false, false);
            Capture(true, false);
            Consume(.02f);
            Assert.IsTrue(Boost.IsDashing);
        }

        [Test]
        public void NewBattleRestoresTankButHeldKeysNeedRelease()
        {
            Capture(true, false);
            Consume(.02f);
            Invoke("ResetAbilityState", true, true);
            Consume(.02f);
            Assert.AreEqual(60f, Boost.Energy);
            Assert.AreEqual(0f, Boost.DashCooldownRemaining);
            Assert.IsFalse(Boost.IsBoosting);
            Capture(false, false);
            Capture(true, false);
            Consume(.02f);
            Assert.IsTrue(Boost.IsDashing);
        }

        [Test]
        public void SpacePressedDuringCooldownCannotBecomeADeferredDash()
        {
            Capture(true, false);
            Capture(false, false);
            Consume(.2f);
            Capture(true, false);
            Consume(.02f);
            Consume(2f);
            Assert.IsFalse(Boost.IsDashing);
            Assert.GreaterOrEqual(Boost.Energy, 35f);
        }

        [Test]
        public void BufferedReleaseRepressCannotKeepALowEnergyHoldAuthorized()
        {
            Capture(false, true);
            Consume(1f);
            Capture(false, false);
            Capture(false, true);
            Consume(.02f);
            Assert.IsFalse(Boost.IsSustaining);
            Assert.AreEqual(20f, Boost.Energy, .0001f);
        }

        [Test]
        public void BrakeTapBetweenFixedStepsDropsAnOlderPendingSpace()
        {
            Capture(true, true);
            Invoke("CaptureBrakeFrame", true);
            Invoke("CaptureBrakeFrame", false);
            Consume(.02f);
            Assert.AreEqual(60f, Boost.Energy);
            Assert.IsFalse(Boost.IsBoosting);
        }

        [Test]
        public void UnityDownEdgeSurvivesAnAlreadyReleasedHeldSample()
        {
            Capture(false, false);
            Invoke("CaptureAbilityEdges", true, false);
            Consume(.02f);
            Assert.IsTrue(Boost.IsDashing);
            Assert.AreEqual(35f, Boost.Energy);
        }
        void Capture(bool dashHeld, bool holdHeld) => Invoke("CaptureAbilityFrame", dashHeld, holdHeld);
        void Consume(float dt, bool canBoost = true, bool braking = false)
            => Invoke("ConsumeAbilityFrame", dt, canBoost, braking, 9f);
        void Invoke(string method, params object[] arguments)
            => typeof(KeyboardInputSource).GetMethod(method, Private).Invoke(_input, arguments);
    }
}
