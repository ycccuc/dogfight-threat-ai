using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 飞行手感公式的单测。
    ///
    /// 这些断言写的就是"飞机感"的定义 —— 手感可以调数值，但下面这几条性质不能被调坏：
    ///   低速转不动、有转向惯性、不超极速、反推能减速。
    /// 以后有人改参数改到"停住也能原地转身"，这些测试会立刻拦住。
    /// </summary>
    public sealed class FlightModelTests
    {
        static readonly FlightSpec Spec = FlightSpec.Default;

        [Test]
        public void 推力沿机头方向()
        {
            Vec2 force = FlightModel.ThrustForce(Spec, Vec2.Up, 1f);
            Assert.AreEqual(0f, force.X, 1e-4f);
            Assert.AreEqual(Spec.Thrust, force.Y, 1e-3f);
        }

        [Test]
        public void 油门为零时不产生推力()
        {
            Vec2 force = FlightModel.ThrustForce(Spec, Vec2.Up, 0f);
            Assert.AreEqual(0f, force.Magnitude, 1e-4f);
        }

        [Test]
        public void 反推方向与机头相反()
        {
            Vec2 force = FlightModel.ThrustForce(Spec, Vec2.Right, -1f);
            Assert.Less(force.X, 0f, "反推应该指向机头反方向");
        }

        [Test]
        public void 速度超过极速会被截断且方向不变()
        {
            Vec2 fast = new Vec2(100f, 0f);
            Vec2 clamped = FlightModel.ClampVelocity(Spec, fast);
            Assert.AreEqual(Spec.MaxSpeed, clamped.Magnitude, 1e-3f);
            Assert.AreEqual(0f, clamped.AngleRadians, 1e-4f);
        }

        [Test]
        public void 速度未超限时不做任何改动()
        {
            Vec2 slow = new Vec2(1f, 1f);
            Vec2 clamped = FlightModel.ClampVelocity(Spec, slow);
            Assert.AreEqual(slow.X, clamped.X, 1e-5f);
            Assert.AreEqual(slow.Y, clamped.Y, 1e-5f);
        }

        [Test]
        public void 静止时转向能力降到最低但不为零()
        {
            float authority = FlightModel.TurnAuthority(Spec, 0f);
            Assert.AreEqual(1f - Spec.StallPenalty, authority, 1e-4f);
            Assert.Greater(authority, 0f, "完全转不动体验太差，应保留一部分");
        }

        [Test]
        public void 达到失速速度后转向能力完全恢复()
        {
            float authority = FlightModel.TurnAuthority(Spec, Spec.StallSpeed);
            Assert.AreEqual(1f, authority, 1e-4f);
        }

        [Test]
        public void 高速时转向速率与速度无关()
        {
            float atStall = FlightModel.TurnAuthority(Spec, Spec.StallSpeed);
            float atMax = FlightModel.TurnAuthority(Spec, Spec.MaxSpeed);
            Assert.AreEqual(atStall, atMax, 1e-4f, "过了失速速度之后，转向能力不应该再受速度影响");
        }

        [Test]
        public void 左转输入得到正角速度右转得到负角速度()
        {
            float left = FlightModel.TargetAngularSpeed(Spec, 1f, Spec.MaxSpeed);
            float right = FlightModel.TargetAngularSpeed(Spec, -1f, Spec.MaxSpeed);
            Assert.Greater(left, 0f, "左转 = 逆时针 = 正");
            Assert.Less(right, 0f, "右转 = 顺时针 = 负");
            Assert.AreEqual(-left, right, 1e-4f, "左右应对称");
        }

        [Test]
        public void 角速度逼近受转向加速度限制()
        {
            float target = FlightModel.TargetAngularSpeed(Spec, 1f, Spec.MaxSpeed);
            float dt = 1f / 60f;

            // 从静止开始，一步不可能直接到位
            float afterOneStep = FlightModel.StepAngularSpeed(Spec, 0f, target, dt);
            Assert.Less(afterOneStep, target, "一步就到位说明没有转向惯性");
            Assert.AreEqual(Spec.TurnAccel * dt, afterOneStep, 1e-4f);
        }

        [Test]
        public void 角速度会在若干步后收敛到目标()
        {
            float target = FlightModel.TargetAngularSpeed(Spec, 1f, Spec.MaxSpeed);
            float dt = 1f / 60f;
            float current = 0f;

            for (int i = 0; i < 600; i++)
            {
                current = FlightModel.StepAngularSpeed(Spec, current, target, dt);
            }

            Assert.AreEqual(target, current, 1e-3f);
        }

        [Test]
        public void 机头按角速度旋转一个步长()
        {
            Vec2 heading = Vec2.Up;                       // 朝上 = 90°
            float angularSpeed = 90f * MathUtil.Deg2Rad;  // 90°/s，逆时针为正
            Vec2 next = FlightModel.StepHeading(heading, angularSpeed, 1f); // 一整秒 → 再转 90°

            // 朝上再逆时针转 90° = 朝左（180°）。
            // 这里断言分量而不是角度：180° 附近 atan2 的符号会抖，用角度断言会假失败。
            Assert.AreEqual(-1f, next.X, 1e-3f, "应该转到朝左");
            Assert.AreEqual(0f, next.Y, 1e-3f);
        }

        [Test]
        public void 旋转后机头仍是单位向量()
        {
            Vec2 next = FlightModel.StepHeading(Vec2.Up, 3f, 0.37f);
            Assert.AreEqual(1f, next.Magnitude, 1e-4f);
        }

        [Test]
        public void 最小转弯半径等于速度除角速度()
        {
            float radius = FlightModel.TurnRadius(9f, 1.5f);
            Assert.AreEqual(6f, radius, 1e-3f);
            Assert.IsTrue(float.IsPositiveInfinity(FlightModel.TurnRadius(9f, 0f)), "角速度为 0 时半径无穷");
        }

        [Test]
        public void 默认参数在极速下的转弯半径可控()
        {
            float omega = FlightModel.TargetAngularSpeed(Spec, 1f, Spec.MaxSpeed);
            float radius = FlightModel.TurnRadius(Spec.MaxSpeed, omega);
            Assert.Less(radius, Spec.MaxSpeed * 2f, "极速下转弯半径不该超过 2 倍极速，否则会一直冲出战场");
        }
    }
}
