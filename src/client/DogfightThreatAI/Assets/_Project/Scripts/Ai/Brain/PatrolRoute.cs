using System.Collections.Generic;

namespace Dogfight.Ai
{
    /// <summary>
    /// 一条巡逻航线 + 当前进度。
    ///
    /// 航线来自 `PatrolRouteBuilder`（BFS 扩散出空域 → 最远点采样 → 最近邻串环），
    /// 所以它是**由地形算出来的**，不是写死的一串坐标 ——
    /// 这正是立项书批评"固定航线脚本"时想要的区别。
    /// </summary>
    public sealed class PatrolRoute
    {
        readonly List<Vec2> _waypoints;
        int _cursor;

        public PatrolRoute(List<Vec2> waypoints)
        {
            _waypoints = waypoints ?? new List<Vec2>();
        }

        /// <summary>从网格地图生成一条闭环航线（世界坐标）。</summary>
        public static PatrolRoute FromGrid(GridMap map, Vec2Int regionSeed, int regionRadius, int maxWaypoints)
        {
            var waypoints = new List<Vec2>(maxWaypoints + 1);
            if (map != null)
            {
                var builder = new PatrolRouteBuilder(map);
                builder.Build(regionSeed, regionRadius, maxWaypoints, waypoints);
            }
            return new PatrolRoute(waypoints);
        }

        public IReadOnlyList<Vec2> Waypoints => _waypoints;

        public int Count => _waypoints.Count;

        public bool IsEmpty => _waypoints.Count == 0;

        public int Cursor => _cursor;

        public Vec2 Current => _waypoints.Count == 0 ? Vec2.Zero : _waypoints[_cursor];

        /// <summary>下一个路点（循环）。到航线末尾会绕回起点。</summary>
        public void Advance()
        {
            if (_waypoints.Count == 0) return;
            _cursor = (_cursor + 1) % _waypoints.Count;
        }

        public bool HasReached(Vec2 position, float arriveRadius) =>
            !IsEmpty && Vec2.Distance(position, Current) <= arriveRadius;

        public void Reset() => _cursor = 0;

        public override string ToString() =>
            "PatrolRoute(" + _waypoints.Count + " 点, 当前 #" + _cursor + ")";
    }
}
