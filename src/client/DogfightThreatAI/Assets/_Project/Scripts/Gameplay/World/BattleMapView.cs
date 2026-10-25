using System.Collections.Generic;
using DogfightThreatAI.Gameplay.Config;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DogfightThreatAI.Gameplay.World
{
    /// <summary>只绘制镜头附近的地图分块，不参与飞行、碰撞和威胁网格计算。</summary>
    [ExecuteAlways, DefaultExecutionOrder(1000), DisallowMultipleComponent]
    public sealed class BattleMapView : MonoBehaviour
    {
        const int ChunkSize = 32;
        // 镜头可越过物理边界 1.2 单位；四格外沿仅绘制边缘地形，不扩大地图或碰撞范围。
        const int VisualPaddingTiles = 4;
        [SerializeField] BattleMapData map;
        [SerializeField] Camera targetCamera;
        [SerializeField] int sortingOrder = -50;

        readonly Dictionary<Vector2Int, Tilemap> _visible = new Dictionary<Vector2Int, Tilemap>();
        readonly Queue<Tilemap> _pool = new Queue<Tilemap>();
        readonly List<Vector2Int> _expired = new List<Vector2Int>();
        readonly TileBase[] _block = new TileBase[ChunkSize * ChunkSize];
        Tile[] _palette;
        byte[] _layout;
        GameObject _root;
        RectInt _loadedRegion;
        bool _hasRegion;

        public BattleMapData Map => map;
        public int LoadedChunkCount => _visible.Count;

        void OnEnable() => Rebuild();
        void OnDisable() => Clear();
        void LateUpdate() => RefreshVisibleTiles();

        public void Rebuild()
        {
            Clear();
            if (map == null || !map.IsValid) return;
            _layout = map.Layout.bytes;
            _palette = new Tile[map.Palette.Length];
            for (int i = 0; i < _palette.Length; i++)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.hideFlags = HideFlags.HideAndDontSave;
                tile.sprite = map.Palette[i];
                tile.colliderType = Tile.ColliderType.None;
                _palette[i] = tile;
            }
            _root = new GameObject("Map chunks (generated)", typeof(Grid));
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.transform.SetParent(transform, false);
            _root.transform.localPosition = -(Vector3)(map.WorldSize * .5f);
            _root.GetComponent<Grid>().cellSize = new Vector3(map.TileWorldSize, map.TileWorldSize, 1f);
            RefreshVisibleTiles();
        }

        public void RefreshVisibleTiles()
        {
            if (_root == null || _layout == null) return;
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null || !cam.orthographic) return;
            var center = _root.transform.InverseTransformPoint(cam.transform.position);
            float halfY = cam.orthographicSize;
            float halfX = halfY * cam.aspect;
            float span = map.TileWorldSize * ChunkSize;
            int firstChunk = Mathf.FloorToInt(-VisualPaddingTiles / (float)ChunkSize);
            int lastX = (map.Width + VisualPaddingTiles - 1) / ChunkSize;
            int lastY = (map.Height + VisualPaddingTiles - 1) / ChunkSize;
            // 一格余量避免相机插值/像素取样露出空边；换分块时才重建，逐帧不分配数组。
            float pad = map.TileWorldSize;
            int minX = Mathf.Clamp(Mathf.FloorToInt((center.x - halfX - pad) / span), firstChunk, lastX);
            int maxX = Mathf.Clamp(Mathf.FloorToInt((center.x + halfX + pad) / span), firstChunk, lastX);
            int minY = Mathf.Clamp(Mathf.FloorToInt((center.y - halfY - pad) / span), firstChunk, lastY);
            int maxY = Mathf.Clamp(Mathf.FloorToInt((center.y + halfY + pad) / span), firstChunk, lastY);
            var region = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            if (_hasRegion && region.Equals(_loadedRegion)) return;
            _loadedRegion = region;
            _hasRegion = true;

            _expired.Clear();
            foreach (var entry in _visible)
                if (!region.Contains(entry.Key)) _expired.Add(entry.Key);
            foreach (var key in _expired)
            {
                var tilemap = _visible[key];
                tilemap.ClearAllTiles();
                tilemap.gameObject.SetActive(false);
                _pool.Enqueue(tilemap);
                _visible.Remove(key);
            }
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var key = new Vector2Int(x, y);
                    if (_visible.ContainsKey(key)) continue;
                    var chunk = GetChunk();
                    Fill(chunk, x * ChunkSize, y * ChunkSize);
                    _visible.Add(key, chunk);
                }
        }

        Tilemap GetChunk()
        {
            if (_pool.Count > 0)
            {
                var reused = _pool.Dequeue();
                reused.gameObject.SetActive(true);
                return reused;
            }
            var go = new GameObject("Terrain chunk", typeof(Tilemap), typeof(TilemapRenderer));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(_root.transform, false);
            go.GetComponent<TilemapRenderer>().sortingOrder = sortingOrder;
            return go.GetComponent<Tilemap>();
        }

        void Fill(Tilemap chunk, int startX, int startY)
        {
            for (int y = 0; y < ChunkSize; y++)
                for (int x = 0; x < ChunkSize; x++)
                {
                    int gx = startX + x, gy = startY + y;
                    Tile tile = null;
                    if (gx >= -VisualPaddingTiles && gx < map.Width + VisualPaddingTiles &&
                        gy >= -VisualPaddingTiles && gy < map.Height + VisualPaddingTiles)
                    {
                        int sourceX = Mathf.Clamp(gx, 0, map.Width - 1);
                        int sourceY = Mathf.Clamp(gy, 0, map.Height - 1);
                        int index = _layout[(map.Height - 1 - sourceY) * map.Width + sourceX];
                        if (index < _palette.Length) tile = _palette[index];
                    }
                    _block[y * ChunkSize + x] = tile;
                }
            chunk.SetTilesBlock(new BoundsInt(startX, startY, 0, ChunkSize, ChunkSize, 1), _block);
        }

        void Clear()
        {
            if (_root != null) Release(_root);
            _root = null;
            if (_palette != null)
                foreach (var tile in _palette) if (tile != null) Release(tile);
            _palette = null;
            _layout = null;
            _visible.Clear();
            _pool.Clear();
            _expired.Clear();
            _hasRegion = false;
        }

        static void Release(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
