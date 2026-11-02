using DogfightThreatAI.Core.Math;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Config
{
    /// <summary>大厅配置：独立飞机参数、活动范围、出生点。运行状态由模拟持有。</summary>
    [CreateAssetMenu(menuName = "DogfightThreatAI/Lobby", fileName = "Lobby")]
    public sealed class LobbyData : ScriptableObject
    {
        [SerializeField] AircraftData aircraft;
        [SerializeField] Vector2 mapSize = new Vector2(32f, 18f);
        [SerializeField] Vector2 movementHalfSize = new Vector2(13.5f, 7f);
        [SerializeField]
        Vector2[] spawnPoints = { new Vector2(-8f, -5.5f), new Vector2(-2.7f, -5.5f),
            new Vector2(2.7f, -5.5f), new Vector2(8f, -5.5f) };
        [SerializeField, Min(.1f)] float aircraftWidth = .85f;

        public AircraftData Aircraft => aircraft;
        public Vector2 MapSize => mapSize;
        public Bounds2D MovementBounds => Bounds2D.Centered(new Vec2(movementHalfSize.x, movementHalfSize.y));
        public int Capacity => spawnPoints != null ? spawnPoints.Length : 0;
        public float AircraftWidth => aircraftWidth;

        // 把指定出生点的 Unity 配置坐标转换为 Core 向量。
        public Vec2 GetSpawnPoint(int index)
        {
            var point = spawnPoints[index];
            return new Vec2(point.x, point.y);
        }

        public bool IsValid
        {
            get
            {
                if (aircraft == null || aircraft.BodySprite == null || Capacity == 0 ||
                    mapSize.x <= 0 || mapSize.y <= 0 || movementHalfSize.x <= 0 || movementHalfSize.y <= 0 ||
                    movementHalfSize.x >= mapSize.x * .5f || movementHalfSize.y >= mapSize.y * .5f ||
                    aircraftWidth <= 0f)
                {
                    return false;
                }
                for (int i = 0; i < Capacity; i++)
                {
                    if (!MovementBounds.Contains(GetSpawnPoint(i)))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
