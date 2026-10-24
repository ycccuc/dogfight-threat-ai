using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class PlayerBoostTests
    {
        const float Dt = .02f;

        [Test]
        public void StartsWithSixtyEnergyAndReadyDash()
        {
            var boost = new PlayerBoostModel();
            Assert.AreEqual(60f, boost.Energy);
            Assert.AreEqual(1f, boost.Energy01);
            Assert.IsTrue(boost.CanDash);
            Assert.IsFalse(boost.IsBoosting);
            Assert.IsFalse(boost.IsOverheated);
        }

        [Test]
        public void HoldingShiftNeverBuysAnInitialDash()
        {
            var boost = new PlayerBoostModel();
            boost.Step(true, Dt);
            Assert.IsFalse(boost.IsDashing, "Shift is continuous thrust, Space owns dash.");
            Assert.IsTrue(boost.IsSustaining);
            Assert.AreEqual(59.2f, boost.Energy, .0001f);
            Assert.AreEqual(FlightMotionMode.FiniteAcceleration, boost.MotionCommand.Mode);
            Assert.AreEqual(18f, boost.MotionCommand.MaxSpeed);
            Assert.AreEqual(7.5f, boost.MotionCommand.Acceleration);
            Assert.AreEqual(180f, boost.MotionCommand.TurnRateDegPerSec);
        }

        [Test]
        public void SpaceImmediatelyPaysTwentyFiveAndStartsItsOwnCooldown()
        {
            var boost = new PlayerBoostModel();
            boost.Step(true, false, Dt);
            Assert.AreEqual(35f, boost.Energy, .0001f);
            Assert.IsTrue(boost.IsDashing);
            Assert.AreEqual(1.18f, boost.DashCooldownRemaining, .0001f);
            Assert.IsFalse(boost.CanDash);
            Assert.AreEqual(FlightMotionMode.SpeedOverride, boost.MotionCommand.Mode);
            Assert.AreEqual(21.6f, boost.MotionCommand.MaxSpeed);
            var state = FlightState.At(Vec2.Zero);
            NewFlight().Step(ref state, new FlightInput(1f, 0f), Dt, boost.MotionCommand);
            Assert.AreEqual(21.6f, state.Speed, .0001f);
        }

        [Test]
        public void AReleasedSpaceTapCompletesItsWholePaidDash()
        {
            var boost = new PlayerBoostModel();
            boost.Step(true, false, .02f);
            boost.Step(false, false, .15f, 21.6f);
            Assert.IsTrue(boost.IsDashing);
            Assert.AreEqual(35f, boost.Energy);
            boost.Step(false, false, .01f, 21.6f);
            Assert.IsFalse(boost.IsDashing);
            Assert.IsTrue(boost.IsCoasting);
            Assert.AreEqual(240f, boost.MotionCommand.ExitTurnRateDegPerSec);
        }

        [Test]
        public void PressDuringDashCooldownIsDroppedWithoutQueuing()
        {
            var boost = new PlayerBoostModel();
            boost.Step(true, false, .2f);
            boost.Step(true, false, .1f, 20f);
            Assert.AreEqual(35f, boost.Energy, .0001f);
            Assert.IsFalse(boost.IsDashing);
            boost.Step(false, false, 1f);
            Assert.IsFalse(boost.IsDashing);
            boost.Step(true, false, Dt);
            Assert.IsTrue(boost.IsDashing);
        }

        [Test]
        public void AFullHoldPaysForExactlyOnePointFiveSeconds()
        {
            var boost = new PlayerBoostModel();
            boost.Step(true, 1.5f);
            Assert.AreEqual(0f, boost.Energy);
            Assert.IsTrue(boost.RequiresRelease);
            Assert.IsTrue(boost.IsOverheated);
            Assert.AreEqual(.9f, boost.OverheatRemaining, .0001f);
            Assert.AreEqual(1.5f, boost.PoweredSecondsThisStep, .0001f);
        }

        [Test]
        public void AnAuthorizedHoldContinuesBelowTwentyFive()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, 1f);
            Assert.AreEqual(20f, boost.Energy);
            Assert.IsTrue(boost.IsSustaining);
            boost.Step(false, true, .25f);
            Assert.AreEqual(10f, boost.Energy);
            Assert.IsTrue(boost.IsSustaining);
        }

        [Test]
        public void EarlyReleaseHasFiniteTailWithoutOverheat()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, .5f, 9f);
            boost.Step(false, false, Dt, 18f);
            Assert.IsFalse(boost.IsBoosting);
            Assert.IsFalse(boost.IsOverheated);
            Assert.IsTrue(boost.IsCoasting);
            Assert.AreEqual(9f, boost.MotionCommand.MaxSpeed);
            Assert.AreEqual(30f, boost.MotionCommand.Deceleration);
            Assert.AreEqual(180f, boost.MotionCommand.TurnRateDegPerSec);
            boost.Step(false, false, Dt, 9f);
            Assert.AreEqual(240f, boost.MotionCommand.TurnRateDegPerSec);
        }

        [Test]
        public void HoldFromRestKeepsNormalAccelerationBelowCruise()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, Dt, 0f);
            var state = FlightState.At(Vec2.Zero);
            NewFlight().Step(ref state, new FlightInput(1f, 0f), Dt, boost.MotionCommand);
            Assert.AreEqual(.36f, state.Speed, .0001f);
            Assert.AreEqual(59.2f, boost.Energy, .0001f);
        }

        [Test]
        public void DashPreemptsHoldAndRestoresItWhenEnoughEnergyRemains()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, .1f);
            boost.Step(true, true, .18f, 9.75f);
            Assert.AreEqual(31f, boost.Energy, .0001f, "Dash must not also charge Hold drain.");
            Assert.IsTrue(boost.IsSustaining);
            Assert.AreEqual(18f, boost.MotionCommand.ExitSpeedLimit);
            Assert.AreEqual(180f, boost.MotionCommand.ExitTurnRateDegPerSec);
            boost.Step(false, true, Dt, 21.6f);
            var state = new FlightState { Velocity = new Vec2(21.6f, 0f) };
            NewFlight().Step(ref state, new FlightInput(1f, 0f), Dt, boost.MotionCommand);
            Assert.AreEqual(21f, state.Speed, .0001f, "Resuming Hold decelerates the real dash tail instead of clamping it to 18.");
        }

        [Test]
        public void DashWithoutEnoughEnergyForHoldRequiresShiftRelease()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, .5f);
            boost.Step(true, true, .18f, 12.75f);
            Assert.AreEqual(15f, boost.Energy, .0001f);
            Assert.IsFalse(boost.IsSustaining);
            Assert.IsTrue(boost.RequiresRelease);
            boost.Step(false, true, 4f);
            Assert.AreEqual(60f, boost.Energy, .0001f);
            Assert.IsFalse(boost.IsBoosting);
            boost.Step(false, false, Dt);
            boost.Step(false, true, Dt);
            Assert.IsTrue(boost.IsSustaining);
        }

        [Test]
        public void RejectedSpaceDoesNotInterruptAnAuthorizedHold()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, 1f);
            boost.Step(true, true, Dt);
            Assert.IsTrue(boost.IsSustaining);
            Assert.IsFalse(boost.IsDashing);
            Assert.AreEqual(19.2f, boost.Energy, .0001f);
        }

        [Test]
        public void LowEnergyHoldCannotAutomaticallyStartAfterRecharge()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, 1f);
            boost.Step(false, false, .01f);
            boost.Step(false, true, .01f);
            Assert.IsTrue(boost.RequiresRelease);
            boost.Step(false, true, 4f);
            Assert.AreEqual(60f, boost.Energy);
            Assert.IsFalse(boost.IsBoosting);
        }

        [Test]
        public void ExactlyTwentyFiveCompletesDashBeforeItsOverheatDebt()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, .875f);
            boost.Step(true, false, .17f, 15.5625f);
            Assert.AreEqual(0f, boost.Energy, .0001f);
            Assert.IsTrue(boost.IsDashing);
            Assert.IsFalse(boost.IsOverheated);
            boost.Step(false, false, .01f, 21.6f);
            Assert.IsTrue(boost.IsOverheated);
            Assert.AreEqual(.9f, boost.OverheatRemaining, .0001f);
            boost.Step(false, false, .9f);
            Assert.AreEqual(0f, boost.Energy, .0001f);
            boost.Step(false, false, 1.25f);
            Assert.AreEqual(25f, boost.Energy, .0001f);
            Assert.IsTrue(boost.CanDash, "The earliest replacement dash is 2.33 seconds after its payment.");
        }

        [Test]
        public void OverheatBlocksForPointNineThenRefillsImmediatelyAtTwentyPerSecond()
        {
            var boost = Exhaust();
            boost.Step(false, false, .9f);
            Assert.IsFalse(boost.IsOverheated);
            Assert.AreEqual(0f, boost.Energy, .0001f);
            boost.Step(false, false, 1.24f);
            Assert.IsFalse(boost.CanDash);
            boost.Step(false, false, .01f);
            Assert.AreEqual(25f, boost.Energy, .0001f);
            Assert.IsTrue(boost.CanDash);
        }

        [Test]
        public void RepeatedEmptyRequestsAndBrakeCannotClearOrExtendOverheat()
        {
            var boost = Exhaust();
            for (int i = 0; i < 20; i++) boost.Step(true, true, .02f, 18f, true, true);
            Assert.AreEqual(.5f, boost.OverheatRemaining, .0001f);
            Assert.AreEqual(0f, boost.Energy);
            Assert.AreEqual(7.2f, boost.MotionCommand.MaxSpeed);
            Assert.AreEqual(240f, boost.MotionCommand.TurnRateDegPerSec);
            boost.Step(false, false, .5f);
            boost.Step(false, false, 1.25f);
            Assert.IsTrue(boost.CanDash);
        }

        [Test]
        public void CancellingAnExhaustingPaidDashKeepsItsOverheatDebt()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, .875f);
            boost.Step(true, false, .02f);
            boost.Step(false, false, .02f, 21.6f, true, true);
            Assert.IsFalse(boost.IsDashing);
            Assert.IsTrue(boost.IsOverheated);
            Assert.AreEqual(.88f, boost.OverheatRemaining, .0001f);
        }

        [Test]
        public void NormalRechargeWaitsPointSevenSeconds()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, .5f);
            boost.Step(false, false, .7f);
            Assert.AreEqual(40f, boost.Energy, .0001f);
            boost.Step(false, false, .5f);
            Assert.AreEqual(50f, boost.Energy, .0001f);
        }

        [TestCase(.01f)]
        [TestCase(.02f)]
        [TestCase(.0333f)]
        public void ExhaustionResourceTimingIsIndependentOfFixedStep(float dt)
        {
            var boost = new PlayerBoostModel();
            float elapsed = 0f, powered = 0f;
            while (elapsed < 2f - .00001f)
            {
                float slice = System.Math.Min(dt, 2f - elapsed);
                boost.Step(false, true, slice);
                powered += boost.PoweredSecondsThisStep;
                elapsed += slice;
            }
            Assert.AreEqual(1.5f, powered, .0001f);
            Assert.AreEqual(.4f, boost.OverheatRemaining, .0002f);
            Assert.AreEqual(0f, boost.Energy);
        }

        [Test]
        public void ExhaustionOnlyAuthorizesTheAffordablePartOfTheStep()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, 1.49f);
            boost.Step(false, true, .02f, 18f);
            Assert.AreEqual(.01f, boost.PoweredSecondsThisStep, .0001f);
            Assert.AreEqual(.01f, boost.MotionCommand.ActiveSeconds, .0001f);
            Assert.AreEqual(7.2f, boost.MotionCommand.ExitSpeedLimit);
            var state = new FlightState { Velocity = new Vec2(18f, 0f) };
            NewFlight().Step(ref state, new FlightInput(1f, 0f), .02f, boost.MotionCommand);
            Assert.AreEqual(17.7f, state.Speed, .0001f);
        }

        [Test]
        public void SafetyCancelLocksHeldShiftWithoutRefundingEnergy()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, .1f);
            boost.CancelAbilities(true);
            Assert.AreEqual(56f, boost.Energy);
            boost.Step(false, true, .1f);
            Assert.IsFalse(boost.IsBoosting);
            boost.Step(false, false, .1f);
            boost.Step(false, true, Dt);
            Assert.IsTrue(boost.IsSustaining);
        }

        [Test]
        public void ResetRestoresResourceButHeldShiftMustRelease()
        {
            var boost = Exhaust();
            boost.Reset(true);
            Assert.AreEqual(60f, boost.Energy);
            Assert.AreEqual(0f, boost.OverheatRemaining);
            Assert.AreEqual(0f, boost.DashCooldownRemaining);
            boost.Step(false, true, Dt);
            Assert.IsFalse(boost.IsBoosting);
            boost.Step(false, false, Dt);
            boost.Step(false, true, Dt);
            Assert.IsTrue(boost.IsSustaining);
        }

        [TestCase(0f)]
        [TestCase(-.02f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidTimeDoesNotConsumeInputOrChangeState(float dt)
        {
            var boost = new PlayerBoostModel();
            boost.Step(true, false, Dt);
            boost.Step(false, true, dt);
            Assert.AreEqual(35f, boost.Energy);
            Assert.IsTrue(boost.IsDashing);
            Assert.AreEqual(1.18f, boost.DashCooldownRemaining, .0001f);
        }

        [Test]
        public void ABufferedHoldRepressMustPayTheRestartThreshold()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, 1f);
            boost.Step(false, true, .02f, holdPressed: true);
            Assert.IsFalse(boost.IsSustaining, "A new captured press implies a real release even if both happened between fixed steps.");
            Assert.AreEqual(20f, boost.Energy, .0001f);
            Assert.IsTrue(boost.RequiresRelease);
        }
        static PlayerBoostModel Exhaust()
        {
            var boost = new PlayerBoostModel();
            boost.Step(false, true, 1.5f);
            Assert.IsTrue(boost.IsOverheated);
            return boost;
        }

        static FlightModel NewFlight() => new FlightModel(2f, 9f, 18f, 240f, .1f, 8f, .35f);
    }
}
