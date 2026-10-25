using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using UnityEngine;
using PropulsionCue = DogfightThreatAI.Core.World.VisiblePropulsionCue;

namespace DogfightThreatAI.Gameplay.InputSource
{
    /// <summary>Samples frame edges, then consumes each intent once on the simulation step.</summary>
    public sealed class KeyboardInputSource : MonoBehaviour, IPlayerInputSource, IFlightSpeedSource,
        IFlightMotionSource, IVisiblePropulsionCueSource
    {
        [Tooltip("可选：WASD（默认）或 方向键")]
        [SerializeField] bool useArrowKeys;

        readonly PlayerBoostModel _boost = new PlayerBoostModel();
        PlayerController _controller;
        bool _dashHeld, _holdHeld, _dashPressPending, _holdPressPending;
        bool _dashLocked, _holdLocked, _hasFocus = true;

        bool CanUseBoost => isActiveAndEnabled && _controller != null &&
            _controller.isActiveAndEnabled && _controller.IsInitialized;
        public float BoostEnergy01 => _boost.Energy01;
        public float BoostEnergy => _boost.Energy;
        public float Energy => _boost.Energy;
        public float Energy01 => _boost.Energy01;
        public bool IsBoosting => CanUseBoost && _boost.IsBoosting;
        public bool IsDashing => CanUseBoost && _boost.IsDashing;
        public bool IsAccelerating => CanUseBoost && _boost.IsSustaining;
        public bool IsBoostCoasting => CanUseBoost && _boost.IsCoasting;
        public bool IsBoostCoolingDown => CanUseBoost && _boost.IsCoolingDown;
        public bool BoostRequiresRelease => _holdLocked || _boost.RequiresRelease;
        public bool IsOverheated => _boost.IsOverheated;
        public float OverheatRemaining => _boost.OverheatRemaining;
        public float BoostOverheatRemaining => _boost.OverheatRemaining;
        public float DashCooldownRemaining => _boost.DashCooldownRemaining;
        public float BoostDashCooldownRemaining => _boost.DashCooldownRemaining;
        public bool CanDash => CanUseBoost && _hasFocus && !_dashLocked && _boost.CanDash;
        public float SpeedMultiplier => CanUseBoost ? _boost.SpeedMultiplier : 1f;
        public FlightMotionCommand MotionCommand => _boost.MotionCommand;
        public PropulsionCue VisiblePropulsionCue => IsOverheated ? PropulsionCue.Overheat :
            IsDashing ? PropulsionCue.Dash : IsAccelerating ? PropulsionCue.Hold : PropulsionCue.None;

        void Awake()
        {
            _controller = GetComponent<PlayerController>();
            ResetAbilityState(Input.GetKey(KeyCode.Space), ShiftHeld());
        }

        void Update()
        {
            bool wasHoldHeld = _holdHeld;
            CaptureAbilityFrame(Input.GetKey(KeyCode.Space), ShiftHeld());
            CaptureAbilityEdges(Input.GetKeyDown(KeyCode.Space), !wasHoldHeld &&
                (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)));
            CaptureBrakeFrame(useArrowKeys
                ? Input.GetKey(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.DownArrow)
                : Input.GetKey(KeyCode.S) || Input.GetKeyDown(KeyCode.S));
            // A dead/disabled controller stops calling ReadInput, so cancel from the render loop too.
            if (!CanUseBoost) CancelInput();
        }

        public FlightInput ReadInput(float deltaTime)
        {
            float throttle, steer;
            bool braking;
            if (useArrowKeys)
            {
                throttle = Input.GetAxisRaw("Vertical");
                steer = -Input.GetAxisRaw("Horizontal");
                braking = Input.GetKey(KeyCode.DownArrow) || throttle < 0f;
            }
            else
            {
                braking = Input.GetKey(KeyCode.S);
                throttle = braking ? -1f : Input.GetKey(KeyCode.W) ? 1f : 0f;
                steer = (Input.GetKey(KeyCode.A) ? 1f : 0f) + (Input.GetKey(KeyCode.D) ? -1f : 0f);
            }

            // A FixedUpdate may precede Update, while an already cached short tap must survive this sample.
            CaptureAbilityFrame(Input.GetKey(KeyCode.Space), ShiftHeld());
            float forwardSpeed = _controller != null
                ? Vec2.Dot(_controller.State.Velocity, _controller.State.Forward) : 0f;
            ConsumeAbilityFrame(deltaTime, CanUseBoost, braking, forwardSpeed);
            return new FlightInput(braking ? -1f : CanUseBoost && _boost.RequestsForwardThrust ? 1f : throttle, steer);
        }

        void CaptureAbilityFrame(bool dashHeld, bool holdHeld)
        {
            if (_hasFocus)
            {
                if (!dashHeld) _dashLocked = false;
                if (!holdHeld) _holdLocked = false;
                if (dashHeld && !_dashHeld && !_dashLocked) _dashPressPending = true;
                if (holdHeld && !_holdHeld && !_holdLocked) _holdPressPending = true;
            }
            _dashHeld = dashHeld;
            _holdHeld = holdHeld;
        }

        void ConsumeAbilityFrame(float deltaTime, bool canBoost, bool braking, float currentForwardSpeed)
        {
            if (!canBoost || !_hasFocus || braking)
            {
                CancelInput();
                _boost.Step(false, _holdHeld, deltaTime, currentForwardSpeed, canBoost && _hasFocus, braking);
                return;
            }
            bool dashPressed = _dashPressPending && !_dashLocked;
            bool holdPressed = _holdPressPending && !_holdLocked;
            _dashPressPending = _holdPressPending = false;
            _boost.Step(dashPressed, _holdHeld && !_holdLocked, deltaTime, currentForwardSpeed,
                true, false, holdPressed);
        }

        void CaptureBrakeFrame(bool braking)
        {
            if (braking) CancelInput();
        }

        void CaptureAbilityEdges(bool dashPressed, bool holdPressed)
        {
            if (!_hasFocus) return;
            if (dashPressed && !_dashLocked) _dashPressPending = true;
            if (holdPressed && !_holdLocked) _holdPressPending = true;
        }

        void ResetAbilityState(bool dashHeld, bool holdHeld)
        {
            _dashHeld = dashHeld;
            _holdHeld = holdHeld;
            _dashLocked = dashHeld;
            _holdLocked = holdHeld;
            _dashPressPending = _holdPressPending = false;
            _boost.Reset(holdHeld);
        }

        void CancelInput()
        {
            _dashPressPending = _holdPressPending = false;
            _dashLocked |= _dashHeld;
            _holdLocked |= _holdHeld;
            _boost.CancelAbilities(_holdHeld);
        }

        void OnApplicationFocus(bool focused)
        {
            _hasFocus = focused;
            if (focused) return;
            CancelInput();
            // Inputs sampled while unfocused cannot count as the required physical release.
            _dashLocked = _holdLocked = true;
        }

        void OnDisable() => CancelInput();
        static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }
}
