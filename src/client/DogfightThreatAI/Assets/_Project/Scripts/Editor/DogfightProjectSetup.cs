using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dogfight.EditorTools
{
    /// <summary>
    /// 项目一键初始化。
    ///
    /// 为什么用脚本而不是手点：
    ///   排序层、2D 重力、Build Settings 这些是"新同学 clone 下来必须正确"的东西，
    ///   手点容易漏、也没法 review。写成菜单项之后，配置本身就是代码，能进 git、能复核。
    ///
    /// 用法：菜单 Dogfight → 项目初始化。
    /// 幂等：重复执行不会重复加排序层。
    /// </summary>
    public static class DogfightProjectSetup
    {
        /// <summary>
        /// 排序层。顺序 = 渲染先后（靠前的先画，也就是在更底层）。
        /// 与 docs/02-design/美术规范.md 必须一致。
        /// </summary>
        public static readonly string[] SortingLayers =
        {
            "Background",
            "Far",
            "Mid",
            "Near",
            "Ground",
            "Units",
            "Projectiles",
            "VFX",
            "UI",
        };

        const string BattleScenePath = "Assets/_Project/Scenes/Battle.unity";

        [MenuItem("Dogfight/项目初始化（排序层 + 2D 重力 + Build Settings）", priority = 0)]
        public static void InitializeProject()
        {
            int addedLayers = EnsureSortingLayers();
            bool gravityChanged = EnsureTopDownGravity();
            bool buildSettingsChanged = EnsureBattleSceneInBuildSettings();

            AssetDatabase.SaveAssets();

            Debug.Log(
                "[项目初始化] 排序层新增 " + addedLayers + " 个" +
                " · 2D 重力归零 " + (gravityChanged ? "已执行" : "无需改动") +
                " · Build Settings " + (buildSettingsChanged ? "已更新" : "无需改动"));
        }

        /// <summary>确保 9 个排序层都存在。返回新增数量。</summary>
        public static int EnsureSortingLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[项目初始化] 读不到 TagManager.asset，无法创建排序层。");
                return 0;
            }

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("m_SortingLayers");
            if (layers == null)
            {
                Debug.LogError("[项目初始化] TagManager 里没有 m_SortingLayers 字段。");
                return 0;
            }

            var existing = new HashSet<string>();
            for (int i = 0; i < layers.arraySize; i++)
            {
                existing.Add(layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
            }

            int added = 0;
            for (int i = 0; i < SortingLayers.Length; i++)
            {
                string name = SortingLayers[i];
                if (existing.Contains(name)) continue;

                layers.InsertArrayElementAtIndex(layers.arraySize);
                SerializedProperty element = layers.GetArrayElementAtIndex(layers.arraySize - 1);
                element.FindPropertyRelative("name").stringValue = name;
                element.FindPropertyRelative("uniqueID").intValue = 0;
                element.FindPropertyRelative("locked").boolValue = false;
                added++;
            }

            if (added > 0) tagManager.ApplyModifiedProperties();
            return added;
        }

        /// <summary>
        /// 俯视空战没有"下"，全局 2D 重力归零。
        /// 虽然 PlaneAgent 会给每架飞机设 Gravity Scale = 0，
        /// 但把全局值也改掉，能消灭"新加的物体忘了设就往下掉"这一整类 bug。
        /// </summary>
        public static bool EnsureTopDownGravity()
        {
            if (Physics2D.gravity == Vector2.zero) return false;
            Physics2D.gravity = Vector2.zero;
            return true;
        }

        /// <summary>
        /// 把 Battle 场景加进 Build Settings，并移除已经不存在的老场景。
        /// 之前的 Build Settings 指向已被删除的 SampleScene，打包会直接失败。
        /// </summary>
        public static bool EnsureBattleSceneInBuildSettings()
        {
            var desired = new List<EditorBuildSettingsScene>();
            bool battleFound = false;

            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            bool changed = false;

            for (int i = 0; i < current.Length; i++)
            {
                if (!System.IO.File.Exists(current[i].path))
                {
                    changed = true; // 丢弃已删除的场景
                    continue;
                }
                if (current[i].path == BattleScenePath) battleFound = true;
                desired.Add(current[i]);
            }

            if (!battleFound && System.IO.File.Exists(BattleScenePath))
            {
                desired.Insert(0, new EditorBuildSettingsScene(BattleScenePath, true));
                changed = true;
            }

            if (!changed) return false;

            EditorBuildSettings.scenes = desired.ToArray();
            return true;
        }

        [MenuItem("Dogfight/检查项目配置", priority = 1)]
        public static void CheckProjectSettings()
        {
            var problems = new List<string>();

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets != null && assets.Length > 0)
            {
                var tagManager = new SerializedObject(assets[0]);
                SerializedProperty layers = tagManager.FindProperty("m_SortingLayers");
                var existing = new HashSet<string>();
                for (int i = 0; i < layers.arraySize; i++)
                {
                    existing.Add(layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
                }
                for (int i = 0; i < SortingLayers.Length; i++)
                {
                    if (!existing.Contains(SortingLayers[i])) problems.Add("缺排序层: " + SortingLayers[i]);
                }
            }

            if (Physics2D.gravity != Vector2.zero) problems.Add("2D 重力不是 0（当前 " + Physics2D.gravity + "）");

            bool battleInBuild = false;
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (!System.IO.File.Exists(scenes[i].path)) problems.Add("Build Settings 指向已删除的场景: " + scenes[i].path);
                if (scenes[i].path == BattleScenePath) battleInBuild = true;
            }
            if (!battleInBuild) problems.Add("Build Settings 里没有 " + BattleScenePath);

            if (problems.Count == 0)
            {
                Debug.Log("[项目检查] 全部通过。");
            }
            else
            {
                for (int i = 0; i < problems.Count; i++) Debug.LogWarning("[项目检查] " + problems[i]);
                Debug.LogWarning("[项目检查] 共 " + problems.Count + " 项问题 → 运行 Dogfight/项目初始化 可修复大部分。");
            }
        }
    }
}
