namespace Dogfight.Ai
{
    /// <summary>
    /// 飞机输入的抽象。
    ///
    /// 这是整套架构里最值钱的一条边界：玩家和 AI 实现同一个接口，
    /// 于是"AI 自动对战"= 把玩家身上的输入实现换成 AI 实现，
    /// 而不是另写一套游戏逻辑。批量对战跑的就是同一个对局代码。
    ///
    /// 约定：一帧读一次；读到之后在下一帧之前不应再变化。
    /// 转向符号：Turn = +1 表示**左转（逆时针）**，-1 表示右转。
    /// </summary>
    public interface IPlaneInput
    {
        /// <summary>油门：-1（反推/减速）~ +1（全开）。</summary>
        float Throttle { get; }

        /// <summary>转向：+1 = 左转（逆时针），-1 = 右转（顺时针）。</summary>
        float Turn { get; }

        /// <summary>主武器（机炮）是否按住。</summary>
        bool FirePrimary { get; }

        /// <summary>副武器（导弹）是否按住。</summary>
        bool FireSecondary { get; }

        /// <summary>瞄准点的世界坐标。</summary>
        Vec2 AimPoint { get; }
    }

    /// <summary>
    /// 一帧输入的快照（纯数据）。
    /// 刻意不让它实现 IPlaneInput：结构体通过接口访问会被装箱，
    /// 而这是每帧每架飞机都要读的东西。
    /// </summary>
    public readonly struct InputFrame
    {
        public readonly float ThrottleValue;
        public readonly float TurnValue;
        public readonly bool FirePrimaryHeld;
        public readonly bool FireSecondaryHeld;
        public readonly Vec2 AimPointValue;

        public InputFrame(float throttle, float turn, bool firePrimary, bool fireSecondary, Vec2 aimPoint)
        {
            ThrottleValue = MathUtil.Clamp(throttle, -1f, 1f);
            TurnValue = MathUtil.Clamp(turn, -1f, 1f);
            FirePrimaryHeld = firePrimary;
            FireSecondaryHeld = fireSecondary;
            AimPointValue = aimPoint;
        }

        public static readonly InputFrame Idle = new InputFrame(0f, 0f, false, false, Vec2.Zero);

        public InputFrame WithThrottle(float throttle) =>
            new InputFrame(throttle, TurnValue, FirePrimaryHeld, FireSecondaryHeld, AimPointValue);

        public InputFrame WithTurn(float turn) =>
            new InputFrame(ThrottleValue, turn, FirePrimaryHeld, FireSecondaryHeld, AimPointValue);

        public override string ToString() =>
            "Input(thr=" + ThrottleValue.ToString("0.##") + ", turn=" + TurnValue.ToString("0.##") +
            ", fire=" + (FirePrimaryHeld ? "Y" : "n") + ", aim=" + AimPointValue + ")";
    }

    /// <summary>
    /// 由外部（AI / 脚本 / 批量对战）写入的输入源。
    /// 玩家用 KeyboardPlaneInput，AI 用它 —— 两者在 PlaneAgent 眼里没有区别。
    /// </summary>
    public sealed class ScriptedPlaneInput : IPlaneInput
    {
        InputFrame _frame = InputFrame.Idle;

        public void Set(in InputFrame frame) => _frame = frame;

        public void Reset() => _frame = InputFrame.Idle;

        /// <summary>便捷写法：只设油门与转向，不开火。</summary>
        public void SetSteering(float throttle, float turn, Vec2 aimPoint) =>
            _frame = new InputFrame(throttle, turn, false, false, aimPoint);

        public float Throttle => _frame.ThrottleValue;

        public float Turn => _frame.TurnValue;

        public bool FirePrimary => _frame.FirePrimaryHeld;

        public bool FireSecondary => _frame.FireSecondaryHeld;

        public Vec2 AimPoint => _frame.AimPointValue;
    }

    /// <summary>
    /// 只会绕圈的**测试替身**（不是 AI）。
    /// 用途：在没有实现行为树之前，先让批量对战管道跑起来 —— 让对局能推进到超时结算，
    /// 从而验证「Tick 循环 + 手动步进物理 + 结果统计」这条链路是通的。
    /// 等行为树落地后，它只保留在单测里当稳定输入源。
    /// </summary>
    public sealed class CircleTestInput : IPlaneInput
    {
        float _elapsed;
        readonly float _period;
        readonly float _throttle;

        public CircleTestInput(float periodSeconds = 6f, float throttle = 1f)
        {
            _period = periodSeconds > 0.01f ? periodSeconds : 6f;
            _throttle = MathUtil.Clamp(throttle, -1f, 1f);
        }

        /// <summary>每帧推进一次时间（由对局循环调用）。</summary>
        public void Step(float dt) => _elapsed += dt;

        public float Throttle => _throttle;

        /// <summary>按正弦左右摆动 —— 确定性、可复现。</summary>
        public float Turn
        {
            get
            {
                float phase = _elapsed / _period * MathUtil.TwoPi;
                return (float)System.Math.Sin(phase);
            }
        }

        public bool FirePrimary => false;

        public bool FireSecondary => false;

        public Vec2 AimPoint => Vec2.Zero;
    }
}
