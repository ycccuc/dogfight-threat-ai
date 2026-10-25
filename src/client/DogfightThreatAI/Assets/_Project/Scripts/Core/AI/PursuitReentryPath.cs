using System;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>Follows a circle inferred from visible motion for one fixed recovery interval.</summary>
    public sealed class PursuitReentryPath
    {
        Vec2 _center;
        float _side;
        public bool Active { get; private set; }

        public void Begin(in FlightState observed, float turnRate, float confidence, bool enabled)
        {
            Active = enabled && confidence > .6f && MathF.Abs(turnRate) > .5f && observed.Speed > 1f;
            if (!Active) return;
            Vec2 travel = observed.Velocity.Normalized;
            _center = observed.Position + new Vec2(-travel.Y, travel.X) * (observed.Speed / turnRate);
            _side = MathF.Sign(turnRate);
        }

        public Vec2 Goal(in FlightState self)
        {
            Vec2 radial = self.Position - _center;
            float radius = radial.Magnitude;
            Vec2 outward = radius > .001f ? radial / radius : self.Forward;
            Vec2 tangent = new Vec2(-outward.Y, outward.X) * _side;
            float correction = MathF.Max(-1.5f, MathF.Min(2f, (8f - radius) * .35f));
            // A moving tangent cannot be passed and then chased backwards like a fixed point.
            // The center and side stay fixed until recovery ends, including after a late reversal.
            return self.Position + (tangent + outward * correction).Normalized * 8f;
        }
    }
}
