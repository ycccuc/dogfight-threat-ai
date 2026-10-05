using Dogfight.Ai;
using UnityEngine;

namespace Dogfight.Gameplay
{
    /// <summary>
    /// 一架参战飞机。
    ///
    /// 这个类只做三件事（**不做决策**）：
    ///   1. 从 IPlaneInput 读一帧输入（玩家或 AI，它不关心）
    ///   2. 把输入交给 FlightModel 算出力与角速度，作用到 Rigidbody2D
    ///   3. 把最新状态导出成 AgentSnapshot 给 AI / HUD 看
    ///
    /// 手感公式全部在 Dogfight.Ai 的 FlightModel 里（可单测），这里只负责"接上 Unity"。
    /// Rigidbody2D 的配置和参数一样重要：
    ///     Gravity Scale = 0 / Linear Drag = 0.8 / Angular Drag = 0 / Interpolate / Continuous
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlaneAgent : MonoBehaviour
    {
        [Header("─ 身份 ─")]
        [Tooltip("对局内唯一 id，AI 用它认人。")]
        [SerializeField] int _agentId;

        [Tooltip("阵营：0 = 玩家方，1 = 敌方。同阵营互不攻击。")]
        [SerializeField] int _team;

        [Tooltip("最大血量。")]
        [SerializeField] float _maxHp = 100f;

        [Header("─ 配置资产 ─")]
        [Tooltip("飞行参数（在 Project 窗口右键 Create → Dogfight → 飞行参数 创建）。")]
        [SerializeField] FlightProfileSO _flightProfile;

        [Header("─ 输入 ─")]
        [Tooltip("勾上则本机键鼠控制（玩家机）。AI 控制的飞机请取消勾选，用 SetInput 注入。")]
        [SerializeField] bool _playerControlled = true;

        [Header("─ 表现 ─")]
        [Tooltip("留空则自动取子物体上的 SpriteRenderer。")]
        [SerializeField] SpriteRenderer _spriteRenderer;

        [Tooltip("精灵贴图机头方向与 +X 轴的夹角（度）。Kenney 的飞机机头朝上，所以是 -90。")]
        [SerializeField] float _spriteHeadingOffsetDegrees = -90f;

        Rigidbody2D _body;
        KeyboardPlaneInput _keyboardInput;
        IPlaneInput _input;
        FlightSpec _spec;
        float _hp;
        float _angularSpeedRad;

        public int AgentId => _agentId;

        public int Team => _team;

        public bool IsAlive => _hp > 0f;

        public float Hp => _hp;

        public float MaxHp => _maxHp;

        public FlightSpec Spec => _spec;

        /// <summary>当前机头朝向（弧度，0 = +X，逆时针为正）。</summary>
        public float HeadingRadians
        {
            get
            {
                Rigidbody2D body = Body;
                Vector2 up = body.transform.up;
                return Mathf.Atan2(up.y, up.x);
            }
        }

        Rigidbody2D Body
        {
            get
            {
                if (_body == null) _body = GetComponent<Rigidbody2D>();
                return _body;
            }
        }

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (_spriteRenderer == null) _spriteRenderer = GetComponentInChildren<SpriteRenderer>();

            _spec = _flightProfile != null ? _flightProfile.Bake() : FlightSpec.Default;
            _hp = _maxHp;

            ApplyRigidbodySettings();

            if (_playerControlled)
            {
                _keyboardInput = new KeyboardPlaneInput(Camera.main);
                _input = _keyboardInput;
            }
        }

        /// <summary>把配置里的物理相关项写进 Rigidbody2D —— 免得靠人手在 Inspector 里记。</summary>
        void ApplyRigidbodySettings()
        {
            _body.gravityScale = 0f;
            _body.drag = _spec.LinearDrag;
            _body.angularDrag = 0f;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        /// <summary>给 AI 用：替换输入源。传 null 表示松开所有输入。</summary>
        public void SetInput(IPlaneInput input) => _input = input;

        /// <summary>
        /// 推进一个固定步。**只施力，不推进物理** —— 物理由 MatchLoop 统一 Simulate。
        /// 这个分工是刻意的：批量对战要能控制"什么时候、走多少步"。
        /// </summary>
        public void Step(float dt)
        {
            if (!IsAlive) return;

            float throttle = 0f;
            float turn = 0f;

            if (_input != null)
            {
                throttle = _input.Throttle;
                turn = _input.Turn;

                // 键鼠输入自带瞄准点，AI 输入则由行为树决定 AimPoint
                if (_keyboardInput != null && ReferenceEquals(_input, _keyboardInput))
                {
                    _keyboardInput.Poll(transform.position);
                }
            }

            Vector2 heading = _body.transform.up;
            Vec2 headingVec = new Vec2(heading.x, heading.y);

            // 1) 推力（沿机头）
            Vec2 force = FlightModel.ThrustForce(_spec, headingVec, throttle);
            _body.AddForce(new Vector2(force.X, force.Y));

            // 2) 极速截断（只削超出部分）
            Vector2 velocity = _body.velocity;
            Vec2 clamped = FlightModel.ClampVelocity(_spec, new Vec2(velocity.x, velocity.y));
            if (clamped.X != velocity.x || clamped.Y != velocity.y)
            {
                _body.velocity = new Vector2(clamped.X, clamped.Y);
            }

            // 3) 转向（带转向惯性与失速惩罚）
            float speed = _body.velocity.magnitude;
            float targetAngular = FlightModel.TargetAngularSpeed(_spec, turn, speed);
            _angularSpeedRad = FlightModel.StepAngularSpeed(_spec, _angularSpeedRad, targetAngular, dt);
            _body.angularVelocity = _angularSpeedRad * Mathf.Rad2Deg;

            UpdateSpriteRotation();
        }

        /// <summary>
        /// 机头朝向 = 机身的 up 方向。Kenney 的飞机贴图机头朝上，
        /// 所以精灵本身不需要额外旋转；这个偏移是为了将来换机头朝右的贴图。
        /// </summary>
        void UpdateSpriteRotation()
        {
            if (_spriteRenderer == null) return;
            if (Mathf.Approximately(_spriteHeadingOffsetDegrees, 0f)) return;

            Transform t = _spriteRenderer.transform;
            Vector3 euler = t.localEulerAngles;
            euler.z = _spriteHeadingOffsetDegrees;
            t.localEulerAngles = euler;
        }

        public void ApplyDamage(float amount)
        {
            if (!IsAlive) return;
            _hp -= amount < 0f ? 0f : amount;
            if (_hp <= 0f)
            {
                _hp = 0f;
                OnDied();
            }
        }

        void OnDied()
        {
            // 雏形阶段：死亡只是停住并隐藏，不做爆炸特效（特效属于表现层，W1 再接）
            if (_spriteRenderer != null) _spriteRenderer.enabled = false;
            if (_body != null)
            {
                _body.velocity = Vector2.zero;
                _body.angularVelocity = 0f;
                _body.simulated = false;
            }
        }

        /// <summary>导出快照给 AI 与 HUD。AI 只看这个结构体，不碰 MonoBehaviour。</summary>
        public AgentSnapshot ToSnapshot()
        {
            Vector2 p = transform.position;
            Vector2 v = _body != null ? _body.velocity : Vector2.zero;
            return new AgentSnapshot(
                _agentId,
                new Vec2(p.x, p.y),
                new Vec2(v.x, v.y),
                HeadingRadians,
                _hp,
                _maxHp,
                IsAlive,
                _team);
        }

        /// <summary>给 Inspector 与场景搭建脚本用。</summary>
        public void ConfigureForSetup(int agentId, int team, bool playerControlled, FlightProfileSO profile)
        {
            _agentId = agentId;
            _team = team;
            _playerControlled = playerControlled;
            _flightProfile = profile;
        }

        void OnDrawGizmosSelected()
        {
            // 调参用：画出机头方向与速度矢量
            Vector3 nose = transform.up * 2f;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, transform.position + nose);

            if (Application.isPlaying && _body != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(transform.position, transform.position + (Vector3)_body.velocity);
            }
        }
    }
}
