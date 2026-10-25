using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>
    /// 读取快照、提前减速并保持间距；仅输出飞行意图，由外部模拟推进真实状态。
    /// 正 Steer 与 FlightModel 一致，表示逆时针转向。
    /// </summary>
    public sealed class ChaseInputSource : IPlayerInputSource
    {
        readonly IWorldView _world;
        readonly int _selfId;
        readonly double _turnRateRad;
        readonly float _cruiseThrottle;
        readonly double _arrivalRadius;
        readonly double _distanceTolerance;
        readonly AircraftSpec _aircraft;
        readonly FlightModel _prediction;

        public ChaseInputSource(IWorldView world, int selfId, in AircraftSpec aircraft,
                                float cruiseThrottle = 1f, float arrivalRadius = 2f,
                                float distanceTolerance = 0.15f)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (!IsFinite(aircraft.MaxSpeed) || aircraft.MaxSpeed <= 0f ||
                !IsFinite(aircraft.MinSpeed) || aircraft.MinSpeed < 0f || aircraft.MinSpeed > aircraft.MaxSpeed ||
                !IsFinite(aircraft.Acceleration) || aircraft.Acceleration <= 0f ||
                !IsFinite(aircraft.TurnRateDegPerSec) || aircraft.TurnRateDegPerSec <= 0f ||
                !IsFinite(aircraft.Drag) || aircraft.Drag < 0f ||
                !IsFinite(aircraft.LateralGrip) || aircraft.LateralGrip < 0f ||
                !IsFinite(aircraft.DriftScrub) || aircraft.DriftScrub < 0f || aircraft.DriftScrub > 1f)
                throw new ArgumentOutOfRangeException(nameof(aircraft));
            if (!IsFinite(cruiseThrottle) || cruiseThrottle <= 0f || cruiseThrottle > 1f)
                throw new ArgumentOutOfRangeException(nameof(cruiseThrottle));
            if (!IsFinite(arrivalRadius) || arrivalRadius < 0f)
                throw new ArgumentOutOfRangeException(nameof(arrivalRadius));
            if (!IsFinite(distanceTolerance) || distanceTolerance < 0f)
                throw new ArgumentOutOfRangeException(nameof(distanceTolerance));

            _selfId = selfId;
            _aircraft = aircraft;
            _prediction = FlightModel.From(aircraft);
            _turnRateRad = aircraft.TurnRateDegPerSec * (System.Math.PI / 180d);
            _cruiseThrottle = cruiseThrottle;
            _arrivalRadius = arrivalRadius;
            _distanceTolerance = distanceTolerance;
        }

        public FlightInput ReadInput(float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime <= 0f) return FlightInput.Neutral;

            int count = _world.AircraftCount;
            if (!TryGetSelf(count, out var self) ||
                !TrySelectPlayer(count, self, out var target, out double distanceSquared))
                return new FlightInput(-1f, 0f);

            // double 中计算位置差和距离，避免有限 float 坐标相减/平方溢出。
            double dx = (double)target.Flight.Position.X - self.Flight.Position.X;
            double dy = (double)target.Flight.Position.Y - self.Flight.Position.Y;
            double distance = System.Math.Sqrt(distanceSquared);
            Vec2 forward = self.Flight.Forward;
            // 重合时沿自己的机头离开，方向确定且无需在原地转半圈。
            double nx = distance > 1e-6d ? dx / distance : -forward.X;
            double ny = distance > 1e-6d ? dy / distance : -forward.Y;
            double error = distance - _arrivalRadius;
            if (error < -_distanceTolerance)
            {
                double targetSpeed = System.Math.Sqrt(
                    (double)target.Flight.Velocity.X * target.Flight.Velocity.X +
                    (double)target.Flight.Velocity.Y * target.Flight.Velocity.Y);
                if (targetSpeed > 1e-4d)
                {
                    // 已过近时退到移动玩家后方；向前逃离可能被共同的极速上限抵消。
                    nx = target.Flight.Velocity.X / targetSpeed;
                    ny = target.Flight.Velocity.Y / targetSpeed;
                }
            }
            double cruiseSpeed = _aircraft.MaxSpeed * (double)_cruiseThrottle;
            double correction = System.Math.Abs(error) <= _distanceTolerance ? 0d :
                2d * (error - System.Math.Sign(error) * _distanceTolerance);
            correction = Clamp(correction, -cruiseSpeed, cruiseSpeed);

            if (correction > 0d)
            {
                double relativeX = (double)self.Flight.Velocity.X - target.Flight.Velocity.X;
                double relativeY = (double)self.Flight.Velocity.Y - target.Flight.Velocity.Y;
                double closingSpeed = System.Math.Max(0d, relativeX * nx + relativeY * ny);
                double lateralSpeed = System.Math.Abs(relativeX * ny - relativeY * nx);
                double driftMargin = System.Math.Min(_arrivalRadius,
                    lateralSpeed / System.Math.Max(0.1d, _aircraft.LateralGrip));
                double available = System.Math.Max(0d,
                    error - _distanceTolerance - closingSpeed * deltaTime - driftMargin);
                correction = System.Math.Min(correction,
                    System.Math.Sqrt(2d * _aircraft.Acceleration * available));
            }

            // 速度前馈让敌机随慢速/横向玩家移动，而非一直把机头指向其当前位置。
            double desiredX = target.Flight.Velocity.X + nx * correction;
            double desiredY = target.Flight.Velocity.Y + ny * correction;
            double desiredSpeed = System.Math.Sqrt(desiredX * desiredX + desiredY * desiredY);
            if (desiredSpeed < 1e-4d) return new FlightInput(-1f, 0f);
            if (desiredSpeed > cruiseSpeed)
            {
                desiredX *= cruiseSpeed / desiredSpeed;
                desiredY *= cruiseSpeed / desiredSpeed;
                desiredSpeed = cruiseSpeed;
            }

            double cross = forward.X * desiredY - forward.Y * desiredX;
            double dot = forward.X * desiredX + forward.Y * desiredY;
            double angle = System.Math.Atan2(cross, dot);
            if (angle >= System.Math.PI) angle = -System.Math.PI;
            double desiredSteer = angle / (_turnRateRad * deltaTime);
            float steer = (float)Clamp(desiredSteer, -1d, 1d);
            // 大角度先刹车转头，不一边全速前进一边绕着目标打圈。
            if (System.Math.Abs(angle) > System.Math.PI / 3d) return new FlightInput(-1f, steer);
            return ChooseThrottle(self, target, desiredX, desiredY, desiredSpeed, steer, deltaTime);
        }

        FlightInput ChooseThrottle(AircraftSnapshot self, AircraftSnapshot target,
                                   double desiredX, double desiredY, double desiredSpeed,
                                   float steer, float dt)
        {
            float accelerate = (float)Clamp(desiredSpeed * (1d + _aircraft.Drag * dt) /
                _aircraft.MaxSpeed, System.Math.Min(0.0001d, _cruiseThrottle), _cruiseThrottle);
            var best = new FlightInput(-1f, steer);
            double bestScore = double.PositiveInfinity;
            for (int candidate = 0; candidate < 3; candidate++)
            {
                var input = new FlightInput(candidate == 0 ? -1f : candidate == 1 ? 0f : accelerate, steer);
                var predicted = self.Flight;
                _prediction.Step(ref predicted, input, dt);
                if (!IsFinite(predicted.Velocity) || !IsFinite(predicted.Position)) continue;
                double vx = predicted.Velocity.X - desiredX;
                double vy = predicted.Velocity.Y - desiredY;
                double score = vx * vx + vy * vy;
                double px = (double)target.Flight.Position.X + target.Flight.Velocity.X * (double)dt - predicted.Position.X;
                double py = (double)target.Flight.Position.Y + target.Flight.Velocity.Y * (double)dt - predicted.Position.Y;
                double shortage = System.Math.Max(0d,
                    _arrivalRadius - _distanceTolerance - System.Math.Sqrt(px * px + py * py));
                score += 4d * shortage * shortage;
                // 同分时保留先枚举的刹车/滑行；不引入随机性或热路径数组分配。
                if (score < bestScore) { best = input; bestScore = score; }
            }
            return best;
        }

        bool TryGetSelf(int count, out AircraftSnapshot self)
        {
            for (int i = 0; i < count; i++)
            {
                self = _world.GetAircraft(i);
                if (self.EntityId == _selfId)
                    return self.IsAlive && IsFinite(self.Flight.Position) &&
                        IsFinite(self.Flight.Velocity) && IsFinite(self.Flight.Heading);
            }
            self = default;
            return false;
        }

        bool TrySelectPlayer(int count, AircraftSnapshot self,
                             out AircraftSnapshot target, out double nearestDistanceSquared)
        {
            target = default;
            nearestDistanceSquared = double.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var candidate = _world.GetAircraft(i);
                if (candidate.EntityId == _selfId || candidate.TeamId == self.TeamId ||
                    !candidate.IsAlive || !candidate.IsPlayerControlled ||
                    !IsFinite(candidate.Flight.Position) || !IsFinite(candidate.Flight.Velocity))
                    continue;

                double dx = (double)candidate.Flight.Position.X - self.Flight.Position.X;
                double dy = (double)candidate.Flight.Position.Y - self.Flight.Position.Y;
                double distanceSquared = dx * dx + dy * dy;
                if (!found || distanceSquared < nearestDistanceSquared ||
                    (distanceSquared == nearestDistanceSquared && candidate.EntityId < target.EntityId))
                {
                    found = true;
                    target = candidate;
                    nearestDistanceSquared = distanceSquared;
                }
            }
            return found;
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool IsFinite(Vec2 value) => IsFinite(value.X) && IsFinite(value.Y);
        static double Clamp(double value, double min, double max) => System.Math.Max(min, System.Math.Min(max, value));
    }
}
