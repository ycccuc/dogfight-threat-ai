using System;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class WindupCounterWindowTests
    {
        const float ReactionDelay = .4f;
        const float SafeCenterDistance = 1.15f;
        static readonly AircraftSpec PlayerSpec = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);
        static readonly AircraftSpec EnemySpec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);

        [TestCase(135f, .01f)]
        [TestCase(135f, .02f)]
        [TestCase(225f, .01f)]
        [TestCase(225f, .02f)]
        public void NearObliqueWarningKeepsBodyClearanceUntilTheActualCharge(float heading, float dt)
        {
            // These were the production-window failures: .01 collided around .45 seconds,
            // while .02 passed within .005 of the contact boundary before Charge.
            Outcome result = Simulate(heading, dt, new Response(), true);
            Assert.That(result.LaunchedCharge, Is.True,
                "A normal visible start must complete its .8-second warning, rather than hit with its body during Windup.");
            Assert.That(result.ChargeAt - result.WarningAt, Is.EqualTo(.8f).Within(.0001f));
            Assert.That(result.WindupContact, Is.False);
            Assert.That(result.MinimumWindupDistance, Is.GreaterThanOrEqualTo(SafeCenterDistance),
                "The moving warning needs geometric margin, not a timestep-sensitive grazing pass.");
        }

        [TestCase(135f)]
        [TestCase(225f)]
        public void NearObliqueWarningHasTheSameTurnAndPaidDashCountersAtBothFixedSteps(float heading)
        {
            var turns = new[]
            {
                new Response(1f, 90f), new Response(-1f, 90f),
                new Response(1f, 180f), new Response(-1f, 180f)
            };
            var dashes = new[]
            {
                new Response(0f, 0f, 0f),
                new Response(1f, 45f, 0f), new Response(-1f, 45f, 0f),
                new Response(1f, 90f, .2f), new Response(-1f, 90f, .2f)
            };
            bool stableTurn = false, stableDash = false;
            foreach (Response response in turns)
            {
                Outcome a = Simulate(heading, .01f, response);
                Outcome b = Simulate(heading, .02f, response);
                stableTurn |= SafeResponse(a) && SafeResponse(b);
            }
            foreach (Response response in dashes)
            {
                Outcome a = Simulate(heading, .01f, response);
                Outcome b = Simulate(heading, .02f, response);
                Assert.That(a.EnergySpent, Is.EqualTo(a.Dashes * 25f).Within(.001f));
                Assert.That(b.EnergySpent, Is.EqualTo(b.Dashes * 25f).Within(.001f));
                stableDash |= a.Dashes == 1 && b.Dashes == 1 && SafeResponse(a) && SafeResponse(b);
            }
            Assert.That(stableTurn, Is.True,
                "At least one identical pure-turn response after .4 seconds must safely complete the first attack at both fixed steps.");
            Assert.That(stableDash, Is.True,
                "At least one identical response with a real paid Space dash must safely complete the first attack at both fixed steps.");
        }

        static bool SafeResponse(Outcome outcome) => outcome.LaunchedCharge && outcome.Recovered &&
            outcome.Hits == 0 && outcome.MinimumDistance >= SafeCenterDistance;

        static Outcome Simulate(float playerHeadingDegrees, float dt, Response response, bool stopAtCharge = false)
        {
            var world = new VisibleWorld
            {
                Enemy = new FlightState { Velocity = new Vec2(PursuitDifficultyProfile.ForLevel(3).CruiseSpeed, 0f) },
                Player = FlightState.At(new Vec2(7.5f, 0f), playerHeadingDegrees * MathF.PI / 180f)
            };
            world.Player.Velocity = world.Player.Forward * 9f;
            var combat = new ContactCombatModel(5, 1, .7f, 1f);
            var ai = new PursuitChargeInputSource(world, 2, EnemySpec, combat, null, 3);
            var playerFlight = FlightModel.From(PlayerSpec);
            var enemyFlight = FlightModel.From(EnemySpec);
            var boost = new PlayerBoostModel();
            var result = new Outcome();
            bool dashEdgeSent = false;
            for (int tick = 0; tick * dt < 2.5f && combat.Status.IsPlayerAlive; tick++)
            {
                float now = combat.Status.ElapsedTime;
                world.Now = now;
                Vec2 relative = world.Enemy.Position - world.Player.Position;
                ai.CanStartVisibleAttack = MathF.Abs(relative.X) <= 8.15f && MathF.Abs(relative.Y) <= 4.25f;
                FlightInput enemyInput = ai.ReadInput(dt);
                if (ai.Phase == AttackRunPhase.Windup && result.WarningAt < 0f) result.WarningAt = now;
                if (ai.Phase == AttackRunPhase.Charge && !result.LaunchedCharge)
                {
                    result.LaunchedCharge = true;
                    result.ChargeAt = now;
                    if (stopAtCharge) break;
                }
                if (result.WarningAt >= 0f && ai.Phase == AttackRunPhase.Recover)
                {
                    result.Recovered = true;
                    break;
                }
                float afterReaction = result.WarningAt < 0f ? -1f : now - result.WarningAt - ReactionDelay;
                float steer = afterReaction >= -.00001f && afterReaction < response.TurnDegrees / 240f - .00001f
                    ? response.Turn : 0f;
                bool dash = response.DashDelay.HasValue && !dashEdgeSent && afterReaction >= response.DashDelay.Value - .00001f;
                if (dash) dashEdgeSent = true;
                float energyBefore = boost.Energy;
                boost.Step(dash, false, dt, Vec2.Dot(world.Player.Velocity, world.Player.Forward), true, false);
                result.EnergySpent += MathF.Max(0f, energyBefore - boost.Energy);
                if (dash && boost.MotionCommand.Mode == FlightMotionMode.SpeedOverride && boost.PoweredSecondsThisStep > 0f)
                    result.Dashes++;
                FlightState previousEnemy = world.Enemy, previousPlayer = world.Player;
                enemyFlight.Step(ref world.Enemy, enemyInput, dt, ai.MotionCommand);
                playerFlight.Step(ref world.Player, new FlightInput(1f, steer), dt, boost.MotionCommand);
                world.Cue = boost.MotionCommand.Mode == FlightMotionMode.SpeedOverride && boost.PoweredSecondsThisStep > 0f
                    ? VisiblePropulsionCue.Dash : VisiblePropulsionCue.None;
                float closest = SweptDistance(previousPlayer.Position, world.Player.Position, previousEnemy.Position, world.Enemy.Position);
                result.MinimumDistance = MathF.Min(result.MinimumDistance, closest);
                if (ai.Phase == AttackRunPhase.Windup)
                    result.MinimumWindupDistance = MathF.Min(result.MinimumWindupDistance, closest);
                bool hit = combat.StepContact(previousPlayer, world.Player, previousEnemy, world.Enemy, dt, 1f);
                if (hit && ai.Phase == AttackRunPhase.Windup) result.WindupContact = true;
            }
            result.Hits = combat.Status.HitSequence;
            return result;
        }

        static float SweptDistance(Vec2 previousPlayer, Vec2 player, Vec2 previousEnemy, Vec2 enemy)
        {
            Vec2 start = previousEnemy - previousPlayer;
            Vec2 delta = (enemy - player) - start;
            float fraction = delta.SqrMagnitude > 1e-12f ? MathF.Max(0f, MathF.Min(1f, -Vec2.Dot(start, delta) / delta.SqrMagnitude)) : 0f;
            return (start + delta * fraction).Magnitude;
        }

        readonly struct Response
        {
            public readonly float Turn, TurnDegrees;
            public readonly float? DashDelay;
            public Response(float turn, float turnDegrees, float? dashDelay = null)
            { Turn = turn; TurnDegrees = turnDegrees; DashDelay = dashDelay; }
        }

        sealed class Outcome
        {
            public bool LaunchedCharge, Recovered, WindupContact;
            public float WarningAt = -1f, ChargeAt = -1f;
            public float MinimumDistance = float.MaxValue, MinimumWindupDistance = float.MaxValue, EnergySpent;
            public int Hits, Dashes;
        }

        sealed class VisibleWorld : IWorldView
        {
            public FlightState Player, Enemy;
            public float Now;
            public VisiblePropulsionCue Cue;
            public int AircraftCount => 2;
            public AircraftSnapshot GetAircraft(int index) => index == 0
                ? new AircraftSnapshot(2, 2, Enemy)
                : new AircraftSnapshot(1, 1, Player, true, true, Cue, true, Now);
        }
    }
}
