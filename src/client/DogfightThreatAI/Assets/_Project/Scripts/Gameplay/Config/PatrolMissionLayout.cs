using System;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.UI.Battle;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Config
{
    public sealed class PatrolMissionLayout : ScriptableObject
    {
        [Serializable]
        public sealed class Sector
        {
            public string name;
            public Vector2 center;
            public Vector2 halfSize;
            [Range(1, 4)] public int enemies = 2;
        }

        [Serializable]
        public sealed class Pickup
        {
            public string name;
            public Vector2 position;
        }

        [SerializeField] AircraftData enemyAircraft;
        [SerializeField] WeaponData weapon;
        [SerializeField] Sector[] sectors;
        [SerializeField] Pickup[] pickups;
        [SerializeField, Min(1)] int playerHealth = 5;
        [SerializeField, Min(1)] int enemyHealth = 2;
        [SerializeField, Min(1)] float sightRange = 27f;
        [SerializeField, Range(1, 179)] float sightHalfAngle = 57f;

        public AircraftData EnemyAircraft => enemyAircraft;
        public WeaponData Weapon => weapon;
        public Sector[] Sectors => sectors;
        public Pickup[] Pickups => pickups;
        public int PlayerHealth => playerHealth;
        public int EnemyHealth => enemyHealth;
        public Bounds2D Arena => Bounds2D.Centered(new Vec2(200, 150));
        public bool IsConfigured => enemyAircraft != null && enemyAircraft.BodySprite != null &&
            weapon != null && sectors != null && sectors.Length > 0 && pickups != null;

        public PatrolMission CreateMission(int players = 1)
        {
            var areas = Array.ConvertAll(sectors, s => new PatrolSector(ToCore(s.center), ToCore(s.halfSize), s.enemies));
            return new PatrolMission(Arena, weapon.ToSpec(), AircraftSpecAdapter.ToSpec(enemyAircraft),
                areas, playerHealth, enemyHealth, sightRange, sightHalfAngle, PickupPoints(), players);
        }

        public Vec2[] PickupPoints() => Array.ConvertAll(pickups, p => ToCore(p.position));

        public void BuildHud(PatrolMissionHud hud, Action retry, Action leave)
        {
            hud.Build(Array.ConvertAll(sectors, s => s.name), Array.ConvertAll(sectors, s => ChartPosition(ToCore(s.center))),
                Array.ConvertAll(pickups, p => p.name), Array.ConvertAll(pickups, p => ChartPosition(ToCore(p.position))),
                retry, leave);
        }

        public Vector2 ChartPosition(Vec2 point)
        {
            var bounds = Arena;
            return new Vector2((point.X - bounds.Min.X) / bounds.Width, (point.Y - bounds.Min.Y) / bounds.Height);
        }

        static Vec2 ToCore(Vector2 point) => new Vec2(point.x, point.y);
    }
}
