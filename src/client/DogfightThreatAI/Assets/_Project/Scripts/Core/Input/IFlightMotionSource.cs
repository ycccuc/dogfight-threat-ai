namespace DogfightThreatAI.Core.Input
{
    /// <summary>Optional explicit motion policy read after the source consumes its input step.</summary>
    public interface IFlightMotionSource
    {
        FlightMotionCommand MotionCommand { get; }
    }
}
