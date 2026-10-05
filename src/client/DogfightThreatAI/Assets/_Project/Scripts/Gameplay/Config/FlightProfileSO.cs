using Dogfight.Ai;
using UnityEngine;

namespace Dogfight.Gameplay
{
    /// <summary>
    /// 飞行参数配置（编辑器里调的载体）。
    ///
    /// 分工：**在这里调参**（Inspector 拖拽、Tooltip 提示、运行时可改），
    /// 运行时由 Bake() 转成 Dogfight.Ai 的 FlightSpec（纯数据）交给手感公式。
    /// 为什么不让 Ai 层直接读这个资产：ScriptableObject 属于 UnityEngine，
    /// 而 Ai 层被 asmdef 强制不许引用 UnityEngine —— 那条边界换来的是可单测、可无头跑。
    ///
    /// 创建方式：Project 窗口右键 → Create → Dogfight → 飞行参数
    /// </summary>
    [CreateAssetMenu(fileName = "FlightProfile", menuName = "Dogfight/飞行参数", order = 0)]
    public sealed class FlightProfileSO : ScriptableObject
    {
        [Header("─ 推力 ─")]
        [Tooltip("油门全开时的加速度（单位/秒²）。越大起步越猛。")]
        [SerializeField] float _thrust = 22f;

        [Tooltip("极速上限（单位/秒）。")]
        [SerializeField] float _maxSpeed = 9f;

        [Header("─ 转向 ─")]
        [Tooltip("最大转向速率（度/秒）。越大掉头越快。")]
        [SerializeField] float _turnRateDegrees = 260f;

        [Tooltip("转向加速度（度/秒²）。越大转向越干脆，越小越飘。")]
        [SerializeField] float _turnAccelDegrees = 1800f;

        [Header("─ 失速 ─")]
        [Tooltip("低于这个速度，转向能力开始下降（单位/秒）。")]
        [SerializeField] float _stallSpeed = 1.5f;

        [Tooltip("速度不足时转向损失多少。0 = 完全不影响，1 = 停住就完全转不动。")]
        [Range(0f, 1f)]
        [SerializeField] float _stallPenalty = 0.6f;

        [Header("─ 阻尼 ─")]
        [Tooltip("会写进 Rigidbody2D.drag。这是俯视空战手感的核心旋钮，先调它。")]
        [SerializeField] float _linearDrag = 0.8f;

        /// <summary>烘焙成纯数据。每帧不该调它 —— PlaneAgent 在 Awake 里调一次并缓存。</summary>
        public FlightSpec Bake() =>
            new FlightSpec(
                thrust: _thrust,
                maxSpeed: _maxSpeed,
                turnRate: _turnRateDegrees * MathUtil.Deg2Rad,
                turnAccel: _turnAccelDegrees * MathUtil.Deg2Rad,
                stallSpeed: _stallSpeed,
                stallPenalty: _stallPenalty,
                linearDrag: _linearDrag);

        /// <summary>Inspector 改动时立刻看到"换算后的弧度值"，避免自己算。</summary>
        void OnValidate()
        {
            _thrust = Mathf.Max(0f, _thrust);
            _maxSpeed = Mathf.Max(0.1f, _maxSpeed);
            _turnRateDegrees = Mathf.Max(0f, _turnRateDegrees);
            _turnAccelDegrees = Mathf.Max(0f, _turnAccelDegrees);
            _stallSpeed = Mathf.Max(0.01f, _stallSpeed);
            _linearDrag = Mathf.Max(0f, _linearDrag);
        }

        /// <summary>
        /// 极速下的最小转弯半径（世界单位）= v / ω。
        /// 调参时用它判断"这个参数下飞机能不能在战场宽度内绕回来"。
        /// </summary>
        public float TurnRadiusAtMaxSpeed
        {
            get
            {
                float omega = _turnRateDegrees * Mathf.Deg2Rad * (1f - _stallPenalty);
                return omega <= 1e-4f ? float.PositiveInfinity : _maxSpeed / omega;
            }
        }
    }
}
