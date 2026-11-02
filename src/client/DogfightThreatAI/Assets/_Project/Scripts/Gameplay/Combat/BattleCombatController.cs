using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.InputSource;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>固定步胶水：读取飞行快照与意图，推进纯 C# 战斗，分发事件给表现层。</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class BattleCombatController : MonoBehaviour, ICombatStatusView
    {
        [SerializeField] PlayerController player;
        [SerializeField] PlayerController enemy;
        [Tooltip("玩家和 AI 敌机共用的最大生命值。")]
        [SerializeField, Min(1)] int maxHealth = 5;
        [SerializeField, Min(.1f)] float invulnerabilitySeconds = .7f;
        [Tooltip("GameScene 使用弹丸；旧撞击模型仅供兼容测试对照。")]
        [SerializeField] bool useProjectileCombat;
        [SerializeField] WeaponData weapon;
        [SerializeField] GunnerProfileData[] difficulties;
        [SerializeField, Min(.5f)] float navigationCellSize = 4f;

        ContactCombatModel _model;
        ProjectileCombatSimulation _simulation;
        ChaseAiInputSource _ai;
        AircraftHitbox _playerHitbox, _enemyHitbox;
        AircraftDamageView _playerDamage, _enemyDamage;
        WeaponInputSource _weaponInput;
        BattlePropulsionView _propulsion;

        public PlayerController Player => player;
        public PlayerController Enemy => enemy;
        public bool UsesProjectileCombat => useProjectileCombat;
        public ProjectileCombatSimulation Simulation => _simulation;
        public WeaponSpec Weapon => weapon.ToSpec();
        public GunnerProfileData[] Difficulties => difficulties;
        public float NavigationCellSize => navigationCellSize;
        public CombatStatus Status => _simulation != null ? _simulation.Status : _model != null ? _model.Status : default;
        public float ContactRadius => (_playerHitbox != null ? _playerHitbox.WorldRadius : 0) + (_enemyHitbox != null ? _enemyHitbox.WorldRadius : 0);

        // 连接双方飞机与碰撞体，创建纯 C# 战斗模拟及受击表现。
        void Awake()
        {
            _playerHitbox = player != null ? player.GetComponent<AircraftHitbox>() : null;
            _enemyHitbox = enemy != null ? enemy.GetComponent<AircraftHitbox>() : null;
            if (enemy != null)
            {
                _ai = enemy.GetComponent<ChaseAiInputSource>();
            }
            if (useProjectileCombat)
            {
                if (weapon == null || difficulties == null || difficulties.Length == 0)
                {
                    Debug.LogError("缺少机炮或 AI 难度资产，请运行 Apply Cannon Combat Setup。", this);
                    enabled = false;
                    return;
                }
                _simulation = new ProjectileCombatSimulation(weapon.ToSpec(), maxHealth, invulnerabilitySeconds);
                _weaponInput = player.gameObject.AddComponent<WeaponInputSource>();
                var cue = enemy.GetComponent<AttackCueView>();
                if (cue != null)
                {
                    cue.enabled = false;
                }
            }
            else
            {
                _model = new ContactCombatModel(maxHealth, 1, invulnerabilitySeconds, Mathf.Max(.01f, ContactRadius));
            }
            if (player != null)
            {
                _playerDamage = player.gameObject.AddComponent<AircraftDamageView>();
            }
            if (enemy != null)
            {
                _enemyDamage = enemy.gameObject.AddComponent<AircraftDamageView>();
            }
            _propulsion = new BattlePropulsionView(player, enemy);
        }

        // 在机炮模拟就绪后接入战斗 HUD 与菜单。
        void Start()
        {
            if (useProjectileCombat && _simulation != null)
            {
                gameObject.AddComponent<BattleUiPresenter>().Connect(this);
            }
        }

        // 读取双方飞行快照和开火意图，结算战斗并分发命中事件。
        void FixedUpdate()
        {
            // 双方未就绪、控制器停用或玩家已被击毁时，不再推进战斗。
            if (player == null || enemy == null || !player.IsInitialized || !enemy.IsInitialized ||
                            !player.isActiveAndEnabled || !enemy.isActiveAndEnabled || !Status.IsPlayerAlive)
            {
                return;
            }
            if (_simulation != null)
            {
                _simulation.Tick(Time.fixedDeltaTime, player.PreviousState, player.State, enemy.PreviousState, enemy.State,
                    _playerHitbox != null ? _playerHitbox.WorldRadius : .5f, _enemyHitbox != null ? _enemyHitbox.WorldRadius : .5f,
                    _weaponInput.Consume(), _ai != null && _ai.isActiveAndEnabled && _ai.WantsFire);
                for (int i = 0; i < _simulation.Events.Count; i++)
                {
                    var message = _simulation.Events[i];
                    if (message.Kind != WeaponEventKind.Hit)
                    {
                        continue;
                    }
                    if (message.AircraftId == 1)
                    {
                        _playerDamage?.PlayHit();
                    }
                    else
                    {
                        _enemyDamage?.PlayHit();
                    }
                }
                // 胜负确定后停用双方飞行驱动，保留状态供结果界面读取。
                if (_simulation.IsFinished)
                {
                    player.enabled = false;
                    enemy.enabled = false;
                }
            }
            else
            {
                bool hit = _playerHitbox != null && _enemyHitbox != null && _playerHitbox.IsActive && _enemyHitbox.IsActive
                    ? _model.StepContact(player.PreviousState, player.State, enemy.PreviousState, enemy.State,
                        Time.fixedDeltaTime, ContactRadius, _ai != null ? _ai.CollisionDamage : 1)
                    : _model.Step(
                        player.PreviousState,
                        player.State,
                        enemy.PreviousState,
                        enemy.State,
                        Time.fixedDeltaTime);
                if (hit)
                {
                    _playerDamage?.PlayHit();
                }
                if (!Status.IsPlayerAlive)
                {
                    player.enabled = false;
                }
            }
        }

        // 处理重开输入，并根据权威生命和保护时间更新受击表现。
        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.R) && Time.timeScale > 0)
            {
                RestartBattle();
            }
            _playerDamage?.Present(Status.IsPlayerAlive, Status.InvulnerabilityRemaining);
            _enemyDamage?.Present(_simulation == null || _simulation.EnemyHealth > 0,
                _simulation != null ? _simulation.EnemyInvulnerabilityRemaining : 0f);
        }

        // 恢复时间推进，重新加载当前场景以重建对局。
        public void RestartBattle()
        {
            Time.timeScale = 1;
            SceneManager.LoadScene(
                    gameObject.scene.name,
                    LoadSceneMode.Single);
        }

        // 根据本步战斗和 AI 状态更新推进表现。
        void LateUpdate()
        {
            _propulsion?.Update(Status.IsPlayerAlive, _ai);
        }

        // 释放本控制器创建的推进表现资源。
        void OnDestroy()
        {
            _propulsion?.Dispose();
        }
    }
}
