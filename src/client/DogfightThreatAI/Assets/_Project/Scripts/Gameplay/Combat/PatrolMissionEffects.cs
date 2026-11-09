using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Math;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    public sealed class PatrolMissionEffects : MonoBehaviour
    {
        static readonly Color Intel = new Color32(151, 234, 245, 255);
        readonly LineRenderer[] sparks = new LineRenderer[7];
        LineRenderer[] recordRings;
        Material material;
        Vector3 impact;
        float impactTime = float.NegativeInfinity;

        public void Build(Vec2[] positions)
        {
            material = new Material(Shader.Find("Sprites/Default"));
            var font = Resources.Load<Font>("Menu/Fonts/MenuChinese");
            recordRings = new LineRenderer[positions.Length];
            for (int i = 0; i < recordRings.Length; i++)
            {
                recordRings[i] = BuildRecord(positions[i], font);
            }
            for (int i = 0; i < sparks.Length; i++)
            {
                sparks[i] = CreateLine(transform, "Collision spark", 2, .12f);
                sparks[i].enabled = false;
            }
        }

        LineRenderer BuildRecord(Vec2 position, Font font)
        {
            var root = new GameObject("Recon record").transform;
            root.SetParent(transform, false);
            root.position = new Vector3(position.X, position.Y, -.15f);
            const int segments = 36;
            var ring = CreateLine(root, "Pickup radius", segments, .055f, false);
            ring.loop = true;
            for (int i = 0; i < segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * PatrolMission.PickupRadius);
            }

            var page = CreateLine(root, "Recon page", 6, .10f, false);
            page.loop = true;
            page.SetPositions(new[]
            {
                new Vector3(-.38f, -.48f), new Vector3(-.38f, .48f),
                new Vector3(.12f, .48f), new Vector3(.38f, .21f),
                new Vector3(.38f, -.48f), new Vector3(-.38f, -.48f)
            });
            for (int i = 0; i < 3; i++)
            {
                var rule = CreateLine(root, "Page detail", 2, .05f, false);
                float y = .10f - i * .18f;
                rule.SetPosition(0, new Vector3(-.21f, y));
                rule.SetPosition(1, new Vector3(.19f, y));
            }

            if (font != null)
            {
                var label = new GameObject("Recon label").AddComponent<TextMesh>();
                label.transform.SetParent(root, false);
                label.transform.localPosition = Vector3.down * 1.62f;
                label.font = font;
                label.fontSize = 48;
                label.characterSize = .10f;
                label.anchor = TextAnchor.UpperCenter;
                label.alignment = TextAlignment.Center;
                label.color = Intel;
                label.text = "侦察记录";
                var renderer = label.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = font.material;
                renderer.sortingOrder = 103;
            }
            return ring;
        }

        LineRenderer CreateLine(Transform parent, string label, int points, float width, bool worldSpace = true)
        {
            var line = new GameObject(label).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.useWorldSpace = worldSpace;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.sortingOrder = 102;
            line.startColor = line.endColor = Intel;
            return line;
        }

        public void Collect(int index) => recordRings[index].transform.parent.gameObject.SetActive(false);

        public void Impact(Vec2 point)
        {
            impact = new Vector3(point.X, point.Y, -.2f);
            impactTime = Time.time;
        }

        public void Present()
        {
            var color = Intel;
            color.a = .55f + .20f * Mathf.Sin(Time.time * 5f);
            foreach (var ring in recordRings)
            {
                ring.startColor = ring.endColor = color;
            }

            float age = (Time.time - impactTime) / .38f;
            for (int i = 0; i < sparks.Length; i++)
            {
                var spark = sparks[i];
                spark.enabled = age < 1f;
                if (!spark.enabled)
                {
                    continue;
                }
                float angle = i * (2f * Mathf.PI / sparks.Length);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                spark.SetPosition(0, impact + direction * (.15f + age * .85f));
                spark.SetPosition(1, impact + direction * (.75f + age * 1.05f));
                spark.startColor = new Color(1f, .92f, .61f, 1f - age);
                spark.endColor = new Color(1f, .48f, .17f, (1f - age) * .65f);
            }
        }

        public const float CollisionFeedbackSeconds = .75f;

        public static Color CollisionTint(Color original, float remaining)
        {
            if (remaining <= 0f)
            {
                return original;
            }
            float flash = Mathf.Clamp01((remaining - .60f) / .15f);
            var color = Color.Lerp(original, new Color(1f, .63f, .32f, original.a), flash);
            color.a *= Mathf.Lerp(1f, .42f, Mathf.Clamp01(remaining / .55f));
            return color;
        }

        void OnDestroy() => Destroy(material);
    }
}
