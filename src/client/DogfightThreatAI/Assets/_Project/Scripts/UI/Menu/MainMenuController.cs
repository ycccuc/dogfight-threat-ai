using System.Collections;
using DogfightThreatAI.UI.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Menu
{
    /// <summary>
    /// Scene-local title/menu flow. Builds only in StartMenu; GameScene keeps its own gameplay scripts.
    /// All transitions use unscaled time and gate input until the transition finishes.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class MainMenuController : MonoBehaviour
    {
        public enum MenuPhase { AwaitInput, Revealing, Menu, Panel, Loading }
        public MenuPhase Phase { get; private set; }

        MenuUi _ui;
        [Header("Scene UI")]
        [SerializeField] CanvasGroup _prompt;
        [SerializeField] CanvasGroup _menu;
        [SerializeField] CanvasGroup _blackout;
        [SerializeField] RectTransform _content;
        [SerializeField] Text _description;
        [SerializeField] Text _footer;
        [SerializeField] Button _start;
        [SerializeField] Button _settings;
        [SerializeField] Button _controls;
        [SerializeField] Button _credits;
        [SerializeField] Button _exit;
        CanvasGroup _modal;
        Button _returnFocus;
        GameObject _panel;
        bool _reduceMotion;
        bool _panelBusy;
        int _readyFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            MenuPreferences.ApplySaved();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneNavigation.StartMenuScene || FindObjectOfType<MainMenuController>() != null) return;
            var root = new GameObject("Main Menu", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<MainMenuController>();
        }

        void Awake()
        {
            _ui = new MenuUi();
            _reduceMotion = MenuPreferences.Load().ReduceMotion;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if (_prompt == null) BuildCanvas();
            else BindSceneView();
            Phase = MenuPhase.AwaitInput;
            _readyFrame = Time.frameCount + 1;
        }

        void BindSceneView()
        {
            Wire(_start, StartBattle, "驾驶战机，进入当前的 GameScene 飞行场景。");
            Wire(_settings, OpenSettings, "调整声音、显示与界面动态效果。");
            Wire(_controls, OpenControls, "查看飞行操控与菜单快捷键。");
            Wire(_credits, OpenCredits, "关于这个项目与所使用的素材。");
            Wire(_exit, OpenQuit, "结束本次飞行，退出游戏。");
            MenuUi.Navigation(_start, _settings, _controls, _credits, _exit);
            _prompt.gameObject.SetActive(true);
            _prompt.alpha = 1;
            _menu.alpha = 0;
            _menu.interactable = _menu.blocksRaycasts = false;
            _blackout.alpha = 0;
            _blackout.blocksRaycasts = false;
        }

        void Wire(Button button, UnityEngine.Events.UnityAction action, string description)
        {
            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
            button.GetComponent<MenuItemFeedback>().Configure(
                button.GetComponentInChildren<Text>(), button.transform.Find("Focus").GetComponent<Graphic>(),
                () => _description.text = description);
        }

#if UNITY_EDITOR
        /// <summary>Author the same native uGUI hierarchy into StartMenu.unity, before entering Play mode.</summary>
        public void BuildEditorLayout()
        {
            if (Application.isPlaying) return;
            _ui = new MenuUi();
            if (_prompt == null) BuildCanvas();
            else BindSceneView();
            SetEditorPreview(false);
        }

        public void SetEditorPreview(bool showPrompt)
        {
            if (Application.isPlaying || _prompt == null) return;
            _prompt.gameObject.SetActive(true);
            _prompt.alpha = showPrompt ? 1 : 0;
            _menu.alpha = showPrompt ? 0 : 1;
            _menu.interactable = _menu.blocksRaycasts = false;
            _blackout.alpha = 0;
            _blackout.blocksRaycasts = false;
            _description.text = "驾驶战机，进入当前的 GameScene 飞行场景。";
            Canvas.ForceUpdateCanvases();
        }
#endif

        void BuildCanvas()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            gameObject.layer = LayerMask.NameToLayer("UI");
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("Menu Event System", typeof(EventSystem), typeof(StandaloneInputModule))
                    .transform.SetParent(transform, false);

            var art = MenuUi.Rect("Background", transform, 0, 0, 1920, 1080);
            art.anchorMin = art.anchorMax = art.pivot = new Vector2(.5f, .5f);
            art.anchoredPosition = Vector2.zero;
            var background = art.gameObject.AddComponent<RawImage>();
            background.texture = Resources.Load<Texture2D>("Menu/Background");
            background.raycastTarget = false;
            if (background.texture != null)
            {
                var fit = art.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = (float)background.texture.width / background.texture.height;
            }
            else
            {
                background.color = new Color32(16, 24, 27, 255);
                MenuUi.Fill(art);
                Debug.LogError("Menu background missing: Resources/Menu/Background", this);
            }

            // The supplied artwork is bright on the left. Shade only the text side, without modifying it.
            var shadeRect = MenuUi.Rect("Readability Shade", transform, 0, 0, 0, 0);
            MenuUi.Fill(shadeRect);
            var shade = shadeRect.gameObject.AddComponent<MenuShade>();
            shade.raycastTarget = false;

            _prompt = Group("Press Any Key", transform, 1);
            var prompt = _ui.Text("Prompt", _prompt.transform, "点击任意键进入游戏", 28, 0, 0, 1000, 70);
            var promptShadow = prompt.gameObject.AddComponent<Shadow>();
            promptShadow.effectColor = new Color(0, 0, 0, .9f);
            promptShadow.effectDistance = new Vector2(0, -2);
            prompt.alignment = TextAnchor.MiddleCenter;
            prompt.rectTransform.anchorMin = prompt.rectTransform.anchorMax = new Vector2(.5f, .14f);
            prompt.rectTransform.pivot = new Vector2(.5f, .5f);
            prompt.rectTransform.anchoredPosition = Vector2.zero;

            _menu = Group("Main Menu", transform, 0);
            _menu.interactable = _menu.blocksRaycasts = false;
            _content = MenuUi.Rect("Right Menu", _menu.transform, 0, 0, 620, 900);
            _content.anchorMin = _content.anchorMax = new Vector2(1, .5f);
            _content.pivot = new Vector2(1, .5f);
            _content.anchoredPosition = new Vector2(-78, 0);
            _ui.Text("Eyebrow", _content, "空 战 行 动", 19, 34, 60, 490, 36, MenuUi.Muted);
            _ui.Text("Title", _content, "DOGFIGHT", 144, 28, 85, 590, 210, display: true);
            _ui.Text("Subtitle", _content, "THREAT AI", 96, 32, 229, 570, 150, display: true);
            _ui.Text("Tagline", _content, "实时威胁评估 · 动态决策", 20, 36, 374, 550, 36, MenuUi.Muted);

            _start = Item("StartGame", "开始游戏", 478, StartBattle, "驾驶战机，进入当前的 GameScene 飞行场景。");
            _settings = Item("Settings", "设置", 544, OpenSettings, "调整声音、显示与界面动态效果。");
            _controls = Item("Controls", "操作说明", 610, OpenControls, "查看飞行操控与菜单快捷键。");
            _credits = Item("Credits", "制作名单", 676, OpenCredits, "关于这个项目与所使用的素材。");
            _exit = Item("Quit", "退出游戏", 742, OpenQuit, "结束本次飞行，退出游戏。");
            MenuUi.Navigation(_start, _settings, _controls, _credits, _exit);
            _description = _ui.Text("Description", _content, "", 18, 36, 828, 560, 38, MenuUi.Muted);
            _footer = _ui.Text("Navigation", _menu.transform, "↑ ↓ 选择     Enter 确认     Esc 返回", 17,
                0, 0, 760, 45, MenuUi.Muted);
            _footer.rectTransform.anchorMin = _footer.rectTransform.anchorMax = new Vector2(1, 0);
            _footer.rectTransform.pivot = Vector2.one;
            _footer.rectTransform.anchoredPosition = new Vector2(-98, 62);
            _footer.alignment = TextAnchor.MiddleRight;
            _blackout = Group("Scene Fade", transform, 0);
            var black = MenuUi.Image("Black", _blackout.transform, Color.black, 0, 0, 0, 0, true);
            MenuUi.Fill(black.rectTransform);
            _blackout.blocksRaycasts = false;
        }

        Button Item(string name, string label, float y, UnityEngine.Events.UnityAction action, string help)
            => _ui.Button(name, _content, label, 28, 0, y, 560, 58, action,
                () => { if (_description != null) _description.text = help; });

        static CanvasGroup Group(string name, Transform parent, float alpha)
        {
            var rect = MenuUi.Rect(name, parent, 0, 0, 0, 0);
            MenuUi.Fill(rect);
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = alpha;
            return group;
        }

        void Update()
        {
            if (Phase == MenuPhase.AwaitInput)
            {
                _prompt.alpha = _reduceMotion ? 1 : .78f + .22f * Mathf.Sin(Time.unscaledTime * 1.5f);
                if (Time.frameCount > _readyFrame && Input.anyKeyDown) RevealMenu();
                return;
            }
            if (_panelBusy) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Phase == MenuPhase.Panel) ClosePanel();
                else if (Phase == MenuPhase.Menu) OpenQuit();
            }
            // Tab traverses the same focus order as the arrow keys.
            if ((Phase == MenuPhase.Menu || Phase == MenuPhase.Panel) && Input.GetKeyDown(KeyCode.Tab))
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                var current = selected != null ? selected.GetComponent<Selectable>() : null;
                var next = current == null ? _start :
                    (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                        ? current.FindSelectableOnUp() : current.FindSelectableOnDown());
                if (next != null) EventSystem.current.SetSelectedGameObject(next.gameObject);
            }
        }

        void LateUpdate()
        {
            // Keep the menu above its footer on ultrawide displays, while the artwork still fills the screen.
            float height = ((RectTransform)transform).rect.height;
            _content.localScale = Vector3.one * Mathf.Clamp((height - 160f) / 900f, .4f, 1f);
            if (Phase == MenuPhase.Menu && Application.isFocused && EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == null)
                EventSystem.current.SetSelectedGameObject(_start.gameObject);
        }

        public void RevealMenu()
        {
            if (Phase != MenuPhase.AwaitInput) return;
            Phase = MenuPhase.Revealing;
            StartCoroutine(RevealRoutine());
        }

        IEnumerator RevealRoutine()
        {
            yield return Fade(_prompt, 0, .35f);
            _prompt.gameObject.SetActive(false);
            yield return Fade(_menu, 1, .65f);
            // Never let the key used to dismiss the title also activate StartGame.
            yield return null;
            Phase = MenuPhase.Menu;
            _menu.interactable = _menu.blocksRaycasts = true;
            EventSystem.current.SetSelectedGameObject(_start.gameObject);
        }

        IEnumerator Fade(CanvasGroup group, float target, float seconds)
        {
            float start = group.alpha;
            if (_reduceMotion) { group.alpha = target; yield break; }
            for (float elapsed = 0; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0, 1, elapsed / seconds));
                yield return null;
            }
            group.alpha = target;
        }

        RectTransform OpenPanel(string title, string subtitle, Button returnFocus)
        {
            if (Phase != MenuPhase.Menu) return null;
            Phase = MenuPhase.Panel;
            _returnFocus = returnFocus;
            _menu.interactable = _menu.blocksRaycasts = false;
            _menu.alpha = .24f;
            _modal = Group("Modal", transform, 0);
            _modal.transform.SetSiblingIndex(_blackout.transform.GetSiblingIndex());
            var dim = MenuUi.Image("Dimmer", _modal.transform, new Color(0, 0, 0, .55f), 0, 0, 0, 0, true);
            MenuUi.Fill(dim.rectTransform);
            var rect = MenuUi.Rect(title, _modal.transform, 0, 0, 1120, 790);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            _panel = rect.gameObject;
            MenuUi.Image("Surface", rect, new Color32(13, 23, 27, 248), 0, 0, 1120, 790, true);
            _ui.Text("PanelTitle", rect, title, 48, 64, 42, 970, 78);
            _ui.Text("PanelSubtitle", rect, subtitle, 19, 68, 132, 970, 48, MenuUi.Muted);
            MenuUi.Image("Divider", rect, new Color(1, 1, 1, .15f), 68, 196, 984, 1);
            _modal.interactable = false;
            StartCoroutine(ShowPanel());
            return rect;
        }

        IEnumerator ShowPanel()
        {
            _panelBusy = true;
            yield return Fade(_modal, 1, .22f);
            _modal.interactable = true;
            _panelBusy = false;
            var selectable = _panel.GetComponentInChildren<Selectable>();
            EventSystem.current.SetSelectedGameObject(selectable != null ? selectable.gameObject : null);
        }

        public void ClosePanel()
        {
            if (Phase != MenuPhase.Panel || _panelBusy) return;
            StartCoroutine(HidePanel());
        }

        IEnumerator HidePanel()
        {
            _panelBusy = true;
            _modal.interactable = false;
            yield return Fade(_modal, 0, .18f);
            Destroy(_modal.gameObject);
            _modal = null;
            _panel = null;
            _menu.alpha = 1;
            _menu.interactable = _menu.blocksRaycasts = true;
            Phase = MenuPhase.Menu;
            _panelBusy = false;
            EventSystem.current.SetSelectedGameObject(_returnFocus.gameObject);
        }

        public void OpenSettings()
        {
            var rect = OpenPanel("设置", "调整你的飞行体验。点击应用后保存，Esc 返回并放弃未应用的更改。", _settings);
            if (rect == null) return;
            var draft = MenuPreferences.Load();
            var status = _ui.Text("Status", rect, "", 17, 68, 618, 980, 36, MenuUi.Muted);
            _ui.Text("VolumeLabel", rect, "主音量", 24, 68, 224, 400, 60);
            var amount = _ui.Text("VolumeValue", rect, Mathf.RoundToInt(draft.Volume * 100) + "%", 22,
                926, 224, 120, 60);
            var volume = _ui.Slider(rect, 552, 226, 330, draft.Volume, value =>
            {
                draft.Volume = value;
                amount.text = Mathf.RoundToInt(value * 100) + "%";
                status.text = "尚未应用的更改";
            });
            _ui.Text("DisplayLabel", rect, "显示模式", 24, 68, 316, 420, 60);
            Button display = null;
            display = _ui.Button("DisplayMode", rect, draft.Fullscreen ? "无边框全屏" : "窗口模式", 23,
                552, 316, 500, 60, () =>
                {
                    draft.Fullscreen = !draft.Fullscreen;
                    display.GetComponentInChildren<Text>().text = draft.Fullscreen ? "无边框全屏" : "窗口模式";
                    status.text = "尚未应用的更改";
                });
            _ui.Text("VSyncLabel", rect, "垂直同步", 24, 68, 408, 420, 60);
            Button vsync = null;
            vsync = _ui.Button("VSync", rect, draft.VSync ? "开启" : "关闭", 23, 552, 408, 500, 60, () =>
            {
                draft.VSync = !draft.VSync;
                vsync.GetComponentInChildren<Text>().text = draft.VSync ? "开启" : "关闭";
                status.text = "尚未应用的更改";
            });
            _ui.Text("MotionLabel", rect, "减少动态效果", 24, 68, 500, 420, 60);
            Button motion = null;
            motion = _ui.Button("ReduceMotion", rect, draft.ReduceMotion ? "开启" : "关闭", 23,
                552, 500, 500, 60, () =>
                {
                    draft.ReduceMotion = !draft.ReduceMotion;
                    motion.GetComponentInChildren<Text>().text = draft.ReduceMotion ? "开启" : "关闭";
                    status.text = "尚未应用的更改";
                });
            var apply = _ui.Button("Apply", rect, "应用设置", 23, 68, 690, 270, 60, () =>
            {
                draft.ApplyAndSave();
                _reduceMotion = draft.ReduceMotion;
                status.text = "设置已保存";
            });
            var defaults = _ui.Button("Defaults", rect, "恢复默认", 23, 390, 690, 270, 60, () =>
            {
                draft = MenuPreferences.Defaults;
                volume.SetValueWithoutNotify(draft.Volume);
                amount.text = "100%";
                display.GetComponentInChildren<Text>().text = "无边框全屏";
                vsync.GetComponentInChildren<Text>().text = "开启";
                motion.GetComponentInChildren<Text>().text = "关闭";
                status.text = "已恢复默认值，点击应用后保存";
            });
            var back = _ui.Button("Back", rect, "返回", 23, 798, 690, 254, 60, ClosePanel);
            MenuUi.Navigation(volume, display, vsync, motion, apply, defaults, back);
        }

        public void OpenControls()
        {
            var rect = OpenPanel("操作说明", "起飞之前，先熟悉你的战机。", _controls);
            if (rect == null) return;
            string[] keys = { "W / S", "A / D", "J / 鼠标左键", "Shift / Space", "R", "Esc", "↑↓ / Enter" };
            string[] labels = { "加速 / 刹车", "向左 / 向右转向", "机炮射击（长按连续发射）", "长按加速 / 点按短冲", "重开当前对局", "暂停、选择难度或返回", "菜单选择 / 确认" };
            for (int i = 0; i < keys.Length; i++)
            {
                _ui.Text("Key" + i, rect, keys[i], 24, 70, 218 + i * 54, 390, 50, MenuUi.Ivory);
                _ui.Text("Action" + i, rect, labels[i], 23, 520, 218 + i * 54, 510, 50, MenuUi.Muted);
            }
            _ui.Text("FlightNote", rect, "惯性滑行会侧滑；推进耗尽会过热，短冲不提供无敌。", 18, 68, 622, 980, 42, MenuUi.Muted);
            var back = _ui.Button("Back", rect, "返回", 23, 798, 690, 254, 60, ClosePanel);
            MenuUi.Navigation(back);
        }

        public void OpenCredits()
        {
            var rect = OpenPanel("制作名单", "DOGFIGHT THREAT AI", _credits);
            if (rect == null) return;
            _ui.Text("ProjectName", rect, "基于实时威胁评估与动态决策的飞机对战游戏", 27, 68, 238, 980, 90);
            _ui.Text("Team", rect, "项目制作  /  Dogfight Threat AI 项目组", 23, 68, 354, 980, 58, MenuUi.Muted);
            _ui.Text("Tools", rect, "开发引擎  /  Unity 2022.3 LTS", 23, 68, 428, 980, 58, MenuUi.Muted);
            _ui.Text("Art", rect, "界面背景  /  项目负责人提供", 23, 68, 502, 980, 58, MenuUi.Muted);
            _ui.Text("Fonts", rect, "字体  /  Noto Sans SC · Bebas Neue（SIL OFL 1.1）", 19, 68, 590, 980, 58, MenuUi.Muted);
            var back = _ui.Button("Back", rect, "返回", 23, 798, 690, 254, 60, ClosePanel);
            MenuUi.Navigation(back);
        }

        void OpenQuit()
        {
            var rect = OpenPanel("退出游戏", "确定要退出游戏吗？", _exit);
            if (rect == null) return;
            _ui.Text("Message", rect, "下次见，飞行员。", 30, 68, 294, 970, 100);
            var cancel = _ui.Button("Cancel", rect, "继续停留", 24, 68, 690, 340, 60, ClosePanel);
            var quit = _ui.Button("ConfirmQuit", rect, "退出游戏", 24, 712, 690, 340, 60, () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            MenuUi.Navigation(cancel, quit);
        }

        public void StartBattle()
        {
            if (Phase != MenuPhase.Menu) return;
            if (!SceneNavigation.CanLoadGameScene)
            {
                var rect = OpenPanel("暂时无法开始", "请在 Build Settings 中启用 GameScene 场景后重试。", _start);
                if (rect != null)
                {
                    var back = _ui.Button("Back", rect, "返回", 23, 798, 690, 254, 60, ClosePanel);
                    MenuUi.Navigation(back);
                }
                return;
            }
            Phase = MenuPhase.Loading;
            _menu.interactable = _menu.blocksRaycasts = false;
            EventSystem.current.SetSelectedGameObject(null);
            _blackout.blocksRaycasts = true;
            StartCoroutine(LoadBattle());
        }

        IEnumerator LoadBattle()
        {
            yield return Fade(_blackout, 1, .45f);
            // 先完成现有淡黑动画，再由独立导航脚本加载地图场景。
            yield return SceneNavigation.LoadGameSceneAsync();
        }

    }
}
