using DogfightThreatAI.Core.Flight;

namespace DogfightThreatAI.Core.World
{
    public enum VisiblePropulsionCue { None, Hold, Dash, Overheat }
    /// <summary>AI 读取的飞机值快照，不持有模拟对象或 Unity 对象的引用。</summary>
    public readonly struct AircraftSnapshot
    {
        public readonly int EntityId;
        public readonly int TeamId;
        public readonly bool IsAlive;
        public readonly bool IsPlayerControlled;
        public readonly FlightState Flight;
        public readonly VisiblePropulsionCue PropulsionCue;
        public readonly bool IsVisible;
        public readonly float CueObservedAt;

        public AircraftSnapshot(int entityId, int teamId, FlightState flight,
                                bool isAlive = true, bool isPlayerControlled = false,
                                VisiblePropulsionCue propulsionCue = VisiblePropulsionCue.None,
                                bool isVisible = true, float cueObservedAt = 0f)
        {
            EntityId = entityId;
            TeamId = teamId;
            Flight = flight;
            IsAlive = isAlive;
            IsPlayerControlled = isPlayerControlled;
            PropulsionCue = propulsionCue;
            IsVisible = isVisible;
            CueObservedAt = cueObservedAt;
        }
    }
}
