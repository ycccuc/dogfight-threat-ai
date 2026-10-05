using System.Collections.Generic;

namespace Dogfight.Ai
{
    /// <summary>
    /// 巡逻航线生成器。
    ///
    /// 输入：一片"我这个空域"（用 BFS 从种子格扩散得到），
    /// 输出：一条闭环航线（世界坐标的路点序列）。
    ///
    /// 算法分两步，都是确定性的（同样的地形 + 同样的种子 → 同样的航线）：
    ///   1. 最远点采样：从区域里挑出 maxWaypoints 个互相离得最远的格子 —— 覆盖均匀，不会挤成一团
    ///   2. 最近邻排序：把它们串成一条尽量短的闭环
    /// 这样做出来的巡逻是"来回扫这片空域"，而不是立项书里批评的"固定航线脚本"——
    /// 航线是由**地形**算出来的，地形变了航线就变。
    ///
    /// 一个有状态的类：内部缓冲区复用，避免每个 AI 生成航线时反复分配。
    /// </summary>
    public sealed class PatrolRouteBuilder
    {
        readonly GridMap _map;
        readonly BfsScratch _scratch;
        readonly List<Vec2Int> _cells = new List<Vec2Int>(512);
        readonly List<Vec2Int> _picked = new List<Vec2Int>(64);
        readonly List<float> _minDistance = new List<float>(64);
        readonly List<Vec2Int> _ordered = new List<Vec2Int>(64);

        public PatrolRouteBuilder(GridMap map, BfsScratch scratch = null)
        {
            _map = map;
            _scratch = scratch ?? new BfsScratch();
        }

        /// <summary>
        /// 生成闭环航线，写进 outRoute（世界坐标）。返回路点数；区域无效时返回 0。
        /// </summary>
        /// <param name="regionSeed">空域种子格（一般是敌机的出生格）</param>
        /// <param name="regionRadius">空域半径（格，BFS 步数）</param>
        /// <param name="maxWaypoints">最多几个路点（2~8 比较合适）</param>
        public int Build(Vec2Int regionSeed, int regionRadius, int maxWaypoints, List<Vec2> outRoute)
        {
            outRoute.Clear();
            if (_map == null) return 0;

            BfsPathfinder.CollectReachable(_map, regionSeed, regionRadius, _scratch, _cells);
            if (_cells.Count == 0) return 0;

            int want = MathUtil.Clamp(maxWaypoints, 1, _cells.Count);
            PickSpreadCells(regionSeed, want);
            OrderNearestNeighbour(regionSeed);

            for (int i = 0; i < _ordered.Count; i++)
            {
                outRoute.Add(_map.CellCenter(_ordered[i]));
            }

            // 闭环：回到起点。路点数 ≥ 3 时才有意义的"环"。
            if (outRoute.Count >= 2) outRoute.Add(outRoute[0]);
            return outRoute.Count;
        }

        /// <summary>
        /// 最远点采样：第一个取离种子最远的格子，之后每次取"离已选集合最远"的格子。
        /// 因为是按距离确定性挑选，所以结果可复现。
        /// </summary>
        void PickSpreadCells(Vec2Int seed, int want)
        {
            _picked.Clear();
            _minDistance.Clear();
            if (_cells.Count == 0) return;

            int firstIndex = 0;
            float bestDistance = -1f;
            for (int i = 0; i < _cells.Count; i++)
            {
                float d = (float)_cells[i].ManhattanTo(seed);
                if (d > bestDistance)
                {
                    bestDistance = d;
                    firstIndex = i;
                }
            }

            _picked.Add(_cells[firstIndex]);
            for (int i = 0; i < _cells.Count; i++)
            {
                _minDistance.Add(DistanceSquared(_cells[i], _cells[firstIndex]));
            }

            while (_picked.Count < want)
            {
                int nextIndex = -1;
                float nextBest = -1f;
                for (int i = 0; i < _cells.Count; i++)
                {
                    if (_minDistance[i] > nextBest)
                    {
                        nextBest = _minDistance[i];
                        nextIndex = i;
                    }
                }

                if (nextIndex < 0 || nextBest <= 0f) break;

                Vec2Int chosen = _cells[nextIndex];
                _picked.Add(chosen);
                for (int i = 0; i < _cells.Count; i++)
                {
                    float d = DistanceSquared(_cells[i], chosen);
                    if (d < _minDistance[i]) _minDistance[i] = d;
                }
            }
        }

        /// <summary>最近邻排序：从离种子最近的点出发，每次走最近的未访问点。</summary>
        void OrderNearestNeighbour(Vec2Int seed)
        {
            _ordered.Clear();
            if (_picked.Count == 0) return;

            int startIndex = 0;
            float best = float.MaxValue;
            for (int i = 0; i < _picked.Count; i++)
            {
                float d = (float)_picked[i].ManhattanTo(seed);
                if (d < best)
                {
                    best = d;
                    startIndex = i;
                }
            }

            var used = new bool[_picked.Count];
            int current = startIndex;
            for (int step = 0; step < _picked.Count; step++)
            {
                used[current] = true;
                _ordered.Add(_picked[current]);

                int next = -1;
                float nextBest = float.MaxValue;
                for (int i = 0; i < _picked.Count; i++)
                {
                    if (used[i]) continue;
                    float d = (float)_picked[current].ManhattanTo(_picked[i]);
                    if (d < nextBest)
                    {
                        nextBest = d;
                        next = i;
                    }
                }
                if (next < 0) break;
                current = next;
            }
        }

        static float DistanceSquared(Vec2Int a, Vec2Int b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
