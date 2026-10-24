using System;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>Time-stamped visible motion only. Decisions cannot inspect newer samples than their cutoff.</summary>
    public sealed class PursuitObservationMemory
    {
        const int Capacity = 256;
        const int CurvatureIntervalBudget = 128;
        const float CurvatureWindowSeconds = .48f;
        readonly FlightState[] _states = new FlightState[Capacity];
        readonly float[] _times = new float[Capacity];
        readonly VisiblePropulsionCue[] _cues = new VisiblePropulsionCue[Capacity];
        readonly float[] _cueTimes = new float[Capacity];
        int _next, _count;
        float _lastTime = -1f;

        public void Reset() { _next = _count = 0; _lastTime = -1f; }

        public void Sample(in FlightState state, float time)
            => Sample(new AircraftSnapshot(0, 0, state, propulsionCue: VisiblePropulsionCue.None, cueObservedAt: time), time);

        public void Sample(in AircraftSnapshot snapshot, float time)
        {
            if (!snapshot.IsVisible) return;
            FlightState state = snapshot.Flight;
            if (!Finite(time) || time < 0f || !Finite(state.Position.X) || !Finite(state.Position.Y) ||
                !Finite(state.Velocity.X) || !Finite(state.Velocity.Y) || !Finite(state.Heading)) return;
            if (_count > 0 && time <= _lastTime) return;
            _lastTime = time;
            _states[_next] = state;
            _times[_next] = time;
            _cues[_next] = snapshot.PropulsionCue;
            _cueTimes[_next] = snapshot.CueObservedAt;
            _next = (_next + 1) % Capacity;
            _count = System.Math.Min(Capacity, _count + 1);
        }

        public bool TryObserveCue(float now, float delay, out VisiblePropulsionCue cue, out float observedAt)
        {
            cue = VisiblePropulsionCue.None; observedAt = 0f;
            if (!Finite(now) || !Finite(delay) || delay < 0f) return false;
            for (int i = 0; i < _count; i++)
            {
                int index = Index(i);
                if (_times[index] > now - delay + .000001f || _cueTimes[index] > now - delay + .000001f) continue;
                cue = _cues[index]; observedAt = _cueTimes[index]; return true;
            }
            return false;
        }

        public bool TryObserve(float now, float delay, out FlightState state, out float observedAt,
            out float turnRate, out float confidence)
        {
            state = default;
            observedAt = turnRate = confidence = 0f;
            if (!Finite(now) || !Finite(delay) || now < 0f || delay < 0f) return false;
            if (_count == 0) return false;
            int offset = _count - 1;
            // The first visible sample seeds tracking; later reads cannot substitute newer information.
            for (int i = 0; i < _count; i++)
            {
                int index = Index(i);
                if (_times[index] <= now - delay + .000001f) { offset = i; break; }
            }
            int selected = Index(offset);
            state = _states[selected];
            observedAt = _times[selected];
            float sum = 0f, absolute = 0f, elapsed = 0f;
            int intervals = 0;
            // Keep the original 24 intervals at 50 Hz, but retain the same elapsed
            // history at higher frequencies instead of reducing its confidence.
            for (int i = offset; i + 1 < _count && intervals < CurvatureIntervalBudget &&
                elapsed < CurvatureWindowSeconds - .00001f; i++)
            {
                int current = Index(i), previous = Index(i + 1);
                float duration = _times[current] - _times[previous];
                if (duration <= 0f || duration > .12f || _states[current].Speed < 1f || _states[previous].Speed < 1f) break;
                float angle = Vec2.NormalizeAngle(_states[current].Velocity.AngleRad - _states[previous].Velocity.AngleRad);
                float rate = System.Math.Max(-4.5f, System.Math.Min(4.5f, angle / duration));
                sum += rate * duration;
                absolute += MathF.Abs(rate) * duration;
                elapsed += duration;
                intervals++;
            }
            if (elapsed > .0001f && absolute > .04f)
            {
                turnRate = sum / elapsed;
                confidence = MathF.Min(1f, elapsed / .45f) * MathF.Abs(sum) / absolute;
                if (MathF.Abs(turnRate) < .12f) turnRate = confidence = 0f;
            }
            return true;
        }

        int Index(int newestOffset) => (_next - 1 - newestOffset + Capacity) % Capacity;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
