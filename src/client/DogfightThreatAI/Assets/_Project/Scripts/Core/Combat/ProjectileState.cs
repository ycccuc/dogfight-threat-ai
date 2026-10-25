using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Combat
{
    public struct ProjectileState
    {
        public int Id, OwnerId;
        public Vec2 Position, PreviousPosition, Velocity;
        public float Remaining;
    }
}
