using System.Collections.Generic;

namespace Dogfight.Ai
{
    /// <summary>
    /// BFS 的复用缓冲区。
    ///
    /// 为什么要单独一个类：BFS 每次调用都要"重置访问标记"。如果每帧 new 数组或 Array.Clear，
    /// 在批量对战（一局几万帧 × 几十局）里就是持续的 GC 压力。
    /// 这里用**代数戳**（Generation Stamp）代替清空：只有 Stamp == 当前代数才算访问过，
    /// 于是每帧只需要 Generation++，零分配。
    ///
    /// 注意：这个类是**有状态且非线程安全**的。每个 AI / 每条线程各自持有一个。
    /// </summary>
    public sealed class BfsScratch
    {
        internal int[] CameFrom = new int[0];
        internal int[] Stamp = new int[0];
        internal int[] Depth = new int[0];
        internal int Generation;
        internal readonly Queue<int> Frontier = new Queue<int>(256);

        public BfsScratch()
        {
        }

        public BfsScratch(int cellCount)
        {
            EnsureCapacity(cellCount);
        }

        /// <summary>地图变大时扩容。扩容后旧标记失效，因此代数一并重置。</summary>
        public void EnsureCapacity(int cellCount)
        {
            if (CameFrom.Length >= cellCount) return;
            CameFrom = new int[cellCount];
            Stamp = new int[cellCount];
            Depth = new int[cellCount];
            Generation = 0;
        }

        /// <summary>
        /// 进入新一轮搜索。不清数组，只把代数加一；
        /// 代数用尽时（约 21 亿次）才真正清一次，避免溢出误判。
        /// </summary>
        public void NextGeneration()
        {
            if (Generation == int.MaxValue)
            {
                System.Array.Clear(Stamp, 0, Stamp.Length);
                Generation = 0;
            }
            Generation++;
        }
    }

    /// <summary>
    /// 宽度优先搜索（BFS）。
    ///
    /// 为什么用 BFS 而不是 A*：立项书里 BFS 的职责是**区域巡逻与目标搜索**——
    /// 这两件事要的是"覆盖一片区域"，不是"最快到达某点"。四邻域 BFS 的最短路径
    /// 也是完备且最优的，用来做巡逻航线的骨架正合适。
    /// （A* 留给追击与规避，属于后续阶段，所以这里不提前引入启发式。）
    /// </summary>
    public static class BfsPathfinder
    {
        /// <summary>
        /// 求 start → goal 的最短路径（四邻域）。
        /// 成功时 outPath 为含首尾的格子序列；不可达或端点不可行走返回 false 且 outPath 为空。
        /// </summary>
        public static bool TryFindPath(
            GridMap map,
            Vec2Int start,
            Vec2Int goal,
            BfsScratch scratch,
            List<Vec2Int> outPath)
        {
            outPath.Clear();
            if (map == null || scratch == null) return false;
            if (!map.IsWalkable(start) || !map.IsWalkable(goal)) return false;

            scratch.EnsureCapacity(map.CellCount);
            scratch.NextGeneration();
            int gen = scratch.Generation;

            int startIndex = map.IndexOf(start);
            int goalIndex = map.IndexOf(goal);

            scratch.Frontier.Clear();
            scratch.Frontier.Enqueue(startIndex);
            scratch.Stamp[startIndex] = gen;
            scratch.CameFrom[startIndex] = -1;
            scratch.Depth[startIndex] = 0;

            bool found = startIndex == goalIndex;
            while (!found && scratch.Frontier.Count > 0)
            {
                int current = scratch.Frontier.Dequeue();
                Vec2Int cell = map.CellAt(current);

                for (int i = 0; i < Vec2Int.Neighbors4.Length; i++)
                {
                    Vec2Int next = cell + Vec2Int.Neighbors4[i];
                    if (!map.IsWalkable(next)) continue;

                    int nextIndex = map.IndexOf(next);
                    if (scratch.Stamp[nextIndex] == gen) continue;

                    scratch.Stamp[nextIndex] = gen;
                    scratch.CameFrom[nextIndex] = current;
                    scratch.Depth[nextIndex] = scratch.Depth[current] + 1;

                    if (nextIndex == goalIndex)
                    {
                        found = true;
                        break;
                    }
                    scratch.Frontier.Enqueue(nextIndex);
                }
            }

            if (!found) return false;

            for (int index = goalIndex; index != -1; index = scratch.CameFrom[index])
            {
                outPath.Add(map.CellAt(index));
            }
            outPath.Reverse();
            return true;
        }

        /// <summary>
        /// 从 start 出发、在 maxSteps 步内可达的所有格子（含 start）。
        /// 用于给一架敌机划定"我这片空域"的巡逻范围。
        /// </summary>
        public static void CollectReachable(
            GridMap map,
            Vec2Int start,
            int maxSteps,
            BfsScratch scratch,
            List<Vec2Int> outCells)
        {
            outCells.Clear();
            if (map == null || scratch == null) return;
            if (!map.IsWalkable(start)) return;

            scratch.EnsureCapacity(map.CellCount);
            scratch.NextGeneration();
            int gen = scratch.Generation;

            int startIndex = map.IndexOf(start);
            scratch.Frontier.Clear();
            scratch.Frontier.Enqueue(startIndex);
            scratch.Stamp[startIndex] = gen;
            scratch.Depth[startIndex] = 0;
            outCells.Add(start);

            while (scratch.Frontier.Count > 0)
            {
                int current = scratch.Frontier.Dequeue();
                int depth = scratch.Depth[current];
                if (depth >= maxSteps) continue;

                Vec2Int cell = map.CellAt(current);
                for (int i = 0; i < Vec2Int.Neighbors4.Length; i++)
                {
                    Vec2Int next = cell + Vec2Int.Neighbors4[i];
                    if (!map.IsWalkable(next)) continue;

                    int nextIndex = map.IndexOf(next);
                    if (scratch.Stamp[nextIndex] == gen) continue;

                    scratch.Stamp[nextIndex] = gen;
                    scratch.Depth[nextIndex] = depth + 1;
                    scratch.CameFrom[nextIndex] = current;
                    outCells.Add(next);
                    scratch.Frontier.Enqueue(nextIndex);
                }
            }
        }

        /// <summary>
        /// 从 start 出发、在 maxSteps 步内**不可达**的可行走格子数。
        /// 用途：校验地形没有把地图切成孤岛（被墙隔开的区域，巡逻机永远去不了）。
        /// </summary>
        public static int CountUnreachableWalkable(GridMap map, Vec2Int start, int maxSteps, BfsScratch scratch, List<Vec2Int> reachableBuffer)
        {
            CollectReachable(map, start, maxSteps, scratch, reachableBuffer);
            return map.CountWalkable() - reachableBuffer.Count;
        }
    }
}
