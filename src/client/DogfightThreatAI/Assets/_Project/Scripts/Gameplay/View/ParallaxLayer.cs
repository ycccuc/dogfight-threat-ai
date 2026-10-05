using UnityEngine;

namespace Dogfight.Gameplay
{
    /// <summary>
    /// 单层视差背景。
    ///
    /// 做法：这一层整体跟随相机，但只按 factor 的比例移动 —— factor 越小，看起来离得越远。
    /// 配合 `SpriteRenderer.drawMode = Tiled` 或一张够大的可平铺贴图，
    /// 位置按贴图尺寸取余循环，就能得到无限滚动的效果。
    ///
    /// 排序层按 docs/02-design/美术规范.md：Background / Far / Mid / Near。
    /// 相机上不需要挂任何东西 —— 每层自己去问相机位置，这样加层不用改相机。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParallaxLayer : MonoBehaviour
    {
        [Header("─ 视差 ─")]
        [Tooltip("0 = 完全不动（最远），1 = 和相机同步（最近，等于不产生视差）")]
        [Range(0f, 1f)]
        [SerializeField] float _factor = 0.4f;

        [Tooltip("留空则自动取 Camera.main")]
        [SerializeField] Transform _camera;

        [Header("─ 循环 ─")]
        [Tooltip("勾上则按贴图尺寸循环，做出无限滚动")]
        [SerializeField] bool _wrap = true;

        [Tooltip("循环的贴图尺寸（世界单位）。填 0 则自动取 SpriteRenderer 的贴图宽度/高度")]
        [SerializeField] Vector2 _tileSize = Vector2.zero;

        [Tooltip("为 0 的轴不做循环（例如竖直方向不需要重复）")]
        [SerializeField] bool _wrapX = true;

        [SerializeField] bool _wrapY;

        Vector3 _startPosition;
        Vector3 _cameraStart;
        Vector2 _resolvedTile;

        Transform CameraTransform
        {
            get
            {
                if (_camera == null && Camera.main != null) _camera = Camera.main.transform;
                return _camera;
            }
        }

        void Awake()
        {
            _startPosition = transform.position;
            _resolvedTile = ResolveTileSize();

            Transform cam = CameraTransform;
            if (cam != null) _cameraStart = cam.position;

            if (_wrap && (_resolvedTile.x <= 0f || _resolvedTile.y <= 0f))
            {
                Debug.LogWarning("[ParallaxLayer] " + name +
                                 " 开启了循环但拿不到贴图尺寸，已自动关闭循环。" +
                                 "请把 SpriteRenderer 的 Draw Mode 设为 Tiled，或手动填 Tile Size。");
                _wrap = false;
            }
        }

        void LateUpdate()
        {
            Transform cam = CameraTransform;
            if (cam == null) return;

            // 相机相对起始位置的位移，按 factor 折算到本层
            Vector3 delta = cam.position - _cameraStart;
            float offsetX = delta.x * _factor;
            float offsetY = delta.y * _factor;

            // 取余实现无限循环：只在开启循环的轴上做
            if (_wrap && _wrapX && _resolvedTile.x > 0f) offsetX = Mathf.Repeat(offsetX, _resolvedTile.x);
            if (_wrap && _wrapY && _resolvedTile.y > 0f) offsetY = Mathf.Repeat(offsetY, _resolvedTile.y);

            transform.position = new Vector3(
                _startPosition.x + offsetX,
                _startPosition.y + offsetY,
                _startPosition.z);
        }

        Vector2 ResolveTileSize()
        {
            if (_tileSize.x > 0f && _tileSize.y > 0f) return _tileSize;

            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                Vector3 size = renderer.sprite.bounds.size;
                return new Vector2(size.x, size.y);
            }

            return Vector2.zero;
        }

        /// <summary>让场景搭建工具能在运行时（编辑器脚本）设置层级顺序。</summary>
        public void Configure(float factor, string sortingLayerName, int orderInLayer, bool wrapX)
        {
            _factor = Mathf.Clamp01(factor);
            _wrap = true;
            _wrapX = wrapX;
            _wrapY = false;

            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null && !string.IsNullOrEmpty(sortingLayerName))
            {
                renderer.sortingLayerName = sortingLayerName;
                renderer.sortingOrder = orderInLayer;
            }
        }
    }
}
