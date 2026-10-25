using System;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>Evaluates a direct route and two bounded cuts using already observed motion.</summary>
    public sealed class PursuitSteeringPlanner
    {
        const int Steps = 16;
        const float Horizon = 1.2f;
        static readonly FlightModel DefaultPredictionFlight = new FlightModel(2f,9f,18f,210f,.1f,8f,.35f);
        float _nextPlan, _sideUntil, _lastEvasion, _lastTurn;
        Vec2 _point;
        public int CandidateCount { get; private set; }
        public int SimulationStepsPerCandidate => Steps;
        public int PlanVersion { get; private set; }
        public float SelectedSide { get; private set; }
        public void Reset() { _nextPlan = _sideUntil = _lastEvasion = _lastTurn = 0f; SelectedSide = 0f; PlanVersion = 0; }
        public void RecordVisibleEvasion(float side) { if (MathF.Abs(side) > .01f) _lastEvasion = side > 0f ? 1f : -1f; }

        public Vec2 Plan(in FlightState self, in FlightState target, Vec2 prediction,
            PursuitDifficultyProfile profile, float turnRate, float confidence, float now, bool canStartVisibleAttack = true,
            float visibleApproachHalfWidth = 7.8f, float visibleApproachHalfHeight = 4f)
        {
            if (PlanVersion > 0 && now < _nextPlan - .00001f) return _point;
            _nextPlan = now + .1f; PlanVersion++;
            if (profile.Level < 3) { CandidateCount = 1; return _point = prediction; }
            CandidateCount = 3;
            Vec2 forward = target.Speed > 1f ? target.Velocity.Normalized : target.Forward;
            Vec2 lateral = new Vec2(-forward.Y, forward.X);
            bool wideCut = target.Speed < 1f;
            if (wideCut)
            {
                Vec2 approach = (prediction - self.Position).Normalized;
                lateral = new Vec2(-approach.Y, approach.X);
            }
            bool reversed = confidence > .6f && _lastTurn * turnRate < -.3f;
            _lastTurn = turnRate;
            if (SelectedSide == 0f || now >= _sideUntil || reversed)
            {
                SelectedSide = Vec2.Dot(self.Position - target.Position, lateral) < -.01f ? -1f : 1f;
                if (profile.Level == 4 && _lastEvasion != 0f) SelectedSide = -_lastEvasion;
                if (profile.Level >= 3 && confidence > .6f && MathF.Abs(turnRate) > .2f) SelectedSide = turnRate > 0f ? 1f : -1f;
                _sideUntil = now + 1.2f;
            }
            Vec2 best = prediction; float bestScore = float.MaxValue;
            for (int candidate = 0; candidate < 3; candidate++)
            {
                float side = candidate == 0 ? 0f : candidate == 1 ? -1f : 1f;
                float offset = wideCut ? 6.5f : profile.SideOffset;
                Vec2 goal = prediction + lateral * (side * offset);
                float score = Score(self, target, goal, profile, turnRate, confidence);
                // Hold the chosen side long enough to be readable, while direct pursuit remains an option.
                score += side == SelectedSide ? (wideCut ? -6f : -1.2f) : side == 0f ? .8f : 2.5f;
                if (score < bestScore) { bestScore = score; best = goal; }
            }
            Vec2 relative = self.Position - target.Position;
            bool narrowView = visibleApproachHalfWidth < 7.799f;
            if (!canStartVisibleAttack && (narrowView || MathF.Abs(relative.Y) > 3.5f && MathF.Abs(relative.X) < 6f))
            {
                float entrySide = narrowView && MathF.Abs(relative.X) > .1f ? MathF.Sign(relative.X) : SelectedSide;
                float entryY = narrowView ? (MathF.Abs(relative.Y) > .1f ? MathF.Sign(relative.Y) : SelectedSide) * MathF.Max(0f, visibleApproachHalfHeight - .4f) : 0f;
                float entryWidth = MathF.Min(7.8f, MathF.Max(0f, visibleApproachHalfWidth));
                if (narrowView && target.Speed < 1f)
                {
                    // Camera hints already reserve one world unit inside the frustum.
                    // Use more of that corner for a stopped target's finite turning circle.
                    entryWidth += .3f;
                    entryY = (MathF.Abs(relative.Y) > .1f ? MathF.Sign(relative.Y) : SelectedSide) * (visibleApproachHalfHeight + .1f);
                }
                best = prediction + new Vec2(entrySide * entryWidth, entryY);
            }
            return _point = best;
        }

        static float Score(FlightState self, FlightState target, Vec2 goal,
            PursuitDifficultyProfile profile, float turnRate, float confidence)
        {
            float dt = Horizon / Steps, closest = float.MaxValue;
            for (int step = 0; step < Steps; step++)
            {
                float desired = (goal - self.Position).AngleRad;
                self.Heading += Clamp(Vec2.NormalizeAngle(desired - self.Heading), -profile.TurnRateDegPerSec * .0174532925f * dt, profile.TurnRateDegPerSec * .0174532925f * dt);
                self.Position += self.Forward * (profile.CruiseSpeed * dt);
                if (profile.Level == 4 && confidence > .35f)
                    target.Velocity = Vec2.FromAngle(target.Velocity.AngleRad + turnRate * confidence * dt) * target.Speed;
                target.Position += target.Velocity * dt;
                closest = MathF.Min(closest, Vec2.Distance(self.Position, target.Position));
            }
            return closest + Vec2.Distance(self.Position, target.Position) * .15f;
        }

        public static float SafeWindupSteer(in FlightState self, in FlightState observedTarget,
            float speed, float turnRate, float remaining, float desiredSteer, float preferredSide,
            float observedTurnRate = 0f, FlightModel flight = null, float actualDeltaTime = .02f)
            => MovingAttackPlanner.TrackingSteer(self, observedTarget, speed, turnRate, remaining,
                desiredSteer, preferredSide, observedTurnRate, flight ?? DefaultPredictionFlight, actualDeltaTime);
        /// <summary>Searches finite boost durations, including braking, before granting any propulsion.</summary>
        public static float SafeBoostDuration(in FlightState self, in FlightState target, Vec2 goal,
            PursuitDifficultyProfile profile, float triggerRange, FlightModel flight = null, bool visibleOverheat = false)
        {
            float error = MathF.Abs(Vec2.NormalizeAngle((goal - self.Position).AngleRad - self.Heading));
            if (error > 55f * .0174532925f) return 0f;
            if (self.Speed > profile.BoostSpeed + .1f) return 0f;
            float distance = Vec2.Distance(self.Position, target.Position);
            if (distance <= triggerRange + 2f) return 0f;
            Vec2 toward = (target.Position - self.Position).Normalized;
            float closing = Vec2.Dot(self.Forward * profile.CruiseSpeed - target.Velocity, toward);
            if (profile.Level == 1 && (distance < 14f || error > 25f * .0174532925f)) return 0f;
            if (closing > 5f && distance < 20f && !(profile.Level == 4 && visibleOverheat)) return 0f;
            for (float duration = profile.BoostDuration; duration >= .1999f; duration -= .025f)
                if (Safe(self, target, goal, profile, duration, triggerRange, flight ?? DefaultPredictionFlight)) return MathF.Max(.2f, duration);
            return 0f;
        }

        static bool Safe(FlightState self, FlightState target, Vec2 goal, PursuitDifficultyProfile profile, float duration, float range, FlightModel flight)
        {
            const float dt = .025f;
            float brakeSeconds = MathF.Max(0f, profile.BoostSpeed - profile.CruiseSpeed) / 30f;
            float end = duration + brakeSeconds + dt;
            for (float time = 0f; time < end; time += dt)
            {
                float h = MathF.Min(dt, end - time);
                float desired = (goal + target.Velocity * time - self.Position).AngleRad;
                float steer = Vec2.NormalizeAngle(desired - self.Heading) / (MathF.PI * .5f * h);
                Vec2 previousRelative = self.Position - target.Position;
                var command = FlightMotionCommand.FiniteAcceleration(time < duration ? profile.BoostSpeed : profile.CruiseSpeed, 40f, 30f, 90f, 40f, 0f);
                if (time < duration) command = command.WithActiveSeconds(duration - time, profile.CruiseSpeed, 90f, 30f);
                flight.Step(ref self, new FlightInput(1f, steer), h, command);
                target.Position += target.Velocity * h;
                Vec2 relative = self.Position - target.Position, segment = relative - previousRelative;
                float fraction = segment.SqrMagnitude < .000001f ? 0f : Clamp(-Vec2.Dot(previousRelative, segment) / segment.SqrMagnitude, 0f, 1f);
                if ((previousRelative + segment * fraction).Magnitude <= range + .25f) return false;
            }
            return true;
        }
        static float Clamp(float v, float min, float max) => MathF.Max(min, MathF.Min(max, v));
    }
}
