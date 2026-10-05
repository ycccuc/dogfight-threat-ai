namespace Dogfight.Ai
{
    /// <summary>
    /// 一架飞机在某一时刻的**只读快照**。
    ///
    /// AI 不直接持有对方的 MonoBehaviour，只看这个结构体。
    /// 这样做的直接好处：单测里可以手搓几个 AgentSnapshot 喂给 AI，
    /// 不需要创建任何 GameObject；批量对战里也不需要渲染。
    /// </summary>
    public readonly struct AgentSnapshot
    {
        public readonly int Id;

        public readonly Vec2 Position;

        /// <summary>速度（单位/秒）。</summary>
        public readonly Vec2 Velocity;

        /// <summary>机头朝向（弧度，0 = +X，逆时针为正）。</summary>
        public readonly float HeadingRadians;

        public readonly float Hp;

        public readonly float MaxHp;

        public readonly bool Alive;

        /// <summary>阵营：同阵营互不攻击。0 = 玩家方，1 = 敌方。</summary>
        public readonly int Team;

        public AgentSnapshot(
            int id,
            Vec2 position,
            Vec2 velocity,
            float headingRadians,
            float hp,
            float maxHp,
            bool alive,
            int team)
        {
            Id = id;
            Position = position;
            Velocity = velocity;
            HeadingRadians = headingRadians;
            Hp = hp;
            MaxHp = maxHp;
            Alive = alive;
            Team = team;
        }

        public float Speed => Velocity.Magnitude;

        public float HpRatio => MaxHp > 1e-4f ? MathUtil.Clamp01(Hp / MaxHp) : 0f;

        /// <summary>机头方向的单位向量。</summary>
        public Vec2 Heading => Vec2.FromAngle(HeadingRadians);

        public override string ToString() =>
            "#" + Id + "(t" + Team + ") pos=" + Position + " hp=" + Hp.ToString("0.#");
    }

    /// <summary>
    /// AI 能看到的世界（只读）。
    ///
    /// 刻意做成接口而不是直接给数组：将来接威胁网格时，
    /// 只需要多一个实现（或在本接口上加查询方法），AI 的代码不用改。
    /// 单测里可以给一个手写的假实现。
    /// </summary>
    public interface IAiWorld
    {
        /// <summary>世界里有多少架飞机（含自己、含已死亡的）。</summary>
        int AgentCount { get; }

        AgentSnapshot GetAgent(int index);
    }

    /// <summary>
    /// 把固定数组包装成 IAiWorld。批量对战与单测都用它，避免额外分配。
    /// </summary>
    public sealed class ArrayAiWorld : IAiWorld
    {
        AgentSnapshot[] _agents = new AgentSnapshot[0];
        int _count;

        public int AgentCount => _count;

        public void SetAgents(AgentSnapshot[] agents, int count)
        {
            _agents = agents ?? new AgentSnapshot[0];
            _count = MathUtil.Clamp(count, 0, _agents.Length);
        }

        public AgentSnapshot GetAgent(int index) =>
            index >= 0 && index < _count ? _agents[index] : default;

        public int IndexOf(int id)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_agents[i].Id == id) return i;
            }
            return -1;
        }

        /// <summary>找距离 from 最近、且与 excludeTeam 不同的存活目标。找不到返回 -1（索引）。</summary>
        public int FindNearestEnemyIndex(Vec2 from, int excludeTeam)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _count; i++)
            {
                AgentSnapshot a = _agents[i];
                if (!a.Alive || a.Team == excludeTeam) continue;
                float d = Vec2.SqrDistance(a.Position, from);
                if (d < bestSqr)
                {
                    bestSqr = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>统计某一阵营还活着几架。</summary>
        public int CountAlive(int team)
        {
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_agents[i].Alive && _agents[i].Team == team) n++;
            }
            return n;
        }
    }
}
