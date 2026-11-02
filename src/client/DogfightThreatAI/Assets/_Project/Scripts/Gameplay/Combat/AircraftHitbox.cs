using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>A contact volume anchored to the simulation root; Art-only hit feedback cannot move it.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class AircraftHitbox : MonoBehaviour
    {
        [SerializeField] CircleCollider2D volume;

        public CircleCollider2D Collider
        {
            get
            {
                if (volume == null)
                {
                    volume = GetComponent<CircleCollider2D>();
                }
                return volume;
            }
        }

        public float WorldRadius
        {
            get
            {
                var circle = Collider;
                if (circle == null)
                {
                    return 0f;
                }
                Vector3 scale = transform.lossyScale;
                return circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }
        }

        public bool IsActive => isActiveAndEnabled && Collider != null && Collider.enabled &&
            Collider.gameObject.activeInHierarchy && WorldRadius > 0f &&
            (Collider.attachedRigidbody == null || Collider.attachedRigidbody.simulated);

        // 设置默认接触半径，并把碰撞体固定到模拟根节点。
        void Reset()
        {
            volume = GetComponent<CircleCollider2D>();
            if (volume != null)
            {
                volume.radius = .5f;
            }
            AnchorVolume();
        }

        // 初始化以模拟根节点为中心的接触体积。
        void Awake()
        {
            AnchorVolume();
        }

        // 编辑配置时保持接触体积的锚点与触发器设置。
        void OnValidate()
        {
            AnchorVolume();
        }

        // 清除碰撞体偏移，使用触发体积而不让物理求解器推动飞机。
        void AnchorVolume()
        {
            var circle = Collider;
            if (circle == null)
            {
                return;
            }
            circle.offset = Vector2.zero;
            // FlightModel owns movement. Triggers define contact geometry without another solver moving the aircraft.
            circle.isTrigger = true;
        }
    }
}
