using DogfightThreatAI.Core.World;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    public sealed class PlayerPropulsionView : MonoBehaviour
    {
        LineRenderer trail;
        Material material;

        public void Build(SpriteRenderer aircraft)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return;
            }
            material = new Material(shader);
            trail = new GameObject("PlayerPropulsionTrail").AddComponent<LineRenderer>();
            trail.transform.SetParent(transform, false);
            trail.sharedMaterial = material;
            trail.positionCount = 2;
            trail.startWidth = .17f;
            trail.endWidth = .025f;
            if (aircraft != null)
            {
                trail.sortingLayerID = aircraft.sortingLayerID;
                trail.sortingOrder = aircraft.sortingOrder - 1;
            }
            trail.enabled = false;
        }

        public void Present(bool alive, VisiblePropulsionCue cue, float speed, Vector3 position, Vector3 forward)
        {
            if (trail == null)
            {
                return;
            }
            trail.enabled = alive && cue != VisiblePropulsionCue.None;
            if (!trail.enabled)
            {
                return;
            }
            bool hot = cue == VisiblePropulsionCue.Overheat;
            float length = hot ? .65f : Mathf.Lerp(.8f, 2.5f, Mathf.InverseLerp(9f, 21.6f, speed));
            trail.SetPosition(0, position - forward * .45f);
            trail.SetPosition(1, position - forward * (.45f + length));
            var color = hot ? new Color(1f, .48f, .15f, .7f) : new Color(.55f, .95f, 1f, .85f);
            trail.startColor = color;
            color.a = 0f;
            trail.endColor = color;
        }

        void OnDestroy()
        {
            if (trail != null)
            {
                Destroy(trail.gameObject);
            }
            Destroy(material);
        }
    }
}
