using DogfightThreatAI.Core.Session;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class RoomSessionTests
    {
        [Test]
        public void RemoteMembersCanStartWithoutConnectionZero()
        {
            var room = new RoomSession();
            room.Join(17);
            room.Join(31);
            room.SetReady(17, true);
            room.SetReady(31, true);
            Assert.That(room.CanStart(17, 2), Is.True);
            Assert.That(room.CanStart(31, 2), Is.False);
            Assert.That(room.CanStart(0, 2), Is.False);
        }

        [Test]
        public void OwnerLeavesDuringRoundAndOldestSurvivorInheritsPermission()
        {
            var room = new RoomSession();
            room.Join(8);
            room.Join(3);
            room.Join(5);
            room.Leave(8);
            Assert.That(room.IsOwner(3), Is.True);
            Assert.That(room.IsOwner(8), Is.False);
            Assert.That(room.IsOwner(5), Is.False);
            room.Join(8);
            Assert.That(room.IsOwner(8), Is.False);
        }

        [Test]
        public void NewMemberAndCancelledReadyPreventStarting()
        {
            var room = new RoomSession();
            room.Join(4);
            room.SetReady(4, true);
            Assert.That(room.CanStart(4, 2), Is.False);
            room.Join(9);
            Assert.That(room.CanStart(4, 2), Is.False);
            room.SetReady(9, true);
            Assert.That(room.CanStart(4, 2), Is.True);
            room.SetReady(4, false);
            Assert.That(room.CanStart(4, 2), Is.False);
        }

        [Test]
        public void ReturningToLobbyRequiresFreshReadyButPreservesOwner()
        {
            var room = new RoomSession();
            room.Join(11);
            room.Join(12);
            room.SetReady(11, true);
            room.SetReady(12, true);
            room.ResetReady();
            Assert.That(room.Owner, Is.EqualTo(11));
            Assert.That(room.Count, Is.EqualTo(2));
            Assert.That(room.ReadyCount, Is.Zero);
            Assert.That(room.CanStart(11, 2), Is.False);
        }

        [Test]
        public void EmptyServerCanAcceptAnotherGroupWithoutOldPermissions()
        {
            var room = new RoomSession();
            room.Join(1);
            room.SetReady(1, true);
            room.Leave(1);
            Assert.That(room.Owner, Is.Null);
            Assert.That(room.ReadyCount, Is.Zero);
            Assert.That(room.SetReady(99, true), Is.False);
            room.Join(7);
            Assert.That(room.Owner, Is.EqualTo(7));
            Assert.That(room.ReadyCount, Is.Zero);
            Assert.That(room.Join(7), Is.False);
            Assert.That(room.Count, Is.EqualTo(1));
            room.Clear();
            Assert.That(room.Owner, Is.Null);
        }
    }
}
