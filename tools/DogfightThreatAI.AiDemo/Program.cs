#nullable enable
using System.Globalization;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;

// The same Core as Unity: publish snapshots, read flight intention, step both
// aircraft, and pass the explicit attack window to the sole damage authority.
const float dt = 0.02f;
const int idleLimit = 6000;
const int comparisonSteps = 3750;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

try
{
    Console.WriteLine("Fixed aircraft: player max=9 turn=240; enemy max=9 turn=210. Warning=.80s, charge<=.90s.");
    foreach (float time in new[] { 0f, 20f, 40f, 60f })
    {
        var level = AiDifficultyProfile.ForTime(time);
        Console.WriteLine($"  LV{level.Level} from {time:0}s: observation delay={level.ObservationDelay:0.00}s, lead<={level.PredictionLead:0.00}s, cruise={level.CruiseSpeed:0.0}, miss recovery={level.RecoverySeconds:0.0}s");
    }
    RunScenario("idle player: five warned contacts", PlayerPolicy.Idle, requireFiveHits: true);
    RunScenario("fly/turn for 5s, then brake", PlayerPolicy.FlyThenStop, requireFiveHits: false);
    RunScenario("public warning: wait .30s, short turn, then straight flight", PlayerPolicy.ReactToWarning, requireFiveHits: false);
    Console.WriteLine("PASS: idle attack loop, explicit damage windows and deterministic replays. Moving players are not required to die.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"FAIL: {error.Message}");
    return 1;
}

void RunScenario(string name, PlayerPolicy policy, bool requireFiveHits)
{
    Console.WriteLine(name + ":");
    DemoRun first = Simulate(policy, requireFiveHits, printProgress: true);
    DemoRun replay = Simulate(policy, requireFiveHits, printProgress: false);
    if (first.Frames.Count != replay.Frames.Count)
        throw new InvalidOperationException($"{name}: replay ended on a different step.");
    for (int step = 0; step < first.Frames.Count; step++)
        if (first.Frames[step] != replay.Frames[step])
            throw new InvalidOperationException($"{name}: replay diverged at step {step}.");
    CombatStatus end = first.Frames[^1].Combat;
    Console.WriteLine($"  RESULT HP={end.PlayerHealth}/5 hits={end.HitSequence} warnings={first.Warnings} completed misses={first.Misses} maxLV={first.MaximumLevel} elapsed={end.ElapsedTime:0.00}s replay={first.Frames.Count}/{first.Frames.Count} identical");
}

DemoRun Simulate(PlayerPolicy policy, bool requireFiveHits, bool printProgress)
{
    var enemySpec = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
    var playerSpec = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);
    var arena = Bounds2D.Centered(new Vec2(200f, 150f));
    var enemyModel = FlightModel.From(enemySpec, arena);
    var playerModel = FlightModel.From(playerSpec, arena);
    var enemy = FlightState.At(new Vec2(4f, -2.5f), MathF.PI * .5f);
    var player = FlightState.At(Vec2.Zero, MathF.PI * .5f);
    var world = new DemoWorld();
    var combat = new ContactCombatModel();
    var source = new AttackRunInputSource(world, 2, enemySpec, combat, arena, playerSpec);
    var frames = new List<DemoFrame>();
    var successfulAttacks = new HashSet<int>();
    float previousHitTime = -10f, separationSinceHit = 0f;
    bool sawRecovery = false;
    int warnedId = 0, completedId = 0, misses = 0, warnings = 0, maximumLevel = 1;
    float warningTime = 0f;
    Vec2 dodgeDirection = Vec2.Zero;
    AttackRunPhase previousPhase = AttackRunPhase.NoTarget;
    int previousLevel = 0;
    int limit = requireFiveHits ? idleLimit : comparisonSteps;

    for (int step = 0; step < limit && combat.Status.IsPlayerAlive; step++)
    {
        world.Publish(enemy, player);
        FlightInput aiInput = source.ReadInput(dt);
        AssertLegal(aiInput, step);
        maximumLevel = Math.Max(maximumLevel, source.Level);
        sawRecovery |= source.Phase == AttackRunPhase.Recover;
        if (source.Phase == AttackRunPhase.Windup && source.AttackId != warnedId)
        {
            warnedId = source.AttackId;
            warnings++;
            warningTime = combat.Status.ElapsedTime;
            dodgeDirection = PublicEscapeDirection(source.LockedDirection, player, arena);
            if (printProgress)
                Console.WriteLine($"  WARNING #{warnedId}: LV{source.Level}/{source.Tactic} t={warningTime:0.00}s lock={source.LockedHeading * 180f / MathF.PI:0.0}deg countdown={source.PhaseRemaining:0.00}s pathPoints={source.PreviewPointCount}");
        }
        if (printProgress && (source.Level != previousLevel ||
            (source.Phase != previousPhase && (source.Phase == AttackRunPhase.Charge || source.Phase == AttackRunPhase.Recover))))
            Console.WriteLine($"  t={combat.Status.ElapsedTime:0.00}s LV{source.Level} {source.Tactic} {source.Phase} remaining={source.PhaseRemaining:0.00}s");
        previousLevel = source.Level;
        previousPhase = source.Phase;

        FlightInput playerInput = ChoosePlayerInput(policy, step, source.Phase,
            combat.Status.ElapsedTime - warningTime, warnedId > 0, dodgeDirection, player, playerSpec);
        AssertLegal(playerInput, step);
        var previousEnemy = enemy;
        var previousPlayer = player;
        enemyModel.Step(ref enemy, aiInput, dt);
        playerModel.Step(ref player, playerInput, dt);
        AssertFinite(enemy, step);
        AssertFinite(player, step);
        separationSinceHit = MathF.Max(separationSinceHit, Vec2.Distance(enemy.Position, player.Position));
        AttackWindow window = source.AttackWindow;
        if (combat.Step(previousPlayer, player, previousEnemy, enemy, dt, window))
        {
            var status = combat.Status;
            if (!window.IsActive || source.Phase != AttackRunPhase.Charge || window.AttackId != warnedId ||
                !successfulAttacks.Add(window.AttackId))
                throw new InvalidOperationException("Damage occurred without one new, publicly warned charge window.");
            if (status.ElapsedTime - previousHitTime < 1.98f)
                throw new InvalidOperationException($"Hit {status.HitSequence} ignored invulnerability.");
            if (status.HitSequence > 1 && (separationSinceHit < 3.5f || !sawRecovery))
                throw new InvalidOperationException($"Hit {status.HitSequence} skipped physical separation or recovery.");
            if (printProgress)
                Console.WriteLine($"  HIT #{window.AttackId}: t={status.ElapsedTime:0.00}s HP={status.PlayerHealth}/5 invulnerable={status.InvulnerabilityRemaining:0.00}s");
            previousHitTime = status.ElapsedTime;
            separationSinceHit = 0f;
            sawRecovery = false;
        }
        if (source.Phase == AttackRunPhase.Recover && source.AttackId > completedId)
        {
            completedId = source.AttackId;
            if (!successfulAttacks.Contains(completedId))
            {
                misses++;
                if (printProgress) Console.WriteLine($"  MISS #{completedId}: recovery remains harmless.");
            }
        }
        Vec2 previewEnd = source.PreviewPointCount > 0 ? source.GetPreviewPoint(source.PreviewPointCount - 1) : Vec2.Zero;
        frames.Add(new DemoFrame(enemy, player, aiInput, playerInput, combat.Status,
            source.Phase, source.Tactic, source.Level, source.AttackId, source.LockedHeading,
            source.PhaseRemaining, window, previewEnd));
    }

    if (requireFiveHits && (combat.Status.PlayerHealth != 0 || combat.Status.HitSequence != 5))
        throw new InvalidOperationException($"Idle battle failed to complete five attacks in {limit * dt:0}s; phase={source.Phase}, enemy={enemy.Position}, goal={source.ManeuverPoint}.");
    if (warnings == 0) throw new InvalidOperationException("The comparison never generated a public warning.");
    if (!combat.Status.IsPlayerAlive &&
        (!source.ReadInput(dt).Equals(new FlightInput(-1f, 0f)) || source.Phase != AttackRunPhase.NoTarget))
        throw new InvalidOperationException("Enemy did not brake after player death.");
    return new DemoRun(frames, warnings, misses, maximumLevel);
}

FlightInput ChoosePlayerInput(PlayerPolicy policy, int step, AttackRunPhase phase,
    float sinceWarning, bool sawWarning, Vec2 escape, FlightState player, AircraftSpec playerSpec)
{
    if (policy == PlayerPolicy.Idle) return FlightInput.Neutral;
    if (policy == PlayerPolicy.FlyThenStop)
        return step < 250 ? new FlightInput(1f, step >= 125 ? .6f : 0f) : new FlightInput(-1f, 0f);
    if (!sawWarning || sinceWarning < .30f || (phase != AttackRunPhase.Windup && phase != AttackRunPhase.Charge))
        return new FlightInput(-1f, 0f);
    float angle = MathF.Atan2(player.Forward.X * escape.Y - player.Forward.Y * escape.X,
        Vec2.Dot(player.Forward, escape));
    float steer = sinceWarning < .70f ? Math.Clamp(angle / (playerSpec.TurnRateDegPerSec * MathF.PI / 180f * dt), -1f, 1f) : 0f;
    return new FlightInput(1f, steer);
}

static Vec2 PublicEscapeDirection(Vec2 locked, FlightState player, Bounds2D arena)
{
    Vec2 normal = new Vec2(-locked.Y, locked.X);
    Vec2 preferred = Vec2.Dot(normal, player.Forward) >= 0f ? normal : normal * -1f;
    if (!arena.Contains(player.Position + preferred * 12f)) preferred *= -1f;
    return preferred;
}

static void AssertLegal(FlightInput input, int step)
{
    if (!float.IsFinite(input.Throttle) || !float.IsFinite(input.Steer) ||
        MathF.Abs(input.Throttle) > 1f || MathF.Abs(input.Steer) > 1f)
        throw new InvalidOperationException($"Invalid flight input at step {step}.");
}

static void AssertFinite(FlightState state, int step)
{
    if (!float.IsFinite(state.Position.X) || !float.IsFinite(state.Position.Y) ||
        !float.IsFinite(state.Velocity.X) || !float.IsFinite(state.Velocity.Y) || !float.IsFinite(state.Heading))
        throw new InvalidOperationException($"Invalid aircraft state at step {step}.");
}

enum PlayerPolicy { Idle, FlyThenStop, ReactToWarning }
readonly record struct DemoRun(List<DemoFrame> Frames, int Warnings, int Misses, int MaximumLevel);
readonly record struct DemoFrame(FlightState Enemy, FlightState Player, FlightInput EnemyInput, FlightInput PlayerInput,
    CombatStatus Combat, AttackRunPhase Phase, AttackTactic Tactic, int Level, int AttackId, float LockedHeading,
    float PhaseRemaining, AttackWindow Window, Vec2 PreviewEnd);

sealed class DemoWorld : IWorldView
{
    AircraftSnapshot _enemy, _player;
    public int AircraftCount => 2;
    public void Publish(FlightState enemy, FlightState player)
    {
        _enemy = new AircraftSnapshot(2, 2, enemy);
        _player = new AircraftSnapshot(1, 1, player, isPlayerControlled: true);
    }
    public AircraftSnapshot GetAircraft(int index) => index switch
    {
        0 => _enemy,
        1 => _player,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
