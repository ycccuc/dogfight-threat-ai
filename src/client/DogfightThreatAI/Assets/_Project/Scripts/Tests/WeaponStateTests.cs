using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 武器开火节奏（冷却）的单测。
    ///
    /// 冷却看着简单，但"冷却中点按排队"、"HUD 画进度条"这些需求会让它反复被改。
    /// 把行为钉死之后，改的人立刻知道有没有破坏原有语义。
    /// </summary>
    public sealed class WeaponStateTests
    {
        static WeaponSpec Cannon(float cooldown = 0.15f) =>
            new WeaponSpec(WeaponKind.Cannon, 10f, 12f, cooldown, 0f, 0f, 0f, 1, 1f);

        [Test]
        public void 新武器一开始就能开火()
        {
            var state = new WeaponState();
            Assert.IsTrue(state.CanFire(0f));
        }

        [Test]
        public void 开火后进入冷却()
        {
            var state = new WeaponState();
            WeaponSpec spec = Cannon(cooldown: 0.15f);

            Assert.IsTrue(state.TryFire(0f, spec));
            Assert.IsFalse(state.CanFire(0f), "刚打完不该立刻能再打");
            Assert.IsFalse(state.CanFire(0.1f));
            Assert.IsTrue(state.CanFire(0.15f), "到点就该恢复");
            Assert.IsTrue(state.CanFire(1f));
        }

        [Test]
        public void 冷却剩余时间不会为负()
        {
            var state = new WeaponState();
            state.MarkFired(0f, Cannon(0.15f));

            Assert.AreEqual(0.15f, state.CooldownRemaining(0f), 1e-4f);
            Assert.AreEqual(0.05f, state.CooldownRemaining(0.1f), 1e-4f);
            Assert.AreEqual(0f, state.CooldownRemaining(5f), 1e-4f);
        }

        [Test]
        public void 冷却进度从零走到一()
        {
            var state = new WeaponState();
            WeaponSpec spec = Cannon(cooldown: 1f);
            state.MarkFired(0f, spec);

            Assert.AreEqual(0f, state.CooldownProgress(0f, spec), 1e-3f);
            Assert.AreEqual(0.5f, state.CooldownProgress(0.5f, spec), 1e-3f);
            Assert.AreEqual(1f, state.CooldownProgress(1f, spec), 1e-3f);
            Assert.AreEqual(1f, state.CooldownProgress(9f, spec), 1e-3f);
        }

        [Test]
        public void 冷却中尝试开火不会重置冷却()
        {
            var state = new WeaponState();
            WeaponSpec spec = Cannon(cooldown: 0.15f);
            state.MarkFired(0f, spec);

            Assert.IsFalse(state.TryFire(0.1f, spec), "冷却中不该开火成功");
            Assert.AreEqual(0.15f, state.NextFireAt, 1e-4f, "失败的尝试绝不能顺延冷却 —— 否则连点会让武器永久哑火");
        }

        [Test]
        public void 冷却为零时任何时刻都能开火()
        {
            var state = new WeaponState();
            WeaponSpec spec = Cannon(cooldown: 0f);

            Assert.IsTrue(state.TryFire(0f, spec));
            Assert.IsTrue(state.TryFire(0f, spec));
            Assert.AreEqual(1f, state.CooldownProgress(0f, spec), 1e-4f);
        }

        [Test]
        public void Reset之后立即可开火()
        {
            var state = new WeaponState();
            state.MarkFired(100f, Cannon(5f));
            Assert.IsFalse(state.CanFire(100f));

            state.Reset();
            Assert.IsTrue(state.CanFire(100f));
        }
    }

    /// <summary>
    /// 开火解算（瞄准、散布、枪口、一次完整命中判定）的单测。
    /// 散布必须可复现 —— 否则批量对战的胜率统计就没有意义。
    /// </summary>
    public sealed class GunneryTests
    {
        static WeaponSpec CannonWithSpread(float spreadDegrees) =>
            new WeaponSpec(WeaponKind.Cannon, 10f, 12f, 0.15f, 0f, 0f, spreadDegrees * MathUtil.Deg2Rad, 1, 1f);

        [Test]
        public void 无散布时方向完全不变()
        {
            var rng = new DeterministicRng(1u);
            Vec2 result = Gunnery.ApplySpread(Vec2.Right, 0f, ref rng);
            Assert.AreEqual(0f, result.AngleRadians, 1e-5f);
        }

        [Test]
        public void 散布始终落在给定半角之内()
        {
            var rng = new DeterministicRng(12345u);
            float spread = 5f * MathUtil.Deg2Rad;

            for (int i = 0; i < 200; i++)
            {
                Vec2 dir = Gunnery.ApplySpread(Vec2.Right, spread, ref rng);
                float deviation = MathUtil.Abs(Vec2.DeltaAngle(0f, dir.AngleRadians));
                Assert.LessOrEqual(deviation, spread + 1e-4f, "第 " + i + " 发偏出了散布半角");
                Assert.AreEqual(1f, dir.Magnitude, 1e-4f, "散布后仍应是单位向量");
            }
        }

        [Test]
        public void 同样种子得到完全相同的弹道()
        {
            float spread = 8f * MathUtil.Deg2Rad;
            var a = new DeterministicRng(777u);
            var b = new DeterministicRng(777u);

            for (int i = 0; i < 50; i++)
            {
                Vec2 da = Gunnery.ApplySpread(Vec2.Right, spread, ref a);
                Vec2 db = Gunnery.ApplySpread(Vec2.Right, spread, ref b);
                Assert.AreEqual(da.X, db.X, 1e-6f, "同种子第 " + i + " 发不一致");
                Assert.AreEqual(da.Y, db.Y, 1e-6f);
            }
        }

        [Test]
        public void 瞄准点与自身重合时用兜底方向()
        {
            Vec2 dir = Gunnery.AimDirection(new Vec2(3f, 3f), new Vec2(3f, 3f), Vec2.Up);
            Assert.AreEqual(1f, dir.Y, 1e-4f);
            Assert.AreEqual(0f, dir.X, 1e-4f);
        }

        [Test]
        public void 瞄准方向是单位向量且指向目标()
        {
            Vec2 dir = Gunnery.AimDirection(Vec2.Zero, new Vec2(0f, 7f), Vec2.Right);
            Assert.AreEqual(1f, dir.Magnitude, 1e-4f);
            Assert.AreEqual(90f, dir.AngleRadians * MathUtil.Rad2Deg, 1e-2f);
        }

        [Test]
        public void 枪口位置沿射向前移()
        {
            Vec2 muzzle = Gunnery.MuzzlePosition(new Vec2(1f, 1f), Vec2.Right, 0.5f);
            Assert.AreEqual(1.5f, muzzle.X, 1e-4f);
            Assert.AreEqual(1f, muzzle.Y, 1e-4f);
        }

        [Test]
        public void 完整开火解算会跳过同阵营并命中敌人()
        {
            var targets = new[]
            {
                new CircleTarget(1, new Vec2(3f, 0f), 0.4f, team: 0, alive: true), // 自己人
                new CircleTarget(2, new Vec2(6f, 0f), 0.4f, team: 1, alive: true), // 敌人（近）
                new CircleTarget(3, new Vec2(9f, 0f), 0.4f, team: 1, alive: true), // 敌人（远）
            };
            var rng = new DeterministicRng(42u);

            RayHit hit = Gunnery.ResolveShot(
                Vec2.Zero, Vec2.Right, CannonWithSpread(0f), targets, 3,
                excludeTeam: 0, shooterId: 99, ref rng);

            Assert.IsTrue(hit.Hit);
            Assert.AreEqual(2, hit.TargetId, "应命中最近的敌人，而不是自己人");
        }

        [Test]
        public void 有散布时命中会被随机性影响但仍是合法结果()
        {
            var targets = new[] { new CircleTarget(2, new Vec2(6f, 0f), 0.4f, team: 1, alive: true) };
            var rng = new DeterministicRng(2024u);
            WeaponSpec spec = CannonWithSpread(10f);

            int hits = 0;
            for (int i = 0; i < 100; i++)
            {
                RayHit hit = Gunnery.ResolveShot(Vec2.Zero, Vec2.Right, spec, targets, 1, 0, 99, ref rng);
                if (hit.Hit) hits++;
            }

            // 6 单位处半径 0.4 的目标，10° 散布下命中率应该在合理区间（既不是必中也不是必不中）
            Assert.Greater(hits, 0, "10° 散布不该永远打不中 6 单位外的目标");
            Assert.Less(hits, 100, "10° 散布不该百发百中");
        }
    }
}
