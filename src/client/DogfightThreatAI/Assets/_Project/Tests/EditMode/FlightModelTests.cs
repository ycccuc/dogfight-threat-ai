using System;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.Flight;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    /// <summary>
    /// 【模拟层的价值证明】这几个测试之所以能跑，唯一原因是 FlightModel 是纯 C#。
    ///
    /// 如果飞行逻辑写在 MonoBehaviour 的 Update 里，你根本无法断言
    /// "速度不会超过上限"——因为那需要真实帧循环、需要一个 GameObject、需要 Input。
    /// 这就是"零 UnityEngine"最直接的好处，也是立项书"批量对战 100 局出胜率矩阵"的地基。
    /// </summary>
    public sealed class FlightModelTests
    {
        const float Dt = 1f / 50f;   // 固定步长，必须和 Time.fixedDeltaTime 一致

        static FlightModel NewModel()
        {
            return new FlightModel(
            minSpeed: 2f,
            maxSpeed: 8f,
            acceleration: 12f,
            turnRateDegPerSec: 180f,
            drag: 0f);
        }

        /// <summary>测试 1：油门拉满也不能超过极速上限。</summary>
        [Test]
        public void 油门拉满_速度不会超过上限()
        {
            var model = NewModel();
            var state = FlightState.At(Vec2.Zero);
            var full = new FlightInput(1f, 0f);

            for (int i = 0; i < 500; i++)
            {
                model.Step(ref state, full, Dt);
            }
            Assert.LessOrEqual(state.Speed, 8f + 1e-3f, "速度超过了 maxSpeed，手感边界失效");
            Assert.Greater(state.Speed, 0f, "飞机应该已经飞起来了");
        }

        /// <summary>测试 2：单步内的速度变化量受加速度限制 —— 这就是"惯性"的可测形式。</summary>
        [Test]
        public void 单步加速度受限制_所以有惯性()
        {
            var model = NewModel();
            var state = FlightState.At(Vec2.Zero);
            var full = new FlightInput(1f, 0f);

            model.Step(ref state, full, Dt);

            Assert.LessOrEqual(state.Speed, 12f * Dt + 1e-3f,
                "一步就加速到目标速度 = 没有惯性，玩家会感觉像瞬移");
        }

        /// <summary>测试 3：油门调低时速度会降下来（速度边界是双向的）。</summary>
        [Test]
        public void 油门调低_速度会下降()
        {
            var model = NewModel();
            var state = FlightState.At(Vec2.Zero);

            for (int i = 0; i < 200; i++)
            {
                model.Step(ref state, new FlightInput(1f, 0f), Dt);
            }
            float fast = state.Speed;

            for (int i = 0; i < 200; i++)
            {
                model.Step(ref state, new FlightInput(-1f, 0f), Dt);
            }
            Assert.Less(state.Speed, fast, "减速无效，说明油门没接进速度目标");
            Assert.Less(state.Speed, 0.5f, "按住 S 之后应该基本停下来");
            Assert.GreaterOrEqual(state.Speed, 0f, "速度不能为负");
        }

        /// <summary>测试 4：相同输入序列 ⇒ 完全相同的结果。这是客户端预测/回滚的前提。</summary>
        [Test]
        public void 相同输入_结果完全一致()
        {
            var a = FlightState.At(Vec2.Zero);
            var b = FlightState.At(Vec2.Zero);
            var modelA = NewModel();
            var modelB = NewModel();

            for (int i = 0; i < 200; i++)
            {
                var input = new FlightInput(1f, (i % 20 < 10) ? 1f : -1f);
                modelA.Step(ref a, input, Dt);
                modelB.Step(ref b, input, Dt);
            }

            Assert.AreEqual(a.Position.X, b.Position.X, 1e-6f);
            Assert.AreEqual(a.Position.Y, b.Position.Y, 1e-6f);
            Assert.AreEqual(a.Heading, b.Heading, 1e-6f);
        }

        /// <summary>
        /// 测试 5（回归测试）：不按任何键，飞机必须原地不动。
        /// 这条曾经失败过 —— 油门 0 时被映射成 50% 速度，加上初始朝向 0（+X），
        /// 就表现为"一运行飞机自己一直往右飞"。
        /// </summary>
        [Test]
        public void 无输入_不会自己往前冲()
        {
            var model = NewModel();
            var state = FlightState.At(Vec2.Zero);

            for (int i = 0; i < 500; i++)
            {
                model.Step(ref state, FlightInput.Neutral, Dt);
            }
            Assert.AreEqual(0f, state.Speed, 1e-3f, "不按任何键飞机却在移动");
            Assert.AreEqual(0f, state.Position.Magnitude, 1e-3f, "不按任何键飞机却位移了");
        }

        /// <summary>测试 6：松开油门后应该保持惯性滑行（而不是立刻停住）。</summary>
        [Test]
        public void 松开油门_保持惯性滑行()
        {
            var model = NewModel();
            var state = FlightState.At(Vec2.Zero);

            for (int i = 0; i < 100; i++)
            {
                model.Step(ref state, new FlightInput(1f, 0f), Dt);
            }
            float cruise = state.Speed;

            for (int i = 0; i < 50; i++)
            {
                model.Step(ref state, FlightInput.Neutral, Dt);
            }
            Assert.AreEqual(cruise, state.Speed, 0.05f, "松开油门速度应基本保持，这才是惯性");
        }

        /// <summary>
        /// 测试 7：转向时应该出现"甩尾"（机头方向 ≠ 速度方向），松开转向后自动收敛回直线。
        ///
        /// 这就是赛车漂移手感：甩得出去，也能自己收回来。
        ///   - 如果 grip 太大 → 甩尾角约 0°，感觉像"被钉在轨道上"
        ///   - 如果 grip 太小 → 松开后长时间滑不停，像冰面
        /// </summary>
        [Test]
        public void 转向时出现漂移_松开后自动收敛()
        {
            var model = NewModel();
            var state = FlightState.At(Vec2.Zero);

            for (int i = 0; i < 100; i++)
            {
                model.Step(ref state, new FlightInput(1f, 0f), Dt);  // 直飞
            }
            for (int i = 0; i < 40; i++)
            {
                model.Step(ref state, new FlightInput(1f, 1f), Dt);  // 持续左转 → 甩尾
            }
            float slipWhileTurning = SlipDegrees(state);
            Assert.Greater(slipWhileTurning, 5f, "转向时应该有甩尾（机头与速度方向不一致），否则就是'被钉在轨道上'");

            for (int i = 0; i < 120; i++)
            {
                model.Step(ref state, new FlightInput(1f, 0f), Dt);  // 松开转向
            }
            Assert.Less(SlipDegrees(state), 1f, "松开转向后甩尾应该自己收住");
        }

        /// <summary>测试 8：漂移要掉速（侧滑阻力），避免"免费无限速度漂移"。</summary>
        [Test]
        public void 漂移会掉速()
        {
            var straight = FlightState.At(Vec2.Zero);
            var turning = FlightState.At(Vec2.Zero);
            var m1 = NewModel();
            var m2 = NewModel();

            for (int i = 0; i < 150; i++)
            {
                m1.Step(ref straight, new FlightInput(1f, 0f), Dt);
                m2.Step(ref turning, new FlightInput(1f, 1f), Dt);
            }

            float fwdStraight = Vec2.Dot(straight.Velocity, straight.Forward);
            float fwdTurning = Vec2.Dot(turning.Velocity, turning.Forward);

            Assert.Less(fwdTurning, fwdStraight - 0.2f, "漂移应该损失前进速度");
        }

        /// <summary>测试 9：有了战场边界后，飞机不能飞出战场（固定镜头方案的硬前提）。</summary>
        [Test]
        public void 飞出战场会被挡住()
        {
            var arena = Bounds2D.Centered(new Vec2(5f, 5f));
            var model = new FlightModel(2f, 8f, 12f, 180f, 0f, 6f, 0.35f, arena);
            var state = FlightState.At(Vec2.Zero, 0f);   // 机头朝 +X

            for (int i = 0; i < 500; i++)
            {
                model.Step(ref state, new FlightInput(1f, 0f), Dt);
            }
            Assert.LessOrEqual(state.Position.X, 5f + 1e-3f, "飞出了右边界");
            Assert.GreaterOrEqual(state.Position.X, 4.9f, "应该贴在边界上，而不是被弹回去很远");
        }

        /// <summary>机头方向与速度方向的夹角（度）。0 = 完全贴地，90 = 纯侧滑。</summary>
        static float SlipDegrees(FlightState s)
        {
            if (s.Speed < 1e-3f)
            {
                return 0f;
            }
            float cos = Vec2.Dot(s.Velocity.Normalized, s.Forward);
            if (cos > 1f)
            {
                cos = 1f;
            }
            else if (cos < -1f)
            {
                cos = -1f;
            }
            return MathF.Acos(cos) * 57.29578f;   // 弧度 → 度
        }
    }
}
