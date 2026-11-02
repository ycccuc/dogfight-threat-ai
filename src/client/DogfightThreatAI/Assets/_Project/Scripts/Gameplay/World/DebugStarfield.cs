using UnityEngine;

namespace DogfightThreatAI.Gameplay.World
{
    /// <summary>
    /// 开发期使用的无限平铺视差星点，为相机提供移动参照。
    ///
    /// 相机跟随目标且背景为空时，目标会始终位于屏幕中心，视觉上难以判断移动。
    /// 该组件将星点约束在相机周围的重复区域内，超出后整体平移一个 tile，
    /// 使星点始终铺满屏幕。
    /// parallaxFactor 控制星点相对相机后退的速度：
    /// 1.0 表示后退最快，适合星空；0.5 适合中景；0.0 表示与相机同步。
    ///
    /// 正式美术资源到位后，可替换为多层 SpriteRenderer 组成的视差背景。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugStarfield : MonoBehaviour
    {
        [Header("相机")]
        [Tooltip("留空则用 Camera.main")]
        [SerializeField] Camera targetCamera;

        [Header("重复区域（每块铺满后循环）")]
        [SerializeField] Vector2 tileSize = new Vector2(60f, 40f);

        [Header("数量与大小")]
        [SerializeField, Min(0)] int count = 500;
        [SerializeField] Vector2 sizeRange = new Vector2(0.05f, 0.18f);

        [Header("外观")]
        [SerializeField] Color color = new Color(1f, 1f, 1f, 0.28f);
        [SerializeField] int sortingOrder = -100;

        [Header("视差")]
        [Tooltip("1 = 世界静止（后退最快）；0.5 = 半速；0 = 完全跟随相机（不动）")]
        [SerializeField, Range(0f, 1f)] float parallaxFactor = 1f;

        [Tooltip("固定随机种子，保证每次 Play 星点分布一致（方便对比手感）")]
        [SerializeField] int seed = 20261019;

        Transform[] _stars;
        Vector3[] _bases;
        Sprite _sharedSprite;
        Material _sharedMaterial;
        Texture2D _texture;

        // 按固定种子创建共享显示资源及星点分布。
        void Start()
        {
            CreateSharedResources();

            _stars = new Transform[count];
            _bases = new Vector3[count];

            var rng = new System.Random(seed);
            float halfX = tileSize.x * 0.5f;
            float halfY = tileSize.y * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Star_{i:000}");
                go.transform.SetParent(transform, false);

                float scale = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble());
                go.transform.localScale = new Vector3(scale, scale, 1f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sharedSprite;
                sr.sharedMaterial = _sharedMaterial;
                var c = color;
                c.a *= Mathf.Lerp(0.35f, 1f, (float)rng.NextDouble());
                sr.color = c;
                sr.sortingOrder = sortingOrder;

                _bases[i] = new Vector3(
                    ((float)rng.NextDouble() * 2f - 1f) * halfX,
                    ((float)rng.NextDouble() * 2f - 1f) * halfY,
                    0f);
                _stars[i] = go.transform;
            }
        }

        // 根据相机和视差系数平移星点，并保持重复区域无缝循环。
        void LateUpdate()
        {
            if (_stars == null || _stars.Length == 0)
            {
                return;
            }
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                return;
            }
            Vector3 camPos = cam.transform.position;
            float spanX = Mathf.Max(1f, tileSize.x);
            float spanY = Mathf.Max(1f, tileSize.y);

            for (int i = 0; i < _stars.Length; i++)
            {
                // 相对相机的偏移：parallaxFactor = 1 时变成 (base - camPos) → 星点在世界里不动
                Vector3 rel = _bases[i] - new Vector3(camPos.x, camPos.y, 0f) * parallaxFactor;

                rel.x = Wrap(rel.x, spanX);
                rel.y = Wrap(rel.y, spanY);

                _stars[i].position = new Vector3(camPos.x + rel.x, camPos.y + rel.y, 0f);
            }
        }

        /// <summary>把值折到 [-span/2, span/2)，实现无缝循环。</summary>
        static float Wrap(float v, float span)
        {
            return Mathf.Repeat(v + span * 0.5f, span) - span * 0.5f;
        }

        // 创建星点共用的纹理、精灵和材质。
        void CreateSharedResources()
        {
            _texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }
            _texture.SetPixels32(pixels);
            _texture.Apply();

            _sharedSprite = Sprite.Create(_texture, new Rect(0f, 0f, 4f, 4f),
                                          new Vector2(0.5f, 0.5f), pixelsPerUnit: 4f);
            _sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        // 销毁开发背景创建的共享运行时资源。
        void OnDestroy()
        {
            if (_sharedMaterial != null)
            {
                Destroy(_sharedMaterial);
            }
            if (_sharedSprite != null)
            {
                Destroy(_sharedSprite);
            }
            if (_texture != null)
            {
                Destroy(_texture);
            }
        }
    }
}
