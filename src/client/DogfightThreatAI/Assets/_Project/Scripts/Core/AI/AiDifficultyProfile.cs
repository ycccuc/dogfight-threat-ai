namespace DogfightThreatAI.Core.AI
{
    public readonly struct AiDifficultyProfile
    {
        public readonly int Level;
        public readonly float ObservationDelay, PredictionLead, CruiseSpeed, RecoverySeconds;
        public float WindupSpeed => 1.5f + .5f * Level;

        AiDifficultyProfile(int level, float delay, float lead, float cruise, float recovery)
        {
            Level = level;
            ObservationDelay = delay;
            PredictionLead = lead;
            CruiseSpeed = cruise;
            RecoverySeconds = recovery;
        }

        /// <summary>只改变观察、策略与节奏，不修改飞机规格或预警时长。</summary>
        public static AiDifficultyProfile ForTime(float seconds)
        {
            if (seconds >= 60f) return new AiDifficultyProfile(4, .15f, .45f, 8f, 1.2f);
            if (seconds >= 40f) return new AiDifficultyProfile(3, .2f, .4f, 8f, 1.4f);
            if (seconds >= 20f) return new AiDifficultyProfile(2, .25f, .3f, 7.8f, 1.6f);
            return new AiDifficultyProfile(1, .35f, 0f, 7.5f, 1.8f);
        }
    }
}
