using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    internal sealed class AttackFlightPilot : IWorldView
    {
        readonly ChaseInputSource _arrival;
        readonly AircraftSpec _spec;
        AircraftSnapshot _self, _goal;

        public AttackFlightPilot(in AircraftSpec spec)
        {
            _spec = spec;
            _arrival = new ChaseInputSource(this, 2, spec, 1f, 0.08f, 0.04f);
        }

        public int AircraftCount => 2;

        public AircraftSnapshot GetAircraft(int index)
        {
            return index == 0 ? _self : _goal;
        }

        // 把目标点与速度要求转换为飞行操作意图。
        public FlightInput Move(in FlightState self, Vec2 point, float cruiseSpeed, float dt)
        {
            _self = new AircraftSnapshot(2, 2, self);
            _goal = new AircraftSnapshot(1, 1, FlightState.At(point), true, true);
            FlightInput input = _arrival.ReadInput(dt);
            return new FlightInput(MathF.Min(input.Throttle, cruiseSpeed / _spec.MaxSpeed), input.Steer);
        }

        // 在调整机头瞄准方向的同时处理制动意图。
        public FlightInput AimAndBrake(in FlightState self, float heading, float dt)
        {
            return BrakeToward(self, heading, _spec.TurnRateDegPerSec, dt);
        }

        // 在保持移动的情况下生成瞄准转向。
        public FlightInput AimWhileMoving(in FlightState self, float heading, float speed, float dt)
        {
            FlightInput orientation = BrakeToward(self, heading, _spec.TurnRateDegPerSec, dt);
            double error = System.Math.Abs(System.Math.IEEERemainder(
                    (double)heading - self.Heading,
                    System.Math.PI * 2d));
            // A large turn needs braking; aligned aim keeps advancing under the real acceleration model.
            float throttle = error > System.Math.PI / 12d ? -1f : MathF.Min(1f, speed / _spec.MaxSpeed);
            return new FlightInput(throttle, orientation.Steer);
        }

        internal static FlightInput BrakeToward(in FlightState self, float heading, float turnRate, float dt)
        {
            double angle = System.Math.IEEERemainder((double)heading - self.Heading, System.Math.PI * 2d);
            float steer = (float)System.Math.Max(-1d, System.Math.Min(1d,
                angle / (turnRate * (System.Math.PI / 180d) * dt)));
            return new FlightInput(-1f, steer);
        }

        internal static float HeadingTo(Vec2 from, Vec2 to)
        {
            return (float)System.Math.Atan2((double)to.Y - from.Y, (double)to.X - from.X);
        }

        internal static double Distance(Vec2 a, Vec2 b)
        {
            double x = (double)a.X - b.X, y = (double)a.Y - b.Y;
            return System.Math.Sqrt(x * x + y * y);
        }
    }
}
