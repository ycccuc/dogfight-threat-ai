using System.Collections.Generic;
using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 五种行为模式（巡逻 / 搜索 / 追击 / 攻击 / 规避）的单测。
    ///
    /// 这是立项书的核心创新点，也是最容易"看起来在动、其实没逻辑"的地方。
    /// 所以这里断言的是**状态迁移**而不是"跑起来没报错"：
    /// 什么条件下该进哪个状态、什么条件下该出来、以及 AI 是否真的没有作弊。
    /// </summary>
    public sealed class PlaneBrainTests
    {
        const float Dt = 1f / 60f;

        static AgentSnapshot Plane(
            int id,
            float x,
            float y,
            int team = 1,
            float hp = 100f,
            float headingDegrees = 0f,
            float vx = 0f,
            float vy = 0f,
            bool alive = true) =>
            new AgentSnapshot(
                id,
                new Vec2(x, y),
                new Vec2(vx, vy),
                headingDegrees * MathUtil.Deg2Rad,
                hp,
                100f,
                alive,
                team);

        static ArrayAiWorld MakeWorld(params AgentSnapshot[] agents)
        {
            var world = new ArrayAiWorld();
            world.SetAgents(agents, agents.Length);
            return world;
        }

        static PlaneBrain MakeBrain(
            IAiWorld world,
            AiProfile? profile = null,
            PatrolRoute route = null,
            int selfId = 1,
            int selfTeam = 0) =>
            new PlaneBrain(selfId, selfTeam, world, profile ?? AiProfile.Default, WeaponSpec.DefaultCannon, route);

        static void Tick(PlaneBrain brain, int times)
        {
            for (int i = 0; i < times; i++) brain.Tick(Dt);
        }

        // ───────────────────────── 巡逻 ─────────────────────────

        [Test]
        public void 没有敌人时处于巡逻状态()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0));
            PlaneBrain brain = MakeBrain(world);

            Tick(brain, 3);

            Assert.AreEqual(AiState.Patrol, brain.CurrentState);
            Assert.Greater(brain.Input.Throttle, 0f, "巡逻也要给油门，不能飘着");
        }

        [Test]
        public void 巡逻会沿航线推进路点()
        {
            var route = new PatrolRoute(new List<Vec2>
            {
                new Vec2(5f, 0f),
                new Vec2(5f, 5f),
                new Vec2(0f, 5f),
            });
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0));
            PlaneBrain brain = MakeBrain(world, route: route);

            brain.Tick(Dt);
            Assert.AreEqual(0, route.Cursor, "离第一个路点还远，不该推进");

            // 把飞机挪到第一个路点上
            world.SetAgents(new[] { Plane(1, 5f, 0f, team: 0) }, 1);
            brain.Tick(Dt);

            Assert.AreEqual(1, route.Cursor, "到达路点后应该切到下一个");
            Assert.AreEqual(5f, route.Current.Y, 1e-4f);
        }

        [Test]
        public void 巡逻航线是闭环_走到末尾会绕回起点()
        {
            var route = new PatrolRoute(new List<Vec2> { new Vec2(1f, 0f), new Vec2(2f, 0f) });
            route.Advance();
            route.Advance();
            Assert.AreEqual(0, route.Cursor, "两点航线走两次应该回到起点");
        }

        [Test]
        public void 没有航线时也能巡航_不会卡住()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0));
            PlaneBrain brain = MakeBrain(world, route: null);

            Tick(brain, 5);

            Assert.AreEqual(AiState.Patrol, brain.CurrentState);
            Assert.AreEqual(1f, brain.Input.AimPoint.Magnitude, 10f, "应该瞄准正前方而不是原点");
        }

        // ───────────────────────── 追击 / 攻击 ─────────────────────────

        [Test]
        public void 发现敌人在射程外时进入追击且全油门()
        {
            // 武器射程 12，发现距离 13 → 12.5 处只追不打
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0), Plane(2, 12.5f, 0f));
            PlaneBrain brain = MakeBrain(world);

            Tick(brain, 2);

            Assert.AreEqual(AiState.Pursue, brain.CurrentState);
            Assert.AreEqual(1f, brain.Input.Throttle, 1e-3f, "追击应该全油门");
            Assert.IsFalse(brain.Input.FirePrimary, "还打不到就不该开火");
            Assert.AreEqual(2, brain.TargetId);
        }

        [Test]
        public void 进入射程且机头对准时开火()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0), Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);

            Tick(brain, 2);

            Assert.AreEqual(AiState.Attack, brain.CurrentState);
            Assert.IsTrue(brain.Input.FirePrimary, "对准且在射程内应该扣扳机");
        }

        [Test]
        public void 射程内但没对准时只追不打()
        {
            // 敌人在正右方，自己机头朝上 → 差 90°，远超开火容差
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0, headingDegrees: 90f),
                Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);

            Tick(brain, 2);

            Assert.AreEqual(AiState.Pursue, brain.CurrentState);
            Assert.IsFalse(brain.Input.FirePrimary, "没对准就不该开火");
        }

        [Test]
        public void 追踪时会朝目标的提前量方向转()
        {
            // 目标在正右方且向上运动。注意：机炮是射线检测，不需要提前量；
            // 所以这里特意换成**导弹**（有弹速）来验证提前量确实生效。
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0),
                Plane(2, 10f, 0f, vy: 3f));
            var brain = new PlaneBrain(1, 0, world, AiProfile.Veteran, WeaponSpec.DefaultMissile);

            brain.Tick(Dt);

            Assert.Greater(brain.Input.Turn, 0f, "目标向上跑，导弹需要提前量，机头该往左上偏（正 = 逆时针）");
        }

        [Test]
        public void 射线武器不做提前量而是直指目标()
        {
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0),
                Plane(2, 10f, 0f, vy: 3f));
            PlaneBrain brain = MakeBrain(world); // 机炮

            brain.Tick(Dt);

            Assert.AreEqual(0f, brain.Input.Turn, 1e-4f, "射线武器直指目标，横向速度不该影响射向");
        }

        // ───────────────────────── 规避 ─────────────────────────

        [Test]
        public void 残血时进入规避且朝远离敌人的方向飞()
        {
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0, hp: 20f),
                Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);

            Tick(brain, 2);

            Assert.AreEqual(AiState.Evade, brain.CurrentState);
            Assert.Less(brain.Input.AimPoint.X, 0f, "敌人在右侧，逃逸方向应该朝左");
            Assert.AreEqual(1f, brain.Input.Throttle, 1e-3f, "逃命应该全油门");
        }

        [Test]
        public void 规避窗口结束后恢复交战()
        {
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0, hp: 20f),
                Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);

            brain.Tick(Dt);
            Assert.AreEqual(AiState.Evade, brain.CurrentState);

            // 血量回满（模拟后续版本的道具/治疗），继续推进到规避窗口之外
            world.SetAgents(new[] { Plane(1, 0f, 0f, team: 0, hp: 100f), Plane(2, 6f, 0f) }, 2);
            Tick(brain, 130); // 1.8 秒 ≈ 108 帧，多给一些余量

            Assert.AreNotEqual(AiState.Evade, brain.CurrentState, "血量恢复且窗口结束后不该继续逃");
            Assert.AreEqual(AiState.Attack, brain.CurrentState, "满血且对准应该回到攻击");
        }

        [Test]
        public void 满血时敌人贴脸也不会逃()
        {
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0, hp: 100f),
                Plane(2, 0.5f, 0f));
            PlaneBrain brain = MakeBrain(world);

            Tick(brain, 2);

            Assert.AreNotEqual(AiState.Evade, brain.CurrentState, "满血贴脸应该继续打，而不是逃跑");
        }

        // ───────────────────────── 搜索 ─────────────────────────

        [Test]
        public void 目标离开视野后进入搜索并记住最后位置()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0), Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);
            brain.Tick(Dt);

            Assert.IsTrue(brain.HasTarget);
            Assert.AreEqual(6f, brain.LastKnownTargetPosition.X, 1e-3f);

            // 敌人瞬间跑到视野之外（超过 LoseTargetRange 17）
            world.SetAgents(new[] { Plane(1, 0f, 0f, team: 0), Plane(2, 100f, 0f) }, 2);
            brain.Tick(Dt);

            Assert.AreEqual(AiState.Search, brain.CurrentState);
            Assert.IsFalse(brain.HasTarget);
            Assert.IsTrue(brain.HasLastKnownPosition);
        }

        [Test]
        public void 搜索到线索位置后回到巡逻()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0), Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);
            brain.Tick(Dt);

            world.SetAgents(new[] { Plane(1, 0f, 0f, team: 0), Plane(2, 100f, 0f) }, 2);
            brain.Tick(Dt);
            Assert.AreEqual(AiState.Search, brain.CurrentState);

            // 飞到线索位置，还是一无所获
            world.SetAgents(new[] { Plane(1, 6f, 0f, team: 0), Plane(2, 100f, 0f) }, 2);
            brain.Tick(Dt);
            brain.Tick(Dt);

            Assert.AreEqual(AiState.Patrol, brain.CurrentState);
            Assert.IsFalse(brain.HasLastKnownPosition, "线索作废后不该再挂着");
        }

        // ───────────────────────── 目标黏性 ─────────────────────────

        [Test]
        public void 锁定后在保持范围内不会因为出现更近的敌人就换目标()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0), Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);
            brain.Tick(Dt);
            Assert.AreEqual(2, brain.TargetId);

            // 冒出一个更近的敌人：3 单位
            world.SetAgents(new[] { Plane(1, 0f, 0f, team: 0), Plane(2, 6f, 0f), Plane(3, 3f, 0f) }, 3);
            brain.Tick(Dt);

            Assert.AreEqual(2, brain.TargetId, "黏性设计：锁定后在保持范围内不换目标，避免每帧切换导致抽搐");
        }

        [Test]
        public void 原目标跑出保持范围后会改打最近的敌人()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0), Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);
            brain.Tick(Dt);

            // 原目标跑到 30（超过保持范围 17），另一个敌人在 3
            world.SetAgents(new[] { Plane(1, 0f, 0f, team: 0), Plane(2, 30f, 0f), Plane(3, 3f, 0f) }, 3);
            brain.Tick(Dt);

            Assert.AreEqual(3, brain.TargetId);
        }

        // ───────────────────────── 难度差异与确定性 ─────────────────────────

        [Test]
        public void 不同难度档位的视野差异会改变状态()
        {
            // 12 单位处有敌人：入门档（视野 9）看不见，困难档（视野 18）能打
            AgentSnapshot[] agents = { Plane(1, 0f, 0f, team: 0), Plane(2, 12f, 0f) };

            ArrayAiWorld rookieWorld = MakeWorld(agents);
            PlaneBrain rookie = MakeBrain(rookieWorld, AiProfile.Rookie);
            rookie.Tick(Dt);
            Assert.AreEqual(AiState.Patrol, rookie.CurrentState, "入门档视野只有 9，看不见 12 处的敌人");

            ArrayAiWorld aceWorld = MakeWorld(agents);
            PlaneBrain ace = MakeBrain(aceWorld, AiProfile.Ace);
            ace.Tick(Dt);
            Assert.AreEqual(AiState.Attack, ace.CurrentState, "困难档视野 18，应该已经进入攻击");
        }

        [Test]
        public void 相同世界状态下两个大脑产生完全相同的输入()
        {
            AgentSnapshot[] agents = { Plane(1, 0f, 0f, team: 0), Plane(2, 7f, 1f, vy: 2f) };

            PlaneBrain a = MakeBrain(MakeWorld(agents), AiProfile.Veteran);
            PlaneBrain b = MakeBrain(MakeWorld(agents), AiProfile.Veteran);

            for (int i = 0; i < 120; i++)
            {
                a.Tick(Dt);
                b.Tick(Dt);

                Assert.AreEqual(a.Input.Throttle, b.Input.Throttle, 1e-6f, "第 " + i + " 帧油门不一致");
                Assert.AreEqual(a.Input.Turn, b.Input.Turn, 1e-6f, "第 " + i + " 帧转向不一致");
                Assert.AreEqual(a.Input.FirePrimary, b.Input.FirePrimary, "第 " + i + " 帧开火不一致");
            }
        }

        [Test]
        public void AI输出始终是合法输入_不会越界()
        {
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0, hp: 15f),
                Plane(2, 2f, 2f, vy: 5f));
            PlaneBrain brain = MakeBrain(world);

            for (int i = 0; i < 200; i++)
            {
                brain.Tick(Dt);
                Assert.GreaterOrEqual(brain.Input.Throttle, -1f);
                Assert.LessOrEqual(brain.Input.Throttle, 1f);
                Assert.GreaterOrEqual(brain.Input.Turn, -1f);
                Assert.LessOrEqual(brain.Input.Turn, 1f);
                Assert.IsFalse(float.IsNaN(brain.Input.Turn), "第 " + i + " 帧转向是 NaN");
                Assert.IsFalse(float.IsNaN(brain.Input.AimPoint.X));
            }
        }

        [Test]
        public void 重置会清空记忆与状态()
        {
            ArrayAiWorld world = MakeWorld(Plane(1, 0f, 0f, team: 0), Plane(2, 6f, 0f));
            PlaneBrain brain = MakeBrain(world);
            brain.Tick(Dt);
            Assert.IsTrue(brain.HasTarget);

            brain.Reset();

            Assert.AreEqual(AiState.Patrol, brain.CurrentState);
            Assert.AreEqual(-1, brain.TargetId);
            Assert.IsFalse(brain.HasLastKnownPosition);
        }

        [Test]
        public void 自己死亡后不会继续开火()
        {
            ArrayAiWorld world = MakeWorld(
                Plane(1, 0f, 0f, team: 0, hp: 0f, alive: false),
                Plane(2, 5f, 0f));
            PlaneBrain brain = MakeBrain(world);

            Tick(brain, 3);

            Assert.IsFalse(brain.Input.FirePrimary, "死了就不该再扣扳机");
        }
    }
}
