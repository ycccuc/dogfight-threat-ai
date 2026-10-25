namespace DogfightThreatAI.Core.Input
{
    /// <summary>
    /// 一次操作意图，包含油门与转向。
    /// 玩家、AI 和网络输入均使用该结构，FlightModel 无需知道输入来源。
    /// 该结构仅表示意图，不包含最终速度或位置。
    /// </summary>
    public struct FlightInput
    {
        /// <summary>油门：-1（减速）~ 1（加速）</summary>
        public float Throttle;

        /// <summary>转向：-1（顺时针）~ 1（逆时针），与 FlightModel 中 Heading 的正方向一致。</summary>
        public float Steer;

        public FlightInput(float throttle, float steer)
        {
            Throttle = throttle;
            Steer = steer;
        }

        public static FlightInput Neutral => new FlightInput(0f, 0f);
    }
}
