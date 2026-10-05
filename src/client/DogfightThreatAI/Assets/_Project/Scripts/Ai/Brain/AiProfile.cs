namespace Dogfight.Ai
{
    /// <summary>
    /// 立项书要求的五种行为模式。顺序即"优先级由高到低"，与行为树的选择节点一一对应。
    /// </summary>
    public enum AiState
    {
        /// <summary>巡逻：沿分配到空域的航线巡航，边飞边搜索。</summary>
        Patrol = 0,

        /// <summary>搜索：有目标最后出现的位置，前往确认；到了没找到就回到巡逻。</summary>
        Search = 1,

        /// <summary>追击：已发现目标但还打不到，全油门接近并进入射击位。</summary>
        Pursue = 2,

        /// <summary>攻击：目标在射程内且机头已对准，开火。</summary>
        Attack = 3,

        /// <summary>规避：残血或被人贴脸时脱离，保命优先。</summary>
        Evade = 4,
    }

    /// <summary>
    /// AI 的可调参数。
    ///
    /// 刻意把"难度"做成参数差异而不是另写一套 AI —— 立项书里的三档难度最终要靠
    /// 不同算法（BFS / A* / 遗传算法）区分，但在那之前，先把同一套行为树的
    /// 参数分档跑起来，这样"难度"从第一天就是可观测、可统计的。
    /// </summary>
    public readonly struct AiProfile
    {
        /// <summary>发现敌人的距离。超出它不会主动交战。</summary>
        public readonly float DetectionRange;

        /// <summary>丢失目标的距离。**必须大于 DetectionRange**，否则在边界上会疯狂切换状态。</summary>
        public readonly float LoseTargetRange;

        /// <summary>允许开火的夹角（弧度）：机头与射击方向差在这个范围内才开枪。</summary>
        public readonly float FireAngleTolerance;

        /// <summary>角误差超过它就给满舵。越小转向越"细腻"。</summary>
        public readonly float TurnFullDeflection;

        /// <summary>巡逻路点的到达判定半径。</summary>
        public readonly float ArriveRadius;

        /// <summary>血量比例低于它进入规避。</summary>
        public readonly float EvadeHpRatio;

        /// <summary>规避持续时间（秒）。</summary>
        public readonly float EvadeSeconds;

        /// <summary>敌人贴到这么近也会触发规避（配合中等血量）。</summary>
        public readonly float EvadeDistance;

        /// <summary>巡逻时的油门。</summary>
        public readonly float CruiseThrottle;

        /// <summary>逃逸时的油门。</summary>
        public readonly float FleeThrottle;

        public AiProfile(
            float detectionRange,
            float loseTargetRange,
            float fireAngleTolerance,
            float turnFullDeflection,
            float arriveRadius,
            float evadeHpRatio,
            float evadeSeconds,
            float evadeDistance,
            float cruiseThrottle,
            float fleeThrottle)
        {
            DetectionRange = detectionRange;
            LoseTargetRange = loseTargetRange < detectionRange ? detectionRange : loseTargetRange;
            FireAngleTolerance = fireAngleTolerance;
            TurnFullDeflection = turnFullDeflection < 1e-3f ? 1e-3f : turnFullDeflection;
            ArriveRadius = arriveRadius < 0.05f ? 0.05f : arriveRadius;
            EvadeHpRatio = MathUtil.Clamp01(evadeHpRatio);
            EvadeSeconds = evadeSeconds < 0f ? 0f : evadeSeconds;
            EvadeDistance = evadeDistance;
            CruiseThrottle = MathUtil.Clamp01(cruiseThrottle);
            FleeThrottle = MathUtil.Clamp01(fleeThrottle);
        }

        /// <summary>
        /// 入门档：看得近、打得保守、一挨打就跑。
        /// 玩家能明显感觉到"它有点笨"，但不会毫无还手之力。
        /// </summary>
        public static AiProfile Rookie =>
            new AiProfile(
                detectionRange: 9f,
                loseTargetRange: 12f,
                fireAngleTolerance: 6f * MathUtil.Deg2Rad,
                turnFullDeflection: 20f * MathUtil.Deg2Rad,
                arriveRadius: 1.5f,
                evadeHpRatio: 0.45f,
                evadeSeconds: 2.5f,
                evadeDistance: 1.5f,
                cruiseThrottle: 0.6f,
                fleeThrottle: 1f);

        /// <summary>中档：看得更远、开火窗口更宽、更能忍。</summary>
        public static AiProfile Veteran =>
            new AiProfile(
                detectionRange: 13f,
                loseTargetRange: 17f,
                fireAngleTolerance: 12f * MathUtil.Deg2Rad,
                turnFullDeflection: 26f * MathUtil.Deg2Rad,
                arriveRadius: 1.2f,
                evadeHpRatio: 0.3f,
                evadeSeconds: 1.8f,
                evadeDistance: 1.2f,
                cruiseThrottle: 0.75f,
                fleeThrottle: 1f);

        /// <summary>困难档：视野接近全屏、开火窗口最大、几乎不主动脱离。</summary>
        public static AiProfile Ace =>
            new AiProfile(
                detectionRange: 18f,
                loseTargetRange: 24f,
                fireAngleTolerance: 20f * MathUtil.Deg2Rad,
                turnFullDeflection: 32f * MathUtil.Deg2Rad,
                arriveRadius: 1f,
                evadeHpRatio: 0.15f,
                evadeSeconds: 1.2f,
                evadeDistance: 0.8f,
                cruiseThrottle: 0.9f,
                fleeThrottle: 1f);

        public static AiProfile Default => Veteran;

        public override string ToString() =>
            "AiProfile(detect=" + DetectionRange + ", fire±" +
            (FireAngleTolerance * MathUtil.Rad2Deg).ToString("0.#") + "°, evadeHp<" +
            EvadeHpRatio.ToString("0.##") + ")";
    }
}
