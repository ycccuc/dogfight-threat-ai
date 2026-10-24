using System;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>A frozen action's physical limits and visible tactical skill.</summary>
    public readonly struct PursuitDifficultyProfile
    {
        public readonly int Level, CollisionDamage;
        public readonly float ObservationDelay, ObservationInterval, PredictionLead, LeadScale, SideOffset, RecoverySeconds;
        public readonly float CruiseSpeed, TurnRateDegPerSec, BoostSpeed, BoostDuration, BoostCooldown;

        PursuitDifficultyProfile(int level, float delay, float interval, float lead, float scale, float side, float recovery)
        {
            Level = level;
            CollisionDamage = 1;
            ObservationDelay = delay;
            ObservationInterval = interval;
            PredictionLead = lead;
            LeadScale = scale;
            SideOffset = side;
            RecoverySeconds = recovery;
            CruiseSpeed = 15f + System.Math.Min(level, 3);
            TurnRateDegPerSec = level == 1 ? 420f : level == 2 ? 450f : level == 3 ? 480f : 520f;
            BoostSpeed = 20f + level * 4f;
            BoostDuration = level == 1 ? .45f : level == 2 ? .5f : level == 3 ? .6f : .65f;
            BoostCooldown = level == 1 ? 4.2f : level == 2 ? 3.5f : level == 3 ? 2.8f : 2.2f;
        }

        public static PursuitDifficultyProfile ForTime(float seconds)
            => ForLevel(seconds >= 60f ? 4 : seconds >= 40f ? 3 : seconds >= 20f ? 2 : 1);

        public static PursuitDifficultyProfile ForLevel(int level)
        {
            switch (level)
            {
                case 1: return new PursuitDifficultyProfile(1, .06f, .08f, 0f, 0f, 0f, 1.4f);
                case 2: return new PursuitDifficultyProfile(2, .04f, .06f, .22f, .55f, 0f, .65f);
                case 3: return new PursuitDifficultyProfile(3, .04f, .06f, .4f, .9f, 2.2f, .6f);
                case 4: return new PursuitDifficultyProfile(4, .02f, .04f, .52f, 1.1f, 1.7f, .2f);
                default: throw new ArgumentOutOfRangeException(nameof(level));
            }
        }
    }
}
