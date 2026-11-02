using System;
using System.Collections.Generic;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI.Pathfinding
{
    /// <summary>四邻接 BFS / A*。缓存队列、父节点和二叉堆，多次搜索不重复创建数组。</summary>
    public sealed class GridPathfinder
    {
        readonly Grid2D _grid;
        readonly int[] _parents, _queue, _heap, _heapPositions;
        readonly float[] _distance, _score;
        readonly bool[] _closed;
        int _heapCount;

        public int ExpandedNodes
        {
            get; private set;
        }

        // 按网格容量分配搜索缓冲，避免每次寻路重新分配。
        public GridPathfinder(Grid2D grid)
        {
            _grid = grid;
            _parents = new int[grid.CellCount];
            _queue = new int[grid.CellCount];
            _heap = new int[grid.CellCount];
            _heapPositions = new int[grid.CellCount];
            _distance = new float[grid.CellCount];
            _score = new float[grid.CellCount];
            _closed = new bool[grid.CellCount];
        }

        /// <summary>无权图的最少步数路径。不可达返回 false，输出清空。</summary>
        public bool BreadthFirst(int start, int goal, bool[] blocked, List<int> path)
        {
            Reset(start, goal, blocked, null, path);
            if (Blocked(start, blocked) || Blocked(goal, blocked))
            {
                return false;
            }
            int head = 0, tail = 0;
            _queue[tail++] = start;
            _parents[start] = start;
            while (head < tail)
            {
                int current = _queue[head++];
                ExpandedNodes++;
                if (current == goal)
                {
                    return Reconstruct(start, goal, path);
                }
                _grid.FromIndex(current, out int x, out int y);
                for (int d = 0; d < 4; d++)
                {
                    if (!_grid.TryNeighbor4(x, y, d, out int nx, out int ny))
                    {
                        continue;
                    }
                    int next = _grid.ToIndex(nx, ny);
                    if (_parents[next] != -1 || Blocked(next, blocked))
                    {
                        continue;
                    }
                    _parents[next] = current;
                    _queue[tail++] = next;
                }
            }
            return false;
        }

        /// <summary>A*：代价至少为 1，曼哈顿启发式可采纳；最小堆支持 decrease-key。</summary>
        public bool AStar(int start, int goal, bool[] blocked, float[] costs, List<int> path)
        {
            Reset(start, goal, blocked, costs, path);
            if (Blocked(start, blocked) || Blocked(goal, blocked))
            {
                return false;
            }
            _distance[start] = 0;
            _parents[start] = start;
            _score[start] = Heuristic(start, goal);
            PushOrDecrease(start);
            while (_heapCount > 0)
            {
                int current = Pop();
                ExpandedNodes++;
                if (current == goal)
                {
                    return Reconstruct(start, goal, path);
                }
                _closed[current] = true;
                _grid.FromIndex(current, out int x, out int y);
                for (int d = 0; d < 4; d++)
                {
                    if (!_grid.TryNeighbor4(x, y, d, out int nx, out int ny))
                    {
                        continue;
                    }
                    int next = _grid.ToIndex(nx, ny);
                    if (_closed[next] || Blocked(next, blocked))
                    {
                        continue;
                    }
                    float distance = _distance[current] + (costs == null ? 1f : costs[next]);
                    if (distance >= _distance[next])
                    {
                        continue;
                    }
                    _distance[next] = distance;
                    _parents[next] = current;
                    _score[next] = distance + Heuristic(next, goal);
                    PushOrDecrease(next);
                }
            }
            return false;
        }

        // 检查搜索参数并清理本次搜索会用到的缓存。
        void Reset(int start, int goal, bool[] blocked, float[] costs, List<int> path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            path.Clear();
            ExpandedNodes = 0;
            _heapCount = 0;
            int count = _grid.CellCount;
            if (start < 0 || start >= count || goal < 0 || goal >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            if (blocked != null && blocked.Length != count)
            {
                throw new ArgumentException(nameof(blocked));
            }
            if (costs != null && costs.Length != count)
            {
                throw new ArgumentException(nameof(costs));
            }
            for (int i = 0; i < count; i++)
            {
                if (costs != null && (costs[i] < 1 || float.IsNaN(costs[i]) || float.IsInfinity(costs[i])))
                {
                    throw new ArgumentOutOfRangeException(nameof(costs));
                }
                _parents[i] = _heapPositions[i] = -1;
                _closed[i] = false;
                _distance[i] = float.PositiveInfinity;
            }
        }

        // 查询指定格子是否被当前障碍数据阻挡。
        static bool Blocked(int index, bool[] blocked)
        {
            return blocked != null && blocked[index];
        }

        // 计算四邻接网格到目标的曼哈顿距离估计。
        float Heuristic(int a, int b)
        {
            _grid.FromIndex(a, out int ax, out int ay);
            _grid.FromIndex(b, out int bx, out int by);
            return System.Math.Abs(ax - bx) + System.Math.Abs(ay - by);
        }

        // 沿父节点回溯并输出从起点到目标的路径。
        bool Reconstruct(int start, int goal, List<int> path)
        {
            for (int at = goal; ; at = _parents[at])
            {
                path.Add(at);
                if (at == start)
                {
                    break;
                }
            }
            path.Reverse();
            return true;
        }

        // 将候选格加入最小堆，或调整其降低后的优先级。
        void PushOrDecrease(int node)
        {
            int index = _heapPositions[node];
            if (index < 0)
            {
                index = _heapCount++;
                _heap[index] = node;
                _heapPositions[node] = index;
            }
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Less(_heap[index], _heap[parent]))
                {
                    break;
                }
                Swap(index, parent);
                index = parent;
            }
        }

        // 取出当前优先级最高的候选格，并恢复堆结构。
        int Pop()
        {
            int node = _heap[0];
            _heapPositions[node] = -1;
            _heapCount--;
            if (_heapCount == 0)
            {
                return node;
            }
            _heap[0] = _heap[_heapCount];
            _heapPositions[_heap[0]] = 0;
            int at = 0;
            while (at * 2 + 1 < _heapCount)
            {
                int child = at * 2 + 1;
                if (child + 1 < _heapCount && Less(_heap[child + 1], _heap[child]))
                {
                    child++;
                }
                if (!Less(_heap[child], _heap[at]))
                {
                    break;
                }
                Swap(at, child);
                at = child;
            }
            return node;
        }

        // 比较候选格的排序优先级，供最小堆维护使用。
        bool Less(int a, int b)
        {
            return _score[a] < _score[b] || (_score[a] == _score[b] && a < b);
        }

        // 交换堆中节点，并同步更新它们的位置索引。
        void Swap(int a, int b)
        {
            int temp = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = temp;
            _heapPositions[_heap[a]] = a;
            _heapPositions[_heap[b]] = b;
        }
    }
}
