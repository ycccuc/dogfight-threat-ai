using System.Collections;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DogfightThreatAI.Tests.PlayMode
{
    [Category("LegacyRam")]
    public sealed class AircraftHitboxSceneTests
    {
        float _originalTimeScale;
        BattleCombatController _combat;

        [UnitySetUp]
        public IEnumerator LoadSavedBattle()
        {
            _originalTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.GameScene, LoadSceneMode.Single);
            LegacyRamFixture.Activate();
            _combat = Object.FindObjectOfType<BattleCombatController>();
            Assert.IsNotNull(_combat);
        }

        [UnityTearDown]
        public IEnumerator RestoreTimeScale()
        {
            Time.timeScale = _originalTimeScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SavedBattleHasTwoSimulationAnchoredContactVolumes()
        {
            foreach (var aircraft in new[] { _combat.Player, _combat.Enemy })
            {
                var hitbox = RequiredHitbox(aircraft);
                Assert.AreSame(aircraft.gameObject, hitbox.Collider.gameObject);
                Assert.AreSame(aircraft.GetComponent<Rigidbody2D>(), hitbox.Collider.attachedRigidbody);
                Assert.AreEqual(Vector2.zero, hitbox.Collider.offset);
                Assert.IsTrue(hitbox.Collider.isTrigger);
                Assert.IsTrue(hitbox.IsActive);
                Assert.AreEqual(.5f, hitbox.WorldRadius, .001f);
            }
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArtMovementDoesNotMoveTheDamageVolume()
        {
            var player = _combat.Player;
            var hitbox = RequiredHitbox(player);
            var art = player.GetComponentInChildren<PlayerView>();
            Assert.IsNotNull(art);
            Assert.AreNotSame(player.transform, art.transform);
            Physics2D.SyncTransforms();
            Vector3 center = hitbox.Collider.bounds.center;
            Vector2 rootPosition = player.GetComponent<Rigidbody2D>().position;
            var simulationPosition = player.State.Position;
            art.transform.position += new Vector3(.25f, -.2f, 0f);
            Physics2D.SyncTransforms();
            Assert.Less(Vector3.Distance(center, hitbox.Collider.bounds.center), .0001f);
            Assert.AreEqual(rootPosition, player.GetComponent<Rigidbody2D>().position);
            Assert.AreEqual(simulationPosition, player.State.Position);
            Assert.AreEqual(.5f, hitbox.WorldRadius, .001f);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledVolumeOrSimulationCannotAuthorizeContact()
        {
            var hitbox = RequiredHitbox(_combat.Player);
            Assert.IsTrue(hitbox.IsActive);
            hitbox.Collider.enabled = false;
            Assert.IsFalse(hitbox.IsActive);
            hitbox.Collider.enabled = true;
            Assert.IsTrue(hitbox.IsActive);
            hitbox.enabled = false;
            Assert.IsFalse(hitbox.IsActive);
            hitbox.enabled = true;
            var body = hitbox.Collider.attachedRigidbody;
            body.simulated = false;
            Assert.IsFalse(hitbox.IsActive);
            body.simulated = true;
            Assert.IsTrue(hitbox.IsActive);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        static AircraftHitbox RequiredHitbox(PlayerController aircraft)
        {
            Assert.IsNotNull(aircraft);
            var hitbox = aircraft.GetComponent<AircraftHitbox>();
            Assert.IsNotNull(hitbox, "The saved Battle aircraft must contain its actual contact volume, not only a damage radius in code.");
            Assert.IsNotNull(hitbox.Collider);
            return hitbox;
        }
    }
}
