using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Config;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using NUnit.Framework;

namespace DogfightThreatAI.Tests
{
    public sealed class PatrolMissionTests
    {
        static readonly AircraftSpec Aircraft = new AircraftSpec(0f, 9f, 9f, 210f, 0f, 8f, 8f);
        static readonly WeaponSpec Cannon = new WeaponSpec("cannon", .45f, 46f, 1.5f, .09f, .85f);
        static readonly Bounds2D Arena = new Bounds2D(new Vec2(-200, -150), new Vec2(200, 150));

        [Test]
        public void SquadTargetsAccumulateSeparateVisualEvidence()
        {
            var pilot = new PatrolPilot(Arena, Vec2.Zero, new[] { Vec2.Zero, new Vec2(7, 0) }, 1,
                Aircraft, Cannon, 27, 57, players: 2);
            var self = FlightState.At(Vec2.Zero);
            var visible = FlightState.At(new Vec2(8, 0));
            var hidden = FlightState.At(new Vec2(-8, 0));
            pilot.TickTeam(self, new FlightState?[] { visible, hidden }, 2, false, .35f);
            pilot.TickTeam(self, new FlightState?[] { hidden, visible }, 2, false, .1f);
            Assert.That(pilot.ContactFor(1), Is.EqualTo(PatrolContact.Suspicious));
            Assert.That(pilot.WantsFire, Is.False);
            pilot.TickTeam(self, new FlightState?[] { null, visible }, 2, false, .5f);
            Assert.That(pilot.ContactFor(1), Is.EqualTo(PatrolContact.Confirmed));
            Assert.That(pilot.WantsFire, Is.True);
        }

        [Test]
        public void SquadSharesRecordAndCanFinishAfterLosingOnePlayer()
        {
            var mission = new PatrolMission(Arena, Cannon, Aircraft,
                new[] { new PatrolSector(new Vec2(0, 30), new Vec2(6, 6), 2) },
                5, 2, 27, 57, new[] { Vec2.Zero }, playerCount: 2);
            var previous = FlightState.At(new Vec2(-3, 0));
            var current = FlightState.At(new Vec2(3, 0));
            var pass = new AircraftCombatFrame(previous, current, .42f, false);
            var frames = new[] { pass, pass };
            mission.TickTeam(.02f, frames);
            Assert.That(mission.CollectedCount, Is.EqualTo(1));
            Assert.That(mission.CollectedBy(0), Is.EqualTo(1));
            Assert.That(mission.Combat.Collisions, Is.Empty);
            mission.Combat.Forfeit(1);
            mission.TickTeam(.02f, frames);
            Assert.That(mission.Outcome, Is.EqualTo(MissionOutcome.Active));
            Assert.That(mission.PlayersRemaining, Is.EqualTo(1));
            for (int i = 1; i <= mission.EnemyCount; i++)
            {
                mission.Combat.Forfeit(mission.PlayerCount + i);
            }
            mission.TickTeam(.02f, frames);
            Assert.That(mission.Outcome, Is.EqualTo(MissionOutcome.Victory));
        }

        [Test]
        public void PickupRemembersWhichSquadMemberCrossedItsRadius()
        {
            var mission = new PatrolMission(Arena, Cannon, Aircraft,
                new[] { new PatrolSector(new Vec2(0, 30), new Vec2(6, 6), 2) },
                5, 2, 27, 57, new[] { Vec2.Zero }, playerCount: 2);
            var far = FlightState.At(new Vec2(0, -20));
            var before = FlightState.At(new Vec2(-3, 0));
            var after = FlightState.At(new Vec2(3, 0));
            var crossing = new AircraftCombatFrame(before, after, .42f, false);
            mission.TickTeam(.02f, new[] { new AircraftCombatFrame(far, far, .42f, false), crossing });
            mission.TickTeam(.02f, new[] { crossing, crossing });
            Assert.That(mission.CollectedBy(0), Is.EqualTo(2));
            Assert.That(mission.CollectedCount, Is.EqualTo(1));
        }

        [Test]
        public void PilotPursuesLastHeadingBeforeSearchingWithoutBlindFire()
        {
            var pilot = new PatrolPilot(Arena, Vec2.Zero, new[] { Vec2.Zero, new Vec2(7, 0) }, 1,
                Aircraft, Cannon, 27, 57);
            var self = FlightState.At(Vec2.Zero);
            var last = FlightState.At(new Vec2(8, 0));
            last.Velocity = Vec2.Right * 6f;
            pilot.Tick(self, last, 2, false, .65f);
            Assert.That(pilot.Perception.Contact, Is.EqualTo(PatrolContact.Confirmed));

            var hidden = FlightState.At(new Vec2(-9, 5));
            var input = pilot.Tick(self, hidden, 2, false, .1f);
            Assert.That(pilot.Behavior, Is.EqualTo(PatrolBehavior.Chase));
            Assert.That(input.Steer, Is.EqualTo(0f).Within(.001f));
            Assert.That(pilot.WantsFire, Is.False);
            pilot.Tick(self, hidden, 2, false, 2.6f);
            Assert.That(pilot.Behavior, Is.EqualTo(PatrolBehavior.Search));
            Assert.That(pilot.Perception.LastSeen.Position, Is.EqualTo(last.Position));
            pilot.Tick(self, hidden, 2, false, 9f);
            Assert.That(pilot.Behavior, Is.EqualTo(PatrolBehavior.Patrol));
        }

        [Test]
        public void CrossingARecordCollectsItOnceAndDoesNotEndTheMission()
        {
            var mission = new PatrolMission(Arena, Cannon, Aircraft,
                new[] { new PatrolSector(new Vec2(0, 30), new Vec2(6, 6), 2) },
                5, 2, 27, 57, new[] { Vec2.Zero });
            var previous = FlightState.At(new Vec2(-3, 0));
            var current = FlightState.At(new Vec2(3, 0));
            var pass = new AircraftCombatFrame(previous, current, .42f, false);
            mission.Tick(.02f, pass);
            mission.Tick(.02f, pass);
            Assert.That(mission.IsCollected(0), Is.True);
            Assert.That(mission.CollectedCount, Is.EqualTo(1));
            Assert.That(mission.Outcome, Is.EqualTo(MissionOutcome.Active));
        }

        [Test]
        public void ContactsRequireVisionAndSearchOnlyTheLastConfirmedPosition()
        {
            var sensor = new PatrolPerception(27f, 57f, 8f);
            var aircraft = FlightState.At(Vec2.Zero);
            var behind = FlightState.At(new Vec2(-3f, 0f));
            sensor.Observe(aircraft, behind, 2f);
            Assert.That(sensor.Contact, Is.EqualTo(PatrolContact.None));

            sensor.Hear(behind.Position);
            sensor.Observe(aircraft, behind, .1f);
            Assert.That(sensor.Contact, Is.EqualTo(PatrolContact.Suspicious));
            Assert.That(sensor.VisualEvidence, Is.Zero);

            var ahead = FlightState.At(new Vec2(9f, 0f));
            sensor.Observe(aircraft, ahead, .05f);
            Assert.That(sensor.Contact, Is.EqualTo(PatrolContact.Suspicious));
            sensor.Observe(aircraft, ahead, .6f);
            Assert.That(sensor.Contact, Is.EqualTo(PatrolContact.Confirmed));
            sensor.Observe(aircraft, behind, .1f);
            Assert.That(sensor.Contact, Is.EqualTo(PatrolContact.Lost));
            Assert.That(sensor.LastSeen.Position, Is.EqualTo(ahead.Position));
            sensor.Observe(aircraft, behind, 8f);
            Assert.That(sensor.Contact, Is.EqualTo(PatrolContact.None));
        }

        [Test]
        public void CannonEliminationsClearEachSectorAndFinishTheMission()
        {
            var sectors = new[]
            {
                new PatrolSector(new Vec2(0f, 30f), new Vec2(11f, 9f), 2),
                new PatrolSector(new Vec2(-43f, 64f), new Vec2(13f, 10f), 2),
                new PatrolSector(new Vec2(48f, 84f), new Vec2(14f, 11f), 2)
            };
            var mission = new PatrolMission(Arena, Cannon, Aircraft, sectors, 5, 2, 27f, 57f);
            var far = FlightState.At(new Vec2(0, -50));
            for (int i = 0; i < 300; i++)
            {
                mission.Tick(.02f, new AircraftCombatFrame(far, far, .42f, false));
            }
            Assert.That(mission.EnemiesRemaining, Is.EqualTo(6));
            for (int i = 0; i < mission.EnemyCount; i++)
            {
                Assert.That(mission.EnemyBehavior(i), Is.EqualTo(PatrolBehavior.Patrol));
                Assert.That(mission.EnemyFrame(i).Current.Velocity.Magnitude, Is.GreaterThan(1f));
            }

            for (int i = 0; i < mission.EnemyCount; i++)
            {
                // Put the test pilot on the enemy's left and fire a real round at each step.
                // The cannon cooldown and hit protection still apply.
                for (int step = 0; step < 100 && mission.EnemyHealth(i) > 0; step++)
                {
                    var enemy = mission.EnemyFrame(i).Current;
                    var player = FlightState.At(enemy.Position - Vec2.Right * 2f);
                    mission.Tick(.02f, new AircraftCombatFrame(player, player, .42f, true));
                }
                Assert.That(mission.EnemyHealth(i), Is.Zero);
            }
            Assert.That(mission.Outcome, Is.EqualTo(MissionOutcome.Victory));
            Assert.That(mission.EnemiesRemaining, Is.Zero);
            for (int i = 0; i < sectors.Length; i++)
            {
                Assert.That(mission.SectorRemaining(i), Is.Zero);
            }
        }
    }
}
