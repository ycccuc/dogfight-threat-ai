namespace Dogfight.Ai
{
    /// <summary>
    /// 确定性伪随机数（xorshift32）。
    ///
    /// 为什么不用 System.Random：
    ///   它的实现在不同 .NET 版本 / 平台上不保证一致。而"AI 批量对战 + 胜率统计"
    ///   要求「同样的种子 + 同样的输入 → 同样的结果」，否则两批数据没法比较，
    ///   出了问题也没法复现。这个实现只用了 uint 的位运算，跨平台完全一致。
    ///
    /// 用法：每个对局开一个 Rng，种子写进对战记录，出问题就能按种子重放。
    /// </summary>
    public struct DeterministicRng
    {
        uint _state;

        public DeterministicRng(uint seed)
        {
            // 0 会让 xorshift 永远输出 0，所以换一个非零常量
            _state = seed == 0u ? 0x9E3779B9u : seed;
        }

        /// <summary>当前内部状态，用于存档 / 复现。</summary>
        public uint State => _state;

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0, 1)</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>[min, max)</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>[minInclusive, maxExclusive)</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            int span = maxExclusive - minInclusive;
            if (span <= 0) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)span);
        }

        /// <summary>以 probability 的概率返回 true。</summary>
        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>从数组里随机取一个元素（数组为空返回 default）。</summary>
        public T Pick<T>(T[] items) => items == null || items.Length == 0 ? default : items[Range(0, items.Length)];
    }
}
