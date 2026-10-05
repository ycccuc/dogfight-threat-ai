namespace Dogfight.Ai
{
    /// <summary>
    /// 敌机的大脑：把"看到什么"翻译成"推杆多少"。
    ///
    /// 结构是一棵**优先级选择树**（行为树的选择节点，从上到下第一个成功的生效）：
    ///
    ///     规避   ← 残血或被人贴脸，保命优先
    ///     攻击   ← 目标在射程内且机头已对准 → 开火
    ///     追击   ← 已发现目标但还打不到 → 全油门接近
    ///     搜索   ← 有目标最后出现的位置 → 前往确认
    ///     巡逻   ← 兜底：沿空域航线巡航
    ///
    /// 三件刻意的事：
    ///   1. **AI 不作弊** —— 它只写 IPlaneInput，和玩家走同一条通路，
    ///      因此同样受失速、转向惯性、速度上限的约束。胜率统计才有意义。
    ///   2. **目标黏性** —— 已经在打的敌人在 LoseTargetRange 内就继续打它，
    ///      否则两个距离接近的敌人会让 AI 每帧切目标（画面上是抽搐）。
    ///   3. **状态可观测** —— CurrentState 暴露出来给 HUD 与批量对战统计用。
    /// </summary>
    public sealed class PlaneBrain
    {
        readonly ScriptedPlaneInput _input = new ScriptedPlaneInput();
        readonly BtRunner _runner;
        readonly AiProfile _profile;
        readonly WeaponSpec _primaryWeapon;
        readonly int _selfId;
        readonly int _selfTeam;

        PatrolRoute _route;
        int _targetId = -1;
        Vec2 _lastKnownTargetPosition;
        bool _hasLastKnown;
        float _evadeUntil;

        public PlaneBrain(
            int selfId,
            int selfTeam,
            IAiWorld world,
            in AiProfile profile,
            in WeaponSpec primaryWeapon,
            PatrolRoute route = null)
        {
            _selfId = selfId;
            _selfTeam = selfTeam;
            _profile = profile;
            _primaryWeapon = primaryWeapon;
            _route = route;

            var context = new BtContext(world, selfId);
            _runner = new BtRunner(BuildTree(), context);
        }

        /// <summary>交给 PlaneAgent.SetInput —— AI 与玩家共用的插槽。</summary>
        public IPlaneInput Input => _input;

        public AiState CurrentState { get; private set; } = AiState.Patrol;

        public int TargetId => _targetId;

        public bool HasTarget => _targetId >= 0;

        public bool HasLastKnownPosition => _hasLastKnown;

        public Vec2 LastKnownTargetPosition => _lastKnownTargetPosition;

        public BtContext Context => _runner.Context;

        public PatrolRoute Route => _route;

        public void SetRoute(PatrolRoute route) => _route = route;

        /// <summary>推进一帧。dt 由对局循环给（批量对战里是固定步长）。</summary>
        public void Tick(float dt) => _runner.Tick(dt);

        public void Reset()
        {
            _targetId = -1;
            _lastKnownTargetPosition = Vec2.Zero;
            _hasLastKnown = false;
            _evadeUntil = 0f;
            _input.Reset();
            _route?.Reset();
            _runner.Reset();
            CurrentState = AiState.Patrol;
        }

        BtNode BuildTree()
        {
            return new BtSelector(
                // 阵亡守卫必须放最前面：否则死掉的飞机会继续扣扳机
                new BtSequence(
                    new BtCondition("已阵亡", ctx => !ctx.Self.Alive),
                    new BtAction("待机", DoIdle)),
                new BtSequence(
                    new BtCondition("应规避", ShouldEvade),
                    new BtAction("规避", DoEvade)),
                new BtSequence(
                    new BtCondition("可攻击", CanAttack),
                    new BtAction("攻击", DoAttack)),
                new BtSequence(
                    new BtCondition("有目标", TryAcquireTarget),
                    new BtAction("追击", DoPursue)),
                new BtSequence(
                    new BtCondition("有线索", HasLastKnownClue),
                    new BtAction("搜索", DoSearch)),
                new BtAction("巡逻", DoPatrol));
        }

        // ───────────────────────── 条件 ─────────────────────────

        /// <summary>
        /// 该不该跑。两条触发路径：
        ///   ① 残血（低于 EvadeHpRatio）
        ///   ② 血量不算健康 且 敌人贴得比 EvadeDistance 还近
        /// 触发后进入一个 EvadeSeconds 的窗口，窗口内一直跑 —— 避免"打一下跑一下"的抖动。
        /// </summary>
        bool ShouldEvade(BtContext ctx)
        {
            if (ctx.ElapsedSeconds < _evadeUntil) return true;

            float hpRatio = ctx.Self.HpRatio;
            if (hpRatio <= 0f) return false;

            bool lowHp = hpRatio < _profile.EvadeHpRatio;
            bool tooCloseAndHurt = hpRatio < _profile.EvadeHpRatio * 2f &&
                                   TryFindNearestEnemy(ctx, out AgentSnapshot enemy) &&
                                   Vec2.Distance(enemy.Position, ctx.Self.Position) < _profile.EvadeDistance;

            if (!lowHp && !tooCloseAndHurt) return false;

            _evadeUntil = ctx.ElapsedSeconds + _profile.EvadeSeconds;
            return true;
        }

        bool CanAttack(BtContext ctx)
        {
            RefreshTarget(ctx);
            if (!TryGetTarget(ctx, out AgentSnapshot target)) return false;

            Vec2 aim = Steering.AimDirection(ctx.Self.Position, target, _primaryWeapon);
            float distance = Vec2.Distance(target.Position, ctx.Self.Position);
            if (distance > _primaryWeapon.Range) return false;

            float error = MathUtil.Abs(Vec2.DeltaAngle(ctx.Self.Heading.AngleRadians, aim.AngleRadians));
            return error <= _profile.FireAngleTolerance;
        }

        /// <summary>
        /// 条件：能锁定目标吗。顺带刷新目标记忆 ——
        /// 所以这个条件有副作用，名字刻意叫 TryAcquire 而不是 Has，避免读代码的人误以为是纯查询。
        /// </summary>
        bool TryAcquireTarget(BtContext ctx)
        {
            RefreshTarget(ctx);
            return _targetId >= 0;
        }

        bool HasLastKnownClue(BtContext ctx) => _hasLastKnown;

        // ───────────────────────── 动作 ─────────────────────────

        BtStatus DoIdle(BtContext ctx, float dt)
        {
            _input.Set(InputFrame.Idle);
            return BtStatus.Success;
        }

        BtStatus DoEvade(BtContext ctx, float dt)
        {
            CurrentState = AiState.Evade;

            if (!TryFindNearestEnemy(ctx, out AgentSnapshot enemy))
            {
                // 没有威胁就正常巡航，别对着空气逃
                _input.Set(new InputFrame(_profile.CruiseThrottle, 0f, false, false,
                    ctx.Self.Position + ctx.Self.Heading * 10f));
                return BtStatus.Running;
            }

            Vec2 away = Steering.FleeDirection(ctx.Self.Position, enemy.Position, ctx.Self.Heading);
            float turn = Steering.TurnInputFor(ctx.Self.Heading, away, _profile.TurnFullDeflection);

            _input.Set(new InputFrame(_profile.FleeThrottle, turn, false, false,
                ctx.Self.Position + away * 10f));
            return BtStatus.Running;
        }

        BtStatus DoAttack(BtContext ctx, float dt)
        {
            CurrentState = AiState.Attack;

            if (!TryGetTarget(ctx, out AgentSnapshot target))
            {
                _input.Set(InputFrame.Idle);
                return BtStatus.Failure;
            }

            Vec2 aim = Steering.AimDirection(ctx.Self.Position, target, _primaryWeapon);
            float distance = Vec2.Distance(target.Position, ctx.Self.Position);
            float turn = Steering.TurnInputFor(ctx.Self.Heading, aim, _profile.TurnFullDeflection);
            float throttle = Steering.ArriveThrottle(distance, _profile.ArriveRadius, _profile.CruiseThrottle);

            // 开火：是否真的命中由命中判定决定（Gunnery.ResolveShot），AI 只负责扣扳机
            _input.Set(new InputFrame(throttle, turn, true, false, ctx.Self.Position + aim * distance));
            return BtStatus.Running;
        }

        BtStatus DoPursue(BtContext ctx, float dt)
        {
            CurrentState = AiState.Pursue;

            if (!TryGetTarget(ctx, out AgentSnapshot target))
            {
                _input.Set(InputFrame.Idle);
                return BtStatus.Failure;
            }

            Vec2 aim = Steering.AimDirection(ctx.Self.Position, target, _primaryWeapon);
            float distance = Vec2.Distance(target.Position, ctx.Self.Position);
            float turn = Steering.TurnInputFor(ctx.Self.Heading, aim, _profile.TurnFullDeflection);

            // 追击时全油门：要尽快进入射击位
            _input.Set(new InputFrame(1f, turn, false, false, ctx.Self.Position + aim * distance));
            return BtStatus.Running;
        }

        BtStatus DoSearch(BtContext ctx, float dt)
        {
            CurrentState = AiState.Search;

            Vec2 toLastKnown = _lastKnownTargetPosition - ctx.Self.Position;
            float distance = toLastKnown.Magnitude;

            if (distance <= _profile.ArriveRadius * 2f)
            {
                // 到了最后已知位置还是没人 → 线索作废，下一帧回到巡逻
                _hasLastKnown = false;
                return BtStatus.Success;
            }

            float turn = Steering.TurnInputFor(ctx.Self.Heading, toLastKnown.Normalized, _profile.TurnFullDeflection);
            _input.Set(new InputFrame(_profile.CruiseThrottle, turn, false, false, _lastKnownTargetPosition));
            return BtStatus.Running;
        }

        BtStatus DoPatrol(BtContext ctx, float dt)
        {
            CurrentState = AiState.Patrol;

            if (_route == null || _route.IsEmpty)
            {
                // 没分配航线就直飞（仍然受战场边界约束，由 MatchLoop 负责推回来）
                _input.Set(new InputFrame(_profile.CruiseThrottle, 0f, false, false,
                    ctx.Self.Position + ctx.Self.Heading * 10f));
                return BtStatus.Running;
            }

            if (_route.HasReached(ctx.Self.Position, _profile.ArriveRadius))
            {
                _route.Advance();
            }

            Vec2 waypoint = _route.Current;
            Vec2 toWaypoint = waypoint - ctx.Self.Position;
            float distance = toWaypoint.Magnitude;

            Vec2 desired = toWaypoint.SqrMagnitude > 1e-6f ? toWaypoint.Normalized : ctx.Self.Heading;
            float turn = Steering.TurnInputFor(ctx.Self.Heading, desired, _profile.TurnFullDeflection);
            float throttle = Steering.ArriveThrottle(distance, _profile.ArriveRadius, _profile.CruiseThrottle);

            _input.Set(new InputFrame(throttle, turn, false, false, waypoint));
            return BtStatus.Running;
        }

        // ───────────────────────── 目标管理 ─────────────────────────

        /// <summary>
        /// 刷新目标。用 DetectionRange 去"发现"、用 LoseTargetRange 去"保持"，
        /// 两者之间的滞后区间就是防抖动的关键。
        /// </summary>
        void RefreshTarget(BtContext ctx)
        {
            float range = _targetId >= 0 ? _profile.LoseTargetRange : _profile.DetectionRange;

            if (AiWorldQuery.TryFindNearestEnemy(
                    ctx.World, ctx.Self.Position, _selfTeam, range, _selfId,
                    out AgentSnapshot target,
                    preferId: _targetId,
                    preferRange: _profile.LoseTargetRange))
            {
                _targetId = target.Id;
                _lastKnownTargetPosition = target.Position;
                _hasLastKnown = true;
            }
            else
            {
                _targetId = -1;
            }
        }

        bool TryGetTarget(BtContext ctx, out AgentSnapshot target)
        {
            if (_targetId < 0)
            {
                target = default;
                return false;
            }
            return AiWorldQuery.TryFindById(ctx.World, _targetId, out target) && target.Alive;
        }

        bool TryFindNearestEnemy(BtContext ctx, out AgentSnapshot enemy) =>
            AiWorldQuery.TryFindNearestEnemy(
                ctx.World, ctx.Self.Position, _selfTeam, _profile.DetectionRange * 2f, _selfId, out enemy);
    }
}
