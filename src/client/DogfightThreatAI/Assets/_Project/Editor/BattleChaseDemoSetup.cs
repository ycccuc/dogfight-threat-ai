using System;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.Gameplay.View;
using DogfightThreatAI.UI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DogfightThreatAI.Editor
{
    /// <summary>用 Unity 序列化 API 维护演示 prefab 和场景引用，避免手写场景 YAML。</summary>
    public static class BattleChaseDemoSetup
    {
        const string ScenePath = SceneNavigation.GameScenePath;
        const string PlayerPrefabPath = "Assets/_Project/Prefabs/Aircraft/Player_Ship01.prefab";
        const string EnemyPrefabPath = "Assets/_Project/Prefabs/Aircraft/Enemy_Chaser01.prefab";
        const string EnemyAircraftPath = "Assets/_Project/Settings/Aircraft/Enemy_Attacker.asset";
        const string EnemyName = "Enemy_Chaser01";

        [MenuItem("Tools/Dogfight Threat AI/Setup Battle Chase Demo")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before editing the Battle demo.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var keyboardSources = Object.FindObjectsOfType<KeyboardInputSource>();
            if (keyboardSources.Length != 1)
                throw new InvalidOperationException("Battle must contain exactly one keyboard-controlled player.");
            var player = keyboardSources[0].GetComponent<PlayerController>();
            if (player == null) throw new InvalidOperationException("The player controller is missing.");
            // 物理边界不变；镜头多留出机身/预警走廊的边距，避免墙角玩家永远不满足可见起手条件。
            var cameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
            if (cameraFollow != null)
            {
                var cameraSettings = new SerializedObject(cameraFollow);
                cameraSettings.FindProperty("worldHalfSize").vector2Value = new Vector2(201.2f, 151.2f);
                cameraSettings.ApplyModifiedPropertiesWithoutUndo();
            }

            UpdateAircraftHitboxPrefab(PlayerPrefabPath);
            // 独立 prefab 保留原玩家 prefab，不在资产中保存场景 Target。
            if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath) == null) CreateEnemyPrefab();
            var enemyAircraft = UpdateEnemyAircraft(player.Ship);
            UpdateEnemyPrefab(enemyAircraft);
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
            GameObject enemy = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == EnemyName) { enemy = root; break; }
            if (enemy == null) enemy = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, scene);

            enemy.name = EnemyName;
            enemy.transform.position = player.transform.position + new Vector3(4f, -2.5f, 0f);
            var enemyController = enemy.GetComponent<PlayerController>();
            var controllerSettings = new SerializedObject(enemyController);
            controllerSettings.FindProperty("ship").objectReferenceValue = enemyAircraft;
            controllerSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemyController);

            var combat = Object.FindObjectOfType<BattleCombatController>();
            if (combat == null) combat = new GameObject("BattleCombat").AddComponent<BattleCombatController>();
            var combatSettings = new SerializedObject(combat);
            combatSettings.FindProperty("player").objectReferenceValue = player;
            combatSettings.FindProperty("enemy").objectReferenceValue = enemyController;
            combatSettings.FindProperty("maxHealth").intValue = 5;
            combatSettings.FindProperty("invulnerabilitySeconds").floatValue = .7f;
            combatSettings.ApplyModifiedPropertiesWithoutUndo();
            EnsureAircraftHitbox(player.gameObject);
            EnsureAircraftHitbox(enemy);

            var adapter = enemy.GetComponent<ChaseAiInputSource>();
            if (adapter == null) throw new InvalidOperationException("The enemy AI adapter is missing.");
            var serialized = new SerializedObject(adapter);
            serialized.FindProperty("self").objectReferenceValue = enemyController;
            serialized.FindProperty("target").objectReferenceValue = player;
            serialized.FindProperty("combat").objectReferenceValue = combat;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(adapter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(enemy.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Battle.");
            AssetDatabase.SaveAssets();
            Debug.Log("BATTLE_CHASE_SETUP_OK: saved contact-attack enemy, combat authority and Battle references.");
        }

        static AircraftData UpdateEnemyAircraft(AircraftData playerAircraft)
        {
            var aircraft = AssetDatabase.LoadAssetAtPath<AircraftData>(EnemyAircraftPath);
            if (aircraft == null)
            {
                aircraft = Object.Instantiate(playerAircraft);
                aircraft.name = "Enemy_Attacker";
                AssetDatabase.CreateAsset(aircraft, EnemyAircraftPath);
            }
            var settings = new SerializedObject(aircraft);
            settings.FindProperty("displayName").stringValue = "Enemy Attacker";
            settings.FindProperty("description").stringValue = "Direct pursuit contact demo: tracking aim at normal speed, five-times-speed charge.";
            settings.FindProperty("maxSpeed").floatValue = 9f;
            settings.FindProperty("turnRateDegPerSec").floatValue = 210f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(aircraft);
            return aircraft;
        }

        static void UpdateEnemyPrefab(AircraftData aircraft)
        {
            var root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
            try
            {
                var settings = new SerializedObject(root.GetComponent<PlayerController>());
                settings.FindProperty("ship").objectReferenceValue = aircraft;
                settings.FindProperty("arenaHalfSize").vector2Value = new Vector2(200f, 150f);
                settings.ApplyModifiedPropertiesWithoutUndo();
                var aiSettings = new SerializedObject(root.GetComponent<ChaseAiInputSource>());
                aiSettings.FindProperty("target").objectReferenceValue = null;
                aiSettings.FindProperty("combat").objectReferenceValue = null;
                aiSettings.ApplyModifiedPropertiesWithoutUndo();
                if (root.GetComponent<AttackCueView>() == null) root.AddComponent<AttackCueView>();
                EnsureAircraftHitbox(root);
                PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Could not update enemy combat prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/Dogfight Threat AI/Apply Collision Upgrade")]
        public static void ApplyCollisionUpgrade()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before upgrading Battle contact volumes.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            UpdateAircraftHitboxPrefab(PlayerPrefabPath);
            UpdateAircraftHitboxPrefab(EnemyPrefabPath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var combat = Object.FindObjectOfType<BattleCombatController>();
            if (combat == null || combat.Player == null || combat.Enemy == null)
                throw new InvalidOperationException("The existing Battle combat references are missing; run the initial demo setup first.");
            Vector3 playerPosition = combat.Player.transform.position;
            Vector3 enemyPosition = combat.Enemy.transform.position;
            EnsureAircraftHitbox(combat.Player.gameObject);
            EnsureAircraftHitbox(combat.Enemy.gameObject);
            var settings = new SerializedObject(combat);
            settings.FindProperty("invulnerabilitySeconds").floatValue = .7f;
            settings.ApplyModifiedPropertiesWithoutUndo();

            if (combat.Player.transform.position != playerPosition || combat.Enemy.transform.position != enemyPosition)
                throw new InvalidOperationException("The collision upgrade must preserve both aircraft spawn positions.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save upgraded Battle contact volumes.");
            AssetDatabase.SaveAssets();
            Debug.Log("BATTLE_COLLISION_UPGRADE_OK: saved two root contact volumes and 0.7-second protection; spawns preserved.");
        }

        static void UpdateAircraftHitboxPrefab(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                throw new InvalidOperationException("The aircraft prefab is missing: " + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsureAircraftHitbox(root);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success) throw new InvalidOperationException("Could not save contact volume prefab: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void EnsureAircraftHitbox(GameObject aircraft)
        {
            var hitbox = aircraft.GetComponent<AircraftHitbox>();
            if (hitbox == null) hitbox = aircraft.AddComponent<AircraftHitbox>();
            var circle = hitbox.Collider;
            circle.offset = Vector2.zero;
            circle.radius = .5f;
            circle.isTrigger = true;
            circle.enabled = true;
            hitbox.enabled = true;
            var body = aircraft.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.useFullKinematicContacts = true;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                EditorUtility.SetDirty(body);
                if (PrefabUtility.IsPartOfPrefabInstance(body))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(body);
            }
            EditorUtility.SetDirty(hitbox);
            EditorUtility.SetDirty(circle);
            if (PrefabUtility.IsPartOfPrefabInstance(hitbox)) PrefabUtility.RecordPrefabInstancePropertyModifications(hitbox);
            if (PrefabUtility.IsPartOfPrefabInstance(circle)) PrefabUtility.RecordPrefabInstancePropertyModifications(circle);
        }

        static void CreateEnemyPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                root.name = EnemyName;
                Object.DestroyImmediate(root.GetComponent<KeyboardInputSource>());
                var controller = root.GetComponent<PlayerController>();
                var adapter = root.AddComponent<ChaseAiInputSource>();
                var aiSettings = new SerializedObject(adapter);
                aiSettings.FindProperty("self").objectReferenceValue = controller;
                aiSettings.FindProperty("target").objectReferenceValue = null;
                aiSettings.ApplyModifiedPropertiesWithoutUndo();

                var settings = new SerializedObject(controller);
                settings.FindProperty("inputSource").objectReferenceValue = adapter;
                settings.FindProperty("arenaHalfSize").vector2Value = new Vector2(200f, 150f);
                settings.ApplyModifiedPropertiesWithoutUndo();
                foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>())
                    renderer.color = new Color(1f, 0.25f, 0.2f, 1f);

                PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath, out bool success);
                if (!success) throw new InvalidOperationException("Could not save the enemy prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Tools/Dogfight Threat AI/Open Battle and Play")]
        public static void OpenBattleAndPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.delayCall += () =>
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null)
                {
                    var gameView = EditorWindow.GetWindow(gameViewType);
                    gameView.maximized = true;
                    gameView.Focus();
                }
                GameEntrySetup.ConfigureGamePreview();
                EditorApplication.isPlaying = true;
            };
        }
    }
}
