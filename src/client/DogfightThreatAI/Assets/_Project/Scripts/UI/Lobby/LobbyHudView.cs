using System;
using DogfightThreatAI.UI.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Lobby
{
    /// <summary>大厅显示与退出事件；不引用 Core、Gameplay 或网络。人数由调用方提供。</summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class LobbyHudView : MonoBehaviour
    {
        [SerializeField] RectTransform safeArea;
        [SerializeField] RectTransform localMarker;
        [SerializeField] Text playerCount;
        int _count = -1;
        int _capacity = -1;
        public event Action ExitRequested;
        public string PlayerCountText => playerCount != null ? playerCount.text : "";

        // 场景未保存 HUD 层级时，补建默认显示布局。
        void Awake()
        {
            if (safeArea == null)
            {
                BuildLayout();
            }
        }

        /// <summary>同一布局可由编辑器保存，也可在无预制布局时创建。</summary>
        public void BuildLayout()
        {
            if (safeArea != null)
            {
                return;
            }
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var ui = new MenuUi();
            safeArea = MenuUi.Rect("Safe area", transform, 0, 0, 0, 0);
            MenuUi.Fill(safeArea);
            Color surface = new Color(.045f, .065f, .052f, .82f);
            var title = MenuUi.Image("Title surface", safeArea, surface, 40, 38, 330, 138);
            ui.Text("Title", title.transform, "机 库 大 厅", 40, 26, 12, 280, 64);
            MenuUi.Image("Divider", title.transform, new Color(1, 1, 1, .35f), 28, 83, 266, 1);
            ui.Text("Connection status", title.transform, "本地预览", 20, 28, 94, 260, 30, MenuUi.Muted);

            var members = MenuUi.Image("Members surface", safeArea, surface, 0, 38, 265, 105);
            members.rectTransform.anchorMin = members.rectTransform.anchorMax = new Vector2(1, 1);
            members.rectTransform.pivot = new Vector2(1, 1);
            members.rectTransform.anchoredPosition = new Vector2(-40, -38);
            playerCount = ui.Text("Player count", members.transform, "", 60, 24, 8, 172, 80, display: true);
            ui.Text("Players label", members.transform, "玩家", 18, 198, 38, 62, 32, MenuUi.Muted);

            var controls = MenuUi.Image("Controls surface", safeArea, surface, 0, 0, 444, 100);
            controls.rectTransform.anchorMin = controls.rectTransform.anchorMax = Vector2.zero;
            controls.rectTransform.pivot = Vector2.zero;
            controls.rectTransform.anchoredPosition = new Vector2(40, 38);
            MenuUi.Image("Control rule", controls.transform, MenuUi.Muted, 12, 18, 1, 65);
            ui.Text("Move hint", controls.transform, "W 推进 · S 刹车 · A/D 转向", 20, 32, 12, 400, 38);
            ui.Text("Exit hint", controls.transform, "[ ESC ]  返回主菜单", 20, 32, 51, 390, 36);
            var footer = ui.Text("Footer", safeArea, "LOBBY  /  PROTOTYPE 01", 18, 0, 0, 340, 36, MenuUi.Muted);
            footer.rectTransform.anchorMin = footer.rectTransform.anchorMax = new Vector2(1, 0);
            footer.rectTransform.pivot = new Vector2(1, 0);
            footer.rectTransform.anchoredPosition = new Vector2(-40, 40);
            footer.alignment = TextAnchor.MiddleRight;

            localMarker = MenuUi.Rect("Local aircraft marker", transform, 0, 0, 52, 32);
            localMarker.pivot = new Vector2(.5f, 0);
            MenuUi.Image("Marker surface", localMarker, surface, 0, 0, 52, 32);
            var label = ui.Text("Local name", localMarker, "你", 20, 0, 0, 52, 32);
            label.alignment = TextAnchor.MiddleCenter;
        }

        // 仅在人数变化时更新显示文字，避免逐帧创建字符串。
        public void Present(int count, int capacity)
        {
            if (count == _count && capacity == _capacity)
            {
                return;
            }
            _count = count;
            _capacity = capacity;
            playerCount.text = $"{count} / {capacity}";
        }

        // 把本机飞机的视口位置转换为 HUD 标记位置。
        public void PlaceLocalMarker(Vector3 viewport)
        {
            if (localMarker == null)
            {
                return;
            }
            localMarker.gameObject.SetActive(viewport.z > 0f);
            localMarker.anchorMin = localMarker.anchorMax = new Vector2(viewport.x, viewport.y);
            localMarker.anchoredPosition = new Vector2(0, 34);
        }

        // 把 Esc 按键转换为退出请求。
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                RequestExit();
            }
        }

        // 通知外层控制器离开大厅，UI 不直接操作模拟。
        public void RequestExit()
        {
            ExitRequested?.Invoke();
        }

        // 按屏幕安全区域调整 HUD 锚点，保留边缘可读空间。
        void LateUpdate()
        {
            if (safeArea == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }
            Rect safe = Screen.safeArea;
            safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }
    }
}
