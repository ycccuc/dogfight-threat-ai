using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    public sealed class TargetMotionForecast
    {
        const float MaximumHorizon = 2f;
        const double MaximumObservedTurnRate = 4.5d;
        bool _hasSample, _hasTurn;
        int _targetId;
        float _sampledAt;
        Vec2 _velocity;
        float _turnRate;

        public float TurnRateRadiansPerSecond => _turnRate;

        /// <summary>Receives only actual timestamps from delayed observations; repeated reads do not create new evidence.</summary>
        public void Sample(in AircraftSnapshot observed, float observedAt)
        {
            if (!Finite(observedAt) || observedAt < 0f || !Finite(observed.Flight.Position) ||
                !Finite(observed.Flight.Velocity)) return;
            if (_hasSample && (observed.EntityId != _targetId || observedAt < _sampledAt)) Reset();
            if (_hasSample && observedAt <= _sampledAt) return;
            if (_hasSample)
            {
                double dt = (double)observedAt - _sampledAt;
                double previousSpeedSquared = (double)_velocity.X * _velocity.X + (double)_velocity.Y * _velocity.Y;
                Vec2 velocity = observed.Flight.Velocity;
                double speedSquared = (double)velocity.X * velocity.X + (double)velocity.Y * velocity.Y;
                if (dt <= .75d && previousSpeedSquared >= .09d && speedSquared >= .09d)
                {
                    double cross = (double)_velocity.X * velocity.Y - (double)_velocity.Y * velocity.X;
                    double dot = (double)_velocity.X * velocity.X + (double)_velocity.Y * velocity.Y;
                    float measured = (float)System.Math.Max(-MaximumObservedTurnRate,
                        System.Math.Min(MaximumObservedTurnRate, System.Math.Atan2(cross, dot) / dt));
                    _turnRate = !_hasTurn || measured * _turnRate < 0f ? measured : .7f * measured + .3f * _turnRate;
                    _hasTurn = true;
                }
                else
                {
                    _turnRate = 0f;
                    _hasTurn = false;
                }
            }
            _sampledAt = observedAt;
            _targetId = observed.EntityId;
            _velocity = observed.Flight.Velocity;
            _hasSample = true;
        }

        public void Reset()
        {
            _hasSample = _hasTurn = false;
            _sampledAt = _turnRate = 0f;
            _targetId = 0;
            _velocity = Vec2.Zero;
        }

        /// <summary>Integrates a bounded constant-turn velocity estimate; it is a prediction, never a future-state read.</summary>
        public Vec2 Predict(in AircraftSnapshot observed, float horizon, Bounds2D? arena = null)
        {
            double time = Finite(horizon) ? System.Math.Max(0d, System.Math.Min(MaximumHorizon, horizon)) : 0d;
            double omega = _hasSample && _targetId == observed.EntityId ? _turnRate : 0d;
            double x, y;
            Vec2 velocity = observed.Flight.Velocity;
            if (System.Math.Abs(omega) < .05d)
            {
                x = (double)observed.Flight.Position.X + velocity.X * time;
                y = (double)observed.Flight.Position.Y + velocity.Y * time;
            }
            else
            {
                double angle = omega * time;
                double sine = System.Math.Sin(angle), cosine = System.Math.Cos(angle);
                x = observed.Flight.Position.X + (velocity.X * sine + velocity.Y * (cosine - 1d)) / omega;
                y = observed.Flight.Position.Y + (velocity.X * (1d - cosine) + velocity.Y * sine) / omega;
            }
            var position = new Vec2(Saturate(x), Saturate(y));
            return arena.HasValue ? arena.Value.Clamp(position) : position;
        }

        static float Saturate(double value)
            => (float)System.Math.Max(-float.MaxValue, System.Math.Min(float.MaxValue, value));
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vec2 value) => Finite(value.X) && Finite(value.Y);
    }
}
