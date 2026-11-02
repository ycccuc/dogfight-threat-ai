using DogfightThreatAI.Core.Math;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Adapters
{
    /// <summary>
    /// Vec2 与 UnityEngine.Vector2 的转换扩展。
    /// Core 不引用 UnityEngine，相关转换放在 Gameplay 层。
    /// </summary>
    public static class Vec2UnityExtensions
    {
        // 把 Core 向量转换为 Unity 表现层向量。
        public static Vector2 ToUnity(this Vec2 v)
        {
            return new Vector2(v.X, v.Y);
        }

        // 把 Unity 输入或配置坐标转换为 Core 向量。
        public static Vec2 ToCore(this Vector2 v)
        {
            return new Vec2(v.x, v.y);
        }
    }
}
