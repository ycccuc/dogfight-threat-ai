using System;
using DogfightThreatAI.UI.Menu;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Battle
{
    /// <summary>河谷清剿的任务简报、战斗状态、区域图和结算。</summary>
    public sealed class PatrolMissionHud : MonoBehaviour
    {
        static readonly Color Panel = new Color32(20, 25, 23, 236);
        static readonly Color Amber = new Color32(219, 176, 106, 255);
        static readonly Color Danger = new Color32(242, 135, 115, 255);
        static readonly Color Cleared = new Color32(138, 165, 140, 255);
        static readonly Color Intel = new Color32(142, 209, 223, 255);

        MenuUi ui;
        Text objective;
        Text intel;
        Text alert;
        Text health;
        Text feedback;
        Text pickupToast;
        float pickupToastUntil;
        Image cooldown;
        Image boostBar;
        CanvasGroup orders;
        GameObject combat;
        GameObject overlay;
        RectTransform card;
        Text heading;
        Text description;
        Text primaryLabel;
        Button primary;
        Button retry;
        Button exit;
        GameObject map;
        RectTransform playerMark;
        Image[] mapMarks;
        Image[] pickupMarks;
        Text[] sectorRows;
        Text[] collectionRows;
        Text taskSummary;
        Text collectionTitle;
        Text objectiveChevron;
        Text collectionChevron;
        RectTransform collectionHeader;
        GameObject journal;
        GameObject taskDetails;
        RectTransform collectionDetails;
        Button taskToggle;
        Button collectionToggle;
        Text radarHint;
        Sprite circle;
        Texture2D circleTexture;
        string[] names;
        Action primaryAction;
        const float MapDiameter = 206f;
        const float CannonBarWidth = 140f;
        const float IntroHold = 6f;
        const float IntroFade = 1.25f;

        public void Build(string[] sectorNames, Vector2[] normalizedCenters,
            string[] pickupNames, Vector2[] pickupPositions, Action retryMission, Action leave)
        {
            names = sectorNames;
            ui = new MenuUi();
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                new GameObject("Mission EventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                    .transform.SetParent(transform, false);
            }

            var layer = MenuUi.Rect("Mission overlay", transform, 0, 0, 0, 0);
            MenuUi.Fill(layer);
            combat = layer.gameObject;
            var order = MenuUi.Image("Orders", layer, Panel, 42, 38, 522, 154);
            orders = order.gameObject.AddComponent<CanvasGroup>();
            MenuUi.Image("Accent", order.transform, Amber, 0, 0, 3, 154);
            ui.Text("Operation", order.transform, "作 战 任 务   /   河 谷 清 剿", 18, 24, 10, 465, 30, MenuUi.Muted);
            objective = ui.Text("Objective", order.transform, "", 31, 24, 42, 475, 47);
            intel = ui.Text("Sector guidance", order.transform, "", 22, 24, 91, 475, 40, MenuUi.Muted);
            alert = ui.Text("Detection warning", layer, "", 21, 0, 39, 460, 38, Amber);
            alert.rectTransform.anchorMin = alert.rectTransform.anchorMax = new Vector2(.5f, 1f);
            alert.rectTransform.anchoredPosition = new Vector2(-230f, -39f);
            alert.alignment = TextAnchor.MiddleCenter;

            var systems = MenuUi.Image("Aircraft instruments", layer, new Color32(20, 25, 23, 181), 0, 0, 242, 96);
            systems.rectTransform.anchorMin = systems.rectTransform.anchorMax = Vector2.zero;
            systems.rectTransform.pivot = Vector2.zero;
            systems.rectTransform.anchoredPosition = new Vector2(34, 30);
            health = ui.Text("Health", systems.transform, "", 19, 14, 4, 208, 32);
            ui.Text("Cannon", systems.transform, "机炮", 15, 14, 37, 54, 26, MenuUi.Muted);
            MenuUi.Image("Cooldown track", systems.transform, new Color32(57, 63, 58, 255), 70, 50, CannonBarWidth, 3);
            cooldown = MenuUi.Image("Cooldown", systems.transform, Amber, 70, 50, CannonBarWidth, 3);
            ui.Text("Propulsion", systems.transform, "推进", 15, 14, 62, 54, 24, MenuUi.Muted);
            MenuUi.Image("Boost track", systems.transform, new Color32(57, 63, 58, 255), 70, 74, CannonBarWidth, 3);
            boostBar = MenuUi.Image("Boost", systems.transform, Intel, 70, 74, CannonBarWidth, 3);
            feedback = ui.Text("Combat feedback", systems.transform, "", 18, 0, -31, 340, 28, Amber);
            var toast = MenuUi.Image("Pickup toast", layer, Panel, 0, 0, 610, 44);
            toast.rectTransform.anchorMin = toast.rectTransform.anchorMax = new Vector2(.5f, 0f);
            toast.rectTransform.pivot = new Vector2(.5f, 0f);
            toast.rectTransform.anchoredPosition = new Vector2(0, 70);
            pickupToast = ui.Text("Pickup message", toast.transform, "", 21, 12, 0, 586, 44, Intel);
            pickupToast.alignment = TextAnchor.MiddleCenter;
            toast.gameObject.SetActive(false);

            BuildChart(layer, normalizedCenters, pickupPositions);

            var shade = MenuUi.Image("Mission panel", transform, new Color32(8, 11, 10, 198),
                0, 0, 0, 0, true);
            MenuUi.Fill(shade.rectTransform);
            overlay = shade.gameObject;
            BuildJournal(shade.transform, pickupNames);
            card = MenuUi.Image("Panel card", shade.transform, Panel, 0, 0, 720, 540).rectTransform;
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
            card.anchoredPosition = Vector2.zero;
            MenuUi.Image("Panel edge", card, Amber, 0, 0, 3, 540);
            ui.Text("Mission index", card, "作 战 档 案   /   0 1", 20, 44, 27, 632, 36, Amber);
            heading = ui.Text("Panel title", card, "", 60, 44, 72, 632, 95);
            description = ui.Text("Brief", card, "", 26, 44, 165, 622, 161, MenuUi.Muted);
            MenuUi.Image("Rule", card, new Color32(92, 100, 89, 220), 44, 338, 632, 1);
            primary = ui.Button("Primary action", card, "出击", 30, 44, 359, 632, 53,
                () => primaryAction?.Invoke());
            primaryLabel = primary.GetComponentInChildren<Text>();
            retry = ui.Button("Retry mission", card, "重新出击", 28, 44, 411, 632, 52,
                () => retryMission());
            exit = ui.Button("Return to main menu", card, "返回主菜单", 28, 44, 463, 632, 52,
                () => leave());
        }

        void BuildChart(RectTransform layer, Vector2[] centers, Vector2[] pickups)
        {
            const int resolution = 128;
            circleTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    var offset = new Vector2(x + .5f, y + .5f) - Vector2.one * (resolution * .5f);
                    byte alpha = (byte)(255f * Mathf.Clamp01(resolution * .5f - offset.magnitude));
                    pixels[y * resolution + x] = new Color32(255, 255, 255, alpha);
                }
            }
            circleTexture.SetPixels32(pixels);
            circleTexture.Apply(false, true);
            circle = Sprite.Create(circleTexture, new Rect(0, 0, resolution, resolution), new Vector2(.5f, .5f));

            var chart = MenuUi.Rect("Sector chart", layer, 0, 0, MapDiameter, MapDiameter);
            chart.anchorMin = chart.anchorMax = chart.pivot = Vector2.one;
            chart.anchoredPosition = new Vector2(-36, -32);
            map = chart.gameObject;
            MenuUi.Image("Rim", chart, new Color32(149, 157, 139, 210), 0, 0, MapDiameter, MapDiameter).sprite = circle;
            MenuUi.Image("Radar background", chart, new Color32(20, 25, 23, 224), 2, 2, MapDiameter - 4, MapDiameter - 4).sprite = circle;
            var north = ui.Text("North", chart, "N", 17, 86, 5, 34, 27, MenuUi.Ivory);
            north.alignment = TextAnchor.MiddleCenter;
            var grid = new Color32(123, 146, 135, 80);
            MenuUi.Image("Meridian", chart, grid, 102, 34, 1, 139);
            MenuUi.Image("Parallel", chart, grid, 30, 102, 146, 1);
            mapMarks = new Image[centers.Length];
            for (int i = 0; i < centers.Length; i++)
            {
                var point = ChartPoint(centers[i]);
                mapMarks[i] = MenuUi.Image("Patrol area", chart, Amber, point.x - 5, point.y - 5, 10, 10);
                mapMarks[i].sprite = circle;
            }
            pickupMarks = new Image[pickups.Length];
            for (int i = 0; i < pickups.Length; i++)
            {
                var point = ChartPoint(pickups[i]);
                pickupMarks[i] = MenuUi.Image("Recon record", chart, Intel, point.x, point.y, 7, 7);
                var rect = pickupMarks[i].rectTransform;
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(point.x, -point.y);
                rect.localRotation = Quaternion.Euler(0, 0, 45);
            }
            playerMark = MenuUi.Image("Own aircraft", chart, MenuUi.Ivory, 0, 0, 8, 8).rectTransform;
            playerMark.GetComponent<Image>().sprite = circle;
            radarHint = ui.Text("Radar hint", chart, "", 17, -108, MapDiameter + 3, 314, 30, MenuUi.Ivory);
            radarHint.alignment = TextAnchor.MiddleRight;
            var legend = ui.Text("Radar legend", chart, "◆  侦察记录", 15, 31, MapDiameter + 28, 175, 24, Intel);
            legend.alignment = TextAnchor.MiddleRight;
        }

        void BuildJournal(Transform parent, string[] pickupNames)
        {
            const float width = 408;
            var column = MenuUi.Image("Mission journal", parent, Panel, 0, 0, width, 495).rectTransform;
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0f, .5f);
            column.anchoredPosition = new Vector2(42, 0);
            journal = column.gameObject;
            MenuUi.Image("Journal accent", column, Amber, 0, 0, 3, 495);
            ui.Text("Journal heading", column, "任 务  /  河谷清剿", 29, 24, 19, 360, 48);
            taskSummary = ui.Text("Mission progress", column, "", 20, 24, 70, 360, 31, Amber);
            MenuUi.Image("Journal divider", column, new Color32(92, 100, 89, 220), 24, 112, 360, 1);

            taskToggle = ui.Button("Toggle objectives", column, "清剿目标", 23, 12, 127, 384, 50, () =>
            {
                taskDetails.SetActive(!taskDetails.activeSelf);
                LayoutJournal();
            });
            objectiveChevron = ui.Text("Expand indicator", taskToggle.transform, "", 22, 343, 0, 32, 50, Amber);
            var details = MenuUi.Rect("Objective rows", column, 0, 180, width, 33 * names.Length);
            taskDetails = details.gameObject;
            sectorRows = new Text[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                sectorRows[i] = ui.Text("Patrol progress", details, "", 20, 44, i * 33, 340, 32);
            }

            collectionToggle = ui.Button("Toggle collection", column, "侦察记录   0 / " + pickupNames.Length, 23,
                12, 0, 384, 50, () =>
                {
                    collectionDetails.gameObject.SetActive(!collectionDetails.gameObject.activeSelf);
                    LayoutJournal();
                });
            collectionHeader = (RectTransform)collectionToggle.transform;
            collectionTitle = collectionToggle.GetComponentInChildren<Text>();
            collectionChevron = ui.Text("Expand indicator", collectionHeader, "", 22, 343, 0, 32, 50, Intel);
            collectionDetails = MenuUi.Rect("Collection rows", column, 0, 0, width, 33 * pickupNames.Length);
            collectionRows = new Text[pickupNames.Length];
            for (int i = 0; i < pickupNames.Length; i++)
            {
                collectionRows[i] = ui.Text("Record progress", collectionDetails, "", 20, 44, i * 33, 340, 32);
                PresentCollection(i, pickupNames[i], false, 0);
            }
            collectionDetails.gameObject.SetActive(false);
            LayoutJournal();
        }

        void LayoutJournal()
        {
            bool expanded = taskDetails.activeSelf;
            float row = 181 + (expanded ? sectorRows.Length * 33 : 0);
            collectionHeader.anchoredPosition = new Vector2(12, -row);
            collectionDetails.anchoredPosition = new Vector2(0, -row - 53);
            objectiveChevron.text = expanded ? "▾" : "▸";
            collectionChevron.text = collectionDetails.gameObject.activeSelf ? "▾" : "▸";
        }

        public void ShowBriefing(int enemyCount, Action deploy)
        {
            ShowPanel("河谷清剿",
                $"击毁全部 {enemyCount} 架敌机。蓝标是可选的侦察记录。\nWASD 飞行 · J / 左键开火\n空格 短冲 · Shift 持续推进\nTAB 空域图 · ESC 暂停",
                "出击", deploy, false);
            combat.SetActive(false);
        }

        public void ShowPause(Action resume) => ShowPanel("任务暂停", "ESC 继续飞行", "继续飞行", resume, true, true);

        public void UseNetworkLabels()
        {
            retry.GetComponentInChildren<Text>().text = "带队返回大厅";
            exit.GetComponentInChildren<Text>().text = "退出联机";
        }

        public void ShowNetworkWaiting()
        {
            ShowPanel("等待队友", "全员加载完毕后出击。\n空格 短冲 · Shift 持续推进", "", null, false);
            combat.SetActive(false);
        }

        public void ShowNetworkMenu(Action resume) =>
            ShowPanel("合作清剿", "打开任务菜单期间，战斗继续进行。", "继续飞行", resume, false, true);

        public void ShowResult(bool won, int kills, int total, float seconds, int collected, int pickupCount,
            bool allowRetry = true)
        {
            int time = Mathf.FloorToInt(seconds);
            ShowPanel(won ? "任务完成" : "任务失败",
                $"击毁敌机  {kills} / {total}\n侦察记录  {collected} / {pickupCount}\n任务时间  {time / 60:00}:{time % 60:00}",
                "重新出击", null, allowRetry);
            combat.SetActive(false);
        }

        void ShowPanel(string title, string text, string actionText, Action action, bool allowRetry, bool showJournal = false)
        {
            overlay.SetActive(true);
            journal.SetActive(showJournal);
            card.anchorMin = card.anchorMax = new Vector2(showJournal ? .65f : .5f, .5f);
            card.anchoredPosition = Vector2.zero;
            heading.text = title;
            description.text = text;
            primaryLabel.text = actionText;
            primaryAction = action;
            primary.gameObject.SetActive(action != null);
            retry.gameObject.SetActive(allowRetry);
            if (showJournal)
            {
                if (allowRetry)
                {
                    MenuUi.Navigation(taskToggle, collectionToggle, primary, retry, exit);
                }
                else
                {
                    MenuUi.Navigation(taskToggle, collectionToggle, primary, exit);
                }
            }
            else if (action == null)
            {
                if (allowRetry)
                {
                    MenuUi.Navigation(retry, exit);
                }
                else
                {
                    MenuUi.Navigation(exit);
                }
            }
            else if (allowRetry)
            {
                MenuUi.Navigation(primary, retry, exit);
            }
            else
            {
                MenuUi.Navigation(primary, exit);
            }
            EventSystem.current?.SetSelectedGameObject((action != null ? primary : allowRetry ? retry : exit).gameObject);
        }

        public void ShowFlight()
        {
            overlay.SetActive(false);
            combat.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        public void ToggleChart() => map.SetActive(!map.activeSelf);

        public void PresentStatus(int remaining, int total, int cleared, int zones,
            int hp, int maxHp, float ready, string guidance, string awareness, string radar)
        {
            objective.text = $"剩余敌机  {remaining} / {total}   ·   空域 {cleared} / {zones}";
            taskSummary.text = $"清剿敌机  {total - remaining} / {total}";
            intel.text = guidance;
            radarHint.text = radar;
            alert.text = awareness;
            alert.color = awareness == "敌机已发现你" ? Danger : Amber;
            health.text = $"机体  {hp} / {maxHp}";
            cooldown.rectTransform.sizeDelta = new Vector2(CannonBarWidth * Mathf.Clamp01(ready), 3f);
        }

        public void PresentIntro(float elapsed)
        {
            orders.alpha = 1f - Mathf.SmoothStep(0f, 1f, (elapsed - IntroHold) / IntroFade);
            orders.gameObject.SetActive(orders.alpha > 0f);
        }

        public void PresentBoost(float energy)
        {
            boostBar.rectTransform.sizeDelta = new Vector2(CannonBarWidth * Mathf.Clamp01(energy), 3f);
        }

        public void PresentSector(int index, int remaining)
        {
            sectorRows[index].text = remaining > 0 ? $"{names[index]}  ·  剩余 {remaining} 架" : $"{names[index]}  ·  已清除";
            sectorRows[index].color = remaining > 0 ? MenuUi.Ivory : Cleared;
            mapMarks[index].color = remaining > 0 ? Amber : Cleared;
        }

        public void PresentCollection(int index, string name, bool collected, int count, string collector = null)
        {
            collectionTitle.text = $"侦察记录   {count} / {collectionRows.Length}";
            collectionRows[index].text = collected
                ? $"{name}  ·  {collector ?? "已拾取"}"
                : $"{name}  ·  待拾取";
            collectionRows[index].color = collected ? Cleared : MenuUi.Muted;
            pickupMarks[index].gameObject.SetActive(!collected);
        }

        public void ShowPickup(string message)
        {
            pickupToast.text = message;
            pickupToast.transform.parent.gameObject.SetActive(true);
            pickupToastUntil = Time.unscaledTime + 3.2f;
        }

        void Update()
        {
            if (pickupToastUntil > 0f && Time.unscaledTime >= pickupToastUntil)
            {
                pickupToastUntil = 0f;
                pickupToast.transform.parent.gameObject.SetActive(false);
            }
        }

        public void PresentPosition(Vector2 normalized)
        {
            var point = ChartPoint(normalized);
            playerMark.anchoredPosition = new Vector2(point.x - 4f, -point.y + 4f);
        }

        public void PresentFeedback(string message, bool damage)
        {
            feedback.text = message;
            feedback.color = damage ? Danger : Amber;
        }

        static Vector2 ChartPoint(Vector2 point) =>
            new Vector2(MapDiameter / 2f + (Mathf.Clamp01(point.x) - .5f) * 148f,
                MapDiameter / 2f + (.5f - Mathf.Clamp01(point.y)) * 111f);

        void OnDestroy()
        {
            Destroy(circle);
            Destroy(circleTexture);
        }
    }
}
