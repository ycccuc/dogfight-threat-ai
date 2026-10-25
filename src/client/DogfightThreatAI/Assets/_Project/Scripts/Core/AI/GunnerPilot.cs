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
    /// <summary>行为树输出移动/开火意图。路径搜索只在远距离接近时使用，不把地面美术当障碍。</summary>
    public sealed class GunnerPilot
    {
        readonly Grid2D _grid;
        readonly GridPathfinder _pathfinder;
        readonly List<int> _path;
        readonly IBehaviorNode<GunnerPilot> _tree;
        readonly WeaponSpec _weapon;
        GunnerProfile _profile;
        FlightState _self, _observed;
        Vec2 _aim, _goal;
        bool _alive, _hasObservation;
        float _observationTimer, _routeTimer, _reaction, _distance, _aimError, _shotTimer, _commandSpeed;
        public bool WantsFire { get; private set; }
        public string Behavior { get; private set; } = "接近";
        public int SearchCount { get; private set; }
        public bool LastSearchUsedAStar { get; private set; }
        public FlightMotionCommand MotionCommand => FlightMotionCommand.SpeedOverride(_commandSpeed, _profile.TurnRate);

        public GunnerPilot(Grid2D grid, WeaponSpec weapon, GunnerProfile profile)
        {
            _grid = grid; _weapon = weapon; _profile = profile; _commandSpeed = profile.CruiseSpeed;
            _pathfinder = new GridPathfinder(grid); _path = new List<int>(grid.CellCount);
            _tree = new SelectorNode<GunnerPilot>(
                new SequenceNode<GunnerPilot>(new ConditionNode<GunnerPilot>(c => !c._alive),
                    new ActionNode<GunnerPilot>(c => c.Stop())),
                new SequenceNode<GunnerPilot>(new ConditionNode<GunnerPilot>(c => c._distance < c._profile.RetreatDistance),
                    new ActionNode<GunnerPilot>(c => c.Retreat())),
                new SequenceNode<GunnerPilot>(new ConditionNode<GunnerPilot>(c => c._distance <= c._weapon.ProjectileSpeed * c._weapon.Lifetime * .75f &&
                    c._aimError <= c._profile.AimTolerance), new ActionNode<GunnerPilot>(c => c.Engage())),
                new ActionNode<GunnerPilot>(c => c.Approach()));
        }
        public void SetProfile(GunnerProfile profile)
        { _profile = profile; _observationTimer = _routeTimer = _reaction = 0; _shotTimer = profile.FireDelay;
            _commandSpeed = profile.CruiseSpeed; _hasObservation = false; _path.Clear(); }

        public FlightInput Tick(in FlightState self, in FlightState target, bool alive, float dt)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
            _self = self; _alive = alive; WantsFire = false;
            _commandSpeed = _profile.CruiseSpeed; _shotTimer = MathF.Max(0, _shotTimer - dt);
            _observationTimer -= dt; _routeTimer -= dt;
            if (!_hasObservation || _observationTimer <= 0)
            { _observed = target; _hasObservation = true; _observationTimer = _profile.ObservationInterval; }
            _distance = Vec2.Distance(self.Position, _observed.Position);
            float leadTime = MathF.Min(_weapon.Lifetime, _distance / _weapon.ProjectileSpeed);
            _aim = _observed.Position + _observed.Velocity * (leadTime * _profile.Prediction);
            _aimError = MathF.Abs(Vec2.NormalizeAngle((_aim - self.Position).AngleRad - self.Heading));
            _reaction += dt;
            _tree.Tick(this);
            if (!alive) return FlightInput.Neutral;
            float error = Vec2.NormalizeAngle((_goal - self.Position).AngleRad - self.Heading);
            return new FlightInput(1f, MathF.Max(-1, MathF.Min(1, error / (_profile.TurnRate * .01745329252f * dt))));
        }
        NodeStatus Stop() { Behavior = "结束"; _goal = _self.Position + _self.Forward; return NodeStatus.Success; }
        NodeStatus Retreat()
        {
            Behavior = "拉开距离"; _reaction = 0;
            Vec2 away = (_self.Position - _observed.Position).Normalized;
            if (away.SqrMagnitude < .001f) away = _self.Forward;
            _goal = _grid.Bounds.Clamp(_self.Position + away * _profile.EngageDistance);
            if (Vec2.Distance(_goal, _self.Position) < _profile.RetreatDistance * .5f)
                _goal = _grid.Bounds.Clamp(_self.Position + new Vec2(-away.Y, away.X) * _profile.EngageDistance);
            return NodeStatus.Running;
        }
        NodeStatus Engage()
        {
            Behavior = "瞄准开火"; _goal = _aim;
            // 与目标同向时匹配速度并控制间距，避免高难度因过快贴身反而更少开火。
            float followSpeed = Vec2.Dot(_observed.Velocity, _self.Forward) + (_distance - _profile.EngageDistance) * .7f;
            _commandSpeed = MathF.Max(2, MathF.Min(_profile.CruiseSpeed, followSpeed));
            WantsFire = _reaction >= _profile.FireDelay && _shotTimer <= 0;
            if (WantsFire) _shotTimer = MathF.Max(_profile.ShotSpacing, _weapon.ShotInterval);
            return NodeStatus.Running;
        }
        NodeStatus Approach()
        {
            Behavior = "接近"; _reaction = 0;
            // 进入交战距离后直接朝预测点转向，避免格中心误差妨碍瞄准。
            if (_distance < _profile.EngageDistance * 2) { _goal = _aim; return NodeStatus.Running; }
            if (_routeTimer <= 0 || _path.Count == 0)
            {
                _grid.WorldToCellClamped(_self.Position, out int sx, out int sy);
                _grid.WorldToCellClamped(_aim, out int gx, out int gy);
                int start = _grid.ToIndex(sx, sy), goal = _grid.ToIndex(gx, gy);
                LastSearchUsedAStar = _profile.UseAStar;
                if (_profile.UseAStar) _pathfinder.AStar(start, goal, null, null, _path);
                else _pathfinder.BreadthFirst(start, goal, null, _path);
                SearchCount++; _routeTimer = .5f;
            }
            if (_path.Count > 1)
            {
                _grid.FromIndex(_path[System.Math.Min(3, _path.Count - 1)], out int x, out int y);
                _goal = _grid.CellCenter(x, y);
            }
            else _goal = _aim;
            return NodeStatus.Running;
        }
    }
}
