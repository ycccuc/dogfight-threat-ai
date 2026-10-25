namespace DogfightThreatAI.Core.World
{
    /// <summary>Public visual state only; never exposes input intent or propulsion resources.</summary>
    public interface IVisiblePropulsionCueSource
    {
        VisiblePropulsionCue VisiblePropulsionCue { get; }
    }
}
