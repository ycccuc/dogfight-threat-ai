using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    /// <summary>
    /// Grid2D 是最底层的换算工具，错一格，威胁网格和 A* 全错。
    /// 所以这里把边界情况都钉死：负坐标、边界点、越界、索引往返。
    /// </summary>
    public sealed class Grid2DTests
    {
        static Grid2D NewGrid()
        {
            return Grid2D.Centered(new Vec2(400f, 300f), 4f);
        }  // 100 × 75

        [Test]
        public void 尺寸换算_400x300除以4_应该是100x75()
        {
            var g = NewGrid();
            Assert.AreEqual(100, g.Width);
            Assert.AreEqual(75, g.Height);
            Assert.AreEqual(7500, g.CellCount);
        }

        [Test]
        public void 世界坐标转格子_边界与中心都要对()
        {
            var g = NewGrid();

            // 左下角原点 → 格 (0,0)
            Assert.IsTrue(g.TryWorldToCell(new Vec2(-200f, -150f), out int x0, out int y0));
            Assert.AreEqual(0, x0);
            Assert.AreEqual(0, y0);

            // 右上角内侧一点 → 格 (99,74)
            Assert.IsTrue(g.TryWorldToCell(new Vec2(199.9f, 149.9f), out int x1, out int y1));
            Assert.AreEqual(99, x1);
            Assert.AreEqual(74, y1);

            // 格 (0,0) 的中心应落回 (0,0)
            Vec2 c = g.CellCenter(0, 0);
            Assert.IsTrue(g.TryWorldToCell(c, out int x2, out int y2));
            Assert.AreEqual(0, x2);
            Assert.AreEqual(0, y2);
            Assert.AreEqual(-198f, c.X, 1e-3f);
            Assert.AreEqual(-148f, c.Y, 1e-3f);
        }

        [Test]
        public void 负坐标不能被强转截断()
        {
            // 这条专门防 "(int)(-0.5) == 0" 那个坑
            var g = NewGrid();
            Assert.IsTrue(g.TryWorldToCell(new Vec2(-199.9f, -149.9f), out int x, out int y));
            Assert.AreEqual(0, x, "负坐标应落在格 0，而不是 -1 或错误格");
            Assert.AreEqual(0, y);
        }

        [Test]
        public void 越界返回false_并给出负一()
        {
            var g = NewGrid();
            Assert.IsFalse(g.TryWorldToCell(new Vec2(200.1f, 0f), out int x, out int y));
            Assert.AreEqual(-1, x);
            Assert.AreEqual(-1, y);

            Assert.IsFalse(g.TryWorldToCell(new Vec2(0f, -150.1f), out _, out _));
            Assert.IsFalse(g.TryToIndex(new Vec2(500f, 500f), out int idx));
            Assert.AreEqual(-1, idx);
        }

        [Test]
        public void 夹紧转换_越界也不会崩()
        {
            var g = NewGrid();
            g.WorldToCellClamped(new Vec2(9999f, -9999f), out int x, out int y);
            Assert.AreEqual(99, x);
            Assert.AreEqual(0, y);
        }

        [Test]
        public void 索引往返一致_行优先()
        {
            var g = NewGrid();
            Assert.AreEqual(7499, g.ToIndex(99, 74));
            g.FromIndex(7499, out int x, out int y);
            Assert.AreEqual(99, x);
            Assert.AreEqual(74, y);

            // 同一行连续：索引差 1
            Assert.AreEqual(g.ToIndex(50, 10) + 1, g.ToIndex(51, 10));
            // 同行相邻在内存里连续（行优先的意义）
            Assert.AreEqual(g.Width, g.ToIndex(0, 11) - g.ToIndex(0, 10));
        }

        [Test]
        public void 邻居查询_边界外不算()
        {
            var g = NewGrid();

            // (0,0) 的左邻居和下邻居都在界外
            Assert.IsFalse(g.TryNeighbor4(0, 0, 3, out _, out _));   // (-1, 0)
            Assert.IsFalse(g.TryNeighbor4(0, 0, 2, out _, out _));   // (0, -1)
            Assert.IsTrue(g.TryNeighbor4(0, 0, 1, out int rx, out int ry)); // (1, 0)
            Assert.AreEqual(1, rx);
            Assert.AreEqual(0, ry);

            // 内部格：8 邻居全在界内
            int valid = 0;
            for (int d = 0; d < Grid2D.Neighbors8.Length; d++)
            {
                if (g.TryNeighbor8(10, 10, d, out _, out _))
                {
                    valid++;
                }
            }
            Assert.AreEqual(8, valid);
        }
    }
}
