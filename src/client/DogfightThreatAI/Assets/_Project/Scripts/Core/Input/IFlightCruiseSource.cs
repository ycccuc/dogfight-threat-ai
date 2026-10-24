namespace DogfightThreatAI.Core.Input
{
    /// <summary>Optional powered cruise that maintains normal forward speed while steering.</summary>
    public interface IFlightCruiseSource
    {
        bool MaintainCruiseSpeed { get; }
    }
}
