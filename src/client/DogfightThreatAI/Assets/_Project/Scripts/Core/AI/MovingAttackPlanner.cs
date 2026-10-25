using System;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>A world-space launch promise, entered with a finite, predetermined bank.</summary>
    public readonly struct AttackCommitment
    {
        public readonly float Heading, Bank;
        public readonly Vec2 LaunchPosition;
        public AttackCommitment(float heading, float bank, Vec2 launchPosition)
        { Heading = heading; Bank = bank; LaunchPosition = launchPosition; }
    }

    /// <summary>Plans moving preparation separately from the irrevocable final approach.</summary>
    public static class MovingAttackPlanner
    {
        const int Steps = 16;
        const float Radians = .0174532925f;
        const float BodyClearance = 2f;

        public static float TrackingSteer(in FlightState self, in FlightState observedTarget,
            float speed, float turnRate, float remaining, float desiredSteer, float preferredSide,
            float observedTurnRate, FlightModel flight, float actualDeltaTime)
        {
            float best = desiredSteer, bestScore = float.MaxValue;
            float trackingSeconds = PursuitChargeInputSource.AimSeconds - PursuitChargeInputSource.AimCommitSeconds;
            int firstSegment = System.Math.Min(1, (int)(MathF.Max(0f, .8f - remaining) / (trackingSeconds * .5f)));
            int trackingPolicies = firstSegment == 0 ? 25 : 5;
            var command = FlightMotionCommand.SpeedOverride(speed, turnRate);
            // Both tracking banks and the final constant bank are included in the same
            // forecast. Locking a direction does not silently remove turning distance.
            for (int policy = 0; policy < trackingPolicies; policy++)
            for (int finalBank = 0; finalBank < 9; finalBank++)
            {
                FlightState predicted = self, target = observedTarget;
                float closest = Vec2.Distance(predicted.Position, target.Position), firstSteer = 0f;
                float elapsed = 0f;
                float firstStep = MathF.Min(actualDeltaTime, remaining);
                int count = remaining <= actualDeltaTime + .000001f ? 1 : Steps;
                float laterStep = count == 1 ? 0f : (remaining - firstStep) / (count - 1);
                for (int i = 0; i < count; i++)
                {
                    float step = i == 0 ? firstStep : laterStep;
                    float steer;
                    if (remaining - elapsed <= PursuitChargeInputSource.AimCommitSeconds + .0001f)
                        steer = (finalBank - 4) * .25f;
                    else
                    {
                        int segment = System.Math.Min(1, (int)((.8f - remaining + elapsed) / (trackingSeconds * .5f)));
                        int control = segment == firstSegment ? policy % 5 : policy / 5 % 5;
                        float aim = Vec2.NormalizeAngle((target.Position - predicted.Position).AngleRad - predicted.Heading)
                            / (turnRate * Radians * step);
                        steer = control == 0 ? Clamp(aim, -1f, 1f) : control == 1 ? -1f
                            : control == 2 ? -.8f : control == 3 ? .8f : 1f;
                    }
                    if (i == 0) firstSteer = steer;
                    Vec2 before = predicted.Position - target.Position;
                    flight.Step(ref predicted, new FlightInput(1f, steer), step, command);
                    AdvanceTarget(ref target, observedTurnRate, step);
                    closest = MathF.Min(closest, SweepDistance(before, predicted.Position - target.Position));
                    elapsed += step;
                }
                float score = Score(predicted, target, observedTurnRate, closest)
                    + MathF.Abs(firstSteer - desiredSteer) * .02f;
                if (firstSteer * preferredSide < 0f) score += .015f;
                if (score < bestScore) { bestScore = score; best = firstSteer; }
            }
            return best;
        }

        public static AttackCommitment Commit(in FlightState self, in FlightState observedTarget,
            float speed, float turnRate, float remaining, float preferredSide, float observedTurnRate,
            FlightModel flight, float actualDeltaTime)
        {
            float bestBank = 0f, bestScore = float.MaxValue;
            for (int sample = 0; sample <= 40; sample++)
                Consider(-1f + sample * .05f, self, observedTarget, speed, turnRate, remaining,
                    preferredSide, observedTurnRate, flight, ref bestBank, ref bestScore);
            float coarse = bestBank;
            for (int sample = -5; sample <= 5; sample++)
                Consider(Clamp(coarse + sample * .01f, -1f, 1f), self, observedTarget, speed, turnRate,
                    remaining, preferredSide, observedTurnRate, flight, ref bestBank, ref bestScore);

            float heading = self.Heading + bestBank * turnRate * Radians * remaining;
            FlightState launch = self;
            var command = FlightMotionCommand.SpeedOverride(speed, turnRate);
            // Only the winning trajectory needs the exact fixed-step replay. This makes
            // the announced launch point match the actual one at both .01 and .02.
            for (float time = 0f; time < remaining - .000001f;)
            {
                float step = MathF.Min(actualDeltaTime, remaining - time);
                float steer = CommittedSteer(launch, heading, bestBank, turnRate, step);
                flight.Step(ref launch, new FlightInput(1f, steer), step, command);
                time += step;
            }
            return new AttackCommitment(heading, bestBank, launch.Position);
        }

        public static float CommittedSteer(in FlightState self, float heading, float bank, float turnRate, float dt)
            => Clamp(Vec2.NormalizeAngle(heading - self.Heading) / (turnRate * Radians * dt),
                -MathF.Abs(bank), MathF.Abs(bank));

        static void Consider(float bank, FlightState self, FlightState target, float speed, float turnRate,
            float remaining, float preferredSide, float observedTurnRate, FlightModel flight,
            ref float bestBank, ref float bestScore)
        {
            var command = FlightMotionCommand.SpeedOverride(speed, turnRate);
            float closest = Vec2.Distance(self.Position, target.Position);
            float step = remaining / Steps;
            for (int i = 0; i < Steps; i++)
            {
                Vec2 before = self.Position - target.Position;
                flight.Step(ref self, new FlightInput(1f, bank), step, command);
                AdvanceTarget(ref target, observedTurnRate, step);
                closest = MathF.Min(closest, SweepDistance(before, self.Position - target.Position));
            }
            float score = Score(self, target, observedTurnRate, closest) + MathF.Abs(bank) * .002f;
            if (bank * preferredSide < 0f) score += .005f;
            if (score < bestScore) { bestScore = score; bestBank = bank; }
        }

        static float Score(FlightState self, FlightState target, float turnRate, float closest)
        {
            float alignment = MathF.Abs(Vec2.NormalizeAngle((target.Position - self.Position).AngleRad - self.Heading));
            float miss = ChargeMissDistance(self, target, turnRate);
            float score = miss * 4f + alignment * .2f;
            if (closest < BodyClearance) score += (BodyClearance - closest) * 3000f;
            return score;
        }

        static float ChargeMissDistance(FlightState self, FlightState target, float turnRate)
        {
            float closest = Vec2.Distance(self.Position, target.Position);
            for (int i = 0; i < 12; i++)
            {
                Vec2 before = self.Position - target.Position;
                self.Position += self.Forward * (45f * .025f);
                AdvanceTarget(ref target, turnRate, .025f);
                closest = MathF.Min(closest, SweepDistance(before, self.Position - target.Position));
            }
            return closest;
        }

        static void AdvanceTarget(ref FlightState target, float turnRate, float dt)
        {
            target.Velocity = Vec2.FromAngle(target.Velocity.AngleRad + turnRate * dt) * target.Speed;
            target.Position += target.Velocity * dt;
        }

        static float SweepDistance(Vec2 before, Vec2 after)
        {
            Vec2 delta = after - before;
            float time = delta.SqrMagnitude < .000001f ? 0f : Clamp(-Vec2.Dot(before, delta) / delta.SqrMagnitude, 0f, 1f);
            return (before + delta * time).Magnitude;
        }

        static float Clamp(float value, float minimum, float maximum) => MathF.Max(minimum, MathF.Min(maximum, value));
    }
}
