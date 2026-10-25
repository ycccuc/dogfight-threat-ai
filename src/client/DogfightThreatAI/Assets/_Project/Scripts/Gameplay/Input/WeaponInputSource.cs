using UnityEngine;

namespace DogfightThreatAI.Gameplay.InputSource
{
    /// <summary>渲染帧缓存短按，固定步消费；暂停、失焦后必须松开才可再次射击。</summary>
    public sealed class WeaponInputSource : MonoBehaviour
    {
        bool _held, _pending, _requireRelease, _focused = true;
        public bool Blocked { get; set; }
        void Update()
        {
            bool held = UnityEngine.Input.GetKey(KeyCode.J) || UnityEngine.Input.GetMouseButton(0);
            bool pressed = UnityEngine.Input.GetKeyDown(KeyCode.J) || UnityEngine.Input.GetMouseButtonDown(0);
            Capture(held, pressed, Blocked || Time.timeScale <= 0 || !_focused);
        }
        public void Capture(bool held, bool pressed, bool blocked)
        {
            if (blocked) { _pending = _held = false; _requireRelease = held; return; }
            if (!held && !pressed) _requireRelease = false;
            _held = held && !_requireRelease;
            _pending |= pressed && !_requireRelease;
        }
        public bool Consume()
        {
            bool intent = !Blocked && _focused && !_requireRelease && (_held || _pending);
            _pending = false;
            return intent;
        }
        void OnApplicationFocus(bool focused)
        { _focused = focused; if (!focused) { _held = _pending = false; _requireRelease = true; } }
        void OnDisable() { _held = _pending = false; _requireRelease = true; }
    }
}
