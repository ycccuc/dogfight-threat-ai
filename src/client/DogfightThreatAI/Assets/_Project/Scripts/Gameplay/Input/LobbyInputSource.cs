using DogfightThreatAI.Core.Input;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.InputSource
{
    /// <summary>只采集大厅操作。W 前进、S 刹车、A/D 转向；松开推进键后减速停下。</summary>
    public sealed class LobbyInputSource : MonoBehaviour, IPlayerInputSource
    {
        bool _focused = true;

        // 把大厅的推进、刹车和转向按键转换为飞行意图。
        public FlightInput ReadInput(float deltaTime)
        {
            if (!isActiveAndEnabled || !_focused)
            {
                return new FlightInput(-1f, 0f);
            }
            bool forward = UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow);
            bool brake = UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow);
            float left = UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f;
            float right = UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow) ? 1f : 0f;
            return new FlightInput(forward && !brake ? 1f : -1f, left - right);
        }

        // 记录窗口焦点，失焦期间不接受移动操作。
        void OnApplicationFocus(bool focused)
        {
            _focused = focused;
        }
    }
}
