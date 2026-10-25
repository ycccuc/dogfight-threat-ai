using System;
using System.Linq;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class ProjectileCombatTests
    {
        static WeaponSpec Weapon(float interval = .45f, float speed = 40, float lifetime = 1.5f)
            => new WeaponSpec("cannon", interval, speed, lifetime, .09f, 1);
        static void Tick(ProjectileCombatSimulation simulation, FlightState player, FlightState enemy,
            bool playerFire = false, bool enemyFire = false, float dt = .02f)
            => simulation.Tick(dt, player, player, enemy, enemy, .5f, .5f, playerFire, enemyFire);

        [Test] public void PlayerAndEnemyFireFromTheirOwnNosesAndUseSameSpeed()
        {
            var s = new ProjectileCombatSimulation(Weapon(), 5, .7f);
            Tick(s, FlightState.At(Vec2.Zero), FlightState.At(new Vec2(10, 0), MathF.PI), true, true);
            Assert.That(s.Projectiles.Count, Is.EqualTo(2));
            Assert.That(s.Projectiles[0].Position.X, Is.EqualTo(1));
            Assert.That(s.Projectiles[1].Position.X, Is.EqualTo(9).Within(.001f));
            Assert.That(s.Projectiles.All(p => MathF.Abs(p.Velocity.Magnitude - 40) < .001f), Is.True);
        }
        [Test] public void HoldingFireRespectsSharedCooldown()
        {
            var s = new ProjectileCombatSimulation(Weapon(), 5, .7f);
            int playerShots = 0, enemyShots = 0;
            for (int i = 0; i < 100; i++)
            {
                Tick(s, FlightState.At(Vec2.Zero), FlightState.At(new Vec2(1000, 1000)), true, true);
                playerShots += s.Events.Count(e => e.Kind == WeaponEventKind.Fired && e.AircraftId == 1);
                enemyShots += s.Events.Count(e => e.Kind == WeaponEventKind.Fired && e.AircraftId == 2);
            }
            Assert.That(playerShots, Is.EqualTo(5)); Assert.That(enemyShots, Is.EqualTo(playerShots));
        }
        [Test] public void AircraftContactDoesNotCauseDamage()
        {
            var s = new ProjectileCombatSimulation(Weapon(), 5, .7f);
            for (int i = 0; i < 100; i++) Tick(s, FlightState.At(Vec2.Zero), FlightState.At(Vec2.Zero));
            Assert.That(s.Status.PlayerHealth, Is.EqualTo(5)); Assert.That(s.EnemyHealth, Is.EqualTo(5));
        }
        [Test] public void HighSpeedRoundSweepsThroughTargetAndRemovesExactlyOneSegment()
        {
            var s = new ProjectileCombatSimulation(Weapon(speed: 1000), 5, .7f);
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(10, 0));
            Tick(s, p, e, true); Tick(s, p, e);
            Assert.That(s.EnemyHealth, Is.EqualTo(4)); Assert.That(s.Projectiles, Is.Empty);
            Assert.That(s.Events.Count(ev => ev.Kind == WeaponEventKind.Hit), Is.EqualTo(1));
        }
        [Test] public void SweptRoundHitsAnAircraftCrossingItsPath()
        {
            var s = new ProjectileCombatSimulation(Weapon(), 5, .7f);
            var p = FlightState.At(Vec2.Zero); var previous = FlightState.At(new Vec2(11, -10));
            Tick(s, p, previous, true);
            s.Tick(.5f, p, p, previous, FlightState.At(new Vec2(11, 10)), .5f, .5f, false, false);
            Assert.That(s.EnemyHealth, Is.EqualTo(4));
        }
        [Test] public void ProtectionAbsorbsRoundsWithoutStoringDamage()
        {
            var s = new ProjectileCombatSimulation(Weapon(.05f), 5, .7f);
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(3, 0), MathF.PI);
            for (int i = 0; i < 25; i++) Tick(s, p, e, false, true);
            Assert.That(s.Status.PlayerHealth, Is.EqualTo(4)); Assert.That(s.Status.HitSequence, Is.EqualTo(1));
        }
        [Test] public void ExpiredRoundCannotHitBeyondItsLifetime()
        {
            var s = new ProjectileCombatSimulation(Weapon(lifetime: .1f), 5, .7f);
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(20, 0));
            Tick(s, p, e, true); Tick(s, p, e, dt: 1);
            Assert.That(s.EnemyHealth, Is.EqualTo(5)); Assert.That(s.Projectiles, Is.Empty);
        }
        [Test] public void FiveHitsFinishBattleAndPreventFurtherFire()
        {
            var s = new ProjectileCombatSimulation(Weapon(.05f), 5, 0);
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(3, 0));
            for (int i = 0; i < 100; i++) Tick(s, p, e, true);
            Assert.That(s.EnemyHealth, Is.Zero); Assert.That(s.IsFinished, Is.True);
            Assert.That(s.Projectiles, Is.Empty);
            Tick(s, p, e, true, true); Assert.That(s.Events, Is.Empty);
        }
        [Test] public void RoundCannotHitItsOwner()
        {
            var s = new ProjectileCombatSimulation(Weapon(), 5, .7f);
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(1000, 1000));
            Tick(s, p, e, true); Tick(s, p, e);
            Assert.That(s.Status.PlayerHealth, Is.EqualTo(5));
        }
        [Test] public void InvalidTimeAndUninitializedWeaponAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new ProjectileCombatSimulation(default, 5, .7f));
            var s = new ProjectileCombatSimulation(Weapon(), 5, .7f);
            Assert.Throws<ArgumentOutOfRangeException>(() => Tick(s, default, default, dt: float.NaN));
        }
    }
}
