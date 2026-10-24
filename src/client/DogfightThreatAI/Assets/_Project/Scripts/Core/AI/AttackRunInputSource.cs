using System;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    public enum AttackRunPhase { NoTarget, Pursuit, Positioning, Windup, Charge, Recover }

    /// <summary>Delayed tactics, committed warning, bounded charge, harmless recovery. Only outputs FlightInput.</summary>
    public sealed class AttackRunInputSource : IPlayerInputSource
    {
        public const float WarningSeconds = 0.8f;
        public const float ChargeSeconds = 0.9f;
        readonly IWorldView _world;
        readonly int _selfId;
        readonly AircraftSpec _aircraft;
        readonly ICombatStatusView _combat;
        readonly Bounds2D? _arena;
        readonly PerceptionMemory _memory = new PerceptionMemory();
        readonly TacticalPositionSelector _tactics;
        readonly AttackFlightPilot _pilot;
        readonly AttackSafetyGate _safety;
        readonly AttackTrajectory _motion;
        readonly Vec2[] _preview = new Vec2[90];
        AiDifficultyProfile _profile;
        int _targetId, _seenHit;
        float _lastTime = -1f, _deadline, _nextPlan, _waypointRefreshed, _attackStarted, _freezeUntil;
        float _blockedSince = -1f;
        float _cancelRecoverUntil;
        bool _separated, _dodgeRecorded;
        bool _regainingDistance;
        float _repositionUntil;
        bool _hasDecision, _decisionVisible;
        float _decisionDt;
        int _decisionTargetId;
        FlightState _decisionSelf, _decisionTarget;
        CombatStatus _decisionCombat;
        FlightInput _decisionInput;
        Vec2 _observedAtCommit;
        TacticalPlan _plan;

        public AttackRunPhase Phase { get; private set; }
        public AttackTactic Tactic { get; private set; }
        public int Level => _profile.Level;
        public int AttackId { get; private set; }
        public float Aggression01 => (Level - 1) / 3f;
        public float PhaseRemaining { get; private set; }
        public float LockedHeading { get; private set; }
        public float WarningSpeed => _profile.WindupSpeed;
        public Vec2 LockedDirection => Vec2.FromAngle(LockedHeading);
        public AttackWindow AttackWindow { get; private set; }
        public bool CanStartVisibleAttack { get; set; } = true;
        public int PreviewPointCount { get; private set; }
        public Vec2 ManeuverPoint { get; private set; }
        public Vec2 GetPreviewPoint(int index)
        {
            if (index < 0 || index >= PreviewPointCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _preview[index];
        }

        public AttackRunInputSource(IWorldView world, int selfId, in AircraftSpec aircraft,
            ICombatStatusView combat, Bounds2D? arena = null, AircraftSpec? playerAircraft = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _pilot = new AttackFlightPilot(aircraft);
            if (arena.HasValue && (!Finite(arena.Value.Min) || !Finite(arena.Value.Max) ||
                arena.Value.Width <= 2f || arena.Value.Height <= 2f)) throw new ArgumentOutOfRangeException(nameof(arena));
            _selfId = selfId;
            _aircraft = aircraft;
            _arena = arena;
            _tactics = new TacticalPositionSelector(aircraft);
            _motion = new AttackTrajectory(aircraft, arena);
            _safety = new AttackSafetyGate(aircraft,
                playerAircraft ?? new AircraftSpec(2f, 9f, 18f, 240f, 0.1f, 8f, 0.35f), arena);
            _profile = AiDifficultyProfile.ForTime(0f);
        }

        public FlightInput ReadInput(float deltaTime)
        {
            if (!Finite(deltaTime) || deltaTime <= 0f)
            {
                AttackWindow = AttackWindow.Inactive;
                PreviewPointCount = 0;
                _hasDecision = false;
                return FlightInput.Neutral;
            }
            CombatStatus combat = _combat.Status;
            if (!Finite(combat.ElapsedTime) || combat.ElapsedTime < 0f ||
                !Finite(combat.InvulnerabilityRemaining) || combat.InvulnerabilityRemaining < 0f ||
                !combat.IsPlayerAlive || !TryFindAircraft(out var self, out var target))
            {
                AttackWindow = AttackWindow.Inactive;
                PreviewPointCount = 0;
                _hasDecision = false;
                if (Finite(combat.ElapsedTime) && combat.ElapsedTime >= 0f)
                    PreserveCancelledRecovery(combat.ElapsedTime);
                Phase = AttackRunPhase.NoTarget;
                PhaseRemaining = 0f;
                return new FlightInput(-1f, 0f);
            }
            if (_hasDecision && _decisionDt == deltaTime && _decisionVisible == CanStartVisibleAttack &&
                _decisionTargetId == target.EntityId && SameFlight(_decisionSelf, self.Flight) &&
                SameFlight(_decisionTarget, target.Flight) && SameCombat(_decisionCombat, combat)) return _decisionInput;
            AttackWindow = AttackWindow.Inactive;
            PreviewPointCount = 0;
            FlightInput decision = Decide(deltaTime, combat, self, target);
            _decisionDt = deltaTime;
            _decisionVisible = CanStartVisibleAttack;
            _decisionTargetId = target.EntityId;
            _decisionSelf = self.Flight;
            _decisionTarget = target.Flight;
            _decisionCombat = combat;
            _decisionInput = decision;
            _hasDecision = true;
            return decision;
        }

        FlightInput Decide(float deltaTime, in CombatStatus combat, in AircraftSnapshot self, in AircraftSnapshot target)
        {
            float now = combat.ElapsedTime;
            if (_targetId != 0 && _targetId != target.EntityId) PreserveCancelledRecovery(now);
            if (Phase == AttackRunPhase.NoTarget || _targetId != target.EntityId || now < _lastTime)
            {
                bool resumeRecovery = now >= _lastTime && now < _cancelRecoverUntil;
                _memory.Reset();
                _targetId = target.EntityId;
                _seenHit = combat.HitSequence;
                _nextPlan = 0f;
                _blockedSince = -1f;
                _regainingDistance = false;
                _repositionUntil = 0f;
                if (resumeRecovery)
                {
                    Phase = AttackRunPhase.Recover;
                    _deadline = _cancelRecoverUntil;
                    _freezeUntil = _deadline;
                    _waypointRefreshed = now;
                    _separated = false;
                    ManeuverPoint = InsideArena(self.Flight.Position + self.Flight.Forward * 5.5f, 0.7f);
                }
                else
                {
                    _freezeUntil = _cancelRecoverUntil = 0f;
                    Phase = AttackRunPhase.Pursuit;
                }
            }
            _lastTime = now;
            if (Phase != AttackRunPhase.Windup && Phase != AttackRunPhase.Charge && now >= _freezeUntil)
                _profile = AiDifficultyProfile.ForTime(now);
            _memory.Sample(target, now);
            bool observed = _memory.TryObserve(now, _profile.ObservationDelay, out var observation, out var observedAt);
            if (combat.HitSequence != _seenHit)
            {
                _seenHit = combat.HitSequence;
                BeginRecover(self.Flight, observed ? observation : self, now);
            }
            if (Phase == AttackRunPhase.Windup && now >= _deadline - 0.0001f)
            {
                Phase = AttackRunPhase.Charge;
                _deadline += ChargeSeconds;
            }
            if (Phase == AttackRunPhase.Charge && now >= _deadline - 0.0001f)
                BeginRecover(self.Flight, observed ? observation : self, now);
            if (Phase == AttackRunPhase.Windup || Phase == AttackRunPhase.Charge)
            {
                PhaseRemaining = MathF.Max(0f, _deadline - now);
                BuildPreview(self.Flight);
                if (Phase == AttackRunPhase.Windup) return _motion.WindupInput(self.Flight, LockedHeading, WarningSpeed, deltaTime);
                float end = MathF.Min(1f, PhaseRemaining / deltaTime);
                if (end > 0f) AttackWindow = new AttackWindow(AttackId, true, 0f, end);
                return new FlightInput(1f, 0f);
            }
            if (Phase == AttackRunPhase.Recover)
            {
                // True state supplies only a safety latch, never an aim correction.
                _separated |= AttackFlightPilot.Distance(self.Flight.Position, target.Flight.Position) >= 3.5d;
                PhaseRemaining = MathF.Max(0f, _deadline - now);
                if (observed && !_dodgeRecorded && observedAt >= _attackStarted + 0.3f)
                {
                    Vec2 delta = observation.Flight.Position - _observedAtCommit;
                    float side = LockedDirection.X * delta.Y - LockedDirection.Y * delta.X;
                    if (MathF.Abs(side) > 0.25f) _tactics.RecordDodge(side);
                    _dodgeRecorded = true;
                }
                if (now >= _deadline - 0.0001f && _separated && combat.InvulnerabilityRemaining <= 0f)
                {
                    Phase = AttackRunPhase.Pursuit;
                    _nextPlan = 0f;
                }
                else
                {
                    if (_profile.Level >= 4 && observed && _separated && now >= _deadline &&
                        combat.InvulnerabilityRemaining > 0f)
                    {
                        TacticalPlan next = RefreshAim(_plan, self.Flight, observation, now, observedAt);
                        double nextDistance = AttackFlightPilot.Distance(self.Flight.Position, next.AimPosition);
                        if (nextDistance >= 4.8d && nextDistance <= 8.5d)
                        {
                            // Prepare during the already harmless wait; neither the recovery nor the next warning is shortened.
                            return _pilot.AimAndBrake(self.Flight,
                                AttackFlightPilot.HeadingTo(self.Flight.Position, next.AimPosition), deltaTime);
                        }
                    }
                    if (observed && (now - _waypointRefreshed >= 2.5f ||
                        AttackFlightPilot.Distance(self.Flight.Position, ManeuverPoint) < 0.7d))
                    {
                        ManeuverPoint = EscapePoint(self.Flight, observation);
                        _waypointRefreshed = now;
                    }
                    return _pilot.Move(self.Flight, ManeuverPoint, _profile.CruiseSpeed, deltaTime);
                }
            }
            PhaseRemaining = 0f;
            if (!observed) return new FlightInput(-1f, 0f);
            if (now >= _nextPlan)
            {
                _plan = _tactics.Select(self.Flight, observation, _profile, _arena, now, observedAt);
                _nextPlan = now + 0.25f;
            }
            _plan = RefreshAim(_plan, self.Flight, observation, now, observedAt);
            Tactic = _plan.Tactic;
            ManeuverPoint = _plan.Position;
            if (now < _repositionUntil && AttackFlightPilot.Distance(self.Flight.Position, _plan.Position) > .18d)
            {
                Phase = AttackRunPhase.Positioning;
                return _pilot.Move(self.Flight, _plan.Position, _profile.CruiseSpeed, deltaTime);
            }
            _repositionUntil = 0f;
            Vec2 aim = _plan.AimPosition;
            double distance = AttackFlightPilot.Distance(self.Flight.Position, aim);
            // Hysteresis prevents tiny arrival steps around 4.5 from alternating opposite steering orders.
            if (distance < 4.5d) _regainingDistance = true;
            if (distance >= 4.8d) _regainingDistance = false;
            bool inAttackBand = !_regainingDistance && distance >= 4.5d && distance <= 8.5d;
            bool atPreparation = AttackFlightPilot.Distance(self.Flight.Position, _plan.Position) < 1d;
            Phase = inAttackBand || atPreparation ? AttackRunPhase.Positioning : AttackRunPhase.Pursuit;
            float heading = AttackFlightPilot.HeadingTo(self.Flight.Position, aim);
            if (inAttackBand)
            {
                FlightInput prepare = _pilot.AimWhileMoving(self.Flight, heading, WarningSpeed, deltaTime);
                double error = System.Math.Abs(System.Math.IEEERemainder((double)heading - self.Flight.Heading, 2d * System.Math.PI));
                Vec2 forward = self.Flight.Forward;
                float lateralSpeed = MathF.Abs(Vec2.Dot(self.Flight.Velocity, new Vec2(-forward.Y, forward.X)));
                bool executable = error <= 0.07d && lateralSpeed <= 2.5f &&
                    _motion.CanReachAim(self.Flight, self.Flight.Heading, WarningSpeed, aim);
                if (executable && combat.InvulnerabilityRemaining <= 0f &&
                    CanStartVisibleAttack && _safety.CanStart(self.Flight, target.Flight, self.Flight.Heading, WarningSpeed))
                {
                    LockedHeading = self.Flight.Heading;
                    AttackId++;
                    Phase = AttackRunPhase.Windup;
                    _attackStarted = now;
                    _observedAtCommit = observation.Flight.Position;
                    _dodgeRecorded = false;
                    _deadline = now + WarningSeconds;
                    _freezeUntil = _deadline + ChargeSeconds + _profile.RecoverySeconds;
                    PhaseRemaining = WarningSeconds;
                    _blockedSince = -1f;
                    BuildPreview(self.Flight);
                    return _motion.WindupInput(self.Flight, LockedHeading, WarningSpeed, deltaTime);
                }
                if (executable) prepare = _pilot.AimAndBrake(self.Flight, heading, deltaTime);
                // A denied start must trigger another approach, not a permanent stop at the rejected point.
                // The veto carries no direction; the alternative comes only from the delayed observation.
                if (combat.InvulnerabilityRemaining <= 0f && error <= 0.07d)
                {
                    if (_blockedSince < 0f) _blockedSince = now;
                    if (now - _blockedSince >= 0.6f)
                    {
                        _plan = new TacticalPlan(AlternatePreparation(self.Flight, aim), AttackTactic.Pursuit);
                        Tactic = _plan.Tactic;
                        ManeuverPoint = _plan.Position;
                        _nextPlan = now + 2.5f;
                        _repositionUntil = _nextPlan;
                        _blockedSince = -1f;
                        return _pilot.Move(self.Flight, ManeuverPoint, _profile.CruiseSpeed, deltaTime);
                    }
                }
                return prepare;
            }
            _blockedSince = -1f;
            return _pilot.Move(self.Flight, _plan.Position, _profile.CruiseSpeed, deltaTime);
        }

        void PreserveCancelledRecovery(float now)
        {
            if (Phase == AttackRunPhase.Windup || Phase == AttackRunPhase.Charge)
                _cancelRecoverUntil = MathF.Max(_cancelRecoverUntil, now + _profile.RecoverySeconds);
            else if (Phase == AttackRunPhase.Recover)
                _cancelRecoverUntil = MathF.Max(_cancelRecoverUntil, _deadline);
        }

        TacticalPlan RefreshAim(in TacticalPlan plan, in FlightState self, in AircraftSnapshot observation,
            float now, float observedAt)
        {
            TacticalPlan updated = _tactics.UpdateAim(plan, observation, _profile, _arena, now, observedAt);
            if (_profile.Level < 3) return updated;
            // Forecast to actual flight time, not a fixed guess that misses fast circles.
            for (int iteration = 0; iteration < 3; iteration++)
            {
                float course = AttackFlightPilot.HeadingTo(self.Position, updated.AimPosition);
                float impact = _motion.EstimatePassTime(self, course, WarningSpeed, updated.AimPosition);
                updated = _tactics.UpdateAim(updated, observation, _profile, _arena, now, observedAt, impact);
            }
            return updated;
        }

        Vec2 AlternatePreparation(in FlightState self, Vec2 observedAim)
        {
            float angle = AttackFlightPilot.HeadingTo(observedAim, self.Position);
            // A visibility veto supplies no camera coordinates. Try a broad lateral return using only
            // the delayed target-relative bearing, rather than inching around an off-screen arc.
            if (!CanStartVisibleAttack)
            {
                for (int side = 1; side >= -1; side -= 2)
                {
                    Vec2 lateral = observedAim + Vec2.FromAngle(angle + side * MathF.PI * .5f) * 5.1f;
                    Vec2 legal = InsideArena(lateral, 0.7f);
                    if (AttackFlightPilot.Distance(lateral, legal) < 0.01d) return lateral;
                }
            }
            for (int i = 1; i <= 32; i++)
            {
                Vec2 candidate = observedAim + Vec2.FromAngle(angle + i * MathF.PI / 16f) * 5.1f;
                Vec2 inside = InsideArena(candidate, 0.7f);
                if (AttackFlightPilot.Distance(candidate, inside) < 0.01d) return candidate;
            }
            return _plan.Position;
        }

        void BeginRecover(in FlightState self, in AircraftSnapshot observed, float now)
        {
            _repositionUntil = 0f;
            Phase = AttackRunPhase.Recover;
            _deadline = now + _profile.RecoverySeconds;
            _freezeUntil = _deadline;
            _waypointRefreshed = now;
            _separated = false;
            ManeuverPoint = EscapePoint(self, observed);
        }
        Vec2 EscapePoint(in FlightState self, in AircraftSnapshot observed)
        {
            Vec2 direction = self.Speed > 0.5f ? self.Velocity.Normalized : self.Forward;
            Vec2 best = self.Position;
            double bestScore = double.NegativeInfinity;
            for (int i = 0; i < 16; i++)
            {
                Vec2 candidate = InsideArena(observed.Flight.Position + Vec2.FromAngle(i * MathF.PI / 8f) * 5.5f, 0.7f);
                Vec2 radial = candidate - observed.Flight.Position;
                double score = AttackFlightPilot.Distance(candidate, observed.Flight.Position) +
                    2d * Vec2.Dot(radial.Normalized, direction);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }
        void BuildPreview(in FlightState self)
        {
            float windup = Phase == AttackRunPhase.Windup ? PhaseRemaining : 0f;
            float charge = Phase == AttackRunPhase.Windup ? ChargeSeconds : PhaseRemaining;
            PreviewPointCount = _motion.Build(self, LockedHeading, windup, charge, WarningSpeed, _preview);
        }
        Vec2 InsideArena(Vec2 point, float margin)
        {
            if (!_arena.HasValue) return point;
            Bounds2D b = _arena.Value;
            return new Vec2(Clamp(point.X, b.Min.X + margin, b.Max.X - margin), Clamp(point.Y, b.Min.Y + margin, b.Max.Y - margin));
        }
        bool TryFindAircraft(out AircraftSnapshot self, out AircraftSnapshot target)
        {
            self = default; target = default;
            bool haveSelf = false, haveTarget = false;
            for (int i = 0; i < _world.AircraftCount; i++)
            {
                var candidate = _world.GetAircraft(i);
                if (candidate.EntityId != _selfId) continue;
                self = candidate;
                haveSelf = candidate.IsAlive && Valid(candidate.Flight);
                break;
            }
            if (!haveSelf) return false;
            double nearest = double.PositiveInfinity;
            for (int i = 0; i < _world.AircraftCount; i++)
            {
                var candidate = _world.GetAircraft(i);
                if (candidate.EntityId == _selfId || candidate.TeamId == self.TeamId || !candidate.IsAlive ||
                    !candidate.IsPlayerControlled || !Valid(candidate.Flight)) continue;
                double distance = AttackFlightPilot.Distance(candidate.Flight.Position, self.Flight.Position);
                if (!haveTarget || distance < nearest || (distance == nearest && candidate.EntityId < target.EntityId))
                { target = candidate; nearest = distance; haveTarget = true; }
            }
            return haveTarget;
        }
        static bool Valid(in FlightState flight) => Finite(flight.Position) && Finite(flight.Velocity) && Finite(flight.Heading);
        static bool SameFlight(in FlightState a, in FlightState b) => a.Position.Equals(b.Position) && a.Velocity.Equals(b.Velocity) && a.Heading == b.Heading;
        static bool SameCombat(in CombatStatus a, in CombatStatus b) => a.ElapsedTime == b.ElapsedTime &&
            a.PlayerHealth == b.PlayerHealth && a.MaxPlayerHealth == b.MaxPlayerHealth &&
            a.HitSequence == b.HitSequence && a.InvulnerabilityRemaining == b.InvulnerabilityRemaining;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vec2 value) => Finite(value.X) && Finite(value.Y);
        static float Clamp(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));
    }
}
