using System;

namespace DogfightThreatAI.Core.Input
{
    public enum FlightMotionMode
    {
        Ordinary,
        FiniteAcceleration,
        SpeedOverride
    }

    /// <summary>
    /// Explicit movement policy. Speeds are forward speeds after drag, except ordinary
    /// inertial flight, which retains its natural drag loss. Only FlightModel applies it.
    /// </summary>
    public readonly struct FlightMotionCommand
    {
        public FlightMotionMode Mode { get; }
        public float MaxSpeed { get; }
        public float Acceleration { get; }
        public float Deceleration { get; }
        public float TurnRateDegPerSec { get; }
        public float BelowCruiseAcceleration { get; }
        public float CruiseThreshold { get; }
        /// <summary>Authorized duration within the next step; infinity means the entire step.</summary>
        public float ActiveSeconds { get; }
        public float ExitSpeedLimit { get; }
        public float ExitTurnRateDegPerSec { get; }
        public float ExitDeceleration { get; }

        FlightMotionCommand(FlightMotionMode mode, float maxSpeed, float acceleration,
            float deceleration, float turnRateDegPerSec, float belowCruiseAcceleration,
            float cruiseThreshold, float activeSeconds, float exitSpeedLimit,
            float exitTurnRateDegPerSec, float exitDeceleration)
        {
            RequireNonnegativeFinite(maxSpeed, nameof(maxSpeed));
            RequireNonnegativeFinite(acceleration, nameof(acceleration));
            RequireNonnegativeFinite(deceleration, nameof(deceleration));
            RequireNonnegativeFinite(turnRateDegPerSec, nameof(turnRateDegPerSec));
            RequireNonnegativeFinite(belowCruiseAcceleration, nameof(belowCruiseAcceleration));
            RequireNonnegativeFinite(cruiseThreshold, nameof(cruiseThreshold));
            if (float.IsNaN(activeSeconds) || activeSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(activeSeconds));
            RequireNonnegativeFinite(exitSpeedLimit, nameof(exitSpeedLimit));
            RequireNonnegativeFinite(exitTurnRateDegPerSec, nameof(exitTurnRateDegPerSec));
            RequireNonnegativeFinite(exitDeceleration, nameof(exitDeceleration));

            Mode = mode;
            MaxSpeed = maxSpeed;
            Acceleration = acceleration;
            Deceleration = deceleration;
            TurnRateDegPerSec = turnRateDegPerSec;
            BelowCruiseAcceleration = belowCruiseAcceleration;
            CruiseThreshold = cruiseThreshold;
            ActiveSeconds = activeSeconds;
            ExitSpeedLimit = exitSpeedLimit;
            ExitTurnRateDegPerSec = exitTurnRateDegPerSec;
            ExitDeceleration = exitDeceleration;
        }

        public static FlightMotionCommand Ordinary(float maxSpeed = 9f,
            float turnRateDegPerSec = 240f, float acceleration = 18f, float deceleration = 18f)
            => new FlightMotionCommand(FlightMotionMode.Ordinary, maxSpeed, acceleration,
                deceleration, turnRateDegPerSec, acceleration, maxSpeed,
                float.PositiveInfinity, maxSpeed, turnRateDegPerSec, deceleration);

        public static FlightMotionCommand FiniteAcceleration(float maxSpeed, float acceleration,
            float deceleration, float turnRateDegPerSec, float belowCruiseAcceleration = 18f,
            float cruiseThreshold = 9f)
            => new FlightMotionCommand(FlightMotionMode.FiniteAcceleration, maxSpeed, acceleration,
                deceleration, turnRateDegPerSec, belowCruiseAcceleration, cruiseThreshold,
                float.PositiveInfinity, maxSpeed, turnRateDegPerSec, deceleration);

        /// <summary>Powered heading-aligned movement for a paid dash, aiming cruise or locked charge.</summary>
        public static FlightMotionCommand SpeedOverride(float speed, float turnRateDegPerSec = 240f)
            => new FlightMotionCommand(FlightMotionMode.SpeedOverride, speed, 18f, 30f,
                turnRateDegPerSec, 18f, 9f, float.PositiveInfinity, 9f, turnRateDegPerSec, 30f);

        /// <summary>
        /// Apply this policy only for the paid/remaining duration, then use finite exit
        /// movement for the rest of the step. A zero duration executes only the exit.
        /// </summary>
        public FlightMotionCommand WithActiveSeconds(float activeSeconds, float exitSpeedLimit = 9f,
            float exitTurnRateDegPerSec = 240f, float exitDeceleration = 30f)
            => new FlightMotionCommand(Mode, MaxSpeed, Acceleration, Deceleration,
                TurnRateDegPerSec, BelowCruiseAcceleration, CruiseThreshold,
                activeSeconds, exitSpeedLimit, exitTurnRateDegPerSec, exitDeceleration);

        static void RequireNonnegativeFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
