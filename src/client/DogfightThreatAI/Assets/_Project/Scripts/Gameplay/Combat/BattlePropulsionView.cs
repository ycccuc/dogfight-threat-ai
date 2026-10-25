using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Gameplay.InputSource;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>Read-only propulsion cues. Rendering never supplies input or changes flight/combat state.</summary>
    sealed class BattlePropulsionView : IDisposable
    {
        readonly PlayerController _player, _enemy;
        readonly KeyboardInputSource _keyboard;
        readonly LineRenderer _playerTrail, _enemyTrail, _enemyCore, _chargeWake;
        readonly Material _material;
        readonly PlayerView _playerView, _enemyView;

        public BattlePropulsionView(PlayerController player, PlayerController enemy)
        {
            _player = player;
            _enemy = enemy;
            _keyboard = player != null ? player.GetComponent<KeyboardInputSource>() : null;
            _playerView = player != null ? player.GetComponentInChildren<PlayerView>() : null;
            _enemyView = enemy != null ? enemy.GetComponentInChildren<PlayerView>() : null;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            _material = new Material(shader);
            _playerTrail = CreateTrail(player, "PlayerPropulsionTrail");
            _enemyTrail = CreateTrail(enemy, "EnemyPursuitTrail");
            _enemyCore = CreateTrail(enemy, "EnemyPursuitTrailCore");
            _chargeWake = CreateTrail(enemy, "EnemyChargeWake");
            if (_enemyTrail != null) { _enemyTrail.startWidth = .24f; _enemyTrail.endWidth = .008f; }
            if (_enemyCore != null) { _enemyCore.startWidth = .06f; _enemyCore.endWidth = .002f; }
            if (_chargeWake != null) { _chargeWake.startWidth = .12f; _chargeWake.endWidth = .003f; }
        }

        LineRenderer CreateTrail(PlayerController aircraft, string name)
        {
            if (aircraft == null) return null;
            var trail = new GameObject(name).AddComponent<LineRenderer>();
            trail.transform.SetParent(aircraft.transform, false);
            trail.useWorldSpace = true;
            trail.positionCount = 2;
            trail.sharedMaterial = _material;
            trail.numCapVertices = 0;
            trail.numCornerVertices = 0;
            trail.startWidth = .17f;
            trail.endWidth = .025f;
            var sprite = aircraft.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) { trail.sortingLayerID = sprite.sortingLayerID; trail.sortingOrder = sprite.sortingOrder - 1; }
            trail.enabled = false;
            return trail;
        }

        public void Update(bool alive, ChaseAiInputSource ai)
        {
            bool playerActive = alive && _keyboard != null && (_keyboard.IsBoosting || _keyboard.IsOverheated);
            Color playerColor = _keyboard != null && _keyboard.IsOverheated ? new Color(1f, .48f, .15f, .7f) : new Color(.55f, .95f, 1f, .85f);
            float playerLength = _keyboard != null && _keyboard.IsOverheated ? .65f : Mathf.Lerp(.8f, 2.5f, _player != null ? Mathf.InverseLerp(9f, 21.6f, _player.State.Speed) : 0f);
            SetTrail(_playerTrail, _player, _playerView, playerActive, playerLength, playerColor);
            bool enemyActive = alive && ai != null && (ai.IsPursuitBoosting || ai.IsBoostCoasting);
            float strength = ai != null ? Mathf.Lerp(.72f, 1f, ai.Aggression01) : 1f;
            float length = _enemy != null ? Mathf.Lerp(.8f, 2.1f, Mathf.InverseLerp(9f, 24f, _enemy.State.Speed)) : 1f;
            float alpha = ai != null && ai.IsBoostCoasting ? .20f : .46f;
            SetTrail(_enemyTrail, _enemy, _enemyView, enemyActive, length, new Color(.18f, .62f, 1f, alpha * strength));
            SetTrail(_enemyCore, _enemy, _enemyView, enemyActive, length * .73f, new Color(.76f, .95f, 1f, .95f * strength));
            // This is a rear wake, not another attack corridor or an impact marker.
            bool charging = alive && ai != null && ai.isActiveAndEnabled && ai.Phase == AttackRunPhase.Charge;
            SetTrail(_chargeWake, _enemy, _enemyView, charging, 2.1f, new Color(1f, .85f, .61f, .92f));
        }

        static Vector3 Position(PlayerController aircraft, PlayerView view)
            => view != null ? view.RenderPosition : new Vector3(aircraft.State.Position.X, aircraft.State.Position.Y, aircraft.transform.position.z);

        static void SetTrail(LineRenderer trail, PlayerController aircraft, PlayerView view, bool active, float length, Color color)
        {
            if (trail == null || aircraft == null) return;
            trail.enabled = active;
            if (!active) return;
            Vector3 position = Position(aircraft, view);
            var heading = aircraft.State.Forward;
            Vector3 forward = new Vector3(heading.X, heading.Y);
            trail.SetPosition(0, position - forward * .45f);
            trail.SetPosition(1, position - forward * (.45f + length));
            trail.startColor = color;
            color.a = 0f;
            trail.endColor = color;
        }

        public void DrawEnemyIndicator(ChaseAiInputSource ai, float scale, GUIStyle style, bool alive)
        {
            if (!alive || _enemy == null || ai == null || Camera.main == null) return;
            Camera camera = Camera.main;
            Vector3 screen = camera.WorldToScreenPoint(Position(_enemy, _enemyView));
            float width = Screen.width / scale, height = Screen.height / scale;
            float x = screen.x / scale, y = (Screen.height - screen.y) / scale;
            Color previous = GUI.color;
            if (screen.z > 0f && x >= 0f && x <= width && y >= 0f && y <= height)
            {
                GUI.color = new Color(.08f, .12f, .16f, .9f);
                GUI.DrawTexture(new Rect(x - 24f, y - 31f, 48f, 5f), Texture2D.whiteTexture);
                GUI.color = ai.IsPursuitBoosting ? new Color(.7f, .96f, 1f) : new Color(.35f, .7f, 1f);
                GUI.DrawTexture(new Rect(x - 24f, y - 31f, 48f * Mathf.Clamp01(ai.BoostEnergy / 60f), 5f), Texture2D.whiteTexture);
            }
            else if (ai.Phase == DogfightThreatAI.Core.AI.AttackRunPhase.Pursuit)
            {
                Vector2 center = new Vector2(width * .5f, height * .5f);
                Vector2 delta = new Vector2(x, y) - center;
                if (screen.z <= 0f) delta = -delta;
                float tx = Mathf.Abs(delta.x) > .001f ? (width * .5f - 28f) / Mathf.Abs(delta.x) : float.PositiveInfinity;
                float ty = Mathf.Abs(delta.y) > .001f ? (height * .5f - 28f) / Mathf.Abs(delta.y) : float.PositiveInfinity;
                Vector2 point = center + delta * Mathf.Min(tx, ty);
                Matrix4x4 matrix = GUI.matrix;
                GUI.color = ai.IsPursuitBoosting ? new Color(.65f, .94f, 1f) : new Color(.75f, .8f, .85f);
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + 90f, point);
                GUI.Label(new Rect(point.x - 10f, point.y - 10f, 24f, 24f), "▲", style);
                GUI.matrix = matrix;
            }
            GUI.color = previous;
        }

        public void Dispose()
        {
            if (_playerTrail != null) UnityEngine.Object.Destroy(_playerTrail.gameObject);
            if (_enemyTrail != null) UnityEngine.Object.Destroy(_enemyTrail.gameObject);
            if (_enemyCore != null) UnityEngine.Object.Destroy(_enemyCore.gameObject);
            if (_chargeWake != null) UnityEngine.Object.Destroy(_chargeWake.gameObject);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
