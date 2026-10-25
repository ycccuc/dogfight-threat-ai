using System;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>Four visible tactical skills share moving aim, a committed charge and finite recovery.</summary>
    public sealed class PursuitChargeInputSource : IPlayerInputSource, IFlightSpeedSource, IFlightCruiseSource, IFlightMotionSource
    {
        public const float TriggerRange = 8f, AimSeconds = .8f, ChargeSeconds = .9f;
        public const float AimCommitSeconds = .3f;
        public const float ChargeSpeedMultiplier = 5f;
        public const float RearOrbitSeconds = .35f;
        readonly IWorldView _world;
        readonly ICombatStatusView _combat;
        readonly FlightModel _predictionFlight;
        readonly Bounds2D? _arena;
        readonly Vec2[] _preview = new Vec2[2];
        readonly int _selfId;
        float _deadline, _lastTime = -1f;
        int _targetId, _seenHit;
        Vec2 _escapePoint;
        float _orbitSide, _tacticalSide, _sideLockUntil, _nextObservation;
        readonly PursuitObservationMemory _memory = new PursuitObservationMemory();
        readonly PursuitBoostModel _boost = new PursuitBoostModel();
        readonly PursuitSteeringPlanner _planner = new PursuitSteeringPlanner();
        readonly PursuitReentryPath _reentryPath = new PursuitReentryPath();
        PursuitDifficultyProfile _profile = PursuitDifficultyProfile.ForLevel(1);
        FlightState _observation;
        Vec2 _combatDirection;
        float _observedAt, _turnRate, _turnConfidence;
        bool _hasObservation;
        int _requestedDifficulty;
        float _windupSpeed;
        bool _boostCoasting;
        VisiblePropulsionCue _observedCue;
        float _cueObservedAt;
        Vec2 _chargeOrigin;
        float _chargeStartedAt, _chargeInitialProjection;
        bool _aimCommitted;
        float _committedBank;
        Vec2 _committedLaunchPosition;

        public AttackRunPhase Phase { get; private set; }
        public int AttackId { get; private set; }
        public int Level => _profile.Level;
        public int CollisionDamage => _profile.CollisionDamage;
        public int DifficultyOverride => _requestedDifficulty;
        public AttackTactic Tactic { get; private set; }
        public Vec2 ObservedTargetPosition => _observation.Position;
        public float ObservedAt => _observedAt;
        public Vec2 PredictionPoint { get; private set; }
        public float Adaptation01 => Level == 4 ? _turnConfidence : 0f;
        public float RecoverySeconds => _profile.RecoverySeconds;
        public float PhaseRemaining { get; private set; }
        public float LockedHeading { get; private set; }
        public bool IsAimLocked => Phase == AttackRunPhase.Charge || Phase == AttackRunPhase.Windup && _aimCommitted;
        public Vec2 CommittedLaunchPosition => _committedLaunchPosition;
        public Vec2 LockedDirection => IsAimLocked ? Vec2.FromAngle(LockedHeading) : AimDirection;
        public Vec2 AimDirection { get; private set; }
        public Vec2 ManeuverPoint { get; private set; }
        public AttackWindow AttackWindow { get; private set; }
        public int PreviewPointCount { get; private set; }
        public bool CanStartVisibleAttack { get; set; } = true;
        /// <summary>Public camera geometry limits the moving entry goal; it never gates attack preparation.</summary>
        public float VisibleApproachHalfWidth { get; set; } = 7.8f;
        public float VisibleApproachHalfHeight { get; set; } = 4f;
        public float SpeedMultiplier => Phase == AttackRunPhase.Charge ? ChargeSpeedMultiplier : 1f;
        public bool MaintainCruiseSpeed => Phase == AttackRunPhase.Windup;
        public float CruiseSpeed => _profile.CruiseSpeed;
        public float TurnRateDegPerSec => _profile.TurnRateDegPerSec;
        public float PursuitBoostSpeed => _profile.BoostSpeed;
        public const float BoostEnergyCapacity = PursuitBoostModel.Capacity;
        public float BoostEnergy => _boost.Energy;
        public float BoostCooldownRemaining => _boost.CooldownRemaining;
        public bool IsPursuitBoosting => _boost.IsBoosting;
        public bool IsBoostCoasting => _boostCoasting;
        public FlightMotionCommand MotionCommand { get; private set; } = FlightMotionCommand.Ordinary();
        public PursuitChargeInputSource(IWorldView world, int selfId, in AircraftSpec spec,
            ICombatStatusView combat, Bounds2D? arena = null, int difficultyLevel = 0)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (arena.HasValue && (!Finite(arena.Value.Min) || !Finite(arena.Value.Max) ||
                arena.Value.Width <= 2f || arena.Value.Height <= 2f)) throw new ArgumentOutOfRangeException(nameof(arena));
            RequestDifficulty(difficultyLevel);
            _selfId = selfId;
            _predictionFlight = FlightModel.From(spec, arena);
            _arena = arena;
        }

        /// <summary>Selection applies at the next pursuit boundary, never during a running warning or charge.</summary>
        public void RequestDifficulty(int level)
        {
            if (level < 0 || level > 4) throw new ArgumentOutOfRangeException(nameof(level));
            _requestedDifficulty = level;
        }

        public FlightInput ReadInput(float dt)
        {
            AttackWindow = AttackWindow.Inactive;
            PreviewPointCount = 0;
            PhaseRemaining = 0f;
            CombatStatus status = _combat.Status;
            if (!Finite(dt) || dt <= 0f || !Finite(status.ElapsedTime) || status.ElapsedTime < 0f ||
                !Finite(status.InvulnerabilityRemaining) || status.InvulnerabilityRemaining < 0f ||
                !status.IsPlayerAlive || !FindAircraft(out var self, out var target))
            {
                if (Finite(status.ElapsedTime) && status.ElapsedTime >= 0f) _boost.Cancel(status.ElapsedTime);
                _boostCoasting = false;
                _aimCommitted = false;
                Phase = AttackRunPhase.NoTarget;
                MotionCommand = FlightMotionCommand.FiniteAcceleration(0f, 0f, 60f, 0f, 0f, 0f);
                return new FlightInput(-1f, 0f);
            }
            float now = status.ElapsedTime;
            if (Phase == AttackRunPhase.NoTarget || now < _lastTime || target.EntityId != _targetId)
            {
                if (now < _lastTime) _boost.Reset(now); else _boost.Cancel(now);
                Phase = AttackRunPhase.Pursuit;
                _targetId = target.EntityId;
                _seenHit = status.HitSequence;
                _memory.Reset();
                _hasObservation = false;
                _nextObservation = 0f;
                _tacticalSide = 0f;
                _boostCoasting = false;
                _planner.Reset();
                _aimCommitted = false;
            }
            _lastTime = now;
            bool wasBoosting = _boost.IsBoosting;
            _boost.Advance(now);
            if (wasBoosting && !_boost.IsBoosting && Phase == AttackRunPhase.Pursuit) _boostCoasting = true;
            if (_boostCoasting && Vec2.Dot(self.Velocity, self.Forward) <= CruiseSpeed + .05f) _boostCoasting = false;
            if (Phase == AttackRunPhase.Pursuit && !_boost.IsBoosting && !_boostCoasting) SelectProfile(now);
            Observe(target, now);
            UpdatePrediction(self, now);
            Vec2 toTarget = PredictionPoint - self.Position;
            AimDirection = toTarget.SqrMagnitude > .000001f ? toTarget.Normalized : self.Forward;
            ManeuverPoint = PredictionPoint;

            if (status.HitSequence != _seenHit)
            {
                _seenHit = status.HitSequence;
                if (Phase != AttackRunPhase.Recover) BeginRecovery(self, now, true);
            }
            if (Phase == AttackRunPhase.Windup && now >= _deadline - .0001f)
            {
                // A skipped clock sample may cross both boundaries. Normal fixed steps already
                // committed the visible axis .3 seconds ago and must never aim again here.
                if (!_aimCommitted) { LockedHeading = self.Heading; _aimCommitted = true; }
                _chargeOrigin = self.Position;
                _chargeStartedAt = now;
                _chargeInitialProjection = Vec2.Dot(PredictionPoint - self.Position, self.Forward);
                Phase = AttackRunPhase.Charge;
                _deadline = now + ChargeSeconds;
            }
            if (Phase == AttackRunPhase.Charge)
            {
                Vec2 lockedForward = Vec2.FromAngle(LockedHeading);
                bool passed = now - _chargeStartedAt >= .08f &&
                    Vec2.Dot(self.Position - _chargeOrigin, lockedForward) > MathF.Max(2f, _chargeInitialProjection + 1f) &&
                    Vec2.Dot(PredictionPoint - self.Position, lockedForward) < -1f;
                if (now < _deadline - .0001f && !passed)
                {
                    PhaseRemaining = _deadline - now;
                    AttackWindow = new AttackWindow(AttackId, true, 0f, MathF.Min(1f, PhaseRemaining / dt));
                    MotionCommand = FlightMotionCommand.SpeedOverride(45f, 0f);
                    return new FlightInput(1f, 0f);
                }
                BeginRecovery(self, now);
            }
            if (Phase == AttackRunPhase.Recover)
            {
                if (now < _deadline - .0001f)
                {
                    PhaseRemaining = _deadline - now;
                    ManeuverPoint = _reentryPath.Active ? InsideArena(_reentryPath.Goal(self)) : _escapePoint;
                    MotionCommand = FlightMotionCommand.SpeedOverride(CruiseSpeed, TurnRateDegPerSec);
                    return Follow(self, ManeuverPoint, 1f, dt);
                }
                // Recovery has a fixed end; there is no arrival point or minimum-distance gate to get stuck at.
                Phase = AttackRunPhase.Pursuit;
                SelectProfile(now);
                Observe(target, now);
                UpdatePrediction(self, now);
                Vec2 direction = PredictionPoint - self.Position;
                AimDirection = direction.SqrMagnitude > .000001f ? direction.Normalized : self.Forward;
                ManeuverPoint = PredictionPoint;
            }
            // Protection still prevents damage in ContactCombatModel. Preparing the next
            // full warning does not need to wait for protection to expire as well.
            if (Phase == AttackRunPhase.Pursuit &&
                CanStartVisibleAttack && AttackFlightPilot.Distance(self.Position, target.Flight.Position) <= TriggerRange)
            {
                Phase = AttackRunPhase.Windup;
                _aimCommitted = false;
                _windupSpeed = Clamp(Vec2.Dot(self.Velocity, self.Forward), CruiseSpeed, PursuitBoostSpeed);
                _boost.Cancel(now);
                _boostCoasting = false;
                AttackId++;
                _deadline = now + AimSeconds;
                SelectSide(self, now, true);
                _orbitSide = _tacticalSide;
                _combatDirection = _observation.Speed > 1f ? _observation.Velocity.Normalized : _observation.Forward;
                Tactic = Level == 1 ? AttackTactic.Pursuit : Level == 2 ? AttackTactic.Intercept :
                    _orbitSide > 0f ? AttackTactic.FlankLeft : AttackTactic.FlankRight;
            }
            if (Phase == AttackRunPhase.Windup)
            {
                PhaseRemaining = MathF.Max(0f, _deadline - now);
                if (!_aimCommitted && PhaseRemaining <= AimCommitSeconds + .0001f)
                {
                    FlightState observed = WindupTarget();
                    float launchHorizon = PhaseRemaining;
                    float remainingSteps = launchHorizon / dt;
                    // Charge begins on a real input tick. If .8 is not divisible by dt,
                    // predict all upcoming full windup steps rather than a phantom fraction.
                    if (MathF.Abs(remainingSteps - MathF.Round(remainingSteps)) > .0001f)
                        launchHorizon = MathF.Ceiling(remainingSteps) * dt;
                    AttackCommitment plan = MovingAttackPlanner.Commit(self, observed, _windupSpeed,
                        TurnRateDegPerSec, launchHorizon, _orbitSide, WindupTargetTurnRate(), _predictionFlight, dt);
                    LockedHeading = plan.Heading;
                    _committedBank = plan.Bank;
                    _committedLaunchPosition = plan.LaunchPosition;
                    _aimCommitted = true;
                }
                AimDirection = _aimCommitted ? Vec2.FromAngle(LockedHeading) : self.Forward;
                MotionCommand = FlightMotionCommand.SpeedOverride(_windupSpeed, TurnRateDegPerSec);
                // The warning describes the real nose axis. Its final segment is a promise,
                // and the paid charge uses exactly that axis rather than a new target line.
                _preview[0] = _aimCommitted ? _committedLaunchPosition : self.Position;
                _preview[1] = _preview[0] + AimDirection * 14f;
                PreviewPointCount = 2;
                if (_aimCommitted)
                    return new FlightInput(1f, MovingAttackPlanner.CommittedSteer(self, LockedHeading,
                        _committedBank, TurnRateDegPerSec, dt));
                if (PhaseRemaining > AimSeconds - RearOrbitSeconds)
                {
                    Vec2 side = new Vec2(-_combatDirection.Y, _combatDirection.X);
                    ManeuverPoint = PredictionPoint - _combatDirection * 3f + side * (_orbitSide * 2.2f);
                    if (_arena.HasValue)
                    {
                        Bounds2D bounds = _arena.Value;
                        ManeuverPoint = new Vec2(Clamp(ManeuverPoint.X, bounds.Min.X + .7f, bounds.Max.X - .7f),
                            Clamp(ManeuverPoint.Y, bounds.Min.Y + .7f, bounds.Max.Y - .7f));
                    }
                    float towardPlayer = AttackFlightPilot.HeadingTo(self.Position, PredictionPoint);
                    float aroundRear = AttackFlightPilot.HeadingTo(self.Position, ManeuverPoint);
                    float deflection = (float)System.Math.IEEERemainder((double)aroundRear - towardPlayer, System.Math.PI * 2d);
                    float heading = towardPlayer + Clamp(deflection, -MathF.PI * .25f, MathF.PI * .25f);
                    FlightInput turn = AttackFlightPilot.BrakeToward(self, heading, TurnRateDegPerSec, dt);
                    return SafeWindupInput(self, turn.Steer, dt);
                }
                ManeuverPoint = PredictionPoint;
                return SafeWindupInput(self, Follow(self, PredictionPoint, 1f, dt).Steer, dt);
            }
            SelectSide(self, now, false);
            ManeuverPoint = InsideArena(_planner.Plan(self, _observation, PredictionPoint, _profile, _turnRate, _turnConfidence, now, CanStartVisibleAttack, VisibleApproachHalfWidth, VisibleApproachHalfHeight));
            if (_planner.SelectedSide != 0f) _tacticalSide = _planner.SelectedSide;
            Tactic = Level == 1 ? AttackTactic.Pursuit : Level == 2 ? AttackTactic.Intercept :
                _tacticalSide > 0f ? AttackTactic.FlankLeft : AttackTactic.FlankRight;
            if (_boost.IsBoosting && MathF.Abs(Vec2.NormalizeAngle((ManeuverPoint - self.Position).AngleRad - self.Heading)) > 55f * .0174532925f)
            { _boost.Cancel(now); _boostCoasting = true; }
            if (!_boost.IsBoosting && !_boostCoasting && _hasObservation && _boost.Energy >= PursuitBoostModel.StartCost && _boost.CooldownRemaining <= .00001f)
            {
                float duration = PursuitSteeringPlanner.SafeBoostDuration(self, _observation, ManeuverPoint, _profile, TriggerRange, _predictionFlight,
                    Level == 4 && _observedCue == VisiblePropulsionCue.Overheat && now - _cueObservedAt <= .9f);
                if (duration >= .2f) _boost.TryStart(now, duration, _profile);
            }
            MotionCommand = _boost.IsBoosting
                ? FlightMotionCommand.FiniteAcceleration(PursuitBoostSpeed, 40f, 30f, 90f, 40f, 0f)
                    .WithActiveSeconds(_boost.Remaining, CruiseSpeed, 90f, 30f)
                : _boostCoasting ? FlightMotionCommand.FiniteAcceleration(CruiseSpeed, 40f, 30f, 90f, 40f, 0f)
                : FlightMotionCommand.Ordinary(CruiseSpeed, TurnRateDegPerSec);
            return Follow(self, ManeuverPoint, 1f, dt);
        }

        public Vec2 GetPreviewPoint(int index)
        {
            if (index < 0 || index >= PreviewPointCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _preview[index];
        }

        FlightInput SafeWindupInput(in FlightState self, float desiredSteer, float dt)
        {
            FlightState target = WindupTarget();
            float steer = PursuitSteeringPlanner.SafeWindupSteer(self, target, _windupSpeed,
                TurnRateDegPerSec, MathF.Max(dt, PhaseRemaining), desiredSteer, _orbitSide,
                WindupTargetTurnRate(), _predictionFlight, dt);
            return new FlightInput(1f, steer);
        }

        FlightState WindupTarget()
        {
            FlightState target = _observation;
            target.Position += target.Velocity * MathF.Min(.3f, MathF.Max(0f, _lastTime - _observedAt));
            return target;
        }

        float WindupTargetTurnRate() => _turnConfidence > .35f ? _turnRate * _turnConfidence : 0f;

        FlightInput Follow(in FlightState self, Vec2 point, float throttle, float dt)
        {
            float heading = AttackFlightPilot.Distance(self.Position, point) > .001d
                ? AttackFlightPilot.HeadingTo(self.Position, point) : self.Heading;
            FlightInput turn = AttackFlightPilot.BrakeToward(self, heading, MotionCommand.TurnRateDegPerSec, dt);
            return new FlightInput(throttle, turn.Steer);
        }

        void BeginRecovery(in FlightState self, float now, bool contact = false)
        {
            if (Phase == AttackRunPhase.Recover) return;
            _boost.Cancel(now);
            _boostCoasting = false;
            if (_hasObservation)
            {
                float side = Vec2.Cross(self.Forward, _observation.Velocity);
                if (_observedAt >= now - 1f) _planner.RecordVisibleEvasion(side);
            }
            bool visibleMiss = Phase == AttackRunPhase.Charge && !contact && _hasObservation && _observedAt >= now - 1f;
            Phase = AttackRunPhase.Recover;
            _aimCommitted = false;
            _deadline = now + _profile.RecoverySeconds;
            float escapeDistance = MathF.Max(Level == 4 && !visibleMiss ? 4f : 12f,
                CruiseSpeed * _profile.RecoverySeconds + 2f);
            _escapePoint = self.Position + self.Forward * escapeDistance;
            _reentryPath.Begin(_observation, _turnRate, _turnConfidence, true);
            if (!_arena.HasValue) return;
            Bounds2D bounds = _arena.Value;
            _escapePoint = new Vec2(Clamp(_escapePoint.X, bounds.Min.X + .7f, bounds.Max.X - .7f),
                Clamp(_escapePoint.Y, bounds.Min.Y + .7f, bounds.Max.Y - .7f));
            if (AttackFlightPilot.Distance(self.Position, _escapePoint) < 3d)
                _escapePoint = (bounds.Min + bounds.Max) * .5f;
        }

        void SelectProfile(float now)
        {
            _profile = _requestedDifficulty == 0 ? PursuitDifficultyProfile.ForTime(now) : PursuitDifficultyProfile.ForLevel(_requestedDifficulty);
        }

        void Observe(in AircraftSnapshot target, float now)
        {
            _memory.Sample(target, now);
            if (_hasObservation && now < _nextObservation - .00001f) return;
            _hasObservation = _memory.TryObserve(now, _profile.ObservationDelay, out _observation,
                out _observedAt, out _turnRate, out _turnConfidence);
            _nextObservation = now + _profile.ObservationInterval;
            _memory.TryObserveCue(now, _profile.ObservationDelay, out _observedCue, out _cueObservedAt);
        }

        void UpdatePrediction(in FlightState self, float now)
        {
            float distance = (float)AttackFlightPilot.Distance(self.Position, _observation.Position);
            float lead = MathF.Min(_profile.PredictionLead, distance / (45f + _observation.Speed * .5f) * _profile.LeadScale);
            float age = MathF.Min(.3f, MathF.Max(0f, now - _observedAt));
            float horizon = MathF.Min(.6f, age + lead);
            Vec2 displacement = _observation.Velocity * horizon;
            if (_turnConfidence > .35f && MathF.Abs(_turnRate) > .12f)
            {
                float rate = _turnRate * _turnConfidence;
                float angle = rate * horizon;
                Vec2 forward = _observation.Velocity.Normalized;
                Vec2 side = new Vec2(-forward.Y, forward.X);
                displacement = forward * (_observation.Speed * MathF.Sin(angle) / rate) +
                    side * (_observation.Speed * (1f - MathF.Cos(angle)) / rate);
            }
            if (displacement.SqrMagnitude > 3.5f * 3.5f) displacement = displacement.Normalized * 3.5f;
            PredictionPoint = InsideArena(_observation.Position + displacement);
        }

        void SelectSide(in FlightState self, float now, bool beginningAim)
        {
            if (_tacticalSide != 0f && now < _sideLockUntil && !beginningAim) return;
            Vec2 forward = _observation.Speed > 1f ? _observation.Velocity.Normalized : _observation.Forward;
            Vec2 side = new Vec2(-forward.Y, forward.X);
            float distance = Vec2.Dot(self.Position - _observation.Position, side);
            _tacticalSide = distance < -.01f ? -1f : 1f;
            if (_turnConfidence > .6f && MathF.Abs(_turnRate) > .2f)
                _tacticalSide = _turnRate > 0f ? 1f : -1f;
            _sideLockUntil = now + 1.2f;
        }

        Vec2 InsideArena(Vec2 point)
        {
            if (!_arena.HasValue) return point;
            Bounds2D bounds = _arena.Value;
            return new Vec2(Clamp(point.X, bounds.Min.X + .7f, bounds.Max.X - .7f),
                Clamp(point.Y, bounds.Min.Y + .7f, bounds.Max.Y - .7f));
        }

        bool FindAircraft(out FlightState self, out AircraftSnapshot target)
        {
            self = default;
            target = default;
            AircraftSnapshot own = default;
            bool found = false, foundTarget = false;
            for (int i = 0; i < _world.AircraftCount; i++)
            {
                var item = _world.GetAircraft(i);
                if (item.EntityId != _selfId) continue;
                own = item;
                found = item.IsAlive && Valid(item.Flight);
                self = item.Flight;
                break;
            }
            if (!found) return false;
            double nearest = double.PositiveInfinity;
            for (int i = 0; i < _world.AircraftCount; i++)
            {
                var item = _world.GetAircraft(i);
                if (item.EntityId == _selfId || item.TeamId == own.TeamId || !item.IsAlive ||
                    !item.IsPlayerControlled || !Valid(item.Flight)) continue;
                double distance = AttackFlightPilot.Distance(self.Position, item.Flight.Position);
                if (!foundTarget || distance < nearest || (distance == nearest && item.EntityId < target.EntityId))
                { target = item; nearest = distance; foundTarget = true; }
            }
            return foundTarget;
        }

        static float Clamp(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));
        static bool Valid(in FlightState state) => Finite(state.Position) && Finite(state.Velocity) && Finite(state.Heading);
        static bool Finite(Vec2 value) => Finite(value.X) && Finite(value.Y);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
