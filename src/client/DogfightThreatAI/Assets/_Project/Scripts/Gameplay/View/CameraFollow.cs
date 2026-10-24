using UnityEngine;

namespace DogfightThreatAI.Gameplay.View
{
    /// <summary>
    /// 相机跟随组件，提供平滑跟随、前瞻偏移和世界边界夹紧。
    /// 跟随使用 SmoothDamp，目标不设置为相机的父物体，避免相机随目标旋转。
    /// 跟随逻辑在 LateUpdate 中执行，避免与目标移动产生时序冲突。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        [Header("目标与偏移")]
        [SerializeField] Transform target;
        [Tooltip("相机相对目标的偏移。2D 俯视通常是 (0, 0, -10)")]
        [SerializeField] Vector3 offset = new Vector3(0f, 0f, -10f);

        [Header("跟随手感")]
        [Tooltip("跟随平滑时间。0 = 硬跟随（会抖），0.1~0.2 = 舒服")]
        [SerializeField, Min(0f)] float smoothTime = 0.12f;

        [Tooltip("朝「实际移动方向」前瞻的距离（世界单位）。\n" +
                 "0 = 目标永远钉在屏幕正中心；2~4 = 目标在中心前方，能看出高速掠进")]
        [SerializeField, Min(0f)] float lookAhead = 3f;

        [Tooltip("前瞻方向的平滑时间，避免转向时镜头甩动")]
        [SerializeField, Min(0.01f)] float lookAheadSmoothTime = 0.3f;

        [Header("世界边界（大地图必设）")]
        [Tooltip("勾上后镜头会被限制在地图内，不会拍到地图外的空白")]
        [SerializeField] bool clampToWorld = true;

        [Tooltip("世界半尺寸（以原点为中心）。例如地图 400×300 → 填 (200, 150)")]
        [SerializeField] Vector2 worldHalfSize = new Vector2(200f, 150f);

        Camera _camera;
        Transform _trackedTarget;
        PlayerView _targetView;
        Vector3 _velocity;
        Vector3 _lastTargetPosition;
        Vector3 _lookDirection;
        Vector3 _lookDirectionVelocity;
        bool _hasLastPosition;

        void Awake() => _camera = GetComponent<Camera>();

        public void SetTarget(Transform t)
        {
            target = t;
            _hasLastPosition = false;
        }

        /// <summary>运行时设置世界半尺寸（例如从关卡配置读）。</summary>
        public void SetWorldHalfSize(Vector2 half) => worldHalfSize = half;

        void LateUpdate()
        {
            if (target == null) return;
            if (_trackedTarget != target)
            {
                _trackedTarget = target;
                _targetView = target.GetComponent<PlayerView>();
                _hasLastPosition = false;
            }
            Vector3 targetPosition = _targetView != null ? _targetView.RenderPosition : target.position;

            // 用「这一帧目标移动了多少」推断移动方向——不依赖 PlayerController / 机头角度，
            // 所以 AI、观战、回放复用同一个相机都不用改。
            Vector3 delta = targetPosition - _lastTargetPosition;
            if (_hasLastPosition)
            {
                Vector3 desiredDir = delta.magnitude > 1e-5f ? delta.normalized : Vector3.zero;
                _lookDirection = Vector3.SmoothDamp(_lookDirection, desiredDir,
                                                    ref _lookDirectionVelocity, lookAheadSmoothTime);
            }
            _lastTargetPosition = targetPosition;
            _hasLastPosition = true;

            Vector3 desired = targetPosition + offset + _lookDirection * lookAhead;
            desired.z = offset.z;                      // 2D：z 永远保持 -10

            if (clampToWorld) desired = ClampInsideWorld(desired);

            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime);
        }

        /// <summary>
        /// 用「相机自己的半视口」去夹，而不是直接夹世界半尺寸——
        /// 否则镜头中心能贴到地图边缘，屏幕边上就会露出一大片地图外的空白。
        /// </summary>
        Vector3 ClampInsideWorld(Vector3 p)
        {
            if (_camera == null) _camera = GetComponent<Camera>();

            float halfHeight = _camera != null ? _camera.orthographicSize : 5f;
            float halfWidth  = _camera != null ? halfHeight * Mathf.Max(0.01f, _camera.aspect) : 9f;

            float maxX = Mathf.Max(0f, worldHalfSize.x - halfWidth);
            float maxY = Mathf.Max(0f, worldHalfSize.y - halfHeight);

            p.x = Mathf.Clamp(p.x, -maxX, maxX);
            p.y = Mathf.Clamp(p.y, -maxY, maxY);
            return p;
        }
    }
}
