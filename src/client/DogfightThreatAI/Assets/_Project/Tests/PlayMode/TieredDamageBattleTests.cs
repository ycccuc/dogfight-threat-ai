using System.Collections;
using System.Reflection;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
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
    [Category("LegacyRam")]
    public sealed class TieredDamageBattleTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        BattleCombatController _combat;
        ChaseAiInputSource _ai;
        float _originalScale;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _originalScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.GameScene, LoadSceneMode.Single);
            LegacyRamFixture.Activate();
            Bind();
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            Time.timeScale = _originalScale;
            yield return null;
        }

        void Bind()
        {
            _combat = Object.FindObjectOfType<BattleCombatController>();
            _ai = _combat.Enemy.GetComponent<ChaseAiInputSource>();
            SetState(_combat.Player, FlightState.At(Vec2.Zero));
            SetState(_combat.Enemy, FlightState.At(new Vec2(-6f, 0f)));
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            foreach (var plane in new[] { _combat.Player, _combat.Enemy })
            {
                var hitbox = plane.GetComponent<AircraftHitbox>();
                Assert.IsTrue(hitbox.IsActive);
                Assert.AreEqual(.5f, hitbox.WorldRadius, .001f);
            }
        }

        [UnityTest]
        public IEnumerator AllFourPublicDamageValuesReachTheRealContactAuthority()
        {
            for (int level = 1; level <= 4; level++)
            {
                if (level > 1)
                {
                    yield return SceneManager.LoadSceneAsync(SceneNavigation.GameScene, LoadSceneMode.Single);
                    LegacyRamFixture.Activate();
                    Bind();
                }
                var originalModel = Model();
                _ai.SetDifficulty(level);
                _ai.ReadInput(Time.fixedDeltaTime);
                Assert.AreEqual(level, _ai.Level);
                Assert.AreEqual(1, _ai.CollisionDamage, "All four scene difficulty levels must deal exactly one hull point per contact.");
                Assert.AreEqual(PursuitDifficultyProfile.ForLevel(level).CollisionDamage, _ai.CollisionDamage);
                SetState(_combat.Enemy, FlightState.At(Vec2.Zero));
                TickCombat();
                Assert.AreSame(originalModel, Model());
                Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth);
                Assert.AreEqual(1, _combat.Status.HitSequence);
                Assert.That(_combat.Status.InvulnerabilityRemaining, Is.InRange(.67f, .701f));
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ChangingLevelKeepsTheSameHealthProtectionAndAFullNextWarning()
        {
            var model = Model();
            _ai.SetDifficulty(3);
            _ai.ReadInput(Time.fixedDeltaTime);
            SetState(_combat.Enemy, FlightState.At(Vec2.Zero));
            TickCombat();
            int healthAfterFirstHit = _combat.Status.PlayerHealth;
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Recover, _ai.Phase);
            _ai.SetDifficulty(4);
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(3, _ai.Level);
            Assert.AreSame(model, Model());
            float recovery = _ai.PhaseRemaining;
            AdvanceWithoutContact(recovery + .02f);
            float remainingProtection = _combat.Status.InvulnerabilityRemaining;
            Assert.Greater(remainingProtection, 0f, "The shortened recovery must not clear the existing 0.7-second protection.");
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(4, _ai.Level);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            Assert.AreEqual(.8f, _ai.PhaseRemaining, .0001f,
                "Preparation may begin during protection, but must still announce a complete new warning.");
            Assert.AreSame(model, Model());
            Assert.AreEqual(healthAfterFirstHit, _combat.Status.PlayerHealth);
            Assert.AreEqual(remainingProtection, _combat.Status.InvulnerabilityRemaining, .00001f);
            TickCombat();
            Assert.AreEqual(healthAfterFirstHit, _combat.Status.PlayerHealth);
            Assert.AreEqual(1, _combat.Status.HitSequence);
            Assert.Less(_combat.Status.InvulnerabilityRemaining, remainingProtection);
            AdvanceWithoutContact(_combat.Status.InvulnerabilityRemaining + .02f);
            TickCombat();
            Assert.AreSame(model, Model());
            Assert.AreEqual(Mathf.Max(0, healthAfterFirstHit - _ai.CollisionDamage), _combat.Status.PlayerHealth);
            Assert.AreEqual(2, _combat.Status.HitSequence);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        void AdvanceWithoutContact(float seconds)
        {
            var player = _combat.Player.State;
            var far = FlightState.At(new Vec2(100f, 0f));
            Model().Step(player, player, far, far, seconds);
        }

        ContactCombatModel Model() => (ContactCombatModel)typeof(BattleCombatController).GetField("_model", Private).GetValue(_combat);
        void TickCombat() => typeof(BattleCombatController).GetMethod("FixedUpdate", Private).Invoke(_combat, null);

        static void SetState(PlayerController controller, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(controller, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(controller, state);
            controller.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }
    }
}
