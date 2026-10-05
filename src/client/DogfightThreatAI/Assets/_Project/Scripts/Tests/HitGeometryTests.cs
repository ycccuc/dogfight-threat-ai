using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 命中几何的单测。
    ///
    /// 「这发子弹到底该不该打中」是战斗数值的一部分，不该靠打开编辑器瞄一眼来判断。
    /// 尤其下面这几条边界：起点在圆内、背向射击、擦边、以及高速目标的线段判定 ——
    /// 它们出问题时的表现都是"偶尔打不中"，最难查。
    /// </summary>
    public sealed class HitGeometryTests
    {
        static CircleTarget Target(int id, float x, float y, float radius, int team = 1, bool alive = true) =>
            new CircleTarget(id, new Vec2(x, y), radius, team, alive);

        // ───────────────────────── 射线 vs 圆 ─────────────────────────

        [Test]
        public void 正对圆心时距离等于到表面的距离()
        {
            float distance = HitGeometry.RaycastCircle(Vec2.Zero, Vec2.Right, 100f, new Vec2(10f, 0f), 1f);
            Assert.AreEqual(9f, distance, 1e-3f);
        }

        [Test]
        public void 擦边仍然命中且距离稍大于正对()
        {
            float straight = HitGeometry.RaycastCircle(Vec2.Zero, Vec2.Right, 100f, new Vec2(10f, 0f), 1f);
            float grazing = HitGeometry.RaycastCircle(Vec2.Zero, Vec2.Right, 100f, new Vec2(10f, 0.9f), 1f);
            Assert.Greater(grazing, 0f, "偏 0.9、半径 1，应该命中");
            Assert.Greater(grazing, straight);
            Assert.Less(grazing, 10f);
        }

        [Test]
        public void 偏出半径就不命中()
        {
            float distance = HitGeometry.RaycastCircle(Vec2.Zero, Vec2.Right, 100f, new Vec2(10f, 1.5f), 1f);
            Assert.AreEqual(-1f, distance, "偏 1.5 > 半径 1，不该命中");
        }

        [Test]
        public void 背向目标不命中()
        {
            float distance = HitGeometry.RaycastCircle(Vec2.Zero, Vec2.Right, 100f, new Vec2(-10f, 0f), 1f);
            Assert.AreEqual(-1f, distance);
        }

        [Test]
        public void 超出射程不命中()
        {
            float distance = HitGeometry.RaycastCircle(Vec2.Zero, Vec2.Right, 5f, new Vec2(10f, 0f), 1f);
            Assert.AreEqual(-1f, distance, "目标在 9 处，射程只有 5");
        }

        [Test]
        public void 起点在圆内时返回穿出距离()
        {
            float distance = HitGeometry.RaycastCircle(new Vec2(10f, 0f), Vec2.Right, 100f, new Vec2(10f, 0f), 1f);
            Assert.AreEqual(1f, distance, 1e-3f, "从圆心射出，穿出距离应等于半径");
        }

        [Test]
        public void 退化方向不会返回NaN()
        {
            float distance = HitGeometry.RaycastCircle(Vec2.Zero, Vec2.Zero, 100f, new Vec2(10f, 0f), 1f);
            Assert.AreEqual(-1f, distance);
        }

        // ───────────────────────── 找最近命中 ─────────────────────────

        [Test]
        public void 多个目标时取最近的那个()
        {
            var targets = new[] { Target(1, 20f, 0f, 1f), Target(2, 8f, 0f, 1f), Target(3, 15f, 0f, 1f) };
            RayHit hit = HitGeometry.RaycastNearest(Vec2.Zero, Vec2.Right, 50f, targets, 3, excludeTeam: 0);

            Assert.IsTrue(hit.Hit);
            Assert.AreEqual(2, hit.TargetId, "8 处的目标最近");
            Assert.AreEqual(7f, hit.Distance, 1e-3f);
        }

        [Test]
        public void 不会打到自己人()
        {
            var targets = new[] { Target(1, 5f, 0f, 1f, team: 0), Target(2, 8f, 0f, 1f, team: 1) };
            RayHit hit = HitGeometry.RaycastNearest(Vec2.Zero, Vec2.Right, 50f, targets, 2, excludeTeam: 0);

            Assert.AreEqual(2, hit.TargetId, "同阵营的 #1 应该被跳过");
        }

        [Test]
        public void 不会打到已死亡的目标()
        {
            var targets = new[] { Target(1, 5f, 0f, 1f, team: 1, alive: false), Target(2, 8f, 0f, 1f, team: 1) };
            RayHit hit = HitGeometry.RaycastNearest(Vec2.Zero, Vec2.Right, 50f, targets, 2, excludeTeam: 0);

            Assert.AreEqual(2, hit.TargetId);
        }

        [Test]
        public void 可以排除开火者自己()
        {
            var targets = new[] { Target(7, 0f, 0f, 1f, team: 1), Target(8, 6f, 0f, 1f, team: 1) };
            RayHit hit = HitGeometry.RaycastNearest(Vec2.Zero, Vec2.Right, 50f, targets, 2, 0, ignoreId: 7);

            Assert.AreEqual(8, hit.TargetId, "应跳过 ignoreId 指定的目标");
        }

        [Test]
        public void 全场无命中时返回Miss()
        {
            var targets = new[] { Target(1, 5f, 50f, 1f) };
            RayHit hit = HitGeometry.RaycastNearest(Vec2.Zero, Vec2.Right, 50f, targets, 1, 0);
            Assert.IsFalse(hit.Hit);
            Assert.AreEqual(-1, hit.TargetId);
        }

        [Test]
        public void 命中点与法线是自洽的()
        {
            var targets = new[] { Target(1, 10f, 0f, 1f) };
            RayHit hit = HitGeometry.RaycastNearest(Vec2.Zero, Vec2.Right, 50f, targets, 1, 0);

            Assert.AreEqual(9f, hit.Point.X, 1e-3f);
            Assert.AreEqual(1f, hit.Normal.Magnitude, 1e-3f, "法线应该是单位向量");
            Assert.AreEqual(-1f, hit.Normal.X, 1e-3f, "法线应朝向射线来向");
        }

        // ───────────────────────── 圆 / 线段 ─────────────────────────

        [Test]
        public void 相切算作重叠()
        {
            Assert.IsTrue(HitGeometry.CirclesOverlap(Vec2.Zero, 1f, new Vec2(2f, 0f), 1f));
        }

        [Test]
        public void 分开的圆不重叠()
        {
            Assert.IsFalse(HitGeometry.CirclesOverlap(Vec2.Zero, 1f, new Vec2(2.01f, 0f), 1f));
        }

        [Test]
        public void 点到线段距离_垂足在段内()
        {
            float distance = HitGeometry.DistancePointToSegment(new Vec2(5f, 3f), Vec2.Zero, new Vec2(10f, 0f));
            Assert.AreEqual(3f, distance, 1e-3f);
        }

        [Test]
        public void 点到线段距离_垂足在段外取端点()
        {
            float distance = HitGeometry.DistancePointToSegment(new Vec2(15f, 0f), Vec2.Zero, new Vec2(10f, 0f));
            Assert.AreEqual(5f, distance, 1e-3f);
        }

        [Test]
        public void 线段退化成一个点时等于点距()
        {
            float distance = HitGeometry.DistancePointToSegment(new Vec2(3f, 4f), Vec2.Zero, Vec2.Zero);
            Assert.AreEqual(5f, distance, 1e-3f);
        }

        // ───────────────────────── 最近接近 ─────────────────────────

        [Test]
        public void 迎面而来的两机最近距离为零()
        {
            bool inWindow = HitGeometry.ClosestApproach(
                Vec2.Zero, new Vec2(1f, 0f),
                new Vec2(10f, 0f), new Vec2(-1f, 0f),
                maxTime: 10f, out float time, out float distance);

            Assert.IsTrue(inWindow);
            Assert.AreEqual(5f, time, 1e-3f, "相对速度 2，距离 10，5 秒后相遇");
            Assert.AreEqual(0f, distance, 1e-3f);
        }

        [Test]
        public void 平行同速时不会接近()
        {
            bool inWindow = HitGeometry.ClosestApproach(
                Vec2.Zero, new Vec2(1f, 0f),
                new Vec2(0f, 5f), new Vec2(1f, 0f),
                maxTime: 10f, out float time, out float distance);

            Assert.IsFalse(inWindow, "相对速度为零，不该报告'会接近'");
            Assert.AreEqual(0f, time, 1e-3f);
            Assert.AreEqual(5f, distance, 1e-3f);
        }

        [Test]
        public void 正在远离时最近点就是当前时刻()
        {
            bool inWindow = HitGeometry.ClosestApproach(
                Vec2.Zero, new Vec2(1f, 0f),
                new Vec2(10f, 0f), new Vec2(2f, 0f),
                maxTime: 10f, out float time, out float distance);

            Assert.IsFalse(inWindow);
            Assert.AreEqual(0f, time, 1e-3f, "已经最远/最近点在过去，窗口内不再接近");
            Assert.AreEqual(10f, distance, 1e-3f);
        }
    }
}
