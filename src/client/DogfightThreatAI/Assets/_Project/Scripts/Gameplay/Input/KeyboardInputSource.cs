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

        // 缓存飞机控制器，并按当前持键情况初始化推进输入。
        void Awake()
        {
            _controller = GetComponent<PlayerController>();
            ResetAbilityState(Input.GetKey(KeyCode.Space), ShiftHeld());
        }

        // 缓存短冲与长推的按键边沿，供固定步消费。
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
            if (!CanUseBoost)
            {
                CancelInput();
            }
        }

        // 读取油门和转向，并推进一次玩家能力模型。
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

        // 记录推进键的持续状态，识别本帧新按下的操作。
        void CaptureAbilityFrame(bool dashHeld, bool holdHeld)
        {
            if (_hasFocus)
            {
                if (!dashHeld)
                {
                    _dashLocked = false;
                }
                if (!holdHeld)
                {
                    _holdLocked = false;
                }
                if (dashHeld && !_dashHeld && !_dashLocked)
                {
                    _dashPressPending = true;
                }
                if (holdHeld && !_holdHeld && !_holdLocked)
                {
                    _holdPressPending = true;
                }
            }
            _dashHeld = dashHeld;
            _holdHeld = holdHeld;
        }

        // 消费一次能力输入，处理失焦、刹车与推进门控。
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

        // 刹车时取消尚未消费的推进请求。
        void CaptureBrakeFrame(bool braking)
        {
            if (braking)
            {
                CancelInput();
            }
        }

        // 缓存尚未被固定步消费的短按边沿。
        void CaptureAbilityEdges(bool dashPressed, bool holdPressed)
        {
            if (!_hasFocus)
            {
                return;
            }
            if (dashPressed && !_dashLocked)
            {
                _dashPressPending = true;
            }
            if (holdPressed && !_holdLocked)
            {
                _holdPressPending = true;
            }
        }

        // 重置能力输入与资源模型，并锁住当前仍按下的键。
        void ResetAbilityState(bool dashHeld, bool holdHeld)
        {
            _dashHeld = dashHeld;
            _holdHeld = holdHeld;
            _dashLocked = dashHeld;
            _holdLocked = holdHeld;
            _dashPressPending = _holdPressPending = false;
            _boost.Reset(holdHeld);
        }

        // 清空待消费操作，并要求持有的推进键先松开。
        void CancelInput()
        {
            _dashPressPending = _holdPressPending = false;
            _dashLocked |= _dashHeld;
            _holdLocked |= _holdHeld;
            _boost.CancelAbilities(_holdHeld);
        }

        // 失去焦点时取消推进，避免返回窗口后误触发能力。
        void OnApplicationFocus(bool focused)
        {
            _hasFocus = focused;
            if (focused)
            {
                return;
            }
            CancelInput();
            // Inputs sampled while unfocused cannot count as the required physical release.
            _dashLocked = _holdLocked = true;
        }

        // 停止输入时清理尚未消费的推进请求。
        void OnDisable()
        {
            CancelInput();
        }

        // 统一读取左右 Shift 的持续按下状态。
        static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
    }
}
