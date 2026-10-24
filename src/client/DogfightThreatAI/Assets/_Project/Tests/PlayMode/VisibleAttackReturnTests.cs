using System.Collections;
using System.Reflection;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.InputSource;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DogfightThreatAI.Tests.PlayMode
{
    public sealed class VisibleAttackReturnTests
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        float _originalScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _originalScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _originalScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator AtFortySeconds_EnemyDirectlyReturnsToViewAndStartsAFullAim()
            => ReturnAndWarn(40f);

        [UnityTest]
        public IEnumerator AtSixtySeconds_EnemyDirectlyReturnsToViewAndStartsAFullAim()
            => ReturnAndWarn(60f);

        IEnumerator ReturnAndWarn(float startingClock)
        {
            var combat = Object.FindObjectOfType<BattleCombatController>();
            var ai = Object.FindObjectOfType<ChaseAiInputSource>();
            Assert.IsNotNull(combat);
            Assert.IsNotNull(ai);
            var player = combat.Player;
            var enemy = combat.Enemy;
            var camera = Camera.main;
            Assert.IsNotNull(camera);
            Assert.AreEqual(5f, camera.orthographicSize, .001f);

            var input = player.gameObject.AddComponent<BattleTestInputSource>();
            input.DesiredInput = new FlightInput(-1f, 0f);
            typeof(PlayerController).GetField("inputSource", Private).SetValue(player, input);
            SetState(player, FlightState.At(Vec2.Zero, Mathf.PI * .5f));
            // Contact may now interrupt aiming. Isolate the visibility/countdown contract from collision reactions.
            foreach (var hitbox in Object.FindObjectsOfType<AircraftHitbox>()) hitbox.Collider.enabled = false;
            float radius = ActualRadius(enemy);
            // The upper sprite extent is outside the actual viewport, not merely behind a HUD panel.
            float upperVisibleCenter = camera.orthographicSize - radius;
            float spawnY = upperVisibleCenter + radius * 1.25f;
            SetState(enemy, FlightState.At(new Vec2(0f, spawnY), -Mathf.PI * .5f));
            camera.transform.position = new Vector3(0f, 0f, -10f);
            Assert.IsFalse(FullyVisible(camera, enemy));
            var model = new ContactCombatModel(5, 1, .7f);
            var separated = FlightState.At(new Vec2(100f, 0f));
            var origin = FlightState.At(Vec2.Zero);
            model.Step(origin, origin, separated, separated, startingClock);
            typeof(BattleCombatController).GetField("_model", Private).SetValue(combat, model);
            float began = combat.Status.ElapsedTime;
            Time.timeScale = 5f;

            for (int tick = 0; tick < 400 && ai.Phase != AttackRunPhase.Windup; tick++)
            {
                yield return new WaitForFixedUpdate();
                if (ai.Phase == AttackRunPhase.Windup) break;
                Assert.IsFalse(ai.AttackWindow.IsActive);
            }
            Assert.AreEqual(AttackRunPhase.Windup, ai.Phase,
                $"The direct pursuer must return into view and aim within 8s; elapsed={combat.Status.ElapsedTime - began:F2}, enemy={enemy.State.Position}, radius={radius:F3}");
            TestContext.WriteLine($"warning-entry level={ai.Level}, remaining={ai.PhaseRemaining}, now={combat.Status.ElapsedTime}, " +
                $"enemy={enemy.State.Position}, previous={enemy.PreviousState.Position}, radius={radius}, camera={camera.transform.position}, aspect={camera.aspect}, " +
                $"currentVisible={FullyVisible(camera, enemy)}");
            Assert.That(ai.PhaseRemaining, Is.EqualTo(.8f).Within(.0001f),
                "Inspect the first Windup input, so PreviousState is exactly the state used by the visibility gate.");
            Assert.IsTrue(FullyVisible(camera, enemy.PreviousState, radius),
                "The input state must be fully visible before preparation; the same fixed step can then move a fast aircraft across the viewport edge.");
            Assert.IsTrue(FullyVisible(camera, player));
            Assert.That(ai.PhaseRemaining, Is.InRange(.77f, .81f));
            Assert.IsFalse(ai.AttackWindow.IsActive);
            int expectedLevel = Mathf.Min(4, 1 + Mathf.FloorToInt(startingClock / 20f));
            Assert.AreEqual(expectedLevel, ai.Level);
            Assert.IsTrue(ai.Tactic == AttackTactic.FlankLeft || ai.Tactic == AttackTactic.FlankRight,
                "The advanced tiers must keep their side-cut tactic after returning into view.");
            float warningAt = combat.Status.ElapsedTime;
            int id = ai.AttackId;
            for (int tick = 0; tick < 50 && ai.Phase == AttackRunPhase.Windup; tick++)
            {
                Assert.AreEqual(ai.PhaseRemaining <= PursuitChargeInputSource.AimCommitSeconds + .0001f, ai.IsAimLocked,
                    "The final warning segment commits before charge so a late reversal remains a counter.");
                Assert.AreEqual(id, ai.AttackId);
                Assert.IsFalse(ai.AttackWindow.IsActive);
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual(AttackRunPhase.Charge, ai.Phase);
            Assert.IsTrue(ai.IsAimLocked);
            Assert.That(combat.Status.ElapsedTime - warningAt, Is.InRange(.77f, .85f));
            float locked = ai.LockedHeading;
            for (int tick = 0; tick < 5 && ai.Phase == AttackRunPhase.Charge; tick++)
            {
                Assert.AreEqual(locked, ai.LockedHeading);
                yield return new WaitForFixedUpdate();
            }
            LogAssert.NoUnexpectedReceived();
        }

        static float ActualRadius(PlayerController aircraft)
        {
            float radius = .6f;
            if (aircraft.Ship != null && aircraft.Ship.BodySprite != null)
            {
                var extents = aircraft.Ship.BodySprite.bounds.extents;
                radius = Mathf.Max(radius, new Vector2(extents.x, extents.y).magnitude);
            }
            return radius;
        }
        static bool FullyVisible(Camera camera, PlayerController aircraft)
        {
            return FullyVisible(camera, aircraft.State, ActualRadius(aircraft));
        }
        static bool FullyVisible(Camera camera, FlightState state, float radius)
        {
            Vec2 p = state.Position;
            Vector3 min = camera.WorldToViewportPoint(new Vector3(p.X - radius, p.Y - radius, 0f));
            Vector3 max = camera.WorldToViewportPoint(new Vector3(p.X + radius, p.Y + radius, 0f));
            return min.z > 0f && max.z > 0f && min.x >= -.0001f && min.y >= -.0001f && max.x <= 1.0001f && max.y <= 1.0001f;
        }
        static void SetState(PlayerController controller, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(controller, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(controller, state);
            controller.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }
    }
}
