using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 战场软边界的单测。
    ///
    /// 这类"手感规则"最容易被后续调参改坏（把 margin 调成 0、把方向搞反），
    /// 而一旦反了，飞机会被推向场外 —— 那是很难从画面上看出原因的 bug。
    /// </summary>
    public sealed class ArenaBoundsTests
    {
        static ArenaBounds Standard() => ArenaBounds.FromSize(Vec2.Zero, new Vec2(40f, 22.5f), softMargin: 3f);

        [Test]
        public void 由尺寸构造出正确的四边()
        {
            ArenaBounds a = ArenaBounds.FromSize(new Vec2(10f, 5f), new Vec2(20f, 10f), 2f);
            Assert.AreEqual(0f, a.MinX, 1e-4f);
            Assert.AreEqual(20f, a.MaxX, 1e-4f);
            Assert.AreEqual(0f, a.MinY, 1e-4f);
            Assert.AreEqual(10f, a.MaxY, 1e-4f);
            Assert.AreEqual(new Vec2(10f, 5f), a.Center);
        }

        [Test]
        public void 中心区域在边界内且没有回推力()
        {
            ArenaBounds a = Standard();
            Assert.IsTrue(a.Contains(Vec2.Zero));
            Assert.AreEqual(0f, a.BoundaryForce(Vec2.Zero, 100f).Magnitude, 1e-4f);
            Assert.IsFalse(a.IsNearEdge(Vec2.Zero));
        }

        [Test]
        public void 贴近左边界时被推向场内_方向朝右()
        {
            ArenaBounds a = Standard();
            Vec2 force = a.BoundaryForce(new Vec2(-19.5f, 0f), 100f);
            Assert.Greater(force.X, 0f, "靠左边界应该被往右推");
            Assert.AreEqual(0f, force.Y, 1e-4f, "y 方向不该有分量");
        }

        [Test]
        public void 四个边界的方向都正确()
        {
            ArenaBounds a = Standard();
            Assert.Greater(a.BoundaryForce(new Vec2(-19.9f, 0f), 100f).X, 0f, "左 → 右");
            Assert.Less(a.BoundaryForce(new Vec2(19.9f, 0f), 100f).X, 0f, "右 → 左");
            Assert.Greater(a.BoundaryForce(new Vec2(0f, -10.9f), 100f).Y, 0f, "下 → 上");
            Assert.Less(a.BoundaryForce(new Vec2(0f, 10.9f), 100f).Y, 0f, "上 → 下");
        }

        [Test]
        public void 越靠边回推力越大()
        {
            ArenaBounds a = Standard();
            float justInside = a.BoundaryForce(new Vec2(-17.5f, 0f), 100f).Magnitude;
            float closer = a.BoundaryForce(new Vec2(-18.5f, 0f), 100f).Magnitude;
            float outside = a.BoundaryForce(new Vec2(-21f, 0f), 100f).Magnitude;

            Assert.Greater(justInside, 0f);
            Assert.Greater(closer, justInside, "越靠边应该越强");
            Assert.Greater(outside, closer, "已经出界时应该最强");
        }

        [Test]
        public void 恰好等于软边界宽度时力为零()
        {
            ArenaBounds a = Standard();
            // 左边界是 -20，margin 3 → 从 -17 开始才有推力
            Assert.AreEqual(0f, a.BoundaryForce(new Vec2(-17f, 0f), 100f).Magnitude, 1e-4f);
            Assert.Greater(a.BoundaryForce(new Vec2(-17.01f, 0f), 100f).Magnitude, 0f);
        }

        [Test]
        public void 力的大小与传入强度成正比()
        {
            ArenaBounds a = Standard();
            float weak = a.BoundaryForce(new Vec2(-18f, 0f), 50f).Magnitude;
            float strong = a.BoundaryForce(new Vec2(-18f, 0f), 100f).Magnitude;
            Assert.AreEqual(weak * 2f, strong, 1e-3f);
        }

        [Test]
        public void 硬夹紧把出界点拉回边界上()
        {
            ArenaBounds a = Standard();
            Vec2 clamped = a.Clamp(new Vec2(-99f, 99f));
            Assert.AreEqual(-20f, clamped.X, 1e-4f);
            Assert.AreEqual(11.25f, clamped.Y, 1e-4f);
        }

        [Test]
        public void 距离最近边界能反映贴近程度()
        {
            ArenaBounds a = Standard();
            Assert.AreEqual(11.25f, a.DistanceToNearestEdge(Vec2.Zero), 1e-3f, "中心到上下边界最近");
            Assert.AreEqual(1f, a.DistanceToNearestEdge(new Vec2(-19f, 0f)), 1e-3f);
        }

        [Test]
        public void 无边界时一切安全_不会返回NaN()
        {
            ArenaBounds a = ArenaBounds.Unbounded;
            Assert.IsTrue(a.IsUnbounded);
            Assert.AreEqual(0f, a.BoundaryForce(new Vec2(1e9f, 1e9f), 100f).Magnitude, 1e-4f);
            Assert.AreEqual(float.PositiveInfinity, a.DistanceToNearestEdge(Vec2.Zero));
            Assert.IsFalse(a.IsNearEdge(Vec2.Zero));
        }

        [Test]
        public void 软边界为零时只做硬夹紧不回推()
        {
            ArenaBounds a = ArenaBounds.FromSize(Vec2.Zero, new Vec2(40f, 22.5f), softMargin: 0f);
            Assert.AreEqual(0f, a.BoundaryForce(new Vec2(-19.9f, 0f), 100f).Magnitude, 1e-4f);
            Assert.IsFalse(a.IsNearEdge(new Vec2(-19.9f, 0f)));
        }

        [Test]
        public void 四边长度为零时不会产生除零()
        {
            ArenaBounds a = ArenaBounds.FromSize(Vec2.Zero, Vec2.Zero, softMargin: 1f);
            Vec2 force = a.BoundaryForce(Vec2.Zero, 100f);
            Assert.IsFalse(float.IsNaN(force.X));
            Assert.IsFalse(float.IsNaN(force.Y));
        }
    }
}
