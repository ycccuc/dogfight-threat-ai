using System.Collections.Generic;
using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// BFS 寻路与巡逻航线的单测。
    ///
    /// 这些是立项书里 "BFS 区域巡逻与目标搜索" 的核心逻辑，
    /// 也是暑期原型完全没有的部分 —— 先有测试，才敢往上面加行为树。
    /// </summary>
    public sealed class BfsPathfinderTests
    {
        static GridMap OpenMap(int w = 10, int h = 10) => new GridMap(w, h, 1f, Vec2.Zero);

        static GridMap MapWithWall()
        {
            var map = new GridMap(10, 10, 1f, Vec2.Zero);
            // 在第 x=5 列砌一堵墙，只留 y=9 一个缺口
            for (int y = 0; y < 9; y++) map.SetWalkable(new Vec2Int(5, y), false);
            return map;
        }

        [Test]
        public void 空地直线路径长度等于曼哈顿距离加一()
        {
            GridMap map = OpenMap();
            var scratch = new BfsScratch(map.CellCount);
            var path = new List<Vec2Int>();

            bool found = BfsPathfinder.TryFindPath(map, new Vec2Int(0, 0), new Vec2Int(3, 4), scratch, path);

            Assert.IsTrue(found);
            Assert.AreEqual(8, path.Count, "0,0 → 3,4 的曼哈顿距离是 7，路上共 8 个格子");
            Assert.AreEqual(new Vec2Int(0, 0), path[0]);
            Assert.AreEqual(new Vec2Int(3, 4), path[path.Count - 1]);
        }

        [Test]
        public void 路径每一步都是四邻域相邻的()
        {
            GridMap map = OpenMap();
            var scratch = new BfsScratch(map.CellCount);
            var path = new List<Vec2Int>();

            BfsPathfinder.TryFindPath(map, new Vec2Int(1, 1), new Vec2Int(8, 6), scratch, path);

            for (int i = 1; i < path.Count; i++)
            {
                int dx = System.Math.Abs(path[i].X - path[i - 1].X);
                int dy = System.Math.Abs(path[i].Y - path[i - 1].Y);
                Assert.AreEqual(1, dx + dy, "第 " + i + " 步不是四邻域相邻");
            }
        }

        [Test]
        public void 墙会迫使路径绕行()
        {
            GridMap map = MapWithWall();
            var scratch = new BfsScratch(map.CellCount);
            var path = new List<Vec2Int>();

            bool found = BfsPathfinder.TryFindPath(map, new Vec2Int(0, 0), new Vec2Int(9, 0), scratch, path);

            Assert.IsTrue(found, "墙上留了缺口，应该能绕过去");
            Assert.Greater(path.Count, 10, "绕行路径必然比直线长");
            for (int i = 0; i < path.Count; i++)
            {
                Assert.IsTrue(map.IsWalkable(path[i]), "路径经过了不可行走的格子 " + path[i]);
            }
        }

        [Test]
        public void 完全封死时返回不可达()
        {
            var map = new GridMap(10, 10, 1f, Vec2.Zero);
            for (int y = 0; y < 10; y++) map.SetWalkable(new Vec2Int(5, y), false);

            var scratch = new BfsScratch(map.CellCount);
            var path = new List<Vec2Int>();

            bool found = BfsPathfinder.TryFindPath(map, new Vec2Int(0, 0), new Vec2Int(9, 0), scratch, path);

            Assert.IsFalse(found);
            Assert.AreEqual(0, path.Count, "失败时不应残留半条路径");
        }

        [Test]
        public void 起点或终点不可行走时直接失败()
        {
            GridMap map = OpenMap();
            map.SetWalkable(new Vec2Int(0, 0), false);
            var scratch = new BfsScratch(map.CellCount);
            var path = new List<Vec2Int>();

            Assert.IsFalse(BfsPathfinder.TryFindPath(map, new Vec2Int(0, 0), new Vec2Int(3, 3), scratch, path));
            Assert.IsFalse(BfsPathfinder.TryFindPath(map, new Vec2Int(3, 3), new Vec2Int(0, 0), scratch, path));
        }

        [Test]
        public void 越界坐标一律不可行走()
        {
            GridMap map = OpenMap();
            Assert.IsFalse(map.IsWalkable(new Vec2Int(-1, 0)));
            Assert.IsFalse(map.IsWalkable(new Vec2Int(10, 0)));
            Assert.IsFalse(map.IsWalkable(new Vec2Int(0, 10)));
        }

        [Test]
        public void 反复调用不会互相污染_代数戳机制正确()
        {
            GridMap map = OpenMap();
            var scratch = new BfsScratch(map.CellCount);
            var path = new List<Vec2Int>();

            for (int i = 0; i < 50; i++)
            {
                bool found = BfsPathfinder.TryFindPath(map, new Vec2Int(0, 0), new Vec2Int(9, 9), scratch, path);
                Assert.IsTrue(found, "第 " + i + " 次调用失败 —— 复用缓冲区时状态被污染了");
                Assert.AreEqual(19, path.Count);
            }
        }

        [Test]
        public void 可达区域收集不超过指定半径()
        {
            GridMap map = OpenMap(20, 20);
            var scratch = new BfsScratch(map.CellCount);
            var cells = new List<Vec2Int>();

            BfsPathfinder.CollectReachable(map, new Vec2Int(10, 10), maxSteps: 3, scratch, cells);

            for (int i = 0; i < cells.Count; i++)
            {
                Assert.LessOrEqual(cells[i].ManhattanTo(new Vec2Int(10, 10)), 3, "收集到了超出半径的格子: " + cells[i]);
            }
            Assert.AreEqual(25, cells.Count, "半径 3 的菱形区域共 1+4+8+12 = 25 格");
        }

        [Test]
        public void 孤岛能被统计出来()
        {
            var map = new GridMap(10, 10, 1f, Vec2.Zero);
            // 用墙把右上一格封成孤岛
            map.SetWalkable(new Vec2Int(5, 5), false);
            map.SetWalkable(new Vec2Int(6, 8), false);
            map.SetWalkable(new Vec2Int(8, 7), false);

            var scratch = new BfsScratch(map.CellCount);
            var cells = new List<Vec2Int>();
            int unreachable = BfsPathfinder.CountUnreachableWalkable(map, new Vec2Int(0, 0), 200, scratch, cells);

            Assert.AreEqual(3, unreachable, "三个被围死的格子应被统计为不可达");
        }

        [Test]
        public void 巡逻航线是闭环且落在区域附近()
        {
            GridMap map = OpenMap(20, 20);
            var builder = new PatrolRouteBuilder(map);
            var route = new List<Vec2>();

            int count = builder.Build(new Vec2Int(10, 10), regionRadius: 4, maxWaypoints: 5, route);

            Assert.Greater(count, 0);
            Assert.AreEqual(route[0].X, route[route.Count - 1].X, 1e-4f, "航线应该是闭环");
            Assert.AreEqual(route[0].Y, route[route.Count - 1].Y, 1e-4f);
            for (int i = 0; i < route.Count; i++)
            {
                // 半径 4 的格子，格心最多偏出 (4+1)*cellSize
                Assert.LessOrEqual(Vec2.Distance(route[i], new Vec2(10.5f, 10.5f)), 5.01f, "路点跑出空域了");
            }
        }

        [Test]
        public void 同一地形与种子生成的航线完全一致()
        {
            GridMap map = OpenMap(20, 20);
            var builder = new PatrolRouteBuilder(map);
            var first = new List<Vec2>();
            var second = new List<Vec2>();

            builder.Build(new Vec2Int(5, 5), 3, 4, first);
            builder.Build(new Vec2Int(5, 5), 3, 4, second);

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].X, second[i].X, 1e-5f, "航线必须可复现（批量对战要求）");
                Assert.AreEqual(first[i].Y, second[i].Y, 1e-5f);
            }
        }
    }
}
