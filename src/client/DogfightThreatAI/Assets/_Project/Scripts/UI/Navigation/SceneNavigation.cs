using DogfightThreatAI.UI.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DogfightThreatAI.UI.Navigation
{
    /// <summary>
    /// 只负责场景导航，不保存飞机或地图状态，也不改动菜单/飞行的原有逻辑。
    /// 可把此组件挂到任意按钮所在物体，将 On Click 绑定到 StartGame 或 ReturnToMainMenu。
    /// </summary>
    public sealed class SceneNavigation : MonoBehaviour
    {
        // 必须与实际场景文件名一致，并在 Build Settings 中勾选这两个场景。
        public const string StartMenuScene = "StartMenu";
        public const string GameScene = "GameScene";
        public const string StartMenuScenePath = "Assets/_Project/Scenes/StartMenu/StartMenu.unity";
        public const string GameScenePath = "Assets/_Project/Scenes/GameScene/GameScene.unity";
        bool _loading;

        public static bool CanLoadGameScene => Application.CanStreamedLevelBeLoaded(GameScene);

        /// <summary>普通“开始游戏”按钮可以直接绑定此方法。</summary>
        public void StartGame()
        {
            if (_loading) return;
            _loading = LoadGameSceneAsync() != null;
        }

        /// <summary>游戏内“返回主菜单”按钮绑定此方法；以后暂停菜单也可复用。</summary>
        public void ReturnToMainMenu()
        {
            if (_loading) return;
            _loading = LoadScene(StartMenuScene) != null;
        }

        /// <summary>现有菜单在淡出动画结束后调用，保留原来的过渡和输入门控。</summary>
        public static AsyncOperation LoadGameSceneAsync() => LoadScene(GameScene);

        static AsyncOperation LoadScene(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"无法加载 {sceneName}：请将 StartMenu 和 GameScene 加入 Build Settings 场景列表并勾选启用。");
                return null;
            }
            // 返回时取消可能存在的暂停。Single 卸载上一场景，避免旧飞机、相机、菜单重复存在。
            Time.timeScale = 1f;
            return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // 兼容关闭 Domain Reload 的编辑器，避免重复注册同一回调。
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != GameScene || FindObjectOfType<SceneNavigation>() != null) return;
            // 导航 UI 属于当前游戏场景，返回时随场景一起卸载，不使用 DontDestroyOnLoad。
            var root = new GameObject("Game Navigation", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, scene);
            var navigation = root.AddComponent<SceneNavigation>();
            root.AddComponent<BattleMenuView>().Build(navigation.ReturnToMainMenu);
        }

    }
}
