using System;
using DogfightThreatAI.UI.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Battle
{
    /// <summary>场景内暂停、难度、退出和结算界面。通过事件连接 Gameplay，不读取 AI。</summary>
    public sealed class BattleMenuView : MonoBehaviour
    {
        const string DifficultyKey = "Dogfight.GunnerDifficulty";
        MenuUi _ui;
        GameObject _overlay;
        RectTransform _page;
        CanvasGroup _group;
        string[] _names = Array.Empty<string>(), _descriptions = Array.Empty<string>();
        Action _return;
        float _previousScale = 1;
        string _screen;
        bool _ended, _victory, _reduceMotion;
        public bool IsOpen => _overlay != null && _overlay.activeSelf;
        public int SelectedDifficulty { get; private set; }
        public event Action<int> DifficultySelected;
        public event Action RestartRequested;

        public void Build(Action returnToMenu)
        {
            _return = returnToMenu; _ui = new MenuUi();
            _reduceMotion = MenuPreferences.Load().ReduceMotion;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 60;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
                new GameObject("Battle EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);
            _overlay = MenuUi.Rect("Pause overlay", transform, 0, 0, 0, 0).gameObject;
            MenuUi.Fill((RectTransform)_overlay.transform);
            _group = _overlay.AddComponent<CanvasGroup>();
            var dimmer = MenuUi.Image("Dimmer", _overlay.transform, new Color32(9, 15, 16, 235), 0, 0, 0, 0, true);
            MenuUi.Fill(dimmer.rectTransform);
            var shade = MenuUi.Rect("Shade", _overlay.transform, 0, 0, 0, 0).gameObject.AddComponent<MenuShade>();
            MenuUi.Fill(shade.rectTransform); shade.raycastTarget = false;
            _page = MenuUi.Rect("Menu content", _overlay.transform, 0, 0, 1250, 880);
            _page.anchorMin = _page.anchorMax = _page.pivot = new Vector2(.5f, .5f);
            _page.anchoredPosition = Vector2.zero;
            _overlay.SetActive(false);
        }

        public void ConfigureDifficulties(string[] names, string[] descriptions)
        {
            _names = names; _descriptions = descriptions;
            SelectedDifficulty = Mathf.Clamp(PlayerPrefs.GetInt(DifficultyKey, 1), 0, Mathf.Max(0, names.Length - 1));
        }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (!IsOpen) OpenPause();
                else if (_screen != "pause") OpenPause();
                else if (!_ended) Resume();
            }
            if (IsOpen)
            {
                _group.alpha = _reduceMotion ? 1 : Mathf.MoveTowards(_group.alpha, 1, Time.unscaledDeltaTime * 6);
                if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null) FocusFirst();
            }
        }
        void LateUpdate()
        {
            if (_page == null) return;
            var root = (RectTransform)transform;
            _page.localScale = Vector3.one * Mathf.Min(1, Mathf.Min((root.rect.height - 90) / 880, (root.rect.width - 100) / 1250));
        }
        void Page(string screen, string eyebrow, string title, string subtitle)
        {
            if (!IsOpen) { _previousScale = Time.timeScale; Time.timeScale = 0; _overlay.SetActive(true); _group.alpha = 0; }
            _screen = screen;
            for (int i = _page.childCount - 1; i >= 0; i--)
            { var child = _page.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
            _ui.Text("Eyebrow", _page, eyebrow, 22, 34, 38, 1100, 42, MenuUi.Muted);
            _ui.Text("Title", _page, title, 76, 28, 95, 1120, 116);
            _ui.Text("Subtitle", _page, subtitle, 23, 36, 230, 1130, 78, MenuUi.Muted);
            MenuUi.Image("Divider", _page, new Color(1, 1, 1, .16f), 36, 322, 1160, 1);
        }
        Button Item(string name, string label, float y, Action action)
            => _ui.Button(name, _page, label, 28, 0, y, 640, 66, () => action());
        void FocusFirst()
        {
            var buttons = _page.GetComponentsInChildren<Button>();
            MenuUi.Navigation(buttons);
            if (buttons.Length > 0) EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
        }
        public void OpenPause()
        {
            Page("pause", "DOGFIGHT  /  THREAT AI", _ended ? (_victory ? "空域已清除" : "飞行结束") : "暂停",
                _ended ? "整理航向，再次起飞。" : "喘息片刻，然后继续飞行。");
            if (!_ended) Item("Resume", "继续游戏", 363, Resume);
            if (!_ended && _names.Length > 0) Item("Difficulty", "难度  /  " + _names[SelectedDifficulty], 445, OpenDifficulty);
            Item("Restart", "重新开始", 527, () => { ResumeForNavigation(); RestartRequested?.Invoke(); });
            Item("ReturnToMainMenu", "返回主菜单", 609, () => { ResumeForNavigation(); _return?.Invoke(); });
            Item("Quit", "退出游戏", 691, OpenQuit);
            if (!_ended) _ui.Text("Controls", _page, "W / S  推进与刹车    A / D  转向\nJ / 鼠标左键  射击    Shift  加速    Space  短冲", 21, 718, 380, 490, 190, MenuUi.Muted);
            FocusFirst();
        }
        public void OpenDifficulty()
        {
            Page("difficulty", "DOGFIGHT  /  OPTIONS", "难度", "改变敌人的反应与预判。每次命中均扣除一格生命。");
            for (int i = 0; i < _names.Length; i++)
            {
                int index = i;
                Item("Difficulty" + i, (i == SelectedDifficulty ? "●  " : "     ") + _names[i], 353 + i * 82, () => SelectDifficulty(index));
                _ui.Text("Description" + i, _page, _descriptions[i], 21, 680, 353 + i * 82, 520, 66, MenuUi.Muted);
            }
            Item("Back", "返回", 748, OpenPause); FocusFirst();
        }
        public void SelectDifficulty(int index)
        {
            if (index < 0 || index >= _names.Length) throw new ArgumentOutOfRangeException(nameof(index));
            SelectedDifficulty = index; PlayerPrefs.SetInt(DifficultyKey, index); PlayerPrefs.Save();
            DifficultySelected?.Invoke(index); OpenDifficulty();
            foreach (var button in _page.GetComponentsInChildren<Button>())
                if (button.name == "Difficulty" + index) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
        public void OpenQuit()
        {
            Page("quit", "DOGFIGHT  /  LEAVE", "退出游戏？", "当前对局不会保存。");
            Item("CancelQuit", "继续停留", 445, OpenPause);
            Item("ConfirmQuit", "退出游戏", 609, () =>
            {
                ResumeForNavigation();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            FocusFirst();
        }
        public void ShowResult(bool victory) { _ended = true; _victory = victory; OpenPause(); }
        public void Resume()
        {
            if (_ended || !IsOpen) return;
            ResumeForNavigation();
        }
        void ResumeForNavigation()
        {
            if (!IsOpen) return;
            _overlay.SetActive(false); Time.timeScale = _previousScale;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
        void OnDestroy() { if (IsOpen) Time.timeScale = 1; }
    }
}
