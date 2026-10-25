using System;
using DogfightThreatAI.Core.Input;

namespace DogfightThreatAI.Core.Flight
{
    /// <summary>Paid Space dash and held Shift thrust share one tank; only FlightModel applies movement.</summary>
    public sealed class PlayerBoostModel
    {
        public const float MaximumEnergy = 60f;
        public const float DefaultBoostSeconds = .18f;
        public const float DefaultSpeedMultiplier = 2.4f;
        public const float MaximumSpeedMultiplier = 2f;
        public const float BurstEnergyCost = 25f;
        public const float BurstCooldownSeconds = 1.2f;
        public const float SustainDrainPerSecond = 40f;
        public const float RechargeDelaySeconds = .7f;
        public const float DefaultRechargeSeconds = 3f;
        public const float RechargePerSecond = 20f;
        public const float OverheatSeconds = .9f;
        const double Epsilon = .00001d;
        enum Stage { Idle, Dash, Hold, Tail }
        Stage _stage;
        double _energy = MaximumEnergy, _dashRemaining, _cooldownRemaining, _overheatRemaining, _idleSeconds;
        bool _wasHoldHeld, _overheatDebt;
        float _tailTurnRate = 240f;

        public float Energy => (float)_energy;
        public float Energy01 => (float)(_energy / MaximumEnergy);
        public bool IsBoosting => IsDashing || IsSustaining;
        public bool IsDashing => _stage == Stage.Dash;
        public bool IsSustaining => _stage == Stage.Hold;
        public bool IsCoasting => _stage == Stage.Tail && !IsOverheated;
        public bool IsOverheated => _overheatRemaining > Epsilon;
        public float OverheatRemaining => (float)_overheatRemaining;
        public float DashCooldownRemaining => (float)_cooldownRemaining;
        public float BurstCooldownRemaining => DashCooldownRemaining;
        public bool CanDash => !IsOverheated && !_overheatDebt && !IsDashing &&
            _cooldownRemaining <= Epsilon && _energy + Epsilon >= BurstEnergyCost;
        public bool IsCoolingDown => !IsBoosting && !IsOverheated && _energy < MaximumEnergy - Epsilon &&
            _idleSeconds + Epsilon < RechargeDelaySeconds;
        public bool RequiresRelease { get; private set; }
        public bool RequestsForwardThrust { get; private set; }
        public float PoweredSecondsThisStep { get; private set; }
        public FlightMotionCommand MotionCommand { get; private set; } = FlightMotionCommand.Ordinary();
        // Legacy presentation code may still use this ratio; motion uses MotionCommand.
        public float SpeedMultiplier => IsDashing ? DefaultSpeedMultiplier : IsSustaining ? MaximumSpeedMultiplier : 1f;

        /// <summary>Compatibility: the old held button now means Shift thrust, never a dash.</summary>
        public void Step(bool held, float deltaTime, bool canBoost = true, bool braking = false)
            => Step(false, held, deltaTime, 9f, canBoost, braking);

        /// <summary>
        /// dashPressed is a consumed edge, not a held request. holdPressed preserves a tap sampled
        /// between fixed steps. Speeds are current actual forward speeds, before this step.
        /// </summary>
        public void Step(bool dashPressed, bool holdHeld, float deltaTime, float currentForwardSpeed = 9f,
            bool canBoost = true, bool braking = false, bool holdPressed = false)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f) return;
            if (float.IsNaN(currentForwardSpeed) || float.IsInfinity(currentForwardSpeed)) currentForwardSpeed = 0f;
            PoweredSecondsThisStep = 0f;
            RequestsForwardThrust = false;
            // An explicit Update edge is a fresh press, including release/repress within one fixed step.
            if (holdPressed)
            {
                RequiresRelease = false;
                if (_stage == Stage.Hold && _wasHoldHeld) BeginTail(180f);
            }
            bool pressed = holdPressed || (holdHeld && !_wasHoldHeld);
            _wasHoldHeld = holdHeld;
            if (!holdHeld && !holdPressed) RequiresRelease = false;

            if (!canBoost)
            {
                CancelAbilities(holdHeld);
                SetPassiveMotion(currentForwardSpeed, false);
                return;
            }
            if (braking)
            {
                CancelAbilities(holdHeld);
                dashPressed = pressed = false;
            }
            else
            {
                if (_stage == Stage.Hold && (!holdHeld && !holdPressed || RequiresRelease)) BeginTail(180f);
                if (_stage == Stage.Tail && currentForwardSpeed <= 9f + Epsilon) _stage = Stage.Idle;
                if (dashPressed && CanDash)
                {
                    _energy = System.Math.Max(0d, _energy - BurstEnergyCost);
                    _dashRemaining = DefaultBoostSeconds;
                    _cooldownRemaining = BurstCooldownSeconds;
                    _overheatDebt = _energy <= Epsilon;
                    _stage = Stage.Dash;
                    _idleSeconds = 0d;
                }
                else if (pressed && !RequiresRelease && _stage != Stage.Dash && _stage != Stage.Hold)
                    TryBeginHold();
            }

            Stage initialStage = _stage;
            double initialHeat = _overheatRemaining;
            FlightMotionCommand first = CommandForStage(currentForwardSpeed);
            double firstDuration = 0d;
            bool firstEnded = false;
            double remaining = deltaTime;
            while (remaining > Epsilon)
            {
                if (_stage == Stage.Dash)
                {
                    double elapsed = System.Math.Min(remaining, _dashRemaining);
                    SpendTime(elapsed, ref remaining);
                    _dashRemaining = System.Math.Max(0d, _dashRemaining - elapsed);
                    PoweredSecondsThisStep += (float)elapsed;
                    _idleSeconds = 0d;
                    if (!firstEnded) firstDuration += elapsed;
                    if (_dashRemaining <= Epsilon)
                    {
                        FinishDash(holdHeld && !braking);
                        firstEnded = true;
                    }
                    continue;
                }
                if (_stage == Stage.Hold)
                {
                    double elapsed = System.Math.Min(remaining, _energy / SustainDrainPerSecond);
                    SpendTime(elapsed, ref remaining);
                    _energy = System.Math.Max(0d, _energy - elapsed * SustainDrainPerSecond);
                    PoweredSecondsThisStep += (float)elapsed;
                    _idleSeconds = 0d;
                    if (!firstEnded) firstDuration += elapsed;
                    if (_energy <= Epsilon)
                    {
                        _energy = 0d;
                        RequiresRelease = holdHeld || holdPressed;
                        BeginOverheat();
                        firstEnded = true;
                    }
                    continue;
                }
                if (IsOverheated)
                {
                    double elapsed = System.Math.Min(remaining, _overheatRemaining);
                    SpendTime(elapsed, ref remaining);
                    _overheatRemaining = System.Math.Max(0d, _overheatRemaining - elapsed);
                    if (_overheatRemaining <= Epsilon)
                    {
                        _overheatRemaining = 0d;
                        // The punishment already includes the recovery delay.
                        _idleSeconds = RechargeDelaySeconds;
                    }
                    continue;
                }
                Recharge(remaining);
                SpendTime(remaining, ref remaining);
            }

            _energy = System.Math.Min(MaximumEnergy, System.Math.Max(0d, _energy));
            MotionCommand = first;
            bool initialPowered = initialStage == Stage.Dash || initialStage == Stage.Hold;
            RequestsForwardThrust = initialPowered || initialStage == Stage.Tail;
            if (initialPowered && firstEnded)
            {
                float exitSpeed = IsOverheated || _overheatDebt ? 7.2f : _stage == Stage.Hold ? 18f : 9f;
                float exitTurn = IsOverheated ? 240f : _stage == Stage.Hold ? 180f : _tailTurnRate;
                MotionCommand = first.WithActiveSeconds((float)firstDuration, exitSpeed, exitTurn, 30f);
            }
            else if (initialHeat > Epsilon && initialHeat < deltaTime - Epsilon)
                MotionCommand = first.WithActiveSeconds((float)initialHeat, 9f, 240f, 30f);
        }

        /// <summary>Focus loss, death or braking cancels already paid movement without clearing resource debt.</summary>
        public void CancelAbilities(bool holdHeld = false)
        {
            float turn = _stage == Stage.Hold ? 180f : _stage == Stage.Tail ? _tailTurnRate : 240f;
            _dashRemaining = 0d;
            BeginTail(turn);
            RequiresRelease = holdHeld;
            _wasHoldHeld = holdHeld;
            RequestsForwardThrust = false;
            PoweredSecondsThisStep = 0f;
            if (_overheatDebt) BeginOverheat();
            SetPassiveMotion(9f, false);
        }

        public void Reset(bool holdHeld = false)
        {
            _stage = Stage.Idle;
            _energy = MaximumEnergy;
            _dashRemaining = _cooldownRemaining = _overheatRemaining = _idleSeconds = 0d;
            _overheatDebt = false;
            _wasHoldHeld = holdHeld;
            RequiresRelease = holdHeld;
            RequestsForwardThrust = false;
            PoweredSecondsThisStep = 0f;
            _tailTurnRate = 240f;
            MotionCommand = FlightMotionCommand.Ordinary();
        }

        void TryBeginHold()
        {
            if (IsOverheated || _overheatDebt || _energy + Epsilon < BurstEnergyCost)
            {
                RequiresRelease = true;
                return;
            }
            _stage = Stage.Hold;
            _idleSeconds = 0d;
        }

        void FinishDash(bool holdHeld)
        {
            if (_overheatDebt)
            {
                RequiresRelease = holdHeld;
                BeginOverheat();
                return;
            }
            BeginTail(240f);
            if (holdHeld && !RequiresRelease) TryBeginHold();
        }

        void BeginTail(float turnRate)
        {
            _stage = Stage.Tail;
            _tailTurnRate = turnRate;
        }

        void BeginOverheat()
        {
            _overheatDebt = false;
            _overheatRemaining = OverheatSeconds;
            _idleSeconds = 0d;
            _stage = Stage.Idle;
        }

        FlightMotionCommand CommandForStage(float speed)
        {
            if (IsOverheated) return FlightMotionCommand.FiniteAcceleration(7.2f, 18f, 30f, 240f);
            if (_stage == Stage.Dash) return FlightMotionCommand.SpeedOverride(21.6f);
            if (_stage == Stage.Hold) return FlightMotionCommand.FiniteAcceleration(18f, 7.5f, 30f, 180f);
            if (_stage == Stage.Tail && speed > 9f + Epsilon)
                return FlightMotionCommand.FiniteAcceleration(9f, 18f, 30f, _tailTurnRate);
            return FlightMotionCommand.Ordinary();
        }

        void SetPassiveMotion(float speed, bool thrust)
        {
            MotionCommand = CommandForStage(speed);
            RequestsForwardThrust = thrust;
        }

        void SpendTime(double elapsed, ref double remaining)
        {
            _cooldownRemaining = System.Math.Max(0d, _cooldownRemaining - elapsed);
            remaining = System.Math.Max(0d, remaining - elapsed);
        }

        void Recharge(double elapsed)
        {
            double before = System.Math.Max(0d, _idleSeconds - RechargeDelaySeconds);
            _idleSeconds += elapsed;
            double after = System.Math.Max(0d, _idleSeconds - RechargeDelaySeconds);
            _energy = System.Math.Min(MaximumEnergy, _energy + (after - before) * RechargePerSecond);
        }
    }
}
