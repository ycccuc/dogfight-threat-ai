using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>One flight contract for actual windup, its warning path and the player-protecting veto.</summary>
    public sealed class AttackTrajectory
    {
        public const float StepSeconds = .02f;
        readonly AircraftSpec _spec;
        readonly FlightModel _model;
        public AttackTrajectory(in AircraftSpec spec, Bounds2D? arena = null)
        {
            _spec = spec;
            _model = FlightModel.From(spec, arena);
        }
        public FlightInput WindupInput(in FlightState state, float lockedHeading, float speed, float dt)
        {
            FlightInput orientation = AttackFlightPilot.BrakeToward(state, lockedHeading, _spec.TurnRateDegPerSec, dt);
            return new FlightInput(speed > 0f ? MathF.Min(1f, speed / _spec.MaxSpeed) : -1f, orientation.Steer);
        }
        public int Build(in FlightState state, float heading, float warningRemaining, float chargeRemaining,
            float warningSpeed, Vec2[] points)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("Preview needs at least two points.", nameof(points));
            FlightState predicted = state;
            points[0] = state.Position;
            int count = 1;
            for (float time = 0f; time < warningRemaining + chargeRemaining - .0001f && count < points.Length; time += StepSeconds)
            {
                var input = time < warningRemaining - .0001f
                    ? WindupInput(predicted, heading, warningSpeed, StepSeconds) : new FlightInput(1f, 0f);
                _model.Step(ref predicted, input, StepSeconds);
                points[count++] = predicted.Position;
            }
            return count;
        }
        public bool CanReachAim(in FlightState state, float heading, float warningSpeed, Vec2 aim)
        {
            FlightState predicted = state;
            for (int i = 0; i < 85; i++)
            {
                FlightState previous = predicted;
                var input = i < 40 ? WindupInput(predicted, heading, warningSpeed, StepSeconds) : new FlightInput(1f, 0f);
                _model.Step(ref predicted, input, StepSeconds);
                if (i >= 40 && SegmentDistance(previous.Position, predicted.Position, aim) <= 1.15d) return true;
            }
            return false;
        }
        public float EstimatePassTime(in FlightState state, float heading, float warningSpeed, Vec2 aim)
        {
            FlightState predicted = state;
            double nearest = double.PositiveInfinity;
            float time = 1.1f;
            for (int i = 0; i < 85; i++)
            {
                FlightState previous = predicted;
                var input = i < 40 ? WindupInput(predicted, heading, warningSpeed, StepSeconds) : new FlightInput(1f, 0f);
                _model.Step(ref predicted, input, StepSeconds);
                if (i < 40) continue;
                double dx = (double)predicted.Position.X - previous.Position.X;
                double dy = (double)predicted.Position.Y - previous.Position.Y;
                double px = (double)aim.X - previous.Position.X, py = (double)aim.Y - previous.Position.Y;
                double length = dx * dx + dy * dy;
                double t = length > 1e-12d ? System.Math.Max(0d, System.Math.Min(1d, (px * dx + py * dy) / length)) : 0d;
                double distance = (px - t * dx) * (px - t * dx) + (py - t * dy) * (py - t * dy);
                if (distance < nearest) { nearest = distance; time = (i + (float)t) * StepSeconds; }
            }
            return time;
        }
        static double SegmentDistance(Vec2 a, Vec2 b, Vec2 point)
        {
            double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y;
            double px = (double)point.X - a.X, py = (double)point.Y - a.Y;
            double length = dx * dx + dy * dy;
            double t = length > 1e-12d ? System.Math.Max(0d, System.Math.Min(1d, (px * dx + py * dy) / length)) : 0d;
            return System.Math.Sqrt((px - t * dx) * (px - t * dx) + (py - t * dy) * (py - t * dy));
        }
    }
}
