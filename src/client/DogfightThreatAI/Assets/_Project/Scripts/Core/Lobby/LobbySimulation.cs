using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Lobby
{
    /// <summary>本地大厅的单架飞机模拟。仅接收意图、推进状态；不引用场景、输入设备或网络。</summary>
    public sealed class LobbySimulation
    {
        readonly FlightModel _flight;
        FlightState _current;
        FlightState _previous;

        public FlightState Current => _current;
        public FlightState Previous => _previous;

        // 根据独立机型配置和活动边界，建立大厅飞机的初始状态。
        public LobbySimulation(AircraftSpec aircraft, Bounds2D movementBounds, Vec2 spawn, float heading)
        {
            if (!movementBounds.Contains(spawn))
            {
                throw new ArgumentOutOfRangeException(nameof(spawn));
            }
            _flight = FlightModel.From(aircraft, movementBounds);
            _current = _previous = FlightState.At(spawn, heading);
        }

        // 校验输入和时间步，再由飞行模型推进大厅状态。
        public void Tick(FlightInput input, float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }
            if (!Finite(input.Throttle) || !Finite(input.Steer))
            {
                throw new ArgumentException("Input must be finite.", nameof(input));
            }
            input.Throttle = MathF.Max(-1f, MathF.Min(1f, input.Throttle));
            input.Steer = MathF.Max(-1f, MathF.Min(1f, input.Steer));
            _previous = _current;
            _flight.Step(ref _current, input, deltaTime);
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
