using UnityEngine;


namespace Dogfight.Gameplay
{
    /// <summary>
    /// 相机跟随 + 「朝哪看哪」的前置偏移。
    ///
    /// 做法：相机不直接跟飞机，而是跟一个"偏移点"——这个点沿飞机的速度方向
    /// 往前推一段。飞机跑得越快，看得越远，这是空战手感的重要一环。
    ///
    /// 挂在 Main Camera 上。最省事的用法：什么都不用连，运行时会自动找
    /// 场景里的 PlaneAgent。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("─ 跟随目标 ─")]
        [Tooltip("留空则自动找场景里的 PlaneAgent")]
        [SerializeField] Transform _target;

        [Tooltip("留空则自动从目标上取 Rigidbody2D")]
        [SerializeField] Rigidbody2D _targetBody;

        [Header("─ 前置偏移（朝哪看哪）─")]
        [Tooltip("相机朝速度方向前移的最大距离。0 = 关闭这个效果")]
        [SerializeField] float _lookAhead = 2.5f;

        [Tooltip("到达最大前置偏移所需的速度。一般填飞机的极速")]
        [SerializeField] float _speedForFullLookAhead = 9f;

        [Tooltip("前置偏移的跟随速度。越小越拖沓，越有'甩'的感觉")]
        [SerializeField] float _lookAheadSmooth = 3f;

        [Header("─ 基础跟随 ─")]
        [Tooltip("相机相对目标的固定偏移。z 必须是负数，否则看不见东西")]
        [SerializeField] Vector3 _offset = new Vector3(0f, 0f, -10f);

        [Tooltip("位置跟随的平滑时间。0 = 硬跟（会晕），0.1~0.2 比较舒服")]
        [SerializeField] float _smoothTime = 0.12f;

        Vector3 _lookOffset;
        Vector3 _followVelocity;

        void Awake()
        {
            if (_target == null)
            {
                // 2022.3 里可用的新 API（FindObjectOfType 在 Unity 6 已废弃）
                var plane = FindFirstObjectByType<PlaneAgent>();
                if (plane != null) _target = plane.transform;
            }

            if (_targetBody == null && _target != null)
                _targetBody = _target.GetComponent<Rigidbody2D>();

            if (_target == null)
                Debug.LogWarning("[CameraFollow] 场景里没找到跟随机，把 Target 手动拖一下。");
        }

        // 用 LateUpdate：等飞机在 FixedUpdate 里动完、画面渲染前再摆相机
        void LateUpdate()
        {
            if (_target == null) return;

            // 目标前置偏移：沿速度方向推出去，速度越快推得越远
            Vector3 desiredOffset = Vector3.zero;
            if (_targetBody != null && _lookAhead > 0f)
            {
                Vector2 v = _targetBody.velocity;
                if (v.sqrMagnitude > 0.01f)
                {
                    float t = Mathf.Clamp01(v.magnitude / Mathf.Max(_speedForFullLookAhead, 0.01f));
                    desiredOffset = v.normalized * (_lookAhead * t);
                }
            }

            // 和帧率无关的平滑（直接 Lerp 的话，帧率一变手感就变）
            float k = 1f - Mathf.Exp(-_lookAheadSmooth * Time.deltaTime);
            _lookOffset = Vector3.Lerp(_lookOffset, desiredOffset, k);

            Vector3 goal = _target.position + _lookOffset + _offset;
            transform.position = Vector3.SmoothDamp(
                transform.position, goal, ref _followVelocity, _smoothTime);
        }
    }
}
