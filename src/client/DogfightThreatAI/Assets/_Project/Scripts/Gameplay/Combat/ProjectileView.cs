using System.Collections.Generic;
using DogfightThreatAI.Core.Combat;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>复用弹丸线条；位置来自模拟快照，不以 Transform 或 Unity 碰撞判定命中。</summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        readonly List<LineRenderer> _pool = new List<LineRenderer>();
        Material _material;

        // 预先创建弹丸显示对象，供战斗期间复用。
        public void Build(int capacity)
        {
            _material = new Material(Shader.Find("Sprites/Default"));
            for (int i = 0; i < capacity; i++)
            {
                var line = new GameObject("Cannon round " + i).AddComponent<LineRenderer>();
                line.transform.SetParent(transform, false);
                line.sharedMaterial = _material;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.widthMultiplier = .095f;
                line.sortingOrder = 100;
                line.numCapVertices = 2;
                line.enabled = false;
                _pool.Add(line);
            }
        }

        // 读取弹丸状态并更新显示对象的位置与可见性。
        public void Present(IReadOnlyList<ProjectileState> shots, float alpha)
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var line = _pool[i];
                line.enabled = i < shots.Count;
                if (!line.enabled)
                {
                    continue;
                }
                var shot = shots[i];
                var a = shot.PreviousPosition;
                var b = shot.Position;
                var p = Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), alpha);
                var dir = shot.Velocity.Normalized;
                line.SetPosition(0, new Vector3(p.x, p.y, -.1f));
                line.SetPosition(1, new Vector3(p.x - dir.X * .55f, p.y - dir.Y * .55f, -.1f));
                var color = shot.OwnerId == 1 ? new Color(1, .92f, .69f) : new Color(1, .48f, .30f);
                line.startColor = color;
                color.a = .18f;
                line.endColor = color;
            }
        }

        // 释放弹丸视图持有的运行时资源。
        void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
