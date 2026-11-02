using UnityEngine;

namespace DogfightThreatAI.Gameplay.InputSource
{
    /// <summary>渲染帧缓存短按，固定步消费；暂停、失焦后必须松开才可再次射击。</summary>
    public sealed class WeaponInputSource : MonoBehaviour
    {
        bool _held, _pending, _requireRelease, _focused = true;

        public bool Blocked
        {
            get; set;
        }

        // 采样射击按键，并把短按缓存到固定步。
        void Update()
        {
            bool held = UnityEngine.Input.GetKey(KeyCode.J) || UnityEngine.Input.GetMouseButton(0);
            bool pressed = UnityEngine.Input.GetKeyDown(KeyCode.J) || UnityEngine.Input.GetMouseButtonDown(0);
            Capture(held, pressed, Blocked || Time.timeScale <= 0 || !_focused);
        }

        // 更新持续射击和短按状态，处理暂停与松键门控。
        public void Capture(bool held, bool pressed, bool blocked)
        {
            if (blocked)
            {
                _pending = _held = false;
                _requireRelease = held;
                return;
            }
            if (!held && !pressed)
            {
                _requireRelease = false;
            }
            _held = held && !_requireRelease;
            _pending |= pressed && !_requireRelease;
        }

        // 消费本步射击请求，避免一次短按被重复使用。
        public bool Consume()
        {
            bool intent = !Blocked && _focused && !_requireRelease && (_held || _pending);
            _pending = false;
            return intent;
        }

        // 失焦时取消射击，并要求恢复后先松开射击键。
        void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused)
            {
                _held = _pending = false;
                _requireRelease = true;
            }
        }

        // 清理射击缓存，防止重新启用时沿用旧请求。
        void OnDisable()
        {
            _held = _pending = false;
            _requireRelease = true;
        }
    }
}
