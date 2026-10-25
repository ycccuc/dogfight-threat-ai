using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;

namespace DogfightThreatAI.Core.AI
{
    /// <summary>True state can only veto an unsafe attack; it never supplies tactics with a corrected aim.</summary>
    public sealed class AttackSafetyGate
    {
        const float Dt = 0.02f;
        const int ReactionTicks = 15;
        const int ResponseCount = 30;
        static readonly int[] TurnTicks = { 6, 10, 15, 20, 25, 35 };
        readonly FlightModel _player;
        readonly AttackTrajectory _motion;
        readonly Vec2[] _trajectory = new Vec2[90];
        public AttackSafetyGate(in AircraftSpec enemy, in AircraftSpec player, Bounds2D? arena)
        {
            _motion = new AttackTrajectory(enemy, arena);
            _player = FlightModel.From(player, arena);
        }
        public bool CanStart(in FlightState enemy, in FlightState player, float lockedHeading, float warningSpeed = 0f)
        {
            int pointCount = _motion.Build(enemy, lockedHeading, AttackRunInputSource.WarningSeconds,
                AttackRunInputSource.ChargeSeconds, warningSpeed, _trajectory);
            // No key reads: model possible continuations during a 0.30-second human reaction delay.
            for (int continuation = 0; continuation < 9; continuation++)
            {
                bool escape = false;
                for (int response = 0; response < ResponseCount && !escape; response++)
                {
                    FlightState simulated = player;
                    bool hit = false;
                    for (int i = 1; i < pointCount; i++)
                    {
                        FlightState previous = simulated;
                        FlightInput input = i <= ReactionTicks ? Continuation(continuation) : Response(response, i - ReactionTicks - 1);
                        _player.Step(ref simulated, input, Dt);
                        if (i > 40 && SweptContact(previous.Position, simulated.Position,
                            _trajectory[i - 1], _trajectory[i])) { hit = true; break; }
                    }
                    escape = !hit;
                }
                if (!escape) return false;
            }
            return true;
        }
        static FlightInput Continuation(int index)
            => index == 0 ? FlightInput.Neutral : index == 1 ? new FlightInput(1f, 0f) :
               index == 2 ? new FlightInput(1f, 1f) : index == 3 ? new FlightInput(1f, -1f) :
               index == 4 ? new FlightInput(-1f, 0f) : index == 5 ? new FlightInput(0f, 1f) :
               index == 6 ? new FlightInput(0f, -1f) : new FlightInput(-1f, index == 7 ? 1f : -1f);
        static FlightInput Response(int index, int ticksSinceReaction)
        {
            if (index == 0) return new FlightInput(1f, 0f);
            if (index == 1) return new FlightInput(-1f, 0f);
            if (index < 6)
                return new FlightInput(index < 4 ? 1f : -1f, (index & 1) == 0 ? 1f : -1f);

            // A player can release A/D after a short turn. Requiring continuous full circles
            // incorrectly brings an otherwise successful dodge back into the attack line.
            bool brakeFirst = index >= 18;
            int pattern = index - (brakeFirst ? 18 : 6);
            int turnTicks = TurnTicks[pattern / 2];
            if (ticksSinceReaction >= turnTicks) return new FlightInput(1f, 0f);
            return new FlightInput(brakeFirst ? -1f : 1f, (pattern & 1) == 0 ? 1f : -1f);
        }
        internal static bool SweptContact(Vec2 playerBefore, Vec2 playerAfter, Vec2 enemyBefore, Vec2 enemyAfter)
        {
            double x = (double)playerBefore.X - enemyBefore.X, y = (double)playerBefore.Y - enemyBefore.Y;
            double dx = ((double)playerAfter.X - enemyAfter.X) - x;
            double dy = ((double)playerAfter.Y - enemyAfter.Y) - y;
            double denominator = dx * dx + dy * dy;
            double t = denominator > 1e-12d ? System.Math.Max(0d, System.Math.Min(1d, -(x * dx + y * dy) / denominator)) : 0d;
            return (x + dx * t) * (x + dx * t) + (y + dy * t) * (y + dy * t) <= 1.0001d;
        }
    }
}
