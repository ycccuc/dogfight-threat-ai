using DogfightThreatAI.Gameplay.Config;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Lobby
{
    /// <summary>固定镜头始终容纳完整大厅；宽屏和窄屏保持相同活动范围。</summary>
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class LobbyCamera : MonoBehaviour
    {
        [SerializeField] LobbyData data;
        Camera _camera;

        // 缓存大厅相机，供后续视野适配使用。
        void OnEnable()
        {
            _camera = GetComponent<Camera>();
        }

        // 根据屏幕比例调整正交视野，保证完整大厅可见。
        void LateUpdate()
        {
            if (data == null || _camera == null)
            {
                return;
            }
            _camera.orthographic = true;
            _camera.orthographicSize = Mathf.Max(data.MapSize.y * .5f,
                data.MapSize.x * .5f / Mathf.Max(.1f, _camera.aspect));
        }
    }
}
