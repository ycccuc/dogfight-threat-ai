using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Flight
{
    /// <summary>
    /// 飞机的运动学积分器。
    ///
    /// 该类型不继承 MonoBehaviour，不引用 UnityEngine，
    /// 也不访问 transform、Input、Time 或 Sprite。
    /// 因此可以在 EditMode 测试、AI 推演和逻辑服务器中直接使用。
    ///
    /// 运动模型：
    /// 每个固定步将速度分解为沿机头方向和垂直机头方向两个分量。
    /// 前进分量由油门控制，具有加减速惯性；
    /// 横向分量按 lateralGrip 指数衰减。
    /// 机头转向后，原速度在新坐标系中产生横向分量，形成甩尾。
    /// lateralGrip 越大，横向速度衰减越快；越小，甩尾越明显。
    /// </summary>
    public sealed class FlightModel
    {
        readonly float _minSpeed;
        readonly float _maxSpeed;
        readonly float _acceleration;
        readonly float _turnRateRad;
        readonly float _drag;
        readonly float _lateralGrip;
        readonly float _driftScrub;
        readonly Bounds2D _arena;
        readonly bool _hasArena;

        /// <param name="minSpeed">失速下界（单位/秒）</param>
        /// <param name="maxSpeed">极速上界（单位/秒）</param>
        /// <param name="acceleration">前进速度变化率（单位/秒²）</param>
        /// <param name="turnRateDegPerSec">最大转向速率（度/秒）</param>
        /// <param name="drag">速度阻尼（0 = 无）</param>
        /// <param name="lateralGrip">横向抓地力（1/秒）。越大越贴地，越小越甩尾</param>
        /// <param name="driftScrub">漂移掉速系数（0~1）。侧滑越厉害，前进极速越低</param>
        /// <param name="arena">战场边界；null = 无限大（调试用）</param>
        public FlightModel(float minSpeed, float maxSpeed, float acceleration,
                           float turnRateDegPerSec, float drag,
                           float lateralGrip = 6f, float driftScrub = 0.35f,
                           Bounds2D? arena = null)
        {
            _minSpeed     = minSpeed;
            _maxSpeed     = maxSpeed < minSpeed ? minSpeed : maxSpeed;
            _acceleration = acceleration;
            _turnRateRad  = turnRateDegPerSec * 0.01745329252f;   // 度 → 弧度（不依赖 Mathf）
            _drag         = drag;
            _lateralGrip  = lateralGrip < 0f ? 0f : lateralGrip;
            _driftScrub   = Clamp(driftScrub, 0f, 0.95f);
            _hasArena     = arena.HasValue;
            _arena        = arena.GetValueOrDefault();
        }

        /// <summary>使用纯配置数据构造飞行模型。</summary>
        public static FlightModel From(in AircraftSpec spec, Bounds2D? arena = null)
            => new FlightModel(spec.MinSpeed, spec.MaxSpeed, spec.Acceleration,
                               spec.TurnRateDegPerSec, spec.Drag,
                               spec.LateralGrip, spec.DriftScrub, arena);

        /// <summary>
        /// Explicit speed ability. A multiplier above one produces a straight burst immediately;
        /// returning to one removes its temporary overspeed. The ordinary three-argument path is unchanged.
        /// </summary>
        public void Step(ref FlightState state, in FlightInput input, float dt, float speedMultiplier)
            => Step(ref state, input, dt, speedMultiplier, false);

        /// <summary>Powered cruise keeps forward speed while turning; ordinary flight retains inertia.</summary>
        public void Step(ref FlightState state, in FlightInput input, float dt, float speedMultiplier, bool maintainCruiseSpeed)
        {
            if (float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier) || speedMultiplier < 1f ||
                float.IsInfinity(_maxSpeed * speedMultiplier)) throw new ArgumentOutOfRangeException(nameof(speedMultiplier));
            StepCore(ref state, input, dt, speedMultiplier, true, maintainCruiseSpeed);
        }

        /// <summary>
        /// 推进一个固定时间步。调用方必须传【固定】dt（如 Time.fixedDeltaTime），
        /// 否则服务器与客户端的模拟结果会发散。
        /// </summary>
        public void Step(ref FlightState state, in FlightInput input, float dt)
            => StepCore(ref state, input, dt, 1f, false, false);

        /// <summary>Apply the explicit policy produced for this simulation step.</summary>
        public void Step(ref FlightState state, in FlightInput input, float dt, in FlightMotionCommand command)
        {
            if (dt <= 0f) return;
            float activeSeconds = MathF.Min(dt, command.ActiveSeconds);
            if (activeSeconds > 0f)
                StepMotionSegment(ref state, input, activeSeconds, command);

            float remainingSeconds = dt - activeSeconds;
            if (remainingSeconds > 0f)
            {
                var exit = FlightMotionCommand.FiniteAcceleration(command.ExitSpeedLimit,
                    18f, command.ExitDeceleration, command.ExitTurnRateDegPerSec);
                StepMotionSegment(ref state, input, remainingSeconds, exit);
            }
        }

        void StepMotionSegment(ref FlightState state, in FlightInput input, float dt,
            in FlightMotionCommand command)
        {
            float throttle = Clamp(input.Throttle, -1f, 1f);
            float steer = Clamp(input.Steer, -1f, 1f);
            bool powered = command.Mode == FlightMotionMode.SpeedOverride && throttle > 0f;

            state.Heading += steer * command.TurnRateDegPerSec * .01745329252f * dt;
            Vec2 forward = state.Forward;
            Vec2 right = new Vec2(forward.Y, -forward.X);
            float forwardSpeed = Vec2.Dot(state.Velocity, forward);
            float lateralSpeed = Vec2.Dot(state.Velocity, right);
            float targetForwardSpeed;
            if (throttle > 0f)
                targetForwardSpeed = Clamp(throttle * command.MaxSpeed,
                    MathF.Min(_minSpeed, command.MaxSpeed), command.MaxSpeed);
            else if (throttle < 0f)
                targetForwardSpeed = 0f;
            else
                targetForwardSpeed = command.Mode == FlightMotionMode.Ordinary
                    ? forwardSpeed : MathF.Min(forwardSpeed, command.MaxSpeed);

            // Turning still costs speed and generates sideslip. A finite thrust policy
            // compensates only forward drag; it never erases the lateral component.
            if (!powered && _driftScrub > 0f && state.Speed > 1e-3f)
                targetForwardSpeed *= 1f - _driftScrub * MathF.Abs(lateralSpeed) / state.Speed;

            if (powered)
            {
                forwardSpeed = targetForwardSpeed;
                lateralSpeed = 0f;
            }
            else if (targetForwardSpeed < forwardSpeed)
                forwardSpeed = MoveTowardsScalar(forwardSpeed, targetForwardSpeed, command.Deceleration * dt);
            else if (command.Mode == FlightMotionMode.FiniteAcceleration)
                forwardSpeed = AccelerateForward(forwardSpeed, targetForwardSpeed, dt, command);
            else
                forwardSpeed = MoveTowardsScalar(forwardSpeed, targetForwardSpeed, command.Acceleration * dt);

            lateralSpeed *= MathF.Exp(-_lateralGrip * dt);
            if (_drag > 0f)
            {
                float damp = 1f / (1f + _drag * dt);
                lateralSpeed *= damp;
                // Finite acceleration/deceleration and powered targets are expressed
                // after drag. Ordinary movement keeps the historical inertial loss.
                if (command.Mode == FlightMotionMode.Ordinary)
                    forwardSpeed *= damp;
            }

            state.Velocity = forward * forwardSpeed + right * lateralSpeed;
            state.Position += state.Velocity * dt;
            ApplyArena(ref state);
        }

        static float AccelerateForward(float current, float target, float dt, in FlightMotionCommand command)
        {
            if (current < command.CruiseThreshold)
            {
                float lowerTarget = MathF.Min(target, command.CruiseThreshold);
                if (command.BelowCruiseAcceleration <= 0f) return current;
                float secondsToLowerTarget = (lowerTarget - current) / command.BelowCruiseAcceleration;
                if (secondsToLowerTarget >= dt)
                    return MoveTowardsScalar(current, lowerTarget, command.BelowCruiseAcceleration * dt);
                current = lowerTarget;
                dt -= secondsToLowerTarget;
            }
            return MoveTowardsScalar(current, target, command.Acceleration * dt);
        }

        void ApplyArena(ref FlightState state)
        {
            if (!_hasArena) return;
            Vec2 clamped = _arena.Clamp(state.Position);
            if (clamped.Equals(state.Position)) return;
            Vec2 outward = state.Position - clamped;
            float intoWall = Vec2.Dot(state.Velocity, outward.Normalized);
            if (intoWall > 0f) state.Velocity -= outward.Normalized * intoWall;
            state.Position = clamped;
        }

        void StepCore(ref FlightState state, in FlightInput input, float dt, float speedMultiplier, bool speedAbility, bool maintainCruiseSpeed)
        {
            if (dt <= 0f) return;

            // An ended ability restores the normal limit rather than carrying dash velocity into recovery.
            if (speedAbility && speedMultiplier == 1f && state.Speed > _maxSpeed)
                state.Velocity *= _maxSpeed / state.Speed;

            // 0) 输入夹紧：防止脏输入（网络包被篡改 / 读取错误）破坏物理
            float throttle = Clamp(input.Throttle, -1f, 1f);
            float steer    = Clamp(input.Steer,    -1f, 1f);
            float maximumSpeed = _maxSpeed * speedMultiplier;
            bool bursting = speedAbility && speedMultiplier > 1f && throttle > 0f;
            bool powered = bursting || (maintainCruiseSpeed && throttle > 0f);

            // 1) 转机头 —— 有限角速度，所以"掉头需要时间"
            state.Heading += steer * _turnRateRad * dt;

            // 2) 把速度分解到「机头方向」和「垂直机头方向」
            //    这是漂移的数学基础：机头一转，旧速度在新坐标系里就多出横向分量。
            Vec2 forward = state.Forward;
            Vec2 right   = new Vec2(forward.Y, -forward.X);      // 与机头垂直
            float forwardSpeed = Vec2.Dot(state.Velocity, forward);
            float lateralSpeed = Vec2.Dot(state.Velocity, right);

            // 3) 前进分量：油门控制
            //    W（>0）：加速到极速；至少给一个起步速度
            //    S（<0）：刹到 0
            //    无输入（=0）：保持当前前进速度 —— 惯性滑行，**绝不会自己加速**
            float targetForwardSpeed;
            if (throttle > 0f)
                targetForwardSpeed = Clamp(throttle * maximumSpeed, _minSpeed, maximumSpeed);
            else if (throttle < 0f)
                targetForwardSpeed = 0f;
            else
                targetForwardSpeed = forwardSpeed;

            // 3b) 漂移掉速：侧滑越厉害，前进极速越低（模拟机翼侧滑的阻力）
            //     不做这个的话，漂移是"免费"的，玩家会一直拖着侧滑飞
            float totalSpeed = state.Speed;
            if (!powered && _driftScrub > 0f && totalSpeed > 1e-3f)
            {
                float slipRatio = MathF.Abs(lateralSpeed) / totalSpeed;   // 0 = 纯前飞，1 = 纯侧滑
                targetForwardSpeed *= 1f - _driftScrub * slipRatio;
            }

            // 3c) 前进速度以有限加速度逼近目标 —— 惯性在这一步
            if (powered)
            {
                // A charge is an explicit movement ability, not an oversized throttle value.
                forwardSpeed = targetForwardSpeed;
                lateralSpeed = 0f;
            }
            else forwardSpeed = MoveTowardsScalar(forwardSpeed, targetForwardSpeed, _acceleration * dt);

            // 4) 横向分量被「抓地力」吃掉 —— 漂移与收敛都在这一步
            //    grip 越大 → exp 衰减越快 → 立刻咬住航线
            //    grip 越小 → 横向速度保留越久 → 甩得越远
            lateralSpeed *= MathF.Exp(-_lateralGrip * dt);

            // 5) 阻尼（对总速度的简单近似）
            if (_drag > 0f)
            {
                float damp = 1f / (1f + _drag * dt);
                forwardSpeed *= damp;
                lateralSpeed *= damp;
            }

            // 6) 重新合成速度并位移
            state.Velocity = forward * forwardSpeed + right * lateralSpeed;
            state.Position += state.Velocity * dt;

            // 7) 战场边界：越界就贴边，并把"朝外"的速度分量吃掉
            //    （只夹住位置而不处理速度的话，飞机会贴着墙一直抖）
            if (_hasArena)
            {
                Vec2 clamped = _arena.Clamp(state.Position);
                if (!clamped.Equals(state.Position))
                {
                    Vec2 outward = state.Position - clamped;          // 朝墙外的方向
                    float intoWall = Vec2.Dot(state.Velocity, outward.Normalized);
                    if (intoWall > 0f) state.Velocity -= outward.Normalized * intoWall;
                    state.Position = clamped;
                }
            }
        }

        /// <summary>标量版的"向目标靠拢"，不依赖 Mathf（保持模拟层零 Unity 依赖）。</summary>
        static float MoveTowardsScalar(float current, float target, float maxDelta)
        {
            float d = target - current;
            if (MathF.Abs(d) <= maxDelta) return target;
            return current + MathF.Sign(d) * maxDelta;
        }

        static float Clamp(float v, float lo, float hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
