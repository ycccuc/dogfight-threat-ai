using System;
using System.Collections.Generic;
using DogfightThreatAI.Core.AI.BehaviorTree;
using DogfightThreatAI.Core.AI.Pathfinding;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>
    /// 纯 C# 机炮驾驶员：读取飞行快照，输出移动、推进和开火意图。
    /// 不修改飞行或生命状态。行为树每步重新选择优先级；路径搜索只用于远距离接近。
    /// </summary>
    public sealed class GunnerPilot
    {
        const float DegreesToRadians = MathF.PI / 180f;
        const float RouteRefreshSeconds = .5f;

        readonly Grid2D _grid;
        readonly GridPathfinder _pathfinder;
        readonly List<int> _path;
        readonly IBehaviorNode<GunnerPilot> _tree;
        readonly WeaponSpec _weapon;
        GunnerProfile _profile;
        FlightState _self;
        FlightState _observed;
        Vec2 _aim;
        Vec2 _goal;
        bool _alive;
        bool _hasObservation;
        float _observationTimer;
        float _routeTimer;
        float _reaction;
        float _distance;
        float _aimError;
        float _shotTimer;
        float _commandSpeed;

        public bool WantsFire
        {
            get; private set;
        }
        public string Behavior { get; private set; } = "接近";
        public int SearchCount
        {
            get; private set;
        }
        public bool LastSearchUsedAStar
        {
            get; private set;
        }
        public FlightMotionCommand MotionCommand => _alive
            ? FlightMotionCommand.SpeedOverride(_commandSpeed, _profile.TurnRate)
            : FlightMotionCommand.Ordinary();

        // 校验网格与配置，建立本架机炮 AI 的寻路缓存和行为树。
        public GunnerPilot(Grid2D grid, WeaponSpec weapon, GunnerProfile profile)
        {
            if (grid.CellSize <= 0f || float.IsNaN(grid.CellSize) || float.IsInfinity(grid.CellSize))
            {
                throw new ArgumentException("Grid2D must be initialized.", nameof(grid));
            }
            _grid = grid;
            if (weapon.ShotInterval <= 0f)
            {
                throw new ArgumentException("WeaponSpec must be initialized.", nameof(weapon));
            }
            if (profile.ObservationInterval <= 0f)
            {
                throw new ArgumentException("GunnerProfile must be initialized.", nameof(profile));
            }
            _weapon = weapon;
            _profile = profile;
            _commandSpeed = profile.CruiseSpeed;
            _pathfinder = new GridPathfinder(grid);
            _path = new List<int>(grid.CellCount);
            _tree = CreateBehaviorTree();
        }

        // 优先级：无有效目标 → 过近撤离 → 射程和瞄准允许时交战 → 接近。
        static IBehaviorNode<GunnerPilot> CreateBehaviorTree()
        {
            return new SelectorNode<GunnerPilot>(
                new SequenceNode<GunnerPilot>(new ConditionNode<GunnerPilot>(c => !c._alive),
                    new ActionNode<GunnerPilot>(c => c.Stop())),
                new SequenceNode<GunnerPilot>(new ConditionNode<GunnerPilot>(c => c._distance < c._profile.RetreatDistance),
                    new ActionNode<GunnerPilot>(c => c.Retreat())),
                new SequenceNode<GunnerPilot>(new ConditionNode<GunnerPilot>(c => c.CanEngage()),
                    new ActionNode<GunnerPilot>(c => c.Engage())),
                new ActionNode<GunnerPilot>(c => c.Approach()));
        }

        // 替换难度配置，并清空旧观察、路线和开火意图。
        public void SetProfile(GunnerProfile profile)
        {
            if (profile.ObservationInterval <= 0f)
            {
                throw new ArgumentException("GunnerProfile must be initialized.", nameof(profile));
            }
            _profile = profile;
            ResetDecision();
        }

        /// <param name="alive">自身和目标是否均可参与战斗；由模拟调用方提供。</param>
        public FlightInput Tick(in FlightState self, in FlightState target, bool alive, float dt)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt));
            }
            _self = self;
            _alive = alive;
            WantsFire = false;
            // 当前交战失效时先清空旧决策，避免继续开火或追踪旧目标。
            if (!alive)
            {
                ResetDecision();
                _tree.Tick(this);
                return FlightInput.Neutral;
            }

            _commandSpeed = _profile.CruiseSpeed;
            _shotTimer = MathF.Max(0, _shotTimer - dt);
            _observationTimer -= dt;
            _routeTimer -= dt;
            Observe(target);
            _reaction += dt;
            _tree.Tick(this);

            float error = Vec2.NormalizeAngle((_goal - self.Position).AngleRad - self.Heading);
            float turn = error / (_profile.TurnRate * DegreesToRadians * dt);
            return new FlightInput(1f, MathF.Max(-1f, MathF.Min(1f, turn)));
        }

        // 重置决策缓存和计时，避免沿用失效目标的行为。
        void ResetDecision()
        {
            WantsFire = false;
            _observationTimer = _routeTimer = _reaction = 0f;
            _shotTimer = _profile.FireDelay;
            _commandSpeed = _profile.CruiseSpeed;
            _hasObservation = false;
            _path.Clear();
        }

        // 按观察间隔采样目标状态，并计算提前瞄准点。
        void Observe(in FlightState target)
        {
            if (!_hasObservation || _observationTimer <= 0)
            {
                _observed = target;
                _hasObservation = true;
                _observationTimer = _profile.ObservationInterval;
            }

            _distance = Vec2.Distance(_self.Position, _observed.Position);
            float leadTime = MathF.Min(_weapon.Lifetime, _distance / _weapon.ProjectileSpeed);
            _aim = _observed.Position + _observed.Velocity * (leadTime * _profile.Prediction);
            _aimError = MathF.Abs(Vec2.NormalizeAngle((_aim - _self.Position).AngleRad - _self.Heading));
        }

        // 检查目标是否满足有效射程和瞄准误差要求。
        bool CanEngage()
        {
            return _distance <= _weapon.ProjectileSpeed * _weapon.Lifetime * .75f &&
                    _aimError <= _profile.AimTolerance;
        }

        // 标记当前交战结束，停止产生进攻行为。
        NodeStatus Stop()
        {
            Behavior = "结束";
            _goal = _self.Position + _self.Forward;
            return NodeStatus.Success;
        }

        // 距离过近时选择远离目标的机动点。
        NodeStatus Retreat()
        {
            Behavior = "拉开距离";
            _reaction = 0;
            Vec2 away = (_self.Position - _observed.Position).Normalized;
            if (away.SqrMagnitude < .001f)
            {
                away = _self.Forward;
            }
            _goal = _grid.Bounds.Clamp(_self.Position + away * _profile.EngageDistance);
            if (Vec2.Distance(_goal, _self.Position) < _profile.RetreatDistance * .5f)
            {
                _goal = _grid.Bounds.Clamp(_self.Position + new Vec2(-away.Y, away.X) * _profile.EngageDistance);
            }
            return NodeStatus.Running;
        }

        // 跟踪预测瞄准点，并按反应时间与射击节奏请求开火。
        NodeStatus Engage()
        {
            Behavior = "瞄准开火";
            _goal = _aim;
            // 与目标同向时匹配速度并控制间距，避免高难度因过快贴身反而更少开火。
            float followSpeed = Vec2.Dot(_observed.Velocity, _self.Forward) + (_distance - _profile.EngageDistance) * .7f;
            _commandSpeed = MathF.Max(2, MathF.Min(_profile.CruiseSpeed, followSpeed));
            WantsFire = _reaction >= _profile.FireDelay && _shotTimer <= 0;
            if (WantsFire)
            {
                _shotTimer = MathF.Max(_profile.ShotSpacing, _weapon.ShotInterval);
            }
            return NodeStatus.Running;
        }

        // 接近目标：近处直接跟踪，远处沿网格路线推进。
        NodeStatus Approach()
        {
            Behavior = "接近";
            _reaction = 0;
            // 进入交战距离后直接朝预测点转向，避免格中心误差妨碍瞄准。
            if (_distance < _profile.EngageDistance * 2)
            {
                _goal = _aim;
                return NodeStatus.Running;
            }

            if (_routeTimer <= 0 || _path.Count == 0)
            {
                RefreshRoute();
            }
            if (_path.Count > 1)
            {
                _grid.FromIndex(_path[System.Math.Min(3, _path.Count - 1)], out int x, out int y);
                _goal = _grid.CellCenter(x, y);
            }
            else
            {
                _goal = _aim;
            }
            return NodeStatus.Running;
        }

        // 按难度选择 BFS 或 A*，更新远距离接近的路径缓存。
        void RefreshRoute()
        {
            _grid.WorldToCellClamped(_self.Position, out int sx, out int sy);
            _grid.WorldToCellClamped(_aim, out int gx, out int gy);
            int start = _grid.ToIndex(sx, sy);
            int goal = _grid.ToIndex(gx, gy);
            LastSearchUsedAStar = _profile.UseAStar;

            // 当前空域无障碍和威胁代价；BFS/A* 仅用于接近，不负责绕后或转弯半径。
            if (_profile.UseAStar)
            {
                _pathfinder.AStar(start, goal, null, null, _path);
            }
            else
            {
                _pathfinder.BreadthFirst(start, goal, null, _path);
            }
            SearchCount++;
            _routeTimer = RouteRefreshSeconds;
        }
    }
}
