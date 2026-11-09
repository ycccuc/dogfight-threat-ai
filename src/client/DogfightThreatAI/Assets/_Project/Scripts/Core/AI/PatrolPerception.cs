using System;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.AI
{
    public enum PatrolContact
    {
        None,
        Suspicious,
        Confirmed,
        Lost
    }

    /// <summary>感知只在视锥内积累确认。失联时保留最后一次确认的飞行状态。</summary>
    public sealed class PatrolPerception
    {
        readonly float range;
        readonly float coneDot;
        readonly float searchSeconds;
        float visualEvidence;
        float memoryRemaining;
        float clueRemaining;

        public PatrolContact Contact { get; private set; }
        public float VisualEvidence => visualEvidence;
        public FlightState LastSeen { get; private set; }
        public Vec2 Clue { get; private set; }
        public float TimeSinceSeen => searchSeconds - memoryRemaining;

        public PatrolPerception(float range, float halfAngleDegrees, float searchSeconds)
        {
            if (!(range > 0f) || !(halfAngleDegrees > 0f && halfAngleDegrees < 180f) ||
                !(searchSeconds > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(range));
            }
            this.range = range;
            coneDot = MathF.Cos(halfAngleDegrees * MathF.PI / 180f);
            this.searchSeconds = searchSeconds;
        }

        // 声音只产生区域线索，不能触发确认或刷新已确认的目标位置。
        public void Hear(Vec2 origin)
        {
            Clue = CoarsePosition(origin);
            clueRemaining = 3f;
            if (Contact == PatrolContact.None)
            {
                Contact = PatrolContact.Suspicious;
            }
        }

        public void Observe(in FlightState self, FlightState? observation, float dt)
        {
            FlightState target = observation.GetValueOrDefault();
            var offset = target.Position - self.Position;
            float distance = offset.Magnitude;
            float dot = distance > .001f ? Vec2.Dot(self.Forward, offset / distance) : 1f;
            bool visible = observation.HasValue && distance <= range && dot >= coneDot;

            if (visible)
            {
                float closeness = 1f - distance / range;
                float alignment = MathF.Min(1f, (dot - coneDot) / (1f - coneDot));
                visualEvidence = MathF.Min(1f, visualEvidence +
                    dt * (.65f + 1.65f * closeness + .8f * alignment));
                if (visualEvidence >= 1f || Contact == PatrolContact.Confirmed)
                {
                    LastSeen = target;
                    memoryRemaining = searchSeconds;
                    Contact = PatrolContact.Confirmed;
                    return;
                }
                Clue = CoarsePosition(target.Position);
                clueRemaining = 1.4f;
            }
            else
            {
                // 断开观测后无法偷读真实位置。重新进入视锥仍需要一段确认时间。
                if (Contact == PatrolContact.Confirmed)
                {
                    visualEvidence = .45f;
                }
                visualEvidence = MathF.Max(0f, visualEvidence - dt * .8f);
            }

            memoryRemaining = MathF.Max(0f, memoryRemaining - dt);
            clueRemaining = MathF.Max(0f, clueRemaining - dt);
            Contact = memoryRemaining > 0f ? PatrolContact.Lost :
                clueRemaining > 0f ? PatrolContact.Suspicious : PatrolContact.None;
        }

        static Vec2 CoarsePosition(Vec2 point) =>
            new Vec2(MathF.Floor(point.X / 4f) * 4f + 2f, MathF.Floor(point.Y / 4f) * 4f + 2f);
    }
}
