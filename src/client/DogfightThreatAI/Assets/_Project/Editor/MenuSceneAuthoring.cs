using DogfightThreatAI.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DogfightThreatAI.Editor
{
    /// <summary>Uses Unity's own serializer to save the native Canvas and its references.</summary>
    public static class MenuSceneAuthoring
    {
        const string MenuPath = "Assets/_Project/Scenes/Menu/Menu.unity";

        [MenuItem("Tools/Dogfight Threat AI/打开并构建开始界面")]
        public static void BuildMenuScene()
        {
            if (EditorApplication.isPlaying) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.path != MenuPath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            }
            var controller = Object.FindObjectOfType<MainMenuController>();
            if (controller == null)
            {
                var root = new GameObject("Main Menu", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(root, "Create main menu");
                controller = root.AddComponent<MainMenuController>();
            }
            controller.BuildEditorLayout();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Native menu Canvas saved in " + MenuPath);
        }

        [MenuItem("Tools/Dogfight Threat AI/预览初始界面")]
        static void PreviewSplash() => Preview(true);

        [MenuItem("Tools/Dogfight Threat AI/预览主菜单")]
        static void PreviewMenu() => Preview(false);

        [MenuItem("Tools/Dogfight Threat AI/重建开始界面布局")]
        public static void RebuildMenuScene()
        {
            if (EditorApplication.isPlaying) return;
            if (SceneManager.GetActiveScene().path != MenuPath)
            {
                BuildMenuScene();
                if (SceneManager.GetActiveScene().path != MenuPath) return;
            }
            var controller = Object.FindObjectOfType<MainMenuController>();
            if (controller != null) Undo.DestroyObjectImmediate(controller.gameObject);
            BuildMenuScene();
        }

        static void Preview(bool showPrompt)
        {
            if (EditorApplication.isPlaying) return;
            var controller = Object.FindObjectOfType<MainMenuController>();
            if (controller == null) { BuildMenuScene(); controller = Object.FindObjectOfType<MainMenuController>(); }
            if (controller == null) return;
            controller.SetEditorPreview(showPrompt);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
        }
    }
}
