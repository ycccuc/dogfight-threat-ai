using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 导弹飞行与追踪的单测。
    ///
    /// 导弹是"每帧推进一点"的东西，最容易出的两类问题是：
    ///   1. 高速穿透 —— 一帧走过头，判定没触发，导弹从目标身上穿过去
    ///   2. 无限追踪 —— 转得太快，永远甩不掉
    /// 下面用线段判定与转向速率限制分别钉住这两条。
    /// </summary>
    public sealed class MissileSimTests
    {
        const float Dt = 1f / 60f;

        static WeaponSpec Missile(float range = 18f, float speed = 8f, float turnDegrees = 120f) =>
            new WeaponSpec(WeaponKind.Missile, 35f, range, 1.2f, speed, turnDegrees * MathUtil.Deg2Rad, 0f, 1, 1f);

        static MissileStepOutcome Run(
            ref MissileState missile,
            in WeaponSpec spec,
            Vec2 targetPosition,
            bool targetAlive,
            float targetRadius,
            int maxSteps,
            out int steps,
            float lifetime = MissileSim.DefaultLifetimeSeconds)
        {
            steps = 0;
            for (int i = 0; i < maxSteps; i++)
            {
                MissileStepOutcome outcome = MissileSim.Step(
                    ref missile, spec, targetPosition, targetAlive, targetRadius, Dt,
                    MissileSim.DefaultFuseRadius, lifetime);
                steps++;
                if (outcome != MissileStepOutcome.Flying) return outcome;
            }
            return MissileStepOutcome.Flying;
        }

        [Test]
        public void 正对目标时命中()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile();

            MissileStepOutcome outcome = Run(ref missile, spec, new Vec2(10f, 0f), true, 0.4f, 600, out int steps);

            Assert.AreEqual(MissileStepOutcome.Hit, outcome);
            Assert.Less(steps, 100, "8 单位/秒飞 10 单位不该超过 100 帧");
            Assert.IsFalse(missile.Alive);
        }

        [Test]
        public void 命中时导弹落在目标附近()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile();
            Vec2 target = new Vec2(10f, 0f);

            Run(ref missile, spec, target, true, 0.4f, 600, out int _);

            float distance = Vec2.Distance(missile.Position, target);
            Assert.LessOrEqual(distance, MissileSim.DefaultFuseRadius + 0.4f + 1e-3f,
                "命中点应该在近炸阈值之内（不是飞过去老远才判）");
        }

        [Test]
        public void 目标在正后方时不会瞬间掉头()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile(turnDegrees: 120f);

            MissileSim.Step(ref missile, spec, new Vec2(-10f, 0f), true, 0.4f, Dt);

            // 120°/s、1/60 秒 → 单帧最多转 2°
            Assert.Greater(missile.Direction.X, 0.99f, "一帧之内不该把机头拧到目标方向上去");
        }

        [Test]
        public void 转向速率越高追踪越快()
        {
            int slowSteps = TimeToHit(30f);
            int fastSteps = TimeToHit(240f);
            Assert.Less(fastSteps, slowSteps, "转向更快的导弹应该更早命中");
        }

        static int TimeToHit(float turnDegrees)
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile(range: 200f, speed: 8f, turnDegrees: turnDegrees);
            Run(ref missile, spec, new Vec2(4f, 4f), true, 0.4f, 1200, out int steps, lifetime: 20f);
            return steps;
        }

        [Test]
        public void 无转向时靠近炸引信擦过命中()
        {
            // 转向速率 0 → 直飞；目标偏离 0.5，引信 0.45 + 目标半径 0.1 = 0.55 → 擦到
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile(turnDegrees: 0f);

            MissileStepOutcome outcome = Run(ref missile, spec, new Vec2(10f, 0.5f), true, 0.1f, 600, out int _);

            Assert.AreEqual(MissileStepOutcome.Hit, outcome, "擦过也应该触发近炸引信");
        }

        [Test]
        public void 高速导弹不会穿过目标()
        {
            // 速度拉到 400 单位/秒：单帧位移 6.7 单位，远大于目标尺寸
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 400f, targetId: 1);
            WeaponSpec spec = Missile(range: 500f, speed: 400f, turnDegrees: 0f);

            MissileStepOutcome outcome = Run(ref missile, spec, new Vec2(10f, 0f), true, 0.2f, 60, out int _);

            Assert.AreEqual(MissileStepOutcome.Hit, outcome,
                "线段判定必须能拦住高速穿透 —— 否则子弹会从目标身上穿过去");
        }

        [Test]
        public void 寿命到了会自毁()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile(range: 1000f, speed: 8f, turnDegrees: 0f);

            MissileStepOutcome outcome = Run(ref missile, spec, new Vec2(9999f, 0f), false, 0.4f, 600,
                out int steps, lifetime: 0.2f);

            Assert.AreEqual(MissileStepOutcome.Expired, outcome);
            Assert.LessOrEqual(steps, 13, "0.2 秒 / (1/60) ≈ 12 帧就该到期");
            Assert.IsFalse(missile.Alive);
        }

        [Test]
        public void 超出射程会自毁()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile(range: 5f, speed: 8f, turnDegrees: 0f);

            MissileStepOutcome outcome = Run(ref missile, spec, new Vec2(9999f, 0f), false, 0.4f, 600, out int _);

            Assert.AreEqual(MissileStepOutcome.Expired, outcome);
            Assert.GreaterOrEqual(missile.DistanceTravelled, 5f, "应该正好在射程处失效");
        }

        [Test]
        public void 目标死亡后不再追踪而是直飞()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            WeaponSpec spec = Missile(range: 1000f, speed: 8f, turnDegrees: 180f);

            MissileStepOutcome outcome = Run(ref missile, spec, new Vec2(0f, 5f), false, 0.4f, 600,
                out int _, lifetime: 0.5f);

            Assert.AreEqual(MissileStepOutcome.Expired, outcome);
            Assert.Greater(missile.Direction.X, 0.99f, "目标没了就该沿原方向直飞，不该继续拐弯");
        }

        [Test]
        public void 已死的导弹再推进仍是Expired()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, Vec2.Right, 8f, targetId: 1);
            missile.Alive = false;

            MissileStepOutcome outcome = MissileSim.Step(ref missile, Missile(), Vec2.Right * 5f, true, 0.4f, Dt);
            Assert.AreEqual(MissileStepOutcome.Expired, outcome);
        }

        [Test]
        public void 发射时速度方向被归一化()
        {
            MissileState missile = MissileState.Launch(Vec2.Zero, new Vec2(3f, 4f), 10f, targetId: 1);

            Assert.AreEqual(10f, missile.Velocity.Magnitude, 1e-3f, "速度大小应等于给定弹速");
            Assert.AreEqual(0.6f, missile.Direction.X, 1e-3f, "方向应被归一化");
            Assert.AreEqual(0.8f, missile.Direction.Y, 1e-3f);
        }
    }
}
