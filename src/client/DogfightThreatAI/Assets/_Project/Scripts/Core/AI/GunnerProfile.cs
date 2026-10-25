using System;
namespace DogfightThreatAI.Core.AI
{
    /// <summary>难度由内容资产提供；不修改双方弹速、伤害或武器冷却。</summary>
    public readonly struct GunnerProfile
    {
        public readonly bool UseAStar;
        public readonly float ObservationInterval, Prediction, AimTolerance, FireDelay, CruiseSpeed, TurnRate, RetreatDistance, EngageDistance, ShotSpacing;
        public GunnerProfile(bool useAStar, float observationInterval, float prediction, float aimTolerance,
            float fireDelay, float cruiseSpeed, float turnRate, float retreatDistance, float engageDistance, float shotSpacing = .45f)
        {
            if (!Positive(observationInterval) || prediction < 0 || !Finite(prediction) || !Positive(aimTolerance) ||
                fireDelay < 0 || !Finite(fireDelay) || !Positive(cruiseSpeed) || !Positive(turnRate) ||
                !Positive(retreatDistance) || engageDistance <= retreatDistance || !Finite(engageDistance) || !Positive(shotSpacing))
                throw new ArgumentOutOfRangeException(nameof(observationInterval));
            UseAStar = useAStar; ObservationInterval = observationInterval; Prediction = prediction;
            AimTolerance = aimTolerance; FireDelay = fireDelay; CruiseSpeed = cruiseSpeed; TurnRate = turnRate;
            RetreatDistance = retreatDistance; EngageDistance = engageDistance;
            ShotSpacing = shotSpacing;
        }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Positive(float v) => v > 0 && Finite(v);
    }
}
