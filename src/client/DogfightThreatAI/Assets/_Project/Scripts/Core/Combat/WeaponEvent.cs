using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Combat
{
    public readonly struct WeaponEvent
    {
        public readonly WeaponEventKind Kind;
        public readonly int AircraftId;
        public readonly Vec2 Position;
        public WeaponEvent(WeaponEventKind kind, int aircraftId, Vec2 position)
        { Kind = kind; AircraftId = aircraftId; Position = position; }
    }
}
