using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DogfightThreatAI.UI.Navigation;

namespace DogfightThreatAI.Editor
{
    /// <summary>让编辑器和独立包都从开始界面进入游戏，避免播放 Untitled 空场景。</summary>
    [InitializeOnLoad]
    public static class GameEntrySetup
    {
        const string MenuPath = SceneNavigation.StartMenuScenePath;

        static GameEntrySetup()
        {
            EditorApplication.delayCall += ConfigurePlayEntry;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) ConfigurePlayEntry();
            };
        }

        /// <summary>AI作者工具只为本次试玩选择游戏场景；退出Play后恢复正常菜单入口。</summary>
        public static void ConfigureGamePreview()
        {
            EditorSceneManager.playModeStartScene = Application.isBatchMode ? null :
                AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneNavigation.GameScenePath);
        }

        public static void ConfigurePlayEntry()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // Test Runner 和批处理工具自行安排场景，不能把它们的入口覆盖成菜单。
            if (Application.isBatchMode)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath);
            if (menu != null) EditorSceneManager.playModeStartScene = menu;
        }

        [MenuItem("Tools/Dogfight Threat AI/打开完整游戏入口")]
        public static void OpenMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            ConfigurePlayEntry();
            if (SceneManager.GetActiveScene().path != MenuPath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            }
            EditorApplication.ExecuteMenuItem("Window/General/Game");
        }
    }
}
