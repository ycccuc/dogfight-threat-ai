using System;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Flight
{
    /// <summary>
    /// 飞机在某一时刻的完整状态。
    /// 该结构仅包含运行状态，可复制、可序列化，用于模拟与网络快照。
    /// 配置数据存放在 AircraftData 和 AircraftSpec 中。
    /// </summary>
    [Serializable]
    public struct FlightState
    {
        /// <summary>位置（世界坐标，单位）</summary>
        public Vec2 Position;

        /// <summary>速度向量（单位/秒）。大小即当前速度。</summary>
        public Vec2 Velocity;

        /// <summary>机头朝向（弧度）。**不做取模**，允许累积到很大——这样渲染插值不会在 ±180° 处跳变。</summary>
        public float Heading;

        public float Speed => Velocity.Magnitude;

        /// <summary>机头单位向量</summary>
        public Vec2 Forward => Vec2.FromAngle(Heading);

        // 按给定位置和朝向创建静止的出生状态。
        public static FlightState At(Vec2 position, float heading = 0f)
        {
            return new FlightState { Position = position, Velocity = Vec2.Zero, Heading = heading };
        }
    }
}
