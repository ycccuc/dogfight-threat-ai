using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    public enum AttackTactic { Pursuit, Intercept, FlankLeft, FlankRight }
    public readonly struct TacticalPlan
    {
        public readonly Vec2 Position;
        public readonly Vec2 AimPosition;
        public readonly AttackTactic Tactic;
        public bool IsIntercept => Tactic == AttackTactic.Intercept;
        public TacticalPlan(Vec2 position, AttackTactic tactic) : this(position, tactic, position) { }
        public TacticalPlan(Vec2 position, AttackTactic tactic, Vec2 aimPosition)
        { Position = position; Tactic = tactic; AimPosition = aimPosition; }
    }
    public sealed class TacticalPositionSelector
    {
        const float PreparationRadius = 5.1f;
        const float PlayerClearance = 1.5f;
        readonly AircraftSpec _spec;
        readonly FlightModel _prediction;
        readonly TargetMotionForecast _forecast = new TargetMotionForecast();
        readonly int[] _recentDodges = new int[4];
        int _dodgeNext, _dodgeCount, _historyVersion, _selectedHistoryVersion;
        int _lastTarget, _lastSide;
        float _lastTime = -1f;
        bool _hasApproach;
        TacticalPlan _approach;
        Vec2 _approachCenter;
        float _approachUntil, _approachDistance, _interceptUntil;
        int _approachLevel;

        public TacticalPositionSelector(in AircraftSpec spec)
        {
            _spec = spec;
            // 预测准备路线时不启用夹墙，才能排除实际会撞墙的候选路线。
            _prediction = FlightModel.From(spec);
        }

        public TacticalPlan Select(in FlightState self, in AircraftSnapshot observed,
            in AiDifficultyProfile profile, Bounds2D? arena, float now, float observedAt)
            => SelectCore(self, observed, profile, arena, now, observedAt);

        public TacticalPlan Select(in FlightState self, in AircraftSnapshot observed,
            in AiDifficultyProfile profile, Bounds2D? arena, float now)
            => SelectCore(self, observed, profile, arena, now, now);

        public TacticalPlan UpdateAim(in TacticalPlan plan, in AircraftSnapshot observed,
            in AiDifficultyProfile profile, Bounds2D? arena, float now, float observedAt, float impactSeconds = -1f)
        {
            _forecast.Sample(observed, observedAt);
            Vec2 aim;
            if (profile.Level <= 2)
            {
                aim = new Vec2(
                    Saturate((double)observed.Flight.Position.X + (double)observed.Flight.Velocity.X * profile.PredictionLead),
                    Saturate((double)observed.Flight.Position.Y + (double)observed.Flight.Velocity.Y * profile.PredictionLead));
                aim = Inside(aim, arena, 0f);
            }
            else
            {
                float age = Clamp(now - observedAt, 0f, .5f);
                float travel = profile.Level >= 4 ? 1.15f : 1.05f;
                if (impactSeconds >= 0f) travel = Clamp(impactSeconds, .8f, 1.7f);
                aim = _forecast.Predict(observed, age + travel, arena);
            }
            return new TacticalPlan(plan.Position, plan.Tactic, aim);
        }

        /// <summary>
        /// 只读调用方提供的延迟观察。这里选择准备位置，不代表该位置已通过冲撞准入检查。
        /// 冲撞方向、前摇、有效伤害窗口仍由攻击状态机决定。
        /// </summary>
        TacticalPlan SelectCore(in FlightState self, in AircraftSnapshot observed,
            in AiDifficultyProfile profile, Bounds2D? arena, float now, float observedAt)
        {
            if (_lastTime >= 0f && (now < _lastTime || observed.EntityId != _lastTarget))
            {
                _lastSide = _dodgeCount = _dodgeNext = 0;
                _historyVersion++;
                _hasApproach = false;
                _interceptUntil = 0f;
                _forecast.Reset();
            }
            _lastTime = now;
            _lastTarget = observed.EntityId;
            _forecast.Sample(observed, observedAt);

            Vec2 center = new Vec2(
                Saturate((double)observed.Flight.Position.X + (double)observed.Flight.Velocity.X * profile.PredictionLead),
                Saturate((double)observed.Flight.Position.Y + (double)observed.Flight.Velocity.Y * profile.PredictionLead));
            center = Inside(center, arena, 0f);
            if (profile.Level >= 3) center = _forecast.Predict(observed, profile.PredictionLead, arena);
            Vec2 radial = Direction(center, self.Position);
            if (radial.SqrMagnitude < .01f) radial = -observed.Flight.Forward;
            Vec2 direct = Inside(center + radial * PreparationRadius, arena, .7f);
            if (profile.Level <= 2)
                return UpdateAim(new TacticalPlan(direct, profile.Level <= 1 ? AttackTactic.Pursuit : AttackTactic.Intercept),
                    observed, profile, arena, now, observedAt);

            bool unchangedHistory = profile.Level < 4 || _historyVersion == _selectedHistoryVersion;
            bool sameRegion = Distance(center, _approachCenter) <= 5d;
            if (_hasApproach && _approachLevel == profile.Level && unchangedHistory && sameRegion && now < _approachUntil)
                return UpdateAim(_approach, observed, profile, arena, now, observedAt);
            if (_hasApproach && _approachLevel == profile.Level && now >= _approachUntil && unchangedHistory &&
                _approachDistance - Distance(self.Position, _approach.Position) < .75d)
                _interceptUntil = MathF.Max(_interceptUntil, now + 1.5f);
            if (now < _interceptUntil)
                return UpdateAim(Commit(new TacticalPlan(direct, AttackTactic.Intercept), center, self, profile, now),
                    observed, profile, arena, now, observedAt);

            Vec2 axis = observed.Flight.Speed > .1f ? Direction(Vec2.Zero, observed.Flight.Velocity) : observed.Flight.Forward;
            float habit = 0f;
            for (int i = 0; i < _dodgeCount; i++) habit += _recentDodges[i];
            if (_dodgeCount > 0) habit /= _dodgeCount;
            bool sameHistory = profile.Level < 4 || _historyVersion == _selectedHistoryVersion;
            float bestCost = float.PositiveInfinity;
            Vec2 best = direct;
            int bestSide = 0;

            // 左右各8个候选角度，均位于观察航向的45至135度侧面。
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 8; i++)
            {
                float angle = axis.AngleRad + side * (MathF.PI * .25f + i * MathF.PI / 14f);
                Vec2 candidate = center + Vec2.FromAngle(angle) * PreparationRadius;
                if (!Within(candidate, arena, .7f) || !HasExit(candidate, center, arena) ||
                    !RouteClear(self.Position, candidate, observed.Flight.Position)) continue;
                if (!TryRouteCost(self, candidate, observed, profile, arena, out float routeCost)) continue;

                Vec2 delta = candidate - self.Position;
                float turn = MathF.Abs(Vec2.NormalizeAngle(delta.AngleRad - self.Heading));
                float cost = delta.Magnitude * .08f + turn * .65f + routeCost;
                if (arena.HasValue)
                {
                    var bounds = arena.Value;
                    float clearance = MathF.Min(MathF.Min(candidate.X - bounds.Min.X, bounds.Max.X - candidate.X),
                                                MathF.Min(candidate.Y - bounds.Min.Y, bounds.Max.Y - candidate.Y));
                    cost += .4f / MathF.Max(.5f, clearance);
                }
                if (side == _lastSide && sameHistory) cost -= .45f;
                if (profile.Level >= 4)
                {
                    // 占据玩家最近常躲的那一侧；只在下一次选位时生效，不改旧计划。
                    cost -= side * habit * 1.1f;
                    if (_dodgeCount == 0) cost += side * MathF.Sin(now * .25f) * .08f;
                }
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = candidate;
                    bestSide = side;
                }
            }
            _lastSide = bestSide;
            _selectedHistoryVersion = _historyVersion;
            return UpdateAim(Commit(new TacticalPlan(best, bestSide > 0 ? AttackTactic.FlankLeft :
                bestSide < 0 ? AttackTactic.FlankRight : AttackTactic.Pursuit), center, self, profile, now),
                observed, profile, arena, now, observedAt);
        }

        TacticalPlan Commit(TacticalPlan approach, Vec2 center, in FlightState self,
            in AiDifficultyProfile profile, float now)
        {
            _approach = approach;
            _approachCenter = center;
            _approachDistance = (float)Distance(self.Position, approach.Position);
            _approachLevel = profile.Level;
            _approachUntil = now + (profile.Level >= 4 ? 1f : 1.2f);
            _selectedHistoryVersion = _historyVersion;
            _hasApproach = true;
            return approach;
        }

        /// <summary>正值为观察到向攻击线左侧躲避；最多保留最近四次，不读取玩家输入。</summary>
        public void RecordDodge(float signedSide)
        {
            if (float.IsNaN(signedSide) || float.IsInfinity(signedSide) || MathF.Abs(signedSide) < .01f) return;
            _recentDodges[_dodgeNext] = signedSide > 0f ? 1 : -1;
            _dodgeNext = (_dodgeNext + 1) % _recentDodges.Length;
            _dodgeCount = System.Math.Min(_recentDodges.Length, _dodgeCount + 1);
            _historyVersion++;
        }

        bool TryRouteCost(in FlightState self, Vec2 candidate, in AircraftSnapshot observed,
            in AiDifficultyProfile profile, Bounds2D? arena, out float cost)
        {
            const float dt = .02f;
            var predicted = self;
            cost = 0f;
            float initialDistance = Vec2.Distance(self.Position, observed.Flight.Position);
            for (int i = 0; i < 30; i++)
            {
                var previous = predicted;
                Vec2 delta = candidate - predicted.Position;
                float angle = Vec2.NormalizeAngle(delta.AngleRad - predicted.Heading);
                float steer = Clamp(angle / (_spec.TurnRateDegPerSec * (MathF.PI / 180f) * dt), -1f, 1f);
                float throttle = MathF.Abs(angle) > MathF.PI / 2f ? -1f : Clamp(profile.CruiseSpeed / _spec.MaxSpeed, 0f, 1f);
                _prediction.Step(ref predicted, new Input.FlightInput(throttle, steer), dt);
                if (!Within(predicted.Position, arena, 0f)) return false;
                // 检查真实飞行积分形成的准备路线，而非允许瞬移到候选点。
                float clearance = SegmentDistance(previous.Position, predicted.Position, observed.Flight.Position);
                if (initialDistance >= PlayerClearance && clearance < PlayerClearance) return false;
                cost += Vec2.Distance(previous.Position, predicted.Position) * .03f;
            }
            cost += Vec2.Distance(predicted.Position, candidate) * .04f;
            return true;
        }

        static bool RouteClear(Vec2 start, Vec2 end, Vec2 player)
        {
            float initial = Vec2.Distance(start, player);
            if (initial < PlayerClearance)
                return Vec2.Dot(start - player, end - start) >= 0f && Vec2.Distance(end, player) > initial;
            return SegmentDistance(start, end, player) >= PlayerClearance;
        }

        static float SegmentDistance(Vec2 start, Vec2 end, Vec2 point)
        {
            Vec2 delta = end - start;
            float t = delta.SqrMagnitude > 1e-6f ? Clamp(Vec2.Dot(point - start, delta) / delta.SqrMagnitude, 0f, 1f) : 0f;
            return Vec2.Distance(start + delta * t, point);
        }

        static bool HasExit(Vec2 candidate, Vec2 center, Bounds2D? arena)
        {
            Vec2 radial = (candidate - center).Normalized;
            Vec2 tangent = new Vec2(-radial.Y, radial.X);
            // 准备点两边至少留一个转出通道；最终冲撞出口由状态机另行预测。
            return Within(candidate + tangent * 2f, arena, .3f) || Within(candidate - tangent * 2f, arena, .3f);
        }

        static bool Within(Vec2 point, Bounds2D? arena, float margin)
        {
            if (!arena.HasValue) return true;
            var bounds = arena.Value;
            return point.X >= bounds.Min.X + margin && point.X <= bounds.Max.X - margin &&
                   point.Y >= bounds.Min.Y + margin && point.Y <= bounds.Max.Y - margin;
        }

        static Vec2 Inside(Vec2 point, Bounds2D? arena, float margin)
        {
            if (!arena.HasValue) return point;
            var bounds = arena.Value;
            float mx = MathF.Min(margin, bounds.Width * .25f), my = MathF.Min(margin, bounds.Height * .25f);
            return new Vec2(Clamp(point.X, bounds.Min.X + mx, bounds.Max.X - mx),
                            Clamp(point.Y, bounds.Min.Y + my, bounds.Max.Y - my));
        }

        static float Clamp(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));

        static float Saturate(double value)
            => (float)System.Math.Max(-float.MaxValue, System.Math.Min(float.MaxValue, value));

        static Vec2 Direction(Vec2 from, Vec2 to)
        {
            double x = (double)to.X - from.X, y = (double)to.Y - from.Y;
            double length = System.Math.Sqrt(x * x + y * y);
            return length > 1e-6d ? new Vec2((float)(x / length), (float)(y / length)) : Vec2.Zero;
        }

        static double Distance(Vec2 from, Vec2 to)
        {
            double x = (double)to.X - from.X, y = (double)to.Y - from.Y;
            return System.Math.Sqrt(x * x + y * y);
        }
    }
}
