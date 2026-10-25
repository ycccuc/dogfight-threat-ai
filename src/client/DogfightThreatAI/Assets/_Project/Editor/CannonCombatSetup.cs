using System;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DogfightThreatAI.EditorTools
{
    /// <summary>只创建初版内容资产与场景接线。运行时不生成或改写配置。</summary>
    public static class CannonCombatSetup
    {
        const string Root = "Assets/_Project/Settings";
        [MenuItem("Tools/Dogfight Threat AI/Apply Cannon Combat Setup")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Folder(Root + "/Weapons"); Folder(Root + "/AI");
            var weapon = LoadOrCreate<WeaponData>(Root + "/Weapons/Cannon.asset");
            var names = new[] { "新兵", "标准", "老兵", "王牌" };
            var descriptions = new[] { "反应较慢，适合熟悉飞行与射击。", "会预判航向，保持距离并寻找机会。", "更快地调整航线，射击更果断。", "反应敏捷，预判充分，需要灵活变向。" };
            var values = new[] { new[] { .32f, 0f, 10f, .4f, 8f, 160f },
                new[] { .18f, .5f, 7f, .22f, 10f, 220f }, new[] { .10f, .8f, 5f, .12f, 11f, 250f },
                new[] { .06f, 1f, 4f, .06f, 12f, 280f } };
            var profiles = new GunnerProfileData[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string path = Root + "/AI/Gunner" + (i + 1) + ".asset";
                bool exists = AssetDatabase.LoadAssetAtPath<GunnerProfileData>(path) != null;
                profiles[i] = LoadOrCreate<GunnerProfileData>(path);
                if (exists) continue; // 重复运行保留队员已调好的资产数值。
                var settings = new SerializedObject(profiles[i]);
                settings.FindProperty("displayName").stringValue = names[i];
                settings.FindProperty("description").stringValue = descriptions[i];
                settings.FindProperty("useAStar").boolValue = i > 0;
                settings.FindProperty("shotSpacing").floatValue = new[] { 1.35f, .85f, .6f, .46f }[i];
                var fields = new[] { "observationInterval", "prediction", "aimToleranceDegrees", "fireDelay", "cruiseSpeed", "turnRate" };
                for (int j = 0; j < fields.Length; j++) settings.FindProperty(fields[j]).floatValue = values[i][j];
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/GameScene/GameScene.unity", OpenSceneMode.Single);
            var combat = UnityEngine.Object.FindObjectOfType<BattleCombatController>();
            if (combat == null) throw new InvalidOperationException("GameScene has no battle authority.");
            var battle = new SerializedObject(combat);
            battle.FindProperty("useProjectileCombat").boolValue = true;
            battle.FindProperty("weapon").objectReferenceValue = weapon;
            var entries = battle.FindProperty("difficulties"); entries.arraySize = profiles.Length;
            for (int i = 0; i < profiles.Length; i++) entries.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];
            battle.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save GameScene.");
            AssetDatabase.SaveAssets();
            Debug.Log("CANNON_SETUP_OK: GameScene, common cannon, four difficulty assets saved.");
        }
        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); Folder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
