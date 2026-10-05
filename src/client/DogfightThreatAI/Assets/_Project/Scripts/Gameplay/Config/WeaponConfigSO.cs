using Dogfight.Ai;
using UnityEngine;

namespace Dogfight.Gameplay
{
    /// <summary>
    /// 武器配置（编辑器里调的载体）。
    ///
    /// 除了数值，它还持有**预制体与音效的引用** —— 这正是纯 JSON 配置做不到的事
    /// （JSON 只能写路径字符串，拿不到类型校验，也要额外走 Resources/Addressables）。
    /// 运行时 Bake() 成 WeaponSpec（纯数据）交给伤害公式。
    ///
    /// 创建方式：Project 窗口右键 → Create → Dogfight → 武器配置
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponConfig", menuName = "Dogfight/武器配置", order = 1)]
    public sealed class WeaponConfigSO : ScriptableObject
    {
        [Header("─ 类型 ─")]
        [Tooltip("机炮 = 射线检测（即时命中）；导弹 = 有飞行时间并可追踪。")]
        [SerializeField] WeaponKind _kind = WeaponKind.Cannon;

        [Header("─ 数值 ─")]
        [Tooltip("单发基础伤害（距离 0 时）。")]
        [SerializeField] float _damage = 10f;

        [Tooltip("有效射程（世界单位）。超出后伤害线性衰减到 0，也是射线检测的最远距离。")]
        [SerializeField] float _range = 12f;

        [Tooltip("冷却时间（秒）。")]
        [SerializeField] float _cooldown = 0.15f;

        [Tooltip("弹速（单位/秒）。机炮是射线检测，此值被忽略。")]
        [SerializeField] float _projectileSpeed = 0f;

        [Tooltip("导弹最大转向速率（度/秒）。非追踪武器忽略。")]
        [SerializeField] float _turnRateDegrees = 120f;

        [Tooltip("散布半角（度）。0 = 绝对精准。")]
        [SerializeField] float _spreadDegrees = 0f;

        [Tooltip("单次开火发数（霰弹式机炮 > 1）。")]
        [SerializeField] int _pelletsPerShot = 1;

        [Header("─ 表现 ─")]
        [Tooltip("弹道预制体（自带贴图、拖尾、碰撞体）。机炮可为空（射线检测不需要实体）。")]
        [SerializeField] GameObject _projectilePrefab;

        [Tooltip("开火音效。")]
        [SerializeField] AudioClip _fireSfx;

        [Tooltip("命中特效预制体。")]
        [SerializeField] GameObject _hitVfxPrefab;

        public WeaponKind Kind => _kind;

        public GameObject ProjectilePrefab => _projectilePrefab;

        public AudioClip FireSfx => _fireSfx;

        public GameObject HitVfxPrefab => _hitVfxPrefab;

        /// <summary>烘焙成纯数据。武器不止一把，所以每次开火前取一次即可（很便宜）。</summary>
        public WeaponSpec Bake() =>
            new WeaponSpec(
                kind: _kind,
                damage: _damage,
                range: _range,
                cooldown: _cooldown,
                projectileSpeed: _projectileSpeed,
                turnRate: _turnRateDegrees * MathUtil.Deg2Rad,
                spreadRadians: _spreadDegrees * MathUtil.Deg2Rad,
                pelletsPerShot: _pelletsPerShot,
                damageMultiplier: 1f);

        void OnValidate()
        {
            _damage = Mathf.Max(0f, _damage);
            _range = Mathf.Max(0.1f, _range);
            _cooldown = Mathf.Max(0.01f, _cooldown);
            _projectileSpeed = Mathf.Max(0f, _projectileSpeed);
            _turnRateDegrees = Mathf.Max(0f, _turnRateDegrees);
            _spreadDegrees = Mathf.Clamp(_spreadDegrees, 0f, 180f);
            _pelletsPerShot = Mathf.Max(1, _pelletsPerShot);

            if (_kind == WeaponKind.Missile && _projectileSpeed <= 0f)
            {
                // 导弹必须有飞行时间，否则和射线检测没区别
                _projectileSpeed = 8f;
            }
        }
    }
}
