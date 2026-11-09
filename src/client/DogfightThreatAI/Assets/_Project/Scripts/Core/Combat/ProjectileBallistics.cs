using System;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.Combat
{
    internal static class ProjectileBallistics
    {
        internal static ProjectileState Launch(WeaponSpec weapon, int id, int owner, in FlightState aircraft)
        {
            Vec2 forward = aircraft.Forward;
            Vec2 position = aircraft.Position + forward * weapon.MuzzleOffset;
            return new ProjectileState
            {
                Id = id,
                OwnerId = owner,
                Position = position,
                PreviousPosition = position,
                Velocity = forward * weapon.ProjectileSpeed,
                Remaining = weapon.Lifetime
            };
        }

        internal static bool TryContact(Vec2 from, Vec2 to, float radius, out float fraction)
        {
            fraction = 0f;
            float outside = from.SqrMagnitude - radius * radius;
            if (outside <= 0f)
            {
                return true;
            }
            Vec2 delta = to - from;
            float lengthSquared = delta.SqrMagnitude;
            float approach = Vec2.Dot(from, delta);
            if (lengthSquared <= .000001f || approach >= 0f)
            {
                return false;
            }
            float discriminant = approach * approach - lengthSquared * outside;
            if (discriminant < 0f)
            {
                return false;
            }
            fraction = outside / (-approach + MathF.Sqrt(discriminant));
            return fraction <= 1f;
        }
    }
}
