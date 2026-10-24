using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay.Adapters;
using DogfightThreatAI.Gameplay.Config;
using UnityEngine;

namespace DogfightThreatAI.Gameplay
{
    /// <summary>
    /// 玩家飞机的胶水组件。
    /// 负责读取输入、调用 FlightModel、更新 Rigidbody2D，并将当前状态交给 PlayerView。
    /// 该组件不包含飞行数学，也不包含绘制逻辑。
    /// 单机阶段在本地完成模拟；联机阶段改为上报输入并读取服务器快照。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("数据")]
        [Tooltip("飞机配置资产，包含手感数值与美术引用。")]
        [SerializeField] AircraftData ship;

        [Header("接线")]
        [Tooltip("输入源。当前为 KeyboardInputSource，后续可替换为 AI 或网络输入源。")]
        [SerializeField] MonoBehaviour inputSource;
        [SerializeField] Rigidbody2D body;
        [SerializeField] PlayerView view;

        [Header("战场边界（固定镜头方案用）")]
        [Tooltip("以世界原点为中心的半尺寸。\n" +
                 "Orthographic Size = 5、16:9 时，屏幕可见半高 5、半宽约 8.9。\n" +
                 "填 (8.5, 4.5) 左右可以让飞机刚好在屏幕内活动。\n" +
                 "填 (0,0) 或负数 = 不限制（无限世界）")]
        [SerializeField] Vector2 arenaHalfSize = new Vector2(8.5f, 4.5f);

        FlightModel _model;
        FlightState _state;

        // 上一个固定步的状态，只给渲染插值用（不进 FlightModel，保持模拟层纯粹）
        FlightState _previous;
        bool _hasPrevious;

        public AircraftData Ship => ship;
        /// <summary>模拟状态的值副本；AI 不读取插值后的视图 Transform。</summary>
        public FlightState State => _state;
        /// <summary>本次真实固定步开始时的状态，供接触扫掠检测读取。</summary>
        public FlightState PreviousState => _hasPrevious ? _previous : _state;
        public Bounds2D? ArenaBounds => arenaHalfSize.x > 0f && arenaHalfSize.y > 0f
            ? Bounds2D.Centered(new Vec2(arenaHalfSize.x, arenaHalfSize.y)) : (Bounds2D?)null;
        public bool IsInitialized { get; private set; }

        void Reset() => body = GetComponent<Rigidbody2D>();

        void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (view == null) view = GetComponentInChildren<PlayerView>();

            if (ship == null)
            {
                Debug.LogError($"{name}: 没有指定 Ship Definition", this);
                enabled = false;
                return;
            }
            if (inputSource == null || !(inputSource is IPlayerInputSource))
            {
                Debug.LogError($"{name}: inputSource 必须挂一个实现了 IPlayerInputSource 的组件", this);
                enabled = false;
                return;
            }

            // 战场边界：半尺寸为正才启用，否则视为无限世界
            Bounds2D? arena = null;
            if (arenaHalfSize.x > 0f && arenaHalfSize.y > 0f)
                arena = Bounds2D.Centered(new Vec2(arenaHalfSize.x, arenaHalfSize.y));

            _model = FlightModel.From(AircraftSpecAdapter.ToSpec(ship), arena);

            // 初始机头朝向 = 美术上机头的朝向。
            // Kenney 的舰船图朝上（偏移 90°），所以初始就朝 +Y 飞，视觉和物理一致；
            // 如果换成朝右的图，只需改 AircraftData 的 Visual Forward Offset，不用改代码。
            _state = FlightState.At(new Vec2(transform.position.x, transform.position.y),
                                    ship.VisualForwardOffsetDeg * Mathf.Deg2Rad);

            if (view != null) view.SetVisualForward(ship.VisualForwardOffsetDeg);
            IsInitialized = true;
        }

        void FixedUpdate()
        {
            var source = (IPlayerInputSource)inputSource;
            var input  = source.ReadInput(Time.fixedDeltaTime);

            _previous = _state;   // 首步也必须从出生状态插值，不能使用默认原点
            if (source is IFlightMotionSource motionSource)
                _model.Step(ref _state, input, Time.fixedDeltaTime, motionSource.MotionCommand);
            else if (source is IFlightSpeedSource speedSource)
                _model.Step(ref _state, input, Time.fixedDeltaTime, speedSource.SpeedMultiplier,
                    source is IFlightCruiseSource cruiseSource && cruiseSource.MaintainCruiseSpeed);
            else
                _model.Step(ref _state, input, Time.fixedDeltaTime);
            _hasPrevious = true;

            // 位置以物理引擎为落地方式（Kinematic 刚体不会自作主张改位置）
            body.MovePosition(_state.Position.ToUnity());
        }

        void Update()
        {
            if (view == null) return;

            float alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 0f;
            view.Render(_state, _hasPrevious ? _previous : _state, alpha, ship.BodySprite);
        }
    }
}
