using System.Collections;
using System.Reflection;
using DogfightThreatAI.Core.AI;
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
    public sealed class PursuitBoostBattleTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        BattleCombatController _combat;
        ChaseAiInputSource _ai;
        float _scale;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _scale = Time.timeScale;
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            _combat = Object.FindObjectOfType<BattleCombatController>();
            _ai = _combat.Enemy.GetComponent<ChaseAiInputSource>();
            Camera.main.transform.position = new Vector3(0, 0, -10);
            Camera.main.aspect = 16f / 9f;
        }

        [UnityTearDown]
        public IEnumerator Restore() { Time.timeScale = _scale; yield return null; }

        [UnityTest]
        public IEnumerator PursuitBoostUsesTheRealSceneMotionCommandAndClearsItsTrailAtWarning()
        {
            SetState(_combat.Player, new FlightState { Velocity = new Vec2(9, 0) });
            SetState(_combat.Enemy, new FlightState { Position = new Vec2(-40, 0), Velocity = new Vec2(9, 0) });
            _ai.SetDifficulty(4);
            typeof(PlayerController).GetMethod("FixedUpdate", Private).Invoke(_combat.Enemy, null);
            Assert.IsTrue(_ai.IsPursuitBoosting, "A player outside the enemy's screen position is still observable in the following camera.");
            Assert.AreEqual(30, _ai.BoostEnergy, .01f);
            Assert.AreEqual(90, _ai.MotionCommand.TurnRateDegPerSec);
            Assert.That(_combat.Enemy.State.Speed, Is.InRange(9.7f, 9.81f));
            yield return null;
            Assert.IsTrue(_combat.Enemy.transform.Find("EnemyPursuitTrail").GetComponent<LineRenderer>().enabled);

            SetState(_combat.Enemy, new FlightState { Position = new Vec2(-4, 0), Heading = Mathf.PI * .5f, Velocity = new Vec2(0, 13.5f) });
            _ai.ReadInput(.02f);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            Assert.AreEqual(.8f, _ai.PhaseRemaining, .0001f);
            Assert.AreEqual(FlightMotionMode.SpeedOverride, _ai.MotionCommand.Mode);
            yield return null;
            Assert.IsFalse(_combat.Enemy.transform.Find("EnemyPursuitTrail").GetComponent<LineRenderer>().enabled);
            Assert.IsTrue(_combat.Enemy.GetComponent<AttackCueView>().IsShowingCue);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RestartClearsBothResourcesCooldownAndPenalty()
        {
            var keyboard = _combat.Player.GetComponent<KeyboardInputSource>();
            var model = (PlayerBoostModel)typeof(KeyboardInputSource).GetField("_boost", Private).GetValue(keyboard);
            model.Step(false, true, 1.6f, 9f);
            Assert.IsTrue(model.IsOverheated);
            _ai.SetDifficulty(4);
            _combat.RestartBattle();
            yield return null;
            _combat = Object.FindObjectOfType<BattleCombatController>();
            keyboard = _combat.Player.GetComponent<KeyboardInputSource>();
            _ai = _combat.Enemy.GetComponent<ChaseAiInputSource>();
            Assert.AreEqual(5, _combat.Status.PlayerHealth);
            Assert.AreEqual(60, keyboard.Energy);
            Assert.IsFalse(keyboard.IsOverheated);
            Assert.AreEqual(0, keyboard.DashCooldownRemaining);
            Assert.AreEqual(60, _ai.BoostEnergy);
            Assert.AreEqual(0, _ai.DifficultyOverride);
            LogAssert.NoUnexpectedReceived();
        }

        static void SetState(PlayerController aircraft, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(aircraft, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(aircraft, state);
            aircraft.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }
    }
}
