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
                if (volume == null) volume = GetComponent<CircleCollider2D>();
                return volume;
            }
        }

        public float WorldRadius
        {
            get
            {
                var circle = Collider;
                if (circle == null) return 0f;
                Vector3 scale = transform.lossyScale;
                return circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }
        }

        public bool IsActive => isActiveAndEnabled && Collider != null && Collider.enabled &&
            Collider.gameObject.activeInHierarchy && WorldRadius > 0f &&
            (Collider.attachedRigidbody == null || Collider.attachedRigidbody.simulated);

        void Reset()
        {
            volume = GetComponent<CircleCollider2D>();
            if (volume != null) volume.radius = .5f;
            AnchorVolume();
        }

        void Awake() => AnchorVolume();
        void OnValidate() => AnchorVolume();

        void AnchorVolume()
        {
            var circle = Collider;
            if (circle == null) return;
            circle.offset = Vector2.zero;
            // FlightModel owns movement. Triggers define contact geometry without another solver moving the aircraft.
            circle.isTrigger = true;
        }
    }
}
