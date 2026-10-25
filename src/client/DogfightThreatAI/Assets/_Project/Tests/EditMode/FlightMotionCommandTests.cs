using System;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class FlightMotionCommandTests
    {
        static readonly FlightInput Forward = new FlightInput(1f, 0f);

        static FlightModel NewModel(Bounds2D? arena = null)
            => new FlightModel(2f, 9f, 18f, 240f, .1f, 6f, .35f, arena);

        static FlightState AtSpeed(float speed)
            => new FlightState { Velocity = new Vec2(speed, 0f) };

        static FlightMotionCommand Hold()
            => FlightMotionCommand.FiniteAcceleration(18f, 7.5f, 30f, 180f);

        [Test]
        public void HoldAddsPointOneFiveActualForwardSpeedEachStepUntilEighteen()
        {
            var model = NewModel();
            var state = AtSpeed(9f);
            for (int step = 1; step <= 65; step++)
            {
                model.Step(ref state, Forward, .02f, Hold());
                Assert.That(state.Velocity.X, Is.EqualTo(MathF.Min(18f, 9f + step * .15f)).Within(.0001f));
            }
        }

        [Test]
        public void HoldFromRestUsesNormalAccelerationWithoutAnInitialDash()
        {
            var state = AtSpeed(0f);
            NewModel().Step(ref state, Forward, .02f, Hold());
            Assert.That(state.Velocity.X, Is.EqualTo(.36f).Within(.00001f));
        }

        [Test]
        public void AccelerationThresholdUsesOnlyTheRemainingTimeAboveCruise()
        {
            var state = AtSpeed(8.9f);
            NewModel().Step(ref state, Forward, .02f, Hold());
            float expected = 9f + (.02f - .1f / 18f) * 7.5f;
            Assert.That(state.Velocity.X, Is.EqualTo(expected).Within(.00001f));
        }

        [TestCase(.01f)]
        [TestCase(.02f)]
        [TestCase(.0333f)]
        public void HoldReachesEighteenAfterOnePointTwoSecondsWithinOneStep(float dt)
        {
            var state = AtSpeed(9f);
            var model = NewModel();
            int steps = 0;
            while (state.Velocity.X < 17.9999f && steps < 200)
            {
                model.Step(ref state, Forward, dt, Hold());
                steps++;
            }
            Assert.That(steps * dt, Is.InRange(1.2f - .0001f, 1.2f + dt));
            Assert.That(state.Velocity.X, Is.EqualTo(18f).Within(.0001f));
        }

        [TestCase(9f)]
        [TestCase(18f)]
        [TestCase(7.2f)]
        public void OverspeedReturnsAtThirtyPerSecondWithoutClamping(float limit)
        {
            var state = AtSpeed(21.6f);
            var exit = FlightMotionCommand.FiniteAcceleration(limit, 18f, 30f, 180f);
            NewModel().Step(ref state, Forward, .02f, exit);
            Assert.That(state.Velocity.X, Is.EqualTo(21f).Within(.0001f));
        }

        [Test]
        public void FiniteAccelerationKeepsSidewaysVelocityAndItsNormalDamping()
        {
            var state = AtSpeed(9f);
            state.Velocity.Y = 3f;
            NewModel().Step(ref state, Forward, .02f, Hold());
            Assert.That(state.Velocity.X, Is.EqualTo(9.15f).Within(.0001f));
            Assert.That(state.Velocity.Y, Is.EqualTo(3f * MathF.Exp(-6f * .02f) / 1.002f).Within(.0001f));
        }

        [Test]
        public void FiniteAccelerationUsesItsActualTurnLimit()
        {
            var state = AtSpeed(9f);
            NewModel().Step(ref state, new FlightInput(1f, 1f), .02f, Hold());
            Assert.That(state.Heading, Is.EqualTo(180f * .01745329252f * .02f).Within(.00001f));
            Assert.That(MathF.Abs(Vec2.Dot(state.Velocity, new Vec2(state.Forward.Y, -state.Forward.X))), Is.GreaterThan(.01f));
        }

        [TestCase(.5f)]
        [TestCase(14f)]
        [TestCase(21.6f)]
        [TestCase(45f)]
        public void ExplicitSpeedOverrideIsTheActualPostDragForwardSpeed(float speed)
        {
            var state = AtSpeed(9f);
            state.Velocity.Y = 4f;
            NewModel().Step(ref state, Forward, .02f, FlightMotionCommand.SpeedOverride(speed));
            Assert.That(state.Velocity.X, Is.EqualTo(speed).Within(.0001f));
            Assert.That(state.Velocity.Y, Is.EqualTo(0f).Within(.0001f));
        }

        [Test]
        public void OrdinaryCommandRetainsTheLegacyInertiaAndDrag()
        {
            var ordinary = AtSpeed(4f);
            ordinary.Velocity.Y = 2f;
            var legacy = ordinary;
            var model = NewModel();
            var input = new FlightInput(1f, .25f);
            model.Step(ref legacy, input, .02f);
            model.Step(ref ordinary, input, .02f, FlightMotionCommand.Ordinary());
            Assert.That(ordinary.Velocity.X, Is.EqualTo(legacy.Velocity.X).Within(.00001f));
            Assert.That(ordinary.Velocity.Y, Is.EqualTo(legacy.Velocity.Y).Within(.00001f));
            Assert.That(ordinary.Position.X, Is.EqualTo(legacy.Position.X).Within(.00001f));
        }

        [TestCase(-1f, 0f, 9f, 0f)]
        [TestCase(0f, 0f, 21.6f, 0f)]
        [TestCase(1f, 1f, 9f, 3f)]
        [TestCase(0f, 1f, 12f, 3f)]
        [TestCase(1f, 0f, 21.6f, 0f)]
        public void OrdinaryBrakingNeutralOverspeedAndTurningMatchLegacyMotion(
            float throttle, float steer, float forwardSpeed, float sidewaysSpeed)
        {
            var ordinary = AtSpeed(forwardSpeed);
            ordinary.Velocity.Y = sidewaysSpeed;
            var legacy = ordinary;
            var model = NewModel();
            var input = new FlightInput(throttle, steer);
            for (int step = 0; step < 10; step++)
            {
                model.Step(ref legacy, input, .02f);
                model.Step(ref ordinary, input, .02f, FlightMotionCommand.Ordinary());
                Assert.That(ordinary.Heading, Is.EqualTo(legacy.Heading).Within(.00001f));
                Assert.That(ordinary.Velocity.X, Is.EqualTo(legacy.Velocity.X).Within(.00001f));
                Assert.That(ordinary.Velocity.Y, Is.EqualTo(legacy.Velocity.Y).Within(.00001f));
                Assert.That(ordinary.Position.X, Is.EqualTo(legacy.Position.X).Within(.00001f));
                Assert.That(ordinary.Position.Y, Is.EqualTo(legacy.Position.Y).Within(.00001f));
            }
        }

        [Test]
        public void BrakeTakesPriorityOverAnExplicitSpeedOverride()
        {
            var state = AtSpeed(9f);
            NewModel().Step(ref state, new FlightInput(-1f, 0f), .02f, FlightMotionCommand.SpeedOverride(21.6f));
            Assert.That(state.Velocity.X, Is.EqualTo(8.4f).Within(.0001f));
        }

        [Test]
        public void NeutralFiniteExitStillRemovesOverspeedAtAFiniteRate()
        {
            var state = AtSpeed(21.6f);
            NewModel().Step(ref state, FlightInput.Neutral, .02f,
                FlightMotionCommand.FiniteAcceleration(9f, 18f, 30f, 240f));
            Assert.That(state.Velocity.X, Is.EqualTo(21f).Within(.0001f));
        }

        [Test]
        public void HalfStepOfHoldThenOverheatPaysOnlyForTheAuthorizedMotion()
        {
            var state = AtSpeed(9f);
            NewModel().Step(ref state, Forward, .02f, Hold().WithActiveSeconds(.01f, 7.2f));
            Assert.That(state.Velocity.X, Is.EqualTo(8.775f).Within(.0001f));
            Assert.That(state.Position.X, Is.EqualTo(9.075f * .01f + 8.775f * .01f).Within(.00001f));
        }

        [Test]
        public void HalfStepOfPaidDashCompletesBeforeFiniteExit()
        {
            var state = AtSpeed(9f);
            var command = FlightMotionCommand.SpeedOverride(21.6f).WithActiveSeconds(.01f);
            NewModel().Step(ref state, Forward, .02f, command);
            Assert.That(state.Velocity.X, Is.EqualTo(21.3f).Within(.0001f));
            Assert.That(state.Position.X, Is.EqualTo(.429f).Within(.00001f));
        }

        [Test]
        public void ExitSegmentUsesItsOwnTurnRateAndAnAlreadyExpiredActionAddsNoSpeed()
        {
            var state = AtSpeed(9f);
            var command = Hold().WithActiveSeconds(0f, 7.2f, 240f);
            NewModel().Step(ref state, new FlightInput(1f, 1f), .02f, command);
            Assert.That(state.Heading, Is.EqualTo(240f * .01745329252f * .02f).Within(.00001f));
            Assert.That(state.Speed, Is.LessThan(9f));
        }

        [Test]
        public void ASubCruiseSpeedLimitDoesNotUseLegacyMultiplierValidation()
        {
            var state = AtSpeed(.8f);
            Assert.DoesNotThrow(() => NewModel().Step(ref state, Forward, .02f,
                FlightMotionCommand.FiniteAcceleration(.5f, 18f, 30f, 240f)));
            Assert.That(state.Velocity.X, Is.EqualTo(.5f).Within(.0001f));
        }

        [Test]
        public void MotionStillClampsAtTheArenaAndRemovesOnlyOutwardVelocity()
        {
            var model = NewModel(Bounds2D.Centered(new Vec2(5f, 5f)));
            var state = AtSpeed(9f);
            state.Position = new Vec2(4.9f, 1f);
            state.Velocity.Y = 2f;
            model.Step(ref state, Forward, .02f, Hold());
            Assert.That(state.Position.X, Is.EqualTo(5f).Within(.00001f));
            Assert.That(state.Position.Y, Is.GreaterThan(1f));
            Assert.That(state.Velocity.X, Is.EqualTo(0f).Within(.00001f));
            Assert.That(state.Velocity.Y, Is.GreaterThan(0f));
        }

        [Test]
        public void InvalidMotionParametersAreRejectedWithoutRejectingLowLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FlightMotionCommand.SpeedOverride(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => FlightMotionCommand.FiniteAcceleration(18f, -1f, 30f, 180f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Hold().WithActiveSeconds(-.01f));
            Assert.DoesNotThrow(() => FlightMotionCommand.Ordinary(.5f));
        }
    }
}
