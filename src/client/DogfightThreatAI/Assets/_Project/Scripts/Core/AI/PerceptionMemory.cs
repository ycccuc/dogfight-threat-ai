using System;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    public sealed class PerceptionMemory
    {
        const int Capacity = 128;
        readonly AircraftSnapshot[] _samples = new AircraftSnapshot[Capacity];
        readonly float[] _times = new float[Capacity];
        int _next, _count, _targetId;
        double _lastBucket = double.NegativeInfinity;
        float _lastTime = -1f;

        /// <summary>每个固定的0.1秒时间格最多记录一次，保留真实采样时刻，不补造历史。</summary>
        public void Sample(in AircraftSnapshot target, float time)
        {
            if (!Finite(time) || time < 0f || !Finite(target.Flight.Position) ||
                !Finite(target.Flight.Velocity) || !Finite(target.Flight.Heading)) return;
            if ((_count > 0 && target.EntityId != _targetId) || time < _lastTime) Reset();
            _lastTime = time;
            double bucket = System.Math.Floor((double)time * 10d + 1e-6d);
            if (bucket <= _lastBucket) return;
            _lastBucket = bucket;
            _targetId = target.EntityId;
            _samples[_next] = target;
            _times[_next] = time;
            _next = (_next + 1) % Capacity;
            _count = System.Math.Min(Capacity, _count + 1);
        }

        /// <summary>只返回截止时刻已存在的值快照；绝不利用更新的速度或坐标外推。</summary>
        public bool TryObserve(float now, float delay, out AircraftSnapshot observation, out float observedAt)
        {
            observation = default;
            observedAt = 0f;
            if (!Finite(now) || !Finite(delay) || now < 0f || delay < 0f) return false;
            double cutoff = (double)now - delay;
            for (int i = 0; i < _count; i++)
            {
                int index = (_next - 1 - i + Capacity) % Capacity;
                if (_times[index] > cutoff) continue;
                observation = _samples[index];
                observedAt = _times[index];
                return true;
            }
            return false;
        }

        public void Reset()
        {
            _next = _count = _targetId = 0;
            _lastBucket = double.NegativeInfinity;
            _lastTime = -1f;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vec2 value) => Finite(value.X) && Finite(value.Y);
    }
}
