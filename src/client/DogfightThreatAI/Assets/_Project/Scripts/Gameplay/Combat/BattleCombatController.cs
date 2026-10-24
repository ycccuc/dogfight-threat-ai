using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Gameplay.InputSource;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>两机模拟之后结算接触；HUD 和受击闪烁仅展示这份战斗状态。</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class BattleCombatController : MonoBehaviour, ICombatStatusView
    {
        [SerializeField] PlayerController player;
        [SerializeField] PlayerController enemy;
        [SerializeField, Min(1)] int maxHealth = 5;
        [SerializeField, Min(0.1f)] float invulnerabilitySeconds = .7f;

        ContactCombatModel _model;
        ChaseAiInputSource _ai;
        AircraftHitbox _playerHitbox, _enemyHitbox;
        PlayerView _playerView;
        SpriteRenderer[] _playerRenderers;
        Color[] _playerColors;
        GUIStyle _titleStyle, _labelStyle, _smallStyle;
        float _lastHitTime = -10f;
        BattlePropulsionView _propulsionView;

        public PlayerController Player => player;
        public PlayerController Enemy => enemy;
        public CombatStatus Status => _model != null ? _model.Status : default;
        public float ContactRadius => (_playerHitbox != null ? _playerHitbox.WorldRadius : 0f) +
            (_enemyHitbox != null ? _enemyHitbox.WorldRadius : 0f);

        void Awake()
        {
            _playerHitbox = player != null ? player.GetComponent<AircraftHitbox>() : null;
            _enemyHitbox = enemy != null ? enemy.GetComponent<AircraftHitbox>() : null;
            _playerView = player != null ? player.GetComponentInChildren<PlayerView>() : null;
            _model = new ContactCombatModel(maxHealth, 1, invulnerabilitySeconds, Mathf.Max(.01f, ContactRadius));
            if (enemy != null) _ai = enemy.GetComponent<ChaseAiInputSource>();
            _playerRenderers = player != null ? player.GetComponentsInChildren<SpriteRenderer>() : new SpriteRenderer[0];
            _playerColors = new Color[_playerRenderers.Length];
            for (int i = 0; i < _playerRenderers.Length; i++) _playerColors[i] = _playerRenderers[i].color;
            _propulsionView = new BattlePropulsionView(player, enemy);
        }

        void FixedUpdate()
        {
            if (player == null || enemy == null || !player.IsInitialized || !enemy.IsInitialized ||
                !player.isActiveAndEnabled || !enemy.isActiveAndEnabled || !Status.IsPlayerAlive) return;

            bool hit;
            if (_playerHitbox != null && _enemyHitbox != null && _playerHitbox.IsActive && _enemyHitbox.IsActive)
                hit = _model.StepContact(player.PreviousState, player.State, enemy.PreviousState, enemy.State,
                    Time.fixedDeltaTime, ContactRadius, _ai != null ? _ai.CollisionDamage : 1);
            else
                hit = _model.Step(player.PreviousState, player.State, enemy.PreviousState, enemy.State, Time.fixedDeltaTime);
            if (hit)
            {
                _lastHitTime = Time.time;
                if (_playerView != null) _playerView.PlayHitShake();
            }
            if (!Status.IsPlayerAlive) player.enabled = false;
        }

        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) RestartBattle();
            var status = Status;
            bool blinkDim = status.InvulnerabilityRemaining > 0f && ((int)(Time.time * 12f) & 1) == 0;
            for (int i = 0; i < _playerRenderers.Length; i++)
            {
                if (_playerRenderers[i] == null) continue;
                Color color = _playerColors[i];
                if (!status.IsPlayerAlive) color = new Color(0.4f, 0.4f, 0.4f, 0.65f);
                else if (Time.time - _lastHitTime < 0.15f) color = new Color(1f, 0.35f, 0.35f, 1f);
                else if (blinkDim) color.a *= 0.3f;
                _playerRenderers[i].color = color;
            }
        }

        public void RestartBattle() => SceneManager.LoadScene(gameObject.scene.name, LoadSceneMode.Single);

        void LateUpdate() => _propulsionView?.Update(Status.IsPlayerAlive, _ai);

        void OnDestroy() => _propulsionView?.Dispose();

        void OnGUI()
        {
            if (_model == null) return;
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold };
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
                _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            }
            float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var status = Status;
            GUI.Box(new Rect(18f, 18f, 410f, 330f), GUIContent.none);
            GUI.Label(new Rect(34f, 28f, 320f, 34f), $"HULL  {status.PlayerHealth} / {status.MaxPlayerHealth}", _titleStyle);
            for (int i = 0; i < status.MaxPlayerHealth; i++)
            {
                GUI.color = i < status.PlayerHealth ? new Color(0.25f, 0.95f, 0.68f) : new Color(0.25f, 0.27f, 0.3f);
                GUI.DrawTexture(new Rect(36f + i * 51f, 68f, 43f, 12f), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            int level = _ai != null ? _ai.Level : 1;
            string skill = level == 1 ? "TRACK" : level == 2 ? "INTERCEPT" : level == 3 ? "SIDE CUT" : "ADAPT";
            int collisionDamage = _ai != null ? _ai.CollisionDamage : 1;
            GUI.Label(new Rect(34f, 88f, 365f, 28f), $"AI  LV{level}   {skill}   HIT -{collisionDamage} HULL", _labelStyle);
            var phase = _ai != null ? _ai.Phase : DogfightThreatAI.Core.AI.AttackRunPhase.NoTarget;
            bool committedAim = phase == DogfightThreatAI.Core.AI.AttackRunPhase.Windup && _ai != null && _ai.IsAimLocked;
            GUI.color = committedAim ? new Color(1f, .65f, .2f) : AttackCueView.PhaseColor(phase);
            string timer = _ai != null && _ai.PhaseRemaining > 0f ? $"  {_ai.PhaseRemaining:0.00}s" : "";
            GUI.Label(new Rect(34f, 117f, 365f, 26f), (committedAim ? "!  LOCKED - DODGE" : PhaseLabel(phase)) + timer, _labelStyle);
            GUI.color = Color.white;
            string protection = !status.IsPlayerAlive ? "DESTROYED  -  R TO RESTART" :
                status.InvulnerabilityRemaining > 0f ? $"INVULNERABLE  {status.InvulnerabilityRemaining:0.0}s" :
                "W / S: thrust / brake    A / D: turn";
            GUI.Label(new Rect(34f, 148f, 365f, 26f), protection, _smallStyle);
            GUI.Label(new Rect(34f, 178f, 365f, 24f), $"SURVIVED  {status.ElapsedTime:0.0}s    R: restart", _smallStyle);
            var boost = player != null ? player.GetComponent<KeyboardInputSource>() : null;
            if (boost != null)
            {
                string boostState = !status.IsPlayerAlive ? "OFFLINE" : boost.IsOverheated ? $"OVERHEAT  {boost.OverheatRemaining:0.0}s" : boost.IsDashing ? "DASH" :
                    boost.IsAccelerating ? "ACCELERATING" : boost.BoostRequiresRelease ? "RELEASE SHIFT" :
                    boost.IsBoostCoasting ? "COASTING" : boost.IsBoostCoolingDown ? "COOLING" :
                    boost.BoostEnergy01 >= .999f ? "READY" : "RECHARGING";
                GUI.Label(new Rect(34f, 207f, 380f, 24f), $"ENGINE   {boostState}", _smallStyle);
                GUI.color = new Color(.15f, .2f, .25f);
                GUI.DrawTexture(new Rect(36f, 237f, 374f, 12f), Texture2D.whiteTexture);
                GUI.color = !status.IsPlayerAlive ? new Color(.3f, .32f, .35f) :
                    boost.IsOverheated ? new Color(1f, .4f, .15f) : boost.Energy < 25f ? new Color(1f, .72f, .2f) :
                    boost.IsBoosting ? new Color(.65f, .95f, 1f) : new Color(.2f, .65f, 1f);
                GUI.DrawTexture(new Rect(36f, 237f, 374f * boost.BoostEnergy01, 12f), Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, .8f);
                GUI.DrawTexture(new Rect(36f + 374f * (25f / 60f), 235f, 2f, 16f), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(34f, 253f, 380f, 22f), $"ENERGY  {boost.Energy:0} / 60     DASH COST: 25", _smallStyle);
                string dashState = !status.IsPlayerAlive ? "OFFLINE" : boost.IsOverheated ? "OVERHEATED" :
                    boost.DashCooldownRemaining > .001f ? $"CD  {boost.DashCooldownRemaining:0.0}s" :
                    boost.CanDash ? "READY" : boost.Energy < 25f ? "LOW ENERGY" : "RELEASE KEY";
                GUI.Label(new Rect(34f, 278f, 380f, 24f), $"SPACE: QUICK DASH   {dashState}", _smallStyle);
                GUI.Label(new Rect(34f, 307f, 380f, 22f), "SHIFT: hold to accelerate    S: brake", _smallStyle);
            }
            if (_ai != null)
            {
                GUI.Box(new Rect(18f, 356f, 410f, 95f), GUIContent.none);
                string selection = _ai.DifficultyOverride == 0 ? "AUTO: 20 / 40 / 60s" :
                    _ai.DifficultyOverride != level ? $"LV{_ai.DifficultyOverride} NEXT ATTACK" : "FIXED DIFFICULTY";
                GUI.Label(new Rect(34f, 362f, 380f, 22f), status.IsPlayerAlive ? selection + "    F1-F5" : "R: RESTART, THEN SELECT", _smallStyle);
                bool enabledBefore = GUI.enabled;
                GUI.enabled = status.IsPlayerAlive;
                for (int choice = 0; choice <= 4; choice++)
                {
                    GUI.color = _ai.DifficultyOverride == choice ? new Color(.55f, .85f, 1f) : Color.white;
                    if (GUI.Button(new Rect(36f + choice * 74f, 390f, 68f, 24f), choice == 0 ? "AUTO" : "LV" + choice))
                        _ai.SetDifficulty(choice);
                }
                GUI.enabled = enabledBefore;
                GUI.color = Color.white;
                string pursuit = _ai.IsPursuitBoosting ? "BOOSTING" : _ai.IsBoostCoasting ? "BOOST EXIT" :
                    _ai.BoostCooldownRemaining > .001f ? $"CD {_ai.BoostCooldownRemaining:0.0}s" : _ai.BoostEnergy >= 30f ? "READY" : "RECHARGING";
                GUI.Label(new Rect(34f, 421f, 380f, 22f), $"ENEMY BOOST  {_ai.BoostEnergy:0}/60   {pursuit}", _smallStyle);
                _propulsionView?.DrawEnemyIndicator(_ai, scale, _smallStyle, status.IsPlayerAlive);
            }
            if (!status.IsPlayerAlive)
            {
                float width = Screen.width / scale;
                float height = Screen.height / scale;
                GUI.Box(new Rect(width * 0.5f - 170f, height * 0.5f - 55f, 340f, 110f), GUIContent.none);
                GUI.Label(new Rect(width * 0.5f - 135f, height * 0.5f - 42f, 300f, 38f), "AIRCRAFT DESTROYED", _titleStyle);
                if (GUI.Button(new Rect(width * 0.5f - 110f, height * 0.5f + 4f, 220f, 34f), "R  -  RESTART")) RestartBattle();
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        static string PhaseLabel(DogfightThreatAI.Core.AI.AttackRunPhase phase)
        {
            switch (phase)
            {
                case DogfightThreatAI.Core.AI.AttackRunPhase.Pursuit: return "PURSUIT";
                case DogfightThreatAI.Core.AI.AttackRunPhase.Windup: return "!  AIMING - TRACKING TARGET";
                case DogfightThreatAI.Core.AI.AttackRunPhase.Charge: return "CHARGE";
                case DogfightThreatAI.Core.AI.AttackRunPhase.Recover: return "...  RECOVERING";
                default: return "NO TARGET";
            }
        }

    }
}
