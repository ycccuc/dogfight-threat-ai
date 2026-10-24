using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class MovingAttackSafetyTests
    {
        static readonly AircraftSpec Enemy = new AircraftSpec(2f, 9f, 18f, 210f, .1f, 8f, .35f);
        static readonly AircraftSpec Player = new AircraftSpec(2f, 9f, 18f, 240f, .1f, 8f, .35f);

        [Test]
        public void RealLevelThreeCornerStartRequiresEscapeAfterCoastingLeftDuringReaction()
        {
            // Actual AttackRun seed314219, attack2 at combat time45.98. A coasting left
            // turn changes the player's escape orientation without moving out of the corner.
            AssertUnsafeStartVetoed(
                new Vec2(-199.36206f, -144.6707f), new Vec2(.015273072f, -1.0729432f), -1.6159434f,
                new Vec2(-199.8f, -149.8f), 1.1689394f, 3f);
        }

        [Test]
        public void RealLevelThreeCornerStartRequiresEscapeAfterCoastingRightDuringReaction()
        {
            // Actual AttackRun seed314243, attack2 at combat time45.28; opposite turn/edge.
            AssertUnsafeStartVetoed(
                new Vec2(-199.36012f, 144.69365f), new Vec2(.0289215f, .35689783f), 7.939681f,
                new Vec2(-199.8f, 149.8f), -1.7325884f, 3f);
        }

        [Test]
        public void RealLevelTwoCornerStartRequiresEscapeAfterCoastingTurnDuringReaction()
        {
            // Actual AttackRun seed314231, attack7 at combat time25.44.
            AssertUnsafeStartVetoed(
                new Vec2(194.65804f, -149.36292f), new Vec2(.71720773f, -.0047971047f), -.03796675f,
                new Vec2(199.8f, -149.8f), 2.8597682f, 2.5f);
        }

        static void AssertUnsafeStartVetoed(Vec2 enemyPosition, Vec2 enemyVelocity, float enemyHeading,
            Vec2 playerPosition, float playerHeading, float warningSpeed)
        {
            var enemy = FlightState.At(enemyPosition, enemyHeading);
            enemy.Velocity = enemyVelocity;
            var player = FlightState.At(playerPosition, playerHeading);
            var gate = new AttackSafetyGate(Enemy, Player, Bounds2D.Centered(new Vec2(200f, 150f)));
            Assert.IsFalse(gate.CanStart(enemy, player, enemyHeading, warningSpeed),
                "The real moving-warning start must account for coasting/S plus turning during the full .30-second reaction delay.");
        }
    }
}
