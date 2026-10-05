using System.Collections.Generic;
using System.Text;

namespace Dogfight.Ai
{
    public enum MatchEndReason
    {
        /// <summary>还没结束。</summary>
        None = 0,

        /// <summary>时间到（按存活/得分判定）。</summary>
        Timeout = 1,

        /// <summary>某一方全部被击落。</summary>
        TeamEliminated = 2,

        /// <summary>被外部中止（例如批量对战里检测到卡死）。</summary>
        Aborted = 3,
    }

    /// <summary>
    /// 一局对战的结果（纯数据）。
    /// 批量对战就是"跑很多个 MatchResult 然后统计"。
    /// </summary>
    public readonly struct MatchResult
    {
        public readonly MatchEndReason Reason;

        /// <summary>获胜阵营；平局为 -1。</summary>
        public readonly int WinnerTeam;

        public readonly float DurationSeconds;

        public readonly int Ticks;

        /// <summary>本局使用的随机种子 —— 出问题可以按种子完整重放。</summary>
        public readonly uint Seed;

        /// <summary>各阵营的存活数（下标 = 阵营）。</summary>
        public readonly int[] SurvivorsByTeam;

        public MatchResult(
            MatchEndReason reason,
            int winnerTeam,
            float durationSeconds,
            int ticks,
            uint seed,
            int[] survivorsByTeam)
        {
            Reason = reason;
            WinnerTeam = winnerTeam;
            DurationSeconds = durationSeconds;
            Ticks = ticks;
            Seed = seed;
            SurvivorsByTeam = survivorsByTeam;
        }

        public bool IsDraw => WinnerTeam < 0;

        public override string ToString() =>
            Reason + " winner=T" + WinnerTeam + " " + DurationSeconds.ToString("0.0") + "s ticks=" + Ticks;
    }

    /// <summary>
    /// 批量对战的结果汇总：胜率、平均时长、中止局数。
    /// 故意做成纯 C#（不碰文件 IO）—— 写 CSV 是表现层的事，
    /// 这样这个类可以直接单测："喂 3 个结果，胜率必须是 2/3"。
    /// </summary>
    public sealed class BatchBattleReport
    {
        readonly List<MatchResult> _results = new List<MatchResult>(256);

        public int MatchCount => _results.Count;

        public IReadOnlyList<MatchResult> Results => _results;

        public void Add(in MatchResult result) => _results.Add(result);

        public void Clear() => _results.Clear();

        public int CountWins(int team)
        {
            int n = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].WinnerTeam == team) n++;
            }
            return n;
        }

        public int CountDraws()
        {
            int n = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].IsDraw) n++;
            }
            return n;
        }

        /// <summary>中止局数：正常跑完的局里不该出现，出现了就说明有卡死或异常。</summary>
        public int CountAborted()
        {
            int n = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                if (_results[i].Reason == MatchEndReason.Aborted) n++;
            }
            return n;
        }

        public float WinRate(int team)
        {
            if (_results.Count == 0) return 0f;
            return (float)CountWins(team) / _results.Count;
        }

        public float AverageDurationSeconds()
        {
            if (_results.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < _results.Count; i++) sum += _results[i].DurationSeconds;
            return sum / _results.Count;
        }

        public int AverageTicks()
        {
            if (_results.Count == 0) return 0;
            long sum = 0;
            for (int i = 0; i < _results.Count; i++) sum += _results[i].Ticks;
            return (int)(sum / _results.Count);
        }

        /// <summary>
        /// 输出 CSV。列刻意写成固定的，方便直接丢进 Excel 做胜率矩阵。
        /// </summary>
        public string ToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("index,seed,reason,winner_team,duration_s,ticks,survivors_t0,survivors_t1");
            for (int i = 0; i < _results.Count; i++)
            {
                MatchResult r = _results[i];
                int t0 = r.SurvivorsByTeam != null && r.SurvivorsByTeam.Length > 0 ? r.SurvivorsByTeam[0] : 0;
                int t1 = r.SurvivorsByTeam != null && r.SurvivorsByTeam.Length > 1 ? r.SurvivorsByTeam[1] : 0;
                sb.Append(i).Append(',')
                  .Append(r.Seed).Append(',')
                  .Append(r.Reason).Append(',')
                  .Append(r.WinnerTeam).Append(',')
                  .Append(r.DurationSeconds.ToString("0.###")).Append(',')
                  .Append(r.Ticks).Append(',')
                  .Append(t0).Append(',')
                  .Append(t1)
                  .AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>一行摘要，打印在控制台或窗口上。</summary>
        public string ToSummary()
        {
            return "局数 " + MatchCount +
                   " · 阵营0 胜率 " + (WinRate(0) * 100f).ToString("0.0") + "%" +
                   " · 阵营1 胜率 " + (WinRate(1) * 100f).ToString("0.0") + "%" +
                   " · 平局 " + CountDraws() +
                   " · 中止 " + CountAborted() +
                   " · 平均时长 " + AverageDurationSeconds().ToString("0.0") + "s";
        }
    }
}
