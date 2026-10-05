using Dogfight.Ai;
using NUnit.Framework;

namespace Dogfight.Tests
{
    /// <summary>
    /// 行为树引擎的单测。
    ///
    /// 只测引擎本身（组合语义、Running 续跑、冷却门控），不测具体业务行为 ——
    /// 业务行为（巡逻/搜索/追击/攻击/规避）属于后续阶段，届时另开一组测试。
    /// 引擎先测稳，是因为所有 AI 都建在它上面：这里错了，上面全错。
    /// </summary>
    public sealed class BehaviorTreeTests
    {
        static BtContext NewContext()
        {
            var world = new ArrayAiWorld();
            world.SetAgents(new AgentSnapshot[0], 0);
            return new BtContext(world, selfId: 1);
        }

        static BtNode Always(BtStatus status, string label = "always") =>
            new BtAction(label, (ctx, dt) => status);

        [Test]
        public void 顺序节点全部成功才成功()
        {
            var sequence = new BtSequence(Always(BtStatus.Success), Always(BtStatus.Success));
            Assert.AreEqual(BtStatus.Success, sequence.Tick(NewContext(), 0.02f));
        }

        [Test]
        public void 顺序节点遇失败立即失败()
        {
            var sequence = new BtSequence(Always(BtStatus.Success), Always(BtStatus.Failure), Always(BtStatus.Success));
            Assert.AreEqual(BtStatus.Failure, sequence.Tick(NewContext(), 0.02f));
        }

        [Test]
        public void 顺序节点遇Running会记住位置下次继续()
        {
            int thirdTicks = 0;
            var sequence = new BtSequence(
                Always(BtStatus.Success),
                Always(BtStatus.Running, "gate"),
                new BtAction("third", (ctx, dt) => { thirdTicks++; return BtStatus.Success; }));

            BtContext ctx = NewContext();
            Assert.AreEqual(BtStatus.Running, sequence.Tick(ctx, 0.02f));
            Assert.AreEqual(0, thirdTicks, "卡在 Running 时不应执行后面的节点");

            Assert.AreEqual(BtStatus.Running, sequence.Tick(ctx, 0.02f));
            Assert.AreEqual(0, thirdTicks);
        }

        [Test]
        public void 选择节点遇到第一个成功就返回()
        {
            int secondTicks = 0;
            var selector = new BtSelector(
                Always(BtStatus.Failure),
                Always(BtStatus.Success, "hit"),
                new BtAction("second", (ctx, dt) => { secondTicks++; return BtStatus.Success; }));

            Assert.AreEqual(BtStatus.Success, selector.Tick(NewContext(), 0.02f));
            Assert.AreEqual(0, secondTicks, "命中后不应继续尝试后面的分支");
        }

        [Test]
        public void 选择节点全部失败才失败()
        {
            var selector = new BtSelector(Always(BtStatus.Failure), Always(BtStatus.Failure));
            Assert.AreEqual(BtStatus.Failure, selector.Tick(NewContext(), 0.02f));
        }

        [Test]
        public void 取反节点翻转成功与失败()
        {
            Assert.AreEqual(BtStatus.Failure, new BtInverter(Always(BtStatus.Success)).Tick(NewContext(), 0.02f));
            Assert.AreEqual(BtStatus.Success, new BtInverter(Always(BtStatus.Failure)).Tick(NewContext(), 0.02f));
            Assert.AreEqual(BtStatus.Running, new BtInverter(Always(BtStatus.Running)).Tick(NewContext(), 0.02f));
        }

        [Test]
        public void 条件节点按谓词返回真假()
        {
            var yes = new BtCondition("yes", ctx => true);
            var no = new BtCondition("no", ctx => false);
            Assert.AreEqual(BtStatus.Success, yes.Tick(NewContext(), 0.02f));
            Assert.AreEqual(BtStatus.Failure, no.Tick(NewContext(), 0.02f));
        }

        [Test]
        public void 等待节点在时间到之前一直是Running()
        {
            var wait = new BtWait(0.1f);
            BtContext ctx = NewContext();

            Assert.AreEqual(BtStatus.Running, wait.Tick(ctx, 0.04f));
            Assert.AreEqual(BtStatus.Running, wait.Tick(ctx, 0.04f));
            Assert.AreEqual(BtStatus.Success, wait.Tick(ctx, 0.04f), "三次 0.04 秒累计超过 0.1 秒，应完成");
        }

        [Test]
        public void 等待节点重置后重新计时()
        {
            var wait = new BtWait(0.1f);
            BtContext ctx = NewContext();

            wait.Tick(ctx, 0.06f);
            wait.Reset();
            Assert.AreEqual(BtStatus.Running, wait.Tick(ctx, 0.06f), "重置后不应立刻完成");
        }

        [Test]
        public void 冷却装饰器在成功后封锁一段时间()
        {
            var cooldown = new BtCooldown(0.5f, Always(BtStatus.Success, "shot"));
            BtContext ctx = NewContext();
            var runner = new BtRunner(cooldown, ctx);

            // 冷却 0.5s、每步 0.1s：第 0.1s 成功 → 封锁到 0.6s → 0.2/0.3/0.4/0.5s 全被拦 → 0.6s 恢复
            Assert.AreEqual(BtStatus.Success, runner.Tick(0.1f), "第一次应该能开火");
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(BtStatus.Failure, runner.Tick(0.1f), "冷却中应该被拦住（第 " + (i + 2) + " 步）");
            }
            Assert.AreEqual(BtStatus.Success, runner.Tick(0.1f), "冷却结束后应恢复");
        }

        [Test]
        public void 运行器每帧推进自身的计时()
        {
            var runner = new BtRunner(Always(BtStatus.Success), NewContext());
            runner.Tick(0.25f);
            runner.Tick(0.25f);
            Assert.AreEqual(0.5f, runner.Context.ElapsedSeconds, 1e-4f);
        }

        [Test]
        public void 黑板能存取值并且取不到时给默认值()
        {
            var board = new Blackboard();
            board.Set(BtKeys.TargetId, 42);

            Assert.IsTrue(board.TryGet<int>(BtKeys.TargetId, out int id));
            Assert.AreEqual(42, id);
            Assert.AreEqual(-1, board.GetOrDefault(BtKeys.TargetIndex, -1), "缺失的键应返回调用方给的默认值");
        }

        [Test]
        public void 运行器重置会清空黑板()
        {
            var runner = new BtRunner(Always(BtStatus.Success), NewContext());
            runner.Context.Board.Set("x", 1);
            runner.Reset();
            Assert.IsFalse(runner.Context.Board.Has("x"));
        }

        [Test]
        public void 上下文能按自身id找到自己的快照()
        {
            var world = new ArrayAiWorld();
            world.SetAgents(new[]
            {
                new AgentSnapshot(1, new Vec2(1f, 2f), Vec2.Zero, 0f, 100f, 100f, true, 0),
                new AgentSnapshot(2, new Vec2(5f, 5f), Vec2.Zero, 0f, 100f, 100f, true, 1),
            }, 2);

            var ctx = new BtContext(world, selfId: 2);
            ctx.BeginFrame(0.02f);

            Assert.AreEqual(2, ctx.Self.Id);
            Assert.AreEqual(5f, ctx.Self.Position.X, 1e-4f);
        }

        [Test]
        public void 世界能找出最近的敌人()
        {
            var world = new ArrayAiWorld();
            world.SetAgents(new[]
            {
                new AgentSnapshot(1, new Vec2(0f, 0f), Vec2.Zero, 0f, 100f, 100f, true, 0),
                new AgentSnapshot(2, new Vec2(3f, 0f), Vec2.Zero, 0f, 100f, 100f, true, 1),
                new AgentSnapshot(3, new Vec2(9f, 0f), Vec2.Zero, 0f, 100f, 100f, true, 1),
            }, 3);

            int index = world.FindNearestEnemyIndex(new Vec2(0f, 0f), excludeTeam: 0);

            Assert.AreEqual(1, index);
            Assert.AreEqual(2, world.GetAgent(index).Id);
        }

        [Test]
        public void 世界统计存活时忽略已死亡与同阵营()
        {
            var world = new ArrayAiWorld();
            world.SetAgents(new[]
            {
                new AgentSnapshot(1, Vec2.Zero, Vec2.Zero, 0f, 100f, 100f, true, 1),
                new AgentSnapshot(2, Vec2.Zero, Vec2.Zero, 0f, 0f, 100f, false, 1),
                new AgentSnapshot(3, Vec2.Zero, Vec2.Zero, 0f, 100f, 100f, true, 0),
            }, 3);

            Assert.AreEqual(1, world.CountAlive(1));
            Assert.AreEqual(1, world.CountAlive(0));
        }
    }
}
