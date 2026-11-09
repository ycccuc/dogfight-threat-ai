using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests
{
    public sealed class AirCombatRoundTests
    {
        static readonly WeaponSpec Cannon = new WeaponSpec("cannon", .4f, 10f, 2f, .1f, .25f);

        static AircraftCombatFrame Plane(float x, float y, bool fire = false)
        {
            var state = FlightState.At(new Vec2(x, y));
            return new AircraftCombatFrame(state, state, .25f, fire);
        }

        [Test]
        public void FastShotHitsNearestOpponentEvenIfTheyHaveHigherSeatNumber()
        {
            var round = new AirCombatRound(Cannon, 3, 1, 0);
            var frames = new[] { Plane(0, 0, true), Plane(4, 0), Plane(2, 0) };

            round.Tick(.1f, frames);
            int id = round.Fired[0].Id;
            frames[0] = Plane(0, 0);
            round.Tick(.5f, frames);

            Assert.That(round.Health(1), Is.EqualTo(1));
            Assert.That(round.Health(2), Is.EqualTo(1));
            Assert.That(round.Health(3), Is.Zero);
            Assert.That(round.AliveCount, Is.EqualTo(2));
            Assert.That(round.Removed, Is.EqualTo(new[] { id }));
        }

        [Test]
        public void AlliedAircraftDoNotBlockRounds()
        {
            var round = new AirCombatRound(Cannon, 3, 1, 0f,
                new[] { 1, 1, 1 }, new[] { 0, 0, 1 });
            var frames = new[] { Plane(0, 0, true), Plane(2, 0), Plane(4, 0) };

            round.Tick(.1f, frames);
            frames[0] = Plane(0, 0);
            round.Tick(.5f, frames);

            Assert.That(round.Health(2), Is.EqualTo(1));
            Assert.That(round.Health(3), Is.Zero);
        }

        [Test]
        public void ServerCooldownAndHitProtectionApplyAcrossMultipleFireInputs()
        {
            var round = new AirCombatRound(Cannon, 2, 3, 1f);
            var frames = new[] { Plane(0, 0, true), Plane(1, 0) };

            round.Tick(.1f, frames);
            round.Tick(.1f, frames);
            Assert.That(round.Fired, Is.Empty);
            Assert.That(round.Health(2), Is.EqualTo(2));

            round.Tick(.3f, frames);
            Assert.That(round.Fired.Count, Is.EqualTo(1));
            round.Tick(.1f, frames);
            Assert.That(round.Health(2), Is.EqualTo(2));
            Assert.That(round.Removed.Count, Is.EqualTo(1));
        }

        [Test]
        public void AircraftContactIsSweptAndChargesBothOnceUntilSeparated()
        {
            var round = new AirCombatRound(Cannon, 2, 4, .75f, null, new[] { 0, 1 }, aircraftCollisions: true);
            var before = FlightState.At(new Vec2(-3, 0));
            var after = FlightState.At(new Vec2(3, 0));
            var stationary = Plane(0, 0);
            var frames = new[] { new AircraftCombatFrame(before, after, .25f, false), stationary };
            round.Tick(.02f, frames);
            Assert.That(round.Collisions.Count, Is.EqualTo(1));
            Assert.That(round.Health(1), Is.EqualTo(3));
            Assert.That(round.Health(2), Is.EqualTo(3));
            Assert.That(round.Invulnerability(1), Is.EqualTo(round.Invulnerability(2)));

            frames[0] = Plane(0, 0);
            round.Tick(.02f, frames);
            round.Tick(1f, frames);
            Assert.That(round.Collisions, Is.Empty);
            Assert.That(round.Health(1), Is.EqualTo(3));
            frames[0] = Plane(-3, 0);
            round.Tick(.02f, frames);
            frames[0] = new AircraftCombatFrame(before, after, .25f, false);
            round.Tick(.02f, frames);
            Assert.That(round.Collisions.Count, Is.EqualTo(1));
            Assert.That(round.Health(1), Is.EqualTo(2));
            Assert.That(round.Health(2), Is.EqualTo(2));
        }

        [Test]
        public void ForfeitIsIdempotentAndEliminatedSeatsCannotFire()
        {
            var round = new AirCombatRound(Cannon, 3, 3, 0f);
            var frames = new[] { Plane(0, 0), Plane(2, 0, true), Plane(4, 0) };

            round.Forfeit(2);
            round.Forfeit(2);
            round.Tick(.1f, frames);
            Assert.That(round.Fired, Is.Empty);
            Assert.That(round.AliveCount, Is.EqualTo(2));
            Assert.That(round.WinnerByHealth(), Is.Zero);

            round.Forfeit(1);
            Assert.That(round.WinnerByHealth(), Is.EqualTo(3));
            round.Forfeit(3);
            Assert.That(round.WinnerByHealth(), Is.Zero);
        }
    }
}
