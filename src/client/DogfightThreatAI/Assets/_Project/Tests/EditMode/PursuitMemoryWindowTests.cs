using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class PursuitMemoryWindowTests
    {
        [TestCase(.005f)]
        [TestCase(.01f)]
        [TestCase(.02f)]
        [TestCase(.0333f)]
        public void StableVisibleCurvatureCanActivateRecoveryAtEverySupportedFixedStep(float dt)
        {
            Observation observed = ObserveHistory(dt, false);
            var path = new PursuitReentryPath();
            path.Begin(observed.State, observed.TurnRate, observed.Confidence, true);

            Assert.IsTrue(path.Active,
                $"A full visible turn history must enable the fourth-level recovery at dt={dt}; confidence was {observed.Confidence}.");
            Assert.AreEqual(4.2f, observed.TurnRate, .0002f);
            Assert.GreaterOrEqual(observed.Confidence, .99f,
                "Increasing sample frequency must not shorten the elapsed history used to judge a stable turn.");
        }

        [Test]
        public void AVisibleReversalUsesTheSameElapsedHistoryAtFiftyAndOneHundredHertz()
        {
            Observation fifty = ObserveHistory(.02f, true);
            Observation hundred = ObserveHistory(.01f, true);

            // At t=1, the last .48 seconds contain .08 seconds of the old turn and
            // .40 seconds of the reverse turn. Preserve the original 50 Hz behavior.
            Assert.AreEqual(-4f / 3f, fifty.TurnRate, .0002f);
            Assert.AreEqual(2f / 3f, fifty.Confidence, .0002f);
            Assert.AreEqual(fifty.TurnRate, hundred.TurnRate, .0002f,
                "The same visible reversal must have the same curvature estimate at the same game time.");
            Assert.AreEqual(fifty.Confidence, hundred.Confidence, .0002f,
                "A higher fixed frequency must not forget the pre-reversal part of the same time window.");
        }

        static Observation ObserveHistory(float dt, bool reverses)
        {
            var memory = new PursuitObservationMemory();
            const float speed = 9f, reversalAt = .6f;
            float rate = reverses ? 2f : 4.2f;
            int ticks = (int)MathF.Floor(1f / dt + .0001f);
            for (int tick = 0; tick <= ticks; tick++)
            {
                float time = tick * dt;
                float firstPart = reverses ? MathF.Min(time, reversalAt) : time;
                float firstAngle = rate * firstPart;
                float angle = firstAngle;
                Vec2 position = new Vec2(MathF.Sin(firstAngle), 1f - MathF.Cos(firstAngle)) * (speed / rate);
                if (reverses && time > reversalAt)
                {
                    angle -= rate * (time - reversalAt);
                    position += new Vec2(MathF.Sin(firstAngle) - MathF.Sin(angle),
                        MathF.Cos(angle) - MathF.Cos(firstAngle)) * (speed / rate);
                }
                var state = new FlightState { Position = position, Heading = angle, Velocity = Vec2.FromAngle(angle) * speed };
                memory.Sample(new AircraftSnapshot(1, 1, state, true, true,
                    VisiblePropulsionCue.None, true, time), time);
            }
            float lastSampleAt = ticks * dt;
            Assert.IsTrue(memory.TryObserve(lastSampleAt + .04f, .04f,
                out FlightState observed, out float observedAt, out float turnRate, out float confidence));
            Assert.AreEqual(lastSampleAt, observedAt, .00001f,
                "Both frequencies must compare history ending at the same delayed sample.");
            return new Observation(observed, turnRate, confidence);
        }

        readonly struct Observation
        {
            public readonly FlightState State;
            public readonly float TurnRate, Confidence;
            public Observation(in FlightState state, float turnRate, float confidence)
            { State = state; TurnRate = turnRate; Confidence = confidence; }
        }
    }
}
