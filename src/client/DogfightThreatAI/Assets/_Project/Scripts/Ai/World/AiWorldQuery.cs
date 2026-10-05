namespace Dogfight.Ai
{
    /// <summary>
    /// 对世界的查询工具。
    ///
    /// 为什么不做成 IAiWorld 的成员：接口应该尽量小。
    /// 这些是"基于最小接口组合出来的常用查询"，写成静态工具既好测，
    /// 将来换世界实现（例如接上威胁网格）也不用改接口。
    /// </summary>
    public static class AiWorldQuery
    {
        /// <summary>按 id 找一架飞机。找不到返回 false（out 为 default）。</summary>
        public static bool TryFindById(IAiWorld world, int id, out AgentSnapshot agent)
        {
            agent = default;
            if (world == null || id < 0) return false;

            for (int i = 0; i < world.AgentCount; i++)
            {
                AgentSnapshot candidate = world.GetAgent(i);
                if (candidate.Id == id)
                {
                    agent = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 找最近的可攻击目标（存活的、不同阵营的），可选限制距离。
        /// preferId / preferRange 用于**目标黏性**：已经在打的敌人在这个范围内就继续打它，
        /// 避免两个距离接近的敌人导致 AI 每帧反复切换目标（画面上表现为抽搐）。
        /// </summary>
        public static bool TryFindNearestEnemy(
            IAiWorld world,
            Vec2 from,
            int selfTeam,
            float maxRange,
            int selfId,
            out AgentSnapshot target,
            int preferId = -1,
            float preferRange = 0f)
        {
            target = default;
            if (world == null) return false;

            if (preferId >= 0 &&
                TryFindById(world, preferId, out AgentSnapshot preferred) &&
                preferred.Alive &&
                preferred.Team != selfTeam &&
                (preferRange <= 0f || Vec2.Distance(preferred.Position, from) <= preferRange))
            {
                target = preferred;
                return true;
            }

            float maxRangeSqr = maxRange <= 0f ? float.MaxValue : maxRange * maxRange;
            float bestSqr = float.MaxValue;
            bool found = false;

            for (int i = 0; i < world.AgentCount; i++)
            {
                AgentSnapshot candidate = world.GetAgent(i);
                if (!candidate.Alive) continue;
                if (candidate.Team == selfTeam) continue;
                if (candidate.Id == selfId) continue;

                float sqr = Vec2.SqrDistance(candidate.Position, from);
                if (sqr > maxRangeSqr || sqr >= bestSqr) continue;

                bestSqr = sqr;
                target = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>某一阵营存活数量（判定胜负、评估敌我数量比都用到）。</summary>
        public static int CountAlive(IAiWorld world, int team)
        {
            if (world == null) return 0;
            int n = 0;
            for (int i = 0; i < world.AgentCount; i++)
            {
                AgentSnapshot a = world.GetAgent(i);
                if (a.Alive && a.Team == team) n++;
            }
            return n;
        }

        /// <summary>把世界里的所有飞机导出成命中判定用的圆形目标数组（开火时调用）。</summary>
        public static int CollectCircleTargets(
            IAiWorld world,
            CircleTarget[] buffer,
            int bufferLength,
            float defaultRadius)
        {
            if (world == null || buffer == null) return 0;

            int written = 0;
            int count = world.AgentCount;
            for (int i = 0; i < count && written < bufferLength; i++)
            {
                AgentSnapshot a = world.GetAgent(i);
                buffer[written] = new CircleTarget(a.Id, a.Position, defaultRadius, a.Team, a.Alive);
                written++;
            }
            return written;
        }
    }
}
