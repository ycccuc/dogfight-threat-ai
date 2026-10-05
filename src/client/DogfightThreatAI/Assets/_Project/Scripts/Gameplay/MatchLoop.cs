using Dogfight.Ai;
using UnityEngine;

namespace Dogfight.Gameplay
{
    /// <summary>
    /// 对局循环 —— **全场景唯一的时间推进入口**。
    ///
    /// 为什么必须有它：
    ///   只要游戏逻辑散落在几十个 Update() 里，"AI 自动批量对战"就是不可能的事 ——
    ///   没法快进、没法无头跑、没法复现。把所有推进收敛到一个 Tick(dt) 之后，
    ///   同一份对局代码既可以被真实时间驱动（Update），
    ///   也可以被批量对战用固定步长在几秒内跑完几百局。
    ///
    /// 关于物理：这里把 Physics2D 切成手动模式（SimulationMode2D.Script），
    ///   于是"施力 → 步进物理"的顺序由本循环决定，而不是由 Unity 的 FixedUpdate 偷偷决定。
    ///   这既让批量对战能跑，也让实时游戏变成固定步长（手感更稳、更容易复现）。
    ///   如果某台机器上这个 API 有问题，把 Manual Physics Step 取消勾选即可退回 Unity 自动物理。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchLoop : MonoBehaviour
    {
        public enum Phase
        {
            Idle = 0,
            Running = 1,
            Finished = 2,
        }

        [Header("─ 对局规则 ─")]
        [Tooltip("一局的时长上限（秒）。到时按存活数判定。")]
        [SerializeField] float _timeLimitSeconds = 180f;

        [Tooltip("固定步长（秒）。1/60 = 60Hz。")]
        [SerializeField] float _fixedStep = 1f / 60f;

        [Tooltip("单个渲染帧最多补几步，防止卡顿后一次追赶太多导致雪崩。")]
        [SerializeField] int _maxStepsPerFrame = 8;

        [Header("─ 物理 ─")]
        [Tooltip("由本循环手动步进 Physics2D。批量对战必须为 true。")]
        [SerializeField] bool _manualPhysicsStep = true;

        [Header("─ 战场边界 ─")]
        [Tooltip("勾上则用软边界把飞机推回场内，而不是让它们飞出视野。")]
        [SerializeField] bool _useArena = true;

        [Tooltip("战场中心（世界坐标）。")]
        [SerializeField] Vector2 _arenaCenter = Vector2.zero;

        [Tooltip("战场尺寸（整条边的长度）。默认 40×22.5 正好对应 1280×720 @ PPU 32 的一屏。")]
        [SerializeField] Vector2 _arenaSize = new Vector2(40f, 22.5f);

        [Tooltip("靠边的回推带宽度。俯视空战里撞墙急停手感很差，所以用「推」而不是「挡」。")]
        [SerializeField] float _arenaSoftMargin = 3f;

        [Tooltip("回推强度。太大会像有堵墙，太小会飞出去。")]
        [SerializeField] float _arenaPushStrength = 60f;

        [Header("─ 参战飞机 ─")]
        [SerializeField] PlaneAgent[] _planes;

        float _accumulator;

        public Phase CurrentPhase { get; private set; } = Phase.Idle;

        public float ElapsedSeconds { get; private set; }

        public int TickCount { get; private set; }

        public uint Seed { get; private set; }

        /// <summary>对局结束时触发（结算界面、批量对战统计都挂这里）。</summary>
        public event System.Action<MatchResult> MatchFinished;

        public PlaneAgent[] Planes => _planes;

        public float FixedStep => _fixedStep;

        void Awake()
        {
            Time.fixedDeltaTime = _fixedStep;
            if (_manualPhysicsStep)
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
            }
        }

        void OnDestroy()
        {
            // 退出对局时把物理还给 Unity，避免影响编辑器里其它场景
            if (_manualPhysicsStep)
            {
                Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            }
        }

        void Update()
        {
            if (CurrentPhase != Phase.Running) return;
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 推进对局。实时游戏传 Time.deltaTime；批量对战传固定步长并循环调用。
        /// 内部按固定步长切片，保证不同帧率下行为一致。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (CurrentPhase != Phase.Running) return;

            _accumulator += deltaTime;
            int steps = 0;
            while (_accumulator >= _fixedStep && steps < _maxStepsPerFrame)
            {
                StepOnce(_fixedStep);
                _accumulator -= _fixedStep;
                steps++;
                if (CurrentPhase != Phase.Running) break;
            }
        }

        /// <summary>推进一个固定步（批量对战可以绕过累加器直接调它）。</summary>
        public void StepOnce(float step)
        {
            if (CurrentPhase != Phase.Running) return;

            for (int i = 0; i < _planes.Length; i++)
            {
                PlaneAgent plane = _planes[i];
                if (plane != null) plane.Step(step);
            }

            ApplyArenaForces();

            if (_manualPhysicsStep)
            {
                Physics2D.Simulate(step);
            }

            TickCount++;
            ElapsedSeconds += step;

            EvaluateEndConditions();
        }

        public void BeginMatch(uint seed = 0u)
        {
            Seed = seed;
            ElapsedSeconds = 0f;
            TickCount = 0;
            _accumulator = 0f;
            CurrentPhase = Phase.Running;
        }

        /// <summary>强制结束并生成结果（批量对战里也用它做超时保护）。</summary>
        public MatchResult EndMatch(MatchEndReason reason)
        {
            CurrentPhase = Phase.Finished;
            MatchResult result = BuildResult(reason);
            MatchFinished?.Invoke(result);
            return result;
        }

        void EvaluateEndConditions()
        {
            if (_planes == null || _planes.Length == 0)
            {
                // 空对局（批量对战管道的连通性验证）只按时间结束
                if (ElapsedSeconds >= _timeLimitSeconds) EndMatch(MatchEndReason.Timeout);
                return;
            }

            int aliveTeam0 = CountAlive(0);
            int aliveTeam1 = CountAlive(1);

            if (aliveTeam0 == 0 && aliveTeam1 == 0)
            {
                EndMatch(MatchEndReason.TeamEliminated);
                return;
            }

            if (_planes.Length > 1 && (aliveTeam0 == 0 || aliveTeam1 == 0))
            {
                EndMatch(MatchEndReason.TeamEliminated);
                return;
            }

            if (ElapsedSeconds >= _timeLimitSeconds)
            {
                EndMatch(MatchEndReason.Timeout);
            }
        }

        int CountAlive(int team)
        {
            int n = 0;
            for (int i = 0; i < _planes.Length; i++)
            {
                PlaneAgent p = _planes[i];
                if (p != null && p.Team == team && p.IsAlive) n++;
            }
            return n;
        }

        MatchResult BuildResult(MatchEndReason reason)
        {
            int aliveTeam0 = CountAlive(0);
            int aliveTeam1 = CountAlive(1);

            int winner;
            if (reason == MatchEndReason.TeamEliminated)
            {
                if (aliveTeam0 > 0) winner = 0;
                else if (aliveTeam1 > 0) winner = 1;
                else winner = -1;
            }
            else
            {
                // 超时：按存活数判定，相等算平局
                if (aliveTeam0 > aliveTeam1) winner = 0;
                else if (aliveTeam1 > aliveTeam0) winner = 1;
                else winner = -1;
            }

            return new MatchResult(
                reason,
                winner,
                ElapsedSeconds,
                TickCount,
                Seed,
                new[] { aliveTeam0, aliveTeam1 });
        }

        /// <summary>当前战场边界（由 Inspector 上的中心 / 尺寸 / 回推带推导）。</summary>
        public ArenaBounds Arena =>
            _useArena
                ? ArenaBounds.FromSize(
                    new Vec2(_arenaCenter.x, _arenaCenter.y),
                    new Vec2(_arenaSize.x, _arenaSize.y),
                    _arenaSoftMargin)
                : ArenaBounds.Unbounded;

        /// <summary>
        /// 施加边界回推力。**必须在 Physics2D.Simulate 之前**调用 ——
        /// 这就是把"施力"与"积分"分开的意义：顺序由本循环决定，而不是由 Unity 偷偷决定。
        /// 每架飞机自己不该知道战场多大，这是对局规则，所以放在这里。
        /// </summary>
        void ApplyArenaForces()
        {
            if (!_useArena || _planes == null) return;

            ArenaBounds arena = Arena;
            for (int i = 0; i < _planes.Length; i++)
            {
                PlaneAgent plane = _planes[i];
                if (plane == null || !plane.IsAlive) continue;

                Vec2 force = arena.BoundaryForce(plane.Position, _arenaPushStrength);
                if (force.SqrMagnitude > 0f) plane.ApplyBoundaryForce(force);
            }
        }

        /// <summary>场景里临时挂载用的工具方法（批量对战与测试都用它）。</summary>
        public void SetPlanes(PlaneAgent[] planes) => _planes = planes;

        public void SetTimeLimit(float seconds) => _timeLimitSeconds = seconds;

        /// <summary>
        /// 开关手动物理步进。
        /// 批量对战窗口在**编辑器非播放模式**下跑空对局时必须关掉它 ——
        /// Physics2D.Simulate 只在播放模式有意义。
        /// </summary>
        public void SetManualPhysicsStep(bool manual) => _manualPhysicsStep = manual;

        /// <summary>供批量对战与单测直接读，避免反射。</summary>
        public bool IsManualPhysicsStep => _manualPhysicsStep;
    }
}
