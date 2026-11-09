using System;
using DogfightThreatAI.UI.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Battle
{
    /// <summary>联机战斗的本地 HUD，不依赖网络层或战斗计算。</summary>
    public sealed class NetworkBattleHud : MonoBehaviour
    {
        private Text stats;
        private Text title;
        private Text details;
        private GameObject menu;
        private Button returnButton;
        private GameObject resumeButton;
        private bool menuOpen;
        private int phase = -1;
        private int health = int.MinValue;
        private int alive = -1;
        private int seconds = -1;
        private int winner = -1;
        private int seat = -1;
        private bool host;
        public bool BlocksInput => menuOpen || phase != 1;

        public void Build(Action leave, Action returnToLobby)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();

            if (EventSystem.current == null)
            {
                var events = new GameObject("Battle EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }

            var ui = new MenuUi();
            var status = MenuUi.Image("Flight status", transform, new Color(.04f, .06f, .05f, .9f),
                40, 36, 450, 140);
            stats = ui.Text("Battle status", status.transform, "", 27, 24, 9, 408, 84);
            ui.Text("Controls", status.transform, "WASD 飞行   J / 左键开火   ESC 菜单",
                20, 24, 95, 408, 30, MenuUi.Muted);

            var overlay = MenuUi.Image("Match menu", transform, new Color(.015f, .025f, .02f, .66f),
                0, 0, 0, 0, true);
            MenuUi.Fill(overlay.rectTransform);
            menu = overlay.gameObject;
            var card = MenuUi.Image("Match card", overlay.transform, new Color(.045f, .065f, .052f, 1f),
                0, 0, 560, 340);
            card.rectTransform.anchorMin = card.rectTransform.anchorMax = new Vector2(.5f, .5f);
            card.rectTransform.pivot = new Vector2(.5f, .5f);
            card.rectTransform.anchoredPosition = Vector2.zero;
            title = ui.Text("Title", card.transform, "", 46, 40, 24, 480, 68);
            details = ui.Text("Details", card.transform, "", 24, 40, 104, 480, 72, MenuUi.Muted);
            resumeButton = ui.Button("Resume", card.transform, "继续飞行", 28,
                32, 191, 496, 56, () => menuOpen = false).gameObject;
            returnButton = ui.Button("Lobby", card.transform, "带所有人返回大厅", 28,
                32, 191, 496, 56, () => returnToLobby());
            ui.Button("Leave", card.transform, "退出联机", 28,
                32, 254, 496, 56, () => leave());
            RefreshMenu();
        }

        public void Present(int currentPhase, int currentHealth, int maxHealth, int currentAlive,
            int remaining, int winningSeat, int localSeat, bool isHost)
        {
            if (phase == currentPhase && health == currentHealth && alive == currentAlive &&
                seconds == remaining && winner == winningSeat && seat == localSeat && host == isHost)
            {
                return;
            }
            phase = currentPhase;
            health = currentHealth;
            alive = currentAlive;
            seconds = remaining;
            winner = winningSeat;
            seat = localSeat;
            host = isHost;

            if (phase == 0)
            {
                stats.text = $"等待玩家加载   {seconds} 秒";
            }
            else if (phase == 2)
            {
                stats.text = "对局已结束";
            }
            else
            {
                string local = health <= 0 ? "已击毁 · 观战" : $"机体 {health} / {maxHealth}";
                stats.text = $"{local}\n剩余 {alive} 人   {seconds / 60:00}:{seconds % 60:00}";
            }
            RefreshMenu();
        }

        private void Update()
        {
            if (phase != 2 && Application.isFocused && Input.GetKeyDown(KeyCode.Escape))
            {
                menuOpen = !menuOpen;
            }
            if (menu.activeSelf != (menuOpen || phase == 2))
            {
                RefreshMenu();
            }
        }

        private void RefreshMenu()
        {
            bool finished = phase == 2;
            menu.SetActive(menuOpen || finished);
            if (!menu.activeSelf)
            {
                return;
            }
            resumeButton.SetActive(!finished);
            returnButton.gameObject.SetActive(finished && host);
            if (finished)
            {
                title.text = winner == 0 ? "平局" : winner == seat ? "胜利" : "战败";
                details.text = host ? "可以返回大厅，再准备下一局。" : "等待房主返回大厅，或退出联机。";
            }
            else
            {
                title.text = "联机菜单";
                details.text = host ? "退出会结束房间。打开菜单时，对局仍在继续。"
                    : "退出后返回主菜单。打开菜单时，对局仍在继续。";
            }
        }
    }
}
