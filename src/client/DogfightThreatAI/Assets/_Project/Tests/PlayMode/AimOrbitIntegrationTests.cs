using System.Collections;
using System.Reflection;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DogfightThreatAI.Tests.PlayMode
{
    public sealed class AimOrbitIntegrationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        BattleCombatController _combat;
        ChaseAiInputSource _ai;
        float _originalScale;

        [UnitySetUp]
        public IEnumerator LoadBattleWithoutAutomaticSimulation()
        {
            _originalScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.GameScene, LoadSceneMode.Single);
            _combat = Object.FindObjectOfType<BattleCombatController>();
            Assert.IsNotNull(_combat);
            _ai = _combat.Enemy.GetComponent<ChaseAiInputSource>();
            Assert.IsNotNull(_ai);
            foreach (var hitbox in Object.FindObjectsOfType<AircraftHitbox>())
                hitbox.Collider.enabled = false;

            // Begin at full speed with a real 90-degree turn to make: full throttle alone loses speed here.
            SetState(_combat.Player, FlightState.At(Vec2.Zero, Mathf.PI * .5f));
            SetState(_combat.Enemy, new FlightState
            {
                Position = new Vec2(-6f, 0f),
                Heading = Mathf.PI * .5f,
                Velocity = new Vec2(0f, 9f)
            });
            typeof(ChaseAiInputSource).GetField("_source", Private).SetValue(_ai, null);
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            Camera.main.aspect = 16f / 9f;
        }

        [UnityTearDown]
        public IEnumerator RestoreSimulation()
        {
            Time.timeScale = _originalScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator MovingAimMaintainsCruiseSpeedWhileTheRealControllerTurnsAndAdvances()
        {
            bool turned = false;
            float maximumHeadingStep = _ai.TurnRateDegPerSec * Time.fixedDeltaTime;
            for (int tick = 0; tick < 40; tick++)
            {
                FlightState before = _combat.Enemy.State;
                // Exercise the production input -> optional movement ability -> FlightModel pipeline.
                typeof(PlayerController).GetMethod("FixedUpdate", Private).Invoke(_combat.Enemy, null);
                FlightState after = _combat.Enemy.State;

                Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
                Assert.AreEqual(1, _ai.AttackId);
                Assert.That(after.Speed, Is.EqualTo(_ai.CruiseSpeed).Within(.01f),
                    $"Aim must keep real cruise speed during steering, not only return full throttle; tick={tick}.");
                Assert.Greater(Vec2.Distance(before.Position, after.Position), .17f,
                    "The enemy must actually advance through the scene instead of hovering during aim.");
                float turn = Mathf.Abs(Mathf.DeltaAngle(before.Heading * Mathf.Rad2Deg, after.Heading * Mathf.Rad2Deg));
                Assert.LessOrEqual(turn, maximumHeadingStep + .001f,
                    "Full-speed aim must still respect the aircraft's finite turn rate.");
                turned |= turn > .01f;

                // Disabled contact volumes suppress damage, but the actual combat clock must still advance.
                typeof(BattleCombatController).GetMethod("FixedUpdate", Private).Invoke(_combat, null);
            }
            Assert.IsTrue(turned, "The scenario must exercise real steering rather than a straight-line speed check.");
            Assert.That(_ai.PhaseRemaining, Is.InRange(.019f, .021f),
                "Moving aim must keep its original 0.8-second deadline throughout the maneuver.");
            Assert.AreEqual(5, _combat.Status.PlayerHealth);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        static void SetState(PlayerController controller, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(controller, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(controller, state);
            typeof(PlayerController).GetField("_hasPrevious", Private).SetValue(controller, true);
            controller.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }
    }
}
