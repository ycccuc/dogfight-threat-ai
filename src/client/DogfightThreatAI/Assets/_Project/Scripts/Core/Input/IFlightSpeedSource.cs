namespace DogfightThreatAI.Core.Input
{
    /// <summary>Optional flight ability requested by an input source. FlightModel owns its actual motion.</summary>
    public interface IFlightSpeedSource
    {
        float SpeedMultiplier { get; }
    }
}
