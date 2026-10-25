using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.Combat;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.InputSource
{
    /// <summary>Unity 接线：捕获模拟状态快照，复用纯 C# AI；不推进或修改飞机状态。</summary>
    [RequireComponent(typeof(PlayerController))]
    [DisallowMultipleComponent]
    public sealed class ChaseAiInputSource : MonoBehaviour, IPlayerInputSource, IFlightSpeedSource, IFlightCruiseSource, IFlightMotionSource
    {
        [SerializeField] PlayerController self;
        [Tooltip("追击目标：引用玩家的控制器，读取其模拟状态，不读取 Art Transform。")]
        [SerializeField] PlayerController target;
        [Tooltip("唯一战斗状态来源：命中与无敌帧。")]
        [SerializeField] BattleCombatController combat;
        [Tooltip("0: automatic growth; 1-4: fixed difficulty preview.")]
        [SerializeField, Range(0, 4)] int difficultyOverride;

        PursuitChargeInputSource _source;
        ChaseWorldView _world;
        GunnerPilot _gunner;
        int _gunnerLevel = 1;
        public bool WantsFire => _gunner != null && _gunner.WantsFire;
        public GunnerPilot Gunner => _gunner;

        public PlayerController Target => target;
        public BattleCombatController Combat => combat;
        public AttackRunPhase Phase => _source != null ? _source.Phase : AttackRunPhase.NoTarget;
        public AttackTactic Tactic => _source != null ? _source.Tactic : AttackTactic.Pursuit;
        public int Level => _gunner != null ? _gunnerLevel : _source != null ? _source.Level : 1;
        public int CollisionDamage => _source != null ? _source.CollisionDamage : PursuitDifficultyProfile.ForLevel(1).CollisionDamage;
        public int DifficultyOverride => difficultyOverride;
        public float RecoverySeconds => _source != null ? _source.RecoverySeconds : PursuitDifficultyProfile.ForLevel(1).RecoverySeconds;
        public Vec2 ObservedTargetPosition => _source != null ? _source.ObservedTargetPosition : Vec2.Zero;
        public float ObservedAt => _source != null ? _source.ObservedAt : 0f;
        public Vec2 PredictionPoint => _source != null ? _source.PredictionPoint : Vec2.Zero;
        public float Adaptation01 => _source != null ? _source.Adaptation01 : 0f;
        public int AttackId => _source != null ? _source.AttackId : 0;
        public float PhaseRemaining => _source != null ? _source.PhaseRemaining : 0f;
        public float LockedHeading => _source != null ? _source.LockedHeading : 0f;
        public Vec2 LockedDirection => _source != null ? _source.LockedDirection : Vec2.Zero;
        public Vec2 AimDirection => _source != null ? _source.AimDirection : Vec2.Zero;
        public bool IsAimLocked => _source != null && _source.IsAimLocked;
        public Vec2 ManeuverPoint => _source != null ? _source.ManeuverPoint : Vec2.Zero;
        public AttackWindow AttackWindow => isActiveAndEnabled && _source != null ? _source.AttackWindow : default;
        public int PreviewPointCount => _source != null ? _source.PreviewPointCount : 0;
        public float Aggression01 => (Level - 1) / 3f;
        public float SpeedMultiplier => isActiveAndEnabled && _source != null ? _source.SpeedMultiplier : 1f;
        public bool MaintainCruiseSpeed => isActiveAndEnabled && _source != null && _source.MaintainCruiseSpeed;
        public float CruiseSpeed => _source != null ? _source.CruiseSpeed : PursuitDifficultyProfile.ForLevel(1).CruiseSpeed;
        public float TurnRateDegPerSec => _source != null ? _source.TurnRateDegPerSec : PursuitDifficultyProfile.ForLevel(1).TurnRateDegPerSec;
        public float PursuitBoostSpeed => _source != null ? _source.PursuitBoostSpeed : PursuitDifficultyProfile.ForLevel(1).BoostSpeed;
        public float BoostEnergy => _source != null ? _source.BoostEnergy : PursuitBoostModel.Capacity;
        public float BoostCapacity => PursuitBoostModel.Capacity;
        public float BoostCooldownRemaining => _source != null ? _source.BoostCooldownRemaining : 0f;
        public bool IsPursuitBoosting => isActiveAndEnabled && _source != null && _source.IsPursuitBoosting;
        public bool IsBoostCoasting => isActiveAndEnabled && _source != null && _source.IsBoostCoasting;
        public FlightMotionCommand MotionCommand => isActiveAndEnabled && _gunner != null ? _gunner.MotionCommand :
            isActiveAndEnabled && _source != null ? _source.MotionCommand : FlightMotionCommand.Ordinary();

        public Vec2 GetPreviewPoint(int index)
        {
            if (_source == null) throw new ArgumentOutOfRangeException(nameof(index));
            return _source.GetPreviewPoint(index);
        }

        void Awake()
        {
            if (self == null) self = GetComponent<PlayerController>();
        }

        public void SetDifficulty(int level)
        {
            int max = combat != null && combat.UsesProjectileCombat ? combat.Difficulties.Length : 4;
            if (level < 0 || level > max) throw new ArgumentOutOfRangeException(nameof(level));
            difficultyOverride = level;
            if (_source != null) _source.RequestDifficulty(level);
            if (_gunner != null)
            {
                _gunnerLevel = Mathf.Clamp(level, 1, combat.Difficulties.Length);
                _gunner.SetProfile(combat.Difficulties[_gunnerLevel - 1].ToSpec());
            }
        }

        void Update()
        {
            if (combat != null && combat.UsesProjectileCombat) return;
            if (combat != null && !combat.Status.IsPlayerAlive) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1)) SetDifficulty(1);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.F2)) SetDifficulty(2);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.F3)) SetDifficulty(3);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.F4)) SetDifficulty(4);
            else if (UnityEngine.Input.GetKeyDown(KeyCode.F5)) SetDifficulty(0);
        }

        public FlightInput ReadInput(float deltaTime)
        {
            if (!isActiveAndEnabled || self == null || !self.IsInitialized || combat == null)
                return new FlightInput(-1f, 0f);

            if (combat.UsesProjectileCombat)
            {
                if (_gunner == null)
                {
                    _gunnerLevel = Mathf.Clamp(difficultyOverride, 1, combat.Difficulties.Length);
                    var bounds = self.ArenaBounds ?? Bounds2D.Centered(new Vec2(200, 150));
                    _gunner = new GunnerPilot(new Grid2D(bounds, combat.NavigationCellSize), combat.Weapon,
                        combat.Difficulties[_gunnerLevel - 1].ToSpec());
                }
                bool alive = combat.Status.IsPlayerAlive && combat.Simulation.EnemyHealth > 0 &&
                    target != null && target.IsInitialized && target.isActiveAndEnabled;
                return _gunner.Tick(self.State, alive ? target.State : default, alive, deltaTime);
            }

            // 不在 Awake 读取其他组件的初始化结果：首次固定步调用时再接线。
            if (_source == null)
            {
                _world = new ChaseWorldView();
                _source = new PursuitChargeInputSource(_world, ChaseWorldView.SelfId,
                    AircraftSpecAdapter.ToSpec(self.Ship), combat, self.ArenaBounds, difficultyOverride);
            }

            // 画面外仍直接追踪；入屏后才能开启完整跟瞄，不会进入占位等待。
            var camera = Camera.main;
            _source.VisibleApproachHalfWidth = camera != null && camera.orthographic
                ? Mathf.Min(7.8f, Mathf.Max(0f, camera.orthographicSize * camera.aspect - 1f)) : 7.8f;
            _source.VisibleApproachHalfHeight = camera != null && camera.orthographic
                ? Mathf.Max(0f, camera.orthographicSize - 1f) : 4f;
            bool targetVisible = camera != null && FullyVisible(camera, target);
            _world.Capture(self, target, combat.Status.IsPlayerAlive, targetVisible, combat.Status.ElapsedTime);
            _source.CanStartVisibleAttack = camera != null &&
                FullyVisible(camera, self) && targetVisible;
            return _source.ReadInput(deltaTime);
        }

        static bool FullyVisible(Camera camera, PlayerController aircraft)
        {
            if (aircraft == null || !aircraft.IsInitialized) return false;
            var position = aircraft.State.Position;
            float radius = 0.6f;
            if (aircraft.Ship != null && aircraft.Ship.BodySprite != null)
            {
                var extents = aircraft.Ship.BodySprite.bounds.extents;
                radius = Mathf.Max(radius, new Vector2(extents.x, extents.y).magnitude);
            }
            Vector3 a = camera.WorldToViewportPoint(new Vector3(position.X - radius, position.Y - radius, 0f));
            Vector3 b = camera.WorldToViewportPoint(new Vector3(position.X + radius, position.Y + radius, 0f));
            return a.z > 0f && b.z > 0f && a.x >= 0f && a.y >= 0f && b.x <= 1f && b.y <= 1f;
        }

        sealed class ChaseWorldView : IWorldView
        {
            public const int SelfId = 2;
            AircraftSnapshot _self;
            AircraftSnapshot _target;

            public int AircraftCount => 2;

            public void Capture(PlayerController self, PlayerController target, bool playerAlive, bool targetVisible, float now)
            {
                _self = new AircraftSnapshot(SelfId, 2, self.State,
                    isAlive: self.IsInitialized && self.isActiveAndEnabled);
                bool targetAvailable = playerAlive && target != null && target != self &&
                    target.IsInitialized && target.isActiveAndEnabled;
                _target = new AircraftSnapshot(1, 1,
                    targetAvailable ? target.State : default,
                    isAlive: targetAvailable, isPlayerControlled: true,
                    propulsionCue: targetAvailable && targetVisible ? ReadVisibleCue(target) : VisiblePropulsionCue.None,
                    isVisible: targetVisible, cueObservedAt: now);
            }

            static VisiblePropulsionCue ReadVisibleCue(PlayerController aircraft)
            {
                foreach (MonoBehaviour behaviour in aircraft.GetComponents<MonoBehaviour>())
                    if (behaviour is IVisiblePropulsionCueSource cue && behaviour.isActiveAndEnabled) return cue.VisiblePropulsionCue;
                return VisiblePropulsionCue.None;
            }

            public AircraftSnapshot GetAircraft(int index)
            {
                switch (index)
                {
                    case 0: return _self;
                    case 1: return _target;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }
    }
}
