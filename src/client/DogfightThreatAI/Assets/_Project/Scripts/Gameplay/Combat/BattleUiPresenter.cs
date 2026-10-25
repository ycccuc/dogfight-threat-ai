using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.UI.Battle;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>UI 与战斗之间的显示、命令接线；UI 不引用 Core 或 Gameplay。</summary>
    public sealed class BattleUiPresenter : MonoBehaviour
    {
        BattleCombatController _battle;
        BattleHudView _hud;
        BattleMenuView _menu;
        ProjectileView _projectiles;
        ChaseAiInputSource _ai;
        WeaponInputSource _input;
        bool _resultShown;
        public void Connect(BattleCombatController battle)
        {
            _battle = battle;
            var root = new GameObject("Battle HUD", typeof(RectTransform)); root.transform.SetParent(transform, false);
            _hud = root.AddComponent<BattleHudView>();
            _projectiles = gameObject.AddComponent<ProjectileView>();
            var spec = battle.Weapon;
            _projectiles.Build(2 * (Mathf.CeilToInt(spec.Lifetime / spec.ShotInterval) + 2));
            _ai = battle.Enemy.GetComponent<ChaseAiInputSource>();
            _input = battle.Player.GetComponent<WeaponInputSource>();
            _menu = FindObjectOfType<BattleMenuView>();
            if (_menu != null)
            {
                var data = battle.Difficulties;
                var names = new string[data.Length]; var descriptions = new string[data.Length];
                for (int i = 0; i < data.Length; i++) { names[i] = data[i].DisplayName; descriptions[i] = data[i].Description; }
                _menu.ConfigureDifficulties(names, descriptions);
                _menu.DifficultySelected += SetDifficulty;
                _menu.RestartRequested += battle.RestartBattle;
                SetDifficulty(_menu.SelectedDifficulty);
            }
        }
        void SetDifficulty(int index) => _ai.SetDifficulty(index + 1);
        void Update()
        {
            if (_battle == null) return;
            _input.Blocked = _menu != null && _menu.IsOpen;
            _hud.Present(_battle.Status.PlayerHealth, _battle.Simulation.PlayerCooldown01);
            if (!_resultShown && _battle.Simulation.IsFinished && _menu != null)
            { _resultShown = true; _menu.ShowResult(_battle.Status.IsPlayerAlive); }
        }
        void LateUpdate()
        {
            if (_battle == null) return;
            float alpha = Time.fixedDeltaTime > 0 ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1;
            _projectiles.Present(_battle.Simulation.Projectiles, alpha);
        }
        void OnDestroy()
        {
            if (_menu == null) return;
            _menu.DifficultySelected -= SetDifficulty;
            _menu.RestartRequested -= _battle.RestartBattle;
        }
    }
}
