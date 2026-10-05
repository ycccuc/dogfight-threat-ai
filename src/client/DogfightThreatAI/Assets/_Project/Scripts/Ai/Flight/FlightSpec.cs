namespace Dogfight.Ai
{
    /// <summary>
    /// 飞行动力学参数（纯数据）。
    ///
    /// 刻意不加 [Serializable]：Unity 不会序列化 readonly 字段，
    /// 加了只会让人误以为能在 Inspector 里存。这些数值的来源是 Gameplay 层的
    /// FlightProfileSO（在那里调参），运行时烘焙成这个结构体交给本层使用。
    ///
    /// 参数含义与暑期原型的 PlaneController 一一对应，方便把已经调过的数值搬过来。
    /// </summary>
    public readonly struct FlightSpec
    {
        /// <summary>油门全开时的加速度（单位/秒²）。</summary>
        public readonly float Thrust;

        /// <summary>极速上限（单位/秒）。</summary>
        public readonly float MaxSpeed;

        /// <summary>最大转向速率（**弧度/秒**，逆时针为正）。</summary>
        public readonly float TurnRate;

        /// <summary>转向加速度（**弧度/秒²**）：角速度逼近目标值的速度。</summary>
        public readonly float TurnAccel;

        /// <summary>失速速度：低于它，转向能力开始下降（单位/秒）。</summary>
        public readonly float StallSpeed;

        /// <summary>0~1，速度不足时损失多少转向能力。</summary>
        public readonly float StallPenalty;

        /// <summary>线性阻尼。实际由 Gameplay 层设置 Rigidbody2D.drag，本层只做记录与校验。</summary>
        public readonly float LinearDrag;

        public FlightSpec(
            float thrust,
            float maxSpeed,
            float turnRate,
            float turnAccel,
            float stallSpeed,
            float stallPenalty,
            float linearDrag)
        {
            Thrust = thrust;
            MaxSpeed = maxSpeed;
            TurnRate = turnRate;
            TurnAccel = turnAccel;
            StallSpeed = stallSpeed;
            StallPenalty = MathUtil.Clamp01(stallPenalty);
            LinearDrag = linearDrag;
        }

        /// <summary>
        /// 与暑期原型 PlaneController 默认值等价的参数（角度已换算成弧度）：
        /// thrust 22 / maxSpeed 9 / turnRate 260°/s / turnAccel 1800°/s² / stallSpeed 1.5 / stallPenalty 0.6
        /// </summary>
        public static FlightSpec Default =>
            new FlightSpec(
                thrust: 22f,
                maxSpeed: 9f,
                turnRate: 260f * MathUtil.Deg2Rad,
                turnAccel: 1800f * MathUtil.Deg2Rad,
                stallSpeed: 1.5f,
                stallPenalty: 0.6f,
                linearDrag: 0.8f);

        public override string ToString() =>
            "FlightSpec(thrust=" + Thrust + ", maxSpeed=" + MaxSpeed +
            ", turnRate=" + (TurnRate * MathUtil.Rad2Deg).ToString("0.#") + "°/s" +
            ", stall=" + StallSpeed + ")";
    }
}
