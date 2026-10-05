namespace Dogfight.Ai
{
    /// <summary>
    /// 一个圆形碰撞体（飞机、子弹都用它）。
    /// 为什么要自己定义而不是用 Unity 的 Collider2D：
    /// 命中判定是战斗数值的一部分，必须能在没有引擎的环境里单测 ——
    /// "这发子弹到底该不该打中"不该靠打开编辑器看。
    /// </summary>
    public readonly struct CircleTarget
    {
        public readonly int Id;
        public readonly Vec2 Position;
        public readonly float Radius;
        public readonly int Team;
        public readonly bool Alive;

        public CircleTarget(int id, Vec2 position, float radius, int team, bool alive)
        {
            Id = id;
            Position = position;
            Radius = radius < 0f ? 0f : radius;
            Team = team;
            Alive = alive;
        }
    }

    /// <summary>一次射线检测的结果。Hit = false 时其余字段无意义。</summary>
    public readonly struct RayHit
    {
        public readonly bool Hit;

        public readonly int TargetId;

        /// <summary>沿射线方向的距离。</summary>
        public readonly float Distance;

        public readonly Vec2 Point;

        /// <summary>命中点朝向射线来向的法线（单位向量）。将来做跳弹/火花方向要用。</summary>
        public readonly Vec2 Normal;

        public RayHit(bool hit, int targetId, float distance, Vec2 point, Vec2 normal)
        {
            Hit = hit;
            TargetId = targetId;
            Distance = distance;
            Point = point;
            Normal = normal;
        }

        public static readonly RayHit Miss = new RayHit(false, -1, 0f, Vec2.Zero, Vec2.Zero);

        public override string ToString() =>
            Hit ? "Hit #" + TargetId + " @" + Distance.ToString("0.##") : "Miss";
    }

    /// <summary>
    /// 命中判定的几何计算。全部是纯函数，全部可单测。
    ///
    /// 覆盖第一阶段 ③ 里的两件事：
    ///   - 机炮：**射线检测**（hitscan）—— 一发子弹就是一个线段，找最近的交点
    ///   - 碰撞：圆与圆重叠、点到线段距离、最近接近点（导弹近炸与 AI 规避都用）
    /// </summary>
    public static class HitGeometry
    {
        /// <summary>
        /// 射线与圆求交，返回沿射线方向最近的正向交点距离。
        /// 不相交 / 在 maxRange 之外返回 -1。
        /// 起点在圆内时返回"穿出"的距离（这样贴脸开火也能命中）。
        /// </summary>
        public static float RaycastCircle(Vec2 origin, Vec2 direction, float maxRange, Vec2 center, float radius)
        {
            Vec2 dir = direction.Normalized;
            if (dir.SqrMagnitude < 0.5f) return -1f; // 方向退化

            Vec2 toOrigin = origin - center;
            float b = Vec2.Dot(toOrigin, dir);
            float c = Vec2.Dot(toOrigin, toOrigin) - radius * radius;

            // 起点在圆外且背向圆心 → 不可能命中
            if (c > 0f && b > 0f) return -1f;

            float discriminant = b * b - c;
            if (discriminant < 0f) return -1f;

            float sqrt = MathUtil.Sqrt(discriminant);
            float t = -b - sqrt;

            // t < 0 说明起点在圆内，取穿出点
            if (t < 0f) t = -b + sqrt;
            if (t < 0f || t > maxRange) return -1f;
            return t;
        }

        /// <summary>
        /// 在一组目标里找**最近**的命中（机炮的完整判定）。
        /// excludeTeam 的目标会被跳过（不打自己人）；ignoreId 用于排除开火者自己。
        /// 传数组 + count 而不是 List，避免运行时分配。
        /// </summary>
        public static RayHit RaycastNearest(
            Vec2 origin,
            Vec2 direction,
            float maxRange,
            CircleTarget[] targets,
            int count,
            int excludeTeam,
            int ignoreId = -1)
        {
            if (targets == null || count <= 0) return RayHit.Miss;

            Vec2 dir = direction.Normalized;
            float bestDistance = float.MaxValue;
            int bestId = -1;
            Vec2 bestPoint = Vec2.Zero;
            Vec2 bestNormal = Vec2.Zero;

            for (int i = 0; i < count && i < targets.Length; i++)
            {
                CircleTarget target = targets[i];
                if (!target.Alive) continue;
                if (target.Team == excludeTeam) continue;
                if (target.Id == ignoreId) continue;

                float distance = RaycastCircle(origin, dir, maxRange, target.Position, target.Radius);
                if (distance < 0f || distance >= bestDistance) continue;

                bestDistance = distance;
                bestId = target.Id;
                bestPoint = origin + dir * distance;
                Vec2 away = bestPoint - target.Position;
                bestNormal = away.SqrMagnitude > 1e-8f ? away.Normalized : -dir;
            }

            if (bestId < 0) return RayHit.Miss;
            return new RayHit(true, bestId, bestDistance, bestPoint, bestNormal);
        }

        /// <summary>两圆是否重叠（接触即算重叠）。</summary>
        public static bool CirclesOverlap(Vec2 a, float radiusA, Vec2 b, float radiusB)
        {
            float sum = radiusA + radiusB;
            return Vec2.SqrDistance(a, b) <= sum * sum;
        }

        /// <summary>点到线段的最近距离。导弹擦过判定、以及将来的 AI"会不会撞上"都用它。</summary>
        public static float DistancePointToSegment(Vec2 point, Vec2 segmentStart, Vec2 segmentEnd)
        {
            Vec2 segment = segmentEnd - segmentStart;
            float lengthSqr = segment.SqrMagnitude;
            if (lengthSqr < 1e-8f) return Vec2.Distance(point, segmentStart);

            float t = Vec2.Dot(point - segmentStart, segment) / lengthSqr;
            t = MathUtil.Clamp01(t);
            Vec2 projection = segmentStart + segment * t;
            return Vec2.Distance(point, projection);
        }

        /// <summary>点到线段的最近点坐标。</summary>
        public static Vec2 ClosestPointOnSegment(Vec2 point, Vec2 segmentStart, Vec2 segmentEnd)
        {
            Vec2 segment = segmentEnd - segmentStart;
            float lengthSqr = segment.SqrMagnitude;
            if (lengthSqr < 1e-8f) return segmentStart;

            float t = MathUtil.Clamp01(Vec2.Dot(point - segmentStart, segment) / lengthSqr);
            return segmentStart + segment * t;
        }

        /// <summary>
        /// 两个匀速运动物体的**最近接近**：距离最近的时间点与那时的距离。
        /// 用途：导弹近炸引信、AI 判断"我这样飞会不会撞上他"。
        /// 若在 maxTime 内不会比初始更近，返回 false（time/distance 仍给出端点值）。
        /// </summary>
        public static bool ClosestApproach(
            Vec2 positionA,
            Vec2 velocityA,
            Vec2 positionB,
            Vec2 velocityB,
            float maxTime,
            out float time,
            out float distance)
        {
            Vec2 relativePosition = positionB - positionA;
            Vec2 relativeVelocity = velocityB - velocityA;
            float speedSqr = relativeVelocity.SqrMagnitude;

            if (speedSqr < 1e-8f)
            {
                time = 0f;
                distance = relativePosition.Magnitude;
                return false;
            }

            float t = -Vec2.Dot(relativePosition, relativeVelocity) / speedSqr;
            bool inWindow = t >= 0f && t <= maxTime;
            t = MathUtil.Clamp(t, 0f, maxTime);

            time = t;
            distance = (relativePosition + relativeVelocity * t).Magnitude;
            return inWindow;
        }
    }
}
