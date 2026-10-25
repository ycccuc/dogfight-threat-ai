using DogfightThreatAI.UI.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace DogfightThreatAI.UI.Battle
{
    /// <summary>仅接收显示值，不引用模拟程序集。</summary>
    public sealed class BattleHudView : MonoBehaviour
    {
        SegmentedHealthRing _ring;
        Image _cooldown;
        RectTransform _safe;
        public SegmentedHealthRing Ring => _ring;
        void Awake()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 25;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            _safe = MenuUi.Rect("Safe area", transform, 0, 0, 0, 0);
            var cluster = MenuUi.Rect("Cannon HUD", _safe, 0, 0, 210, 210);
            cluster.anchorMin = cluster.anchorMax = new Vector2(1, 0); cluster.pivot = Vector2.zero;
            cluster.anchoredPosition = new Vector2(-280, 62);
            var shadow = MenuUi.Rect("Ring shadow", cluster, 0, 0, 210, 210).gameObject.AddComponent<SegmentedHealthRing>();
            shadow.color = new Color(0, 0, 0, .65f); shadow.raycastTarget = false;
            _ring = MenuUi.Rect("Health ring", cluster, 3, 3, 204, 204).gameObject.AddComponent<SegmentedHealthRing>();
            _ring.color = MenuUi.Ivory; _ring.raycastTarget = false;
            var icon = MenuUi.Rect("Cannon", cluster, 81, 49, 48, 64).gameObject.AddComponent<CannonIcon>();
            icon.color = MenuUi.Ivory; icon.raycastTarget = false;
            var ui = new MenuUi();
            var nameText = ui.Text("Weapon", cluster, "机炮", 18, 55, 120, 100, 30);
            nameText.alignment = TextAnchor.MiddleCenter;
            MenuUi.Image("Cooldown track", cluster, new Color(1, 1, 1, .18f), 83, 159, 44, 3);
            _cooldown = MenuUi.Image("Cooldown", cluster, MenuUi.Ivory, 83, 159, 44, 3);
            var hint = ui.Text("Pause hint", _safe, "ESC  暂停", 18, 0, 0, 220, 35, MenuUi.Ivory);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = Vector2.zero;
            hint.rectTransform.anchoredPosition = new Vector2(62, 75);
            var textShadow = hint.gameObject.AddComponent<Shadow>();
            textShadow.effectColor = new Color(0, 0, 0, .9f);
        }
        public void Present(int health, float cooldown01)
        {
            _ring.SetHealth(health);
            _cooldown.rectTransform.sizeDelta = new Vector2(44 * (1 - Mathf.Clamp01(cooldown01)), 3);
        }
        void LateUpdate()
        {
            var safe = Screen.safeArea;
            _safe.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            _safe.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            _safe.offsetMin = _safe.offsetMax = Vector2.zero;
        }
    }
}
