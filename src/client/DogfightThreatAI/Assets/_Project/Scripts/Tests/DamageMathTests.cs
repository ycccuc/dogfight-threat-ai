using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 伤害与命中公式的单测。
    ///
    /// 这些测试能存在，正是「Ai 层不依赖 Unity」那条边界换来的：
    /// 不用进 Play 模式、不用创建 GameObject、毫秒级跑完。
    /// 数值平衡会被反复修改，这些断言就是防止"改衰减曲线时顺手把护甲公式改坏"。
    /// </summary>
    public sealed class DamageMathTests
    {
        static WeaponSpec Cannon(float damage = 10f, float range = 12f, float cooldown = 0.15f) =>
            new WeaponSpec(WeaponKind.Cannon, damage, range, cooldown, 0f, 0f, 0f, 1, 1f);

        [Test]
        public void 距离为零时伤害为全额()
        {
            Assert.AreEqual(10f, DamageMath.WithFalloff(10f, 12f, 0f), 1e-4f);
        }

        [Test]
        public void 距离等于射程时伤害衰减到零()
        {
            Assert.AreEqual(0f, DamageMath.WithFalloff(10f, 12f, 12f), 1e-4f);
        }

        [Test]
        public void 衰减是中点一半()
        {
            Assert.AreEqual(5f, DamageMath.WithFalloff(10f, 12f, 6f), 1e-4f);
        }

        [Test]
        public void 超出射程的一发不造成伤害()
        {
            WeaponSpec cannon = Cannon();
            Assert.AreEqual(0f, DamageMath.ForShot(cannon, 12.01f), 1e-4f);
        }

        [Test]
        public void 护甲按比例减伤且会夹紧()
        {
            Assert.AreEqual(5f, DamageMath.ThroughArmor(10f, 0.5f), 1e-4f);
            Assert.AreEqual(10f, DamageMath.ThroughArmor(10f, -3f), 1e-4f, "负护甲不应变成加伤");
            Assert.AreEqual(0f, DamageMath.ThroughArmor(10f, 5f), 1e-4f, "超过 1 的护甲不应变成负伤害");
        }

        [Test]
        public void 击杀所需发数按上限取整()
        {
            WeaponSpec cannon = Cannon(damage: 10f);
            // 距离 0 时每发 10 点，35 点血需要 4 发（3 发只有 30）
            Assert.AreEqual(4, DamageMath.ShotsToKill(cannon, 35f, 0f));
        }

        [Test]
        public void 射程外击杀所需发数为无穷()
        {
            Assert.AreEqual(int.MaxValue, DamageMath.ShotsToKill(Cannon(), 10f, 99f));
        }

        [Test]
        public void 多弹丸武器一轮齐射伤害按发数倍增()
        {
            var shotgun = new WeaponSpec(WeaponKind.Cannon, 4f, 10f, 0.5f, 0f, 0f, 0f, 5, 1f);
            Assert.AreEqual(20f, DamageMath.ForVolley(shotgun, 0f), 1e-4f);
        }

        [Test]
        public void 追踪转向单帧不会超过最大转角()
        {
            Vec2 current = Vec2.Right;
            Vec2 target = Vec2.Up; // 需要转 90°
            float maxTurn = 10f * MathUtil.Deg2Rad;

            Vec2 steered = DamageMath.SteerTowards(current, target, maxTurn);

            float turned = MathUtil.Abs(Vec2.DeltaAngle(current.AngleRadians, steered.AngleRadians));
            Assert.LessOrEqual(turned, maxTurn + 1e-4f);
            Assert.Greater(turned, 0f, "应该确实转了");
        }

        [Test]
        public void 追踪转向在多帧后能对齐目标()
        {
            Vec2 dir = Vec2.Right;
            Vec2 target = Vec2.Up;
            float maxTurn = 10f * MathUtil.Deg2Rad;

            for (int i = 0; i < 20; i++)
            {
                dir = DamageMath.SteerTowards(dir, target, maxTurn);
            }

            Assert.Less(MathUtil.Abs(Vec2.DeltaAngle(dir.AngleRadians, target.AngleRadians)), 1e-3f);
        }

        [Test]
        public void 射线武器提前量瞄准直接指向目标()
        {
            Vec2 aim = DamageMath.LeadAim(Vec2.Zero, new Vec2(10f, 0f), new Vec2(0f, 5f), projectileSpeed: 0f);
            Assert.AreEqual(0f, aim.AngleRadians, 1e-4f, "弹速为 0（射线）时应直指目标当前方向");
        }

        [Test]
        public void 有弹速时提前量会朝目标前进方向偏转()
        {
            // 目标在正右方 10 单位，向正上方以 5/秒移动；弹速 10 → 需要提前量
            Vec2 aim = DamageMath.LeadAim(Vec2.Zero, new Vec2(10f, 0f), new Vec2(0f, 5f), projectileSpeed: 10f);
            Assert.Greater(aim.Y, 0f, "提前量应该朝目标运动方向偏（向上）");
            Assert.Greater(aim.X, 0f, "仍然主要朝右");
        }
    }
}
