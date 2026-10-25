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
    public sealed class FourTierBattleTests
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

        void Bind()
        {
            _combat = Object.FindObjectOfType<BattleCombatController>();
            _ai = _combat.Enemy.GetComponent<ChaseAiInputSource>();
            foreach (var hitbox in Object.FindObjectsOfType<AircraftHitbox>()) hitbox.Collider.enabled = false;
            SetState(_combat.Player, FlightState.At(Vec2.Zero, Mathf.PI * .5f));
            SetState(_combat.Enemy, FlightState.At(new Vec2(-20f, 0f)));
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            Camera.main.aspect = 16f / 9f;
        }

        [UnityTearDown]
        public IEnumerator Restore()
        {
            Time.timeScale = _originalScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SavedBattleAutomaticallyAdvancesThroughFourLevels()
        {
            for (int level = 1; level <= 4; level++)
            {
                _ai.ReadInput(.02f);
                Assert.AreEqual(level, _ai.Level, "The scene adapter must expose the real growing AI level.");
                Assert.AreEqual((level - 1) / 3f, _ai.Aggression01, .001f);
                Assert.AreEqual(AttackRunPhase.Pursuit, _ai.Phase);
                Clock(20f);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DifficultyPreviewSelectsEachRealLevelInsteadOfChangingOnlyTheHud()
        {
            for (int level = 4; level >= 1; level--)
            {
                SelectLevel(level);
                // A level requested during pursuit boost stays frozen until that action ends.
                Clock(2f);
                _ai.ReadInput(.02f);
                Assert.AreEqual(level, _ai.Level);
                var source = typeof(ChaseAiInputSource).GetField("_source", Private).GetValue(_ai);
                Assert.AreEqual(level, source.GetType().GetProperty("Level").GetValue(source));
            }
            SelectLevel(0);
            Clock(40f);
            _ai.ReadInput(.02f);
            Assert.AreEqual(3, _ai.Level);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SelectingAHigherLevelCannotResetAnOngoingWarningOrTurnItsCharge()
        {
            SelectLevel(1);
            SetState(_combat.Enemy, FlightState.At(new Vec2(4f, 0f), Mathf.PI));
            _ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            int attack = _ai.AttackId;
            Clock(.3f);
            _ai.ReadInput(.02f);
            float remaining = _ai.PhaseRemaining;
            SelectLevel(4);
            _ai.ReadInput(.02f);
            Assert.AreEqual(1, _ai.Level);
            Assert.AreEqual(remaining, _ai.PhaseRemaining, .0001f);
            Assert.AreEqual(attack, _ai.AttackId);
            Clock(.5f);
            _ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
            Vec2 committed = _ai.LockedDirection;
            Clock(.2f);
            _ai.ReadInput(.02f);
            Assert.AreEqual(committed, _ai.LockedDirection);
            Assert.AreEqual(1, _ai.Level);
            Clock(.7f);
            _ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Recover, _ai.Phase);
            Clock(2f);
            _ai.ReadInput(.02f);
            Assert.AreEqual(4, _ai.Level);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryDifficultyKeepsAFullMovingWarningWithoutHovering()
        {
            for (int level = 1; level <= 4; level++)
            {
                if (level > 1)
                {
                    yield return SceneManager.LoadSceneAsync(SceneNavigation.GameScene, LoadSceneMode.Single);
                    LegacyRamFixture.Activate();
                    Bind();
                }
                SelectLevel(level);
                SetState(_combat.Enemy, FlightState.At(new Vec2(-4f, 0f), Mathf.PI * .5f));
                for (int tick = 0; tick < 40; tick++)
                {
                    var before = _combat.Enemy.State;
                    typeof(PlayerController).GetMethod("FixedUpdate", Private).Invoke(_combat.Enemy, null);
                    Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
                    Assert.AreEqual(level, _ai.Level);
                    Assert.AreEqual(_ai.CruiseSpeed, _combat.Enemy.State.Speed, .01f);
                    Assert.Greater(Vec2.Distance(before.Position, _combat.Enemy.State.Position), _ai.CruiseSpeed * .019f);
                    Clock(.02f);
                }
                _ai.ReadInput(.02f);
                Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
                Assert.AreEqual(5f, _ai.SpeedMultiplier);
                Assert.IsTrue(_ai.IsAimLocked);
                yield return null;
                foreach (var line in _combat.Enemy.GetComponentsInChildren<LineRenderer>())
                    if (line.name.StartsWith("AttackCue_")) Assert.IsFalse(line.enabled);
            }
        }

        void SelectLevel(int level)
        {
            var method = typeof(ChaseAiInputSource).GetMethod("SetDifficulty", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "A runtime difficulty selector must drive the real source, so all four levels can be tried.");
            method.Invoke(_ai, new object[] { level });
        }

        void Clock(float seconds)
        {
            var model = (ContactCombatModel)typeof(BattleCombatController).GetField("_model", Private).GetValue(_combat);
            var player = _combat.Player.State;
            var enemy = _combat.Enemy.State;
            model.Step(player, player, enemy, enemy, seconds);
        }

        static void SetState(PlayerController controller, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(controller, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(controller, state);
            controller.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }
    }
}
