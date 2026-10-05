using System;
using System.Collections.Generic;

namespace Dogfight.Ai
{
    public enum BtStatus
    {
        Success = 0,
        Failure = 1,

        /// <summary>本帧还没做完，下一帧继续。用于 Wait / 导弹锁定这类跨帧行为。</summary>
        Running = 2,
    }

    /// <summary>
    /// 黑板：行为树节点之间传递数据的共享内存。
    ///
    /// 用 string 键 + object 值是最省事的做法，代价是装箱 —— 但读写发生在
    /// "状态切换时"而不是"每帧每节点"，量很小。（每帧都要读的东西应该走
    /// BtContext 的强类型字段，不要塞黑板。）
    /// </summary>
    public sealed class Blackboard
    {
        readonly Dictionary<string, object> _values = new Dictionary<string, object>(16);

        public void Set<T>(string key, T value) => _values[key] = value;

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out object raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public T GetOrDefault<T>(string key, T fallback = default) =>
            TryGet<T>(key, out T v) ? v : fallback;

        public bool Has(string key) => _values.ContainsKey(key);

        public void Remove(string key) => _values.Remove(key);

        public void Clear() => _values.Clear();
    }

    /// <summary>黑板常用键。集中在这里，避免各处手写字符串拼错。</summary>
    public static class BtKeys
    {
        public const string TargetId = "target.id";
        public const string TargetIndex = "target.index";
        public const string HomeCell = "home.cell";
        public const string PatrolRoute = "patrol.route";
        public const string PatrolCursor = "patrol.cursor";
        public const string StateEnteredAt = "state.enteredAt";
        public const string NextFireAt = "weapon.nextFireAt";
    }

    /// <summary>
    /// 行为树一次 Tick 所需的全部上下文。
    /// 注意它只持有**接口与纯数据**，所以整棵树可以在没有 Unity 的地方跑。
    /// </summary>
    public sealed class BtContext
    {
        public IAiWorld World { get; }

        public Blackboard Board { get; }

        /// <summary>当前 AI 控制的飞机 id。</summary>
        public int SelfId { get; }

        /// <summary>当前 AI 自己的快照缓存（每帧由外部刷新一次，避免反复查表）。</summary>
        public AgentSnapshot Self { get; private set; }

        /// <summary>对局已进行的秒数。行为树的超时/冷却都基于它。</summary>
        public float ElapsedSeconds { get; private set; }

        public BtContext(IAiWorld world, int selfId, Blackboard board = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Board = board ?? new Blackboard();
            SelfId = selfId;
        }

        /// <summary>每帧开头调用一次：刷新自身快照与计时。</summary>
        public void BeginFrame(float dt)
        {
            ElapsedSeconds += dt;
            Self = FindSelf();
        }

        AgentSnapshot FindSelf()
        {
            for (int i = 0; i < World.AgentCount; i++)
            {
                AgentSnapshot a = World.GetAgent(i);
                if (a.Id == SelfId) return a;
            }
            return default;
        }
    }

    /// <summary>行为树节点基类。Tick 必须是"可反复调用"的：Running 状态下次继续。</summary>
    public abstract class BtNode
    {
        public abstract BtStatus Tick(BtContext context, float dt);

        /// <summary>被中断 / 重新进入时清理内部状态（例如 Wait 的计时）。</summary>
        public virtual void Reset()
        {
        }

        /// <summary>调试用名字。</summary>
        public virtual string Name => GetType().Name;

        public override string ToString() => Name;
    }

    /// <summary>
    /// 顺序节点：依次执行，任一失败即失败；全部成功才成功。
    ///
    /// **反应式**：每帧都从第 0 个子节点重新求值，不缓存"上次跑到哪"。
    /// 这一点很关键：如果缓存了位置，那么"条件 + 动作"这种最常见的组合里，
    /// 条件只会在第一次被检查 —— 于是动作一旦返回 Running，AI 就再也出不来这个分支。
    /// 实测后果就是"目标飞到 100 单位外了敌机还在开火"，也就是立项书批评的"固定剧本"。
    /// </summary>
    public sealed class BtSequence : BtNode
    {
        readonly BtNode[] _children;

        public BtSequence(params BtNode[] children) => _children = children ?? Array.Empty<BtNode>();

        public override string Name => "Sequence(" + _children.Length + ")";

        public override BtStatus Tick(BtContext context, float dt)
        {
            for (int i = 0; i < _children.Length; i++)
            {
                BtStatus status = _children[i].Tick(context, dt);
                if (status == BtStatus.Running) return BtStatus.Running;
                if (status == BtStatus.Failure)
                {
                    Reset();
                    return BtStatus.Failure;
                }
            }
            Reset();
            return BtStatus.Success;
        }

        public override void Reset()
        {
            for (int i = 0; i < _children.Length; i++) _children[i].Reset();
        }
    }

    /// <summary>
    /// 选择节点：依次尝试，任一成功即成功；全部失败才失败。
    /// 同样是**反应式**的（理由见 BtSequence）—— 每帧重新从最高优先级开始判断，
    /// 所以"血量一低就立刻切规避"这类规则才会即时生效。
    /// </summary>
    public sealed class BtSelector : BtNode
    {
        readonly BtNode[] _children;

        public BtSelector(params BtNode[] children) => _children = children ?? Array.Empty<BtNode>();

        public override string Name => "Selector(" + _children.Length + ")";

        public override BtStatus Tick(BtContext context, float dt)
        {
            for (int i = 0; i < _children.Length; i++)
            {
                BtStatus status = _children[i].Tick(context, dt);
                if (status == BtStatus.Running) return BtStatus.Running;
                if (status == BtStatus.Success)
                {
                    Reset();
                    return BtStatus.Success;
                }
            }
            Reset();
            return BtStatus.Failure;
        }

        public override void Reset()
        {
            for (int i = 0; i < _children.Length; i++) _children[i].Reset();
        }
    }

    /// <summary>取反节点。Running 原样透传。</summary>
    public sealed class BtInverter : BtNode
    {
        readonly BtNode _child;

        public BtInverter(BtNode child) => _child = child;

        public override BtStatus Tick(BtContext context, float dt)
        {
            BtStatus status = _child.Tick(context, dt);
            if (status == BtStatus.Success) return BtStatus.Failure;
            if (status == BtStatus.Failure) return BtStatus.Success;
            return BtStatus.Running;
        }

        public override void Reset() => _child.Reset();
    }

    /// <summary>条件叶：谓词为真返回 Success，否则 Failure（无状态）。</summary>
    public sealed class BtCondition : BtNode
    {
        readonly Func<BtContext, bool> _predicate;
        readonly string _label;

        public BtCondition(string label, Func<BtContext, bool> predicate)
        {
            _label = label;
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public override string Name => "?" + _label;

        public override BtStatus Tick(BtContext context, float dt) =>
            _predicate(context) ? BtStatus.Success : BtStatus.Failure;
    }

    /// <summary>动作叶：由委托决定结果，可返回 Running 跨帧。</summary>
    public sealed class BtAction : BtNode
    {
        readonly Func<BtContext, float, BtStatus> _action;
        readonly Action _onReset;
        readonly string _label;

        public BtAction(string label, Func<BtContext, float, BtStatus> action, Action onReset = null)
        {
            _label = label;
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _onReset = onReset;
        }

        public override string Name => "*" + _label;

        public override BtStatus Tick(BtContext context, float dt) => _action(context, dt);

        public override void Reset() => _onReset?.Invoke();
    }

    /// <summary>等待节点：持续 seconds 秒返回 Running，之后 Success。</summary>
    public sealed class BtWait : BtNode
    {
        readonly float _seconds;
        float _remaining;

        public BtWait(float seconds)
        {
            _seconds = seconds < 0f ? 0f : seconds;
            // 必须在构造时就置位：_remaining 是值类型的默认 0，
            // 忘了这一步会导致第一次 Tick 立刻判定"时间到"。
            _remaining = _seconds;
        }

        public override string Name => "Wait(" + _seconds.ToString("0.##") + "s)";

        public override BtStatus Tick(BtContext context, float dt)
        {
            _remaining -= dt;
            if (_remaining <= 0f)
            {
                _remaining = _seconds;
                return BtStatus.Success;
            }
            return BtStatus.Running;
        }

        public override void Reset() => _remaining = _seconds;
    }

    /// <summary>
    /// 冷却装饰器：子节点成功后，接下来 cooldownSeconds 秒内直接返回 Failure。
    /// 用于"打完一轮要停一下"这类节奏控制。
    /// </summary>
    public sealed class BtCooldown : BtNode
    {
        readonly BtNode _child;
        readonly float _cooldown;
        float _readyAt;

        public BtCooldown(float cooldownSeconds, BtNode child)
        {
            _cooldown = cooldownSeconds < 0f ? 0f : cooldownSeconds;
            _child = child;
        }

        public override BtStatus Tick(BtContext context, float dt)
        {
            if (context.ElapsedSeconds < _readyAt)
            {
                _child.Reset();
                return BtStatus.Failure;
            }

            BtStatus status = _child.Tick(context, dt);
            if (status == BtStatus.Success) _readyAt = context.ElapsedSeconds + _cooldown;
            return status;
        }

        public override void Reset()
        {
            _readyAt = 0f;
            _child.Reset();
        }
    }

    /// <summary>
    /// 行为树运行器：每帧驱动根节点一次。
    /// 它同时负责把根节点的结果写回一份"给表现层看的状态名"，方便 HUD 显示当前 AI 在想什么。
    /// </summary>
    public sealed class BtRunner
    {
        readonly BtNode _root;
        readonly BtContext _context;

        public BtRunner(BtNode root, BtContext context)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public BtContext Context => _context;

        public BtStatus LastStatus { get; private set; }

        /// <summary>最近一次进入的高层状态名（由外部在切换到具体状态时设置，用于 HUD / 调试）。</summary>
        public string CurrentStateName { get; set; } = "(none)";

        public BtStatus Tick(float dt)
        {
            _context.BeginFrame(dt);
            LastStatus = _root.Tick(_context, dt);
            return LastStatus;
        }

        public void Reset()
        {
            _root.Reset();
            _context.Board.Clear();
            CurrentStateName = "(none)";
        }
    }
}
