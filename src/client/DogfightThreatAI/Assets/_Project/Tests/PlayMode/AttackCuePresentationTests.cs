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
    public sealed class AttackCuePresentationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        PlayerController _player, _enemy;
        ChaseAiInputSource _ai;
        float _originalScale;

        [UnitySetUp]
        public IEnumerator LoadBattle()
        {
            _originalScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            _ai = Object.FindObjectOfType<ChaseAiInputSource>();
            _enemy = _ai.GetComponent<PlayerController>();
            _player = _ai.Target;
            var input = _player.gameObject.AddComponent<BattleTestInputSource>();
            input.DesiredInput = new FlightInput(1f, 0f);
            typeof(PlayerController).GetField("inputSource", Private).SetValue(_player, input);
            SetState(_player, new FlightState { Position = Vec2.Zero, Heading = Mathf.PI * .5f, Velocity = new Vec2(0f, 9f) });
            SetState(_enemy, FlightState.At(new Vec2(3f, 0f), Mathf.PI));
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            // Isolate visual timing from legitimate contacts, which can now cancel any aiming phase.
            foreach (var hitbox in Object.FindObjectsOfType<AircraftHitbox>()) hitbox.Collider.enabled = false;
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreScale()
        {
            Time.timeScale = _originalScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator YellowAimKeepsAThinLiveDirectionWithoutBroadDangerFill()
        {
            LineRenderer streak = VisibleStreak();
            Assert.IsNotNull(streak);
            Assert.That(streak.widthMultiplier, Is.InRange(.10f, .18f));
            Assert.Greater(PathLength(streak), streak.widthMultiplier * 20f);
            AssertNoRepeatedKnots(streak);
            foreach (var line in _enemy.GetComponentsInChildren<LineRenderer>())
                if (line.enabled) Assert.LessOrEqual(line.widthMultiplier, .18f, "A live yellow aim must not retain the old broad charge marker.");
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator WarningLaneFadesAndTapersWithoutAnEndpointArrow()
        {
            LineRenderer lane = VisibleStreak();
            Assert.IsNotNull(lane);
            Assert.Greater(lane.startColor.a, lane.endColor.a + .1f,
                "A long warning lane must fade into space instead of ending as a hard yellow bar.");
            Assert.Greater(lane.widthCurve.Evaluate(0f), lane.widthCurve.Evaluate(1f),
                "The warning should converge into a narrow lane, not retain a blunt full-width end.");
            foreach (var line in _enemy.GetComponentsInChildren<LineRenderer>())
            {
                if (!line.enabled || line.loop || line.positionCount < 2) continue;
                Vector3 axis = line.GetPosition(line.positionCount - 1) - line.GetPosition(0);
                if (axis.sqrMagnitude < .0001f) continue;
                for (int i = 1; i < line.positionCount; i++)
                {
                    Vector3 segment = line.GetPosition(i) - line.GetPosition(i - 1);
                    Assert.Greater(Vector3.Dot(segment.normalized, axis.normalized), .999f,
                        "Warning geometry must follow the real launch axis; no divergent endpoint arrow.");
                }
            }
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MovingAimCommitsAFixedWorldLaunchLaneThenChargesAlongIt()
        {
            Vec2 initialPosition = _enemy.State.Position;
            Time.timeScale = 1f;
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
                Vec2 expected = (_ai.GetPreviewPoint(_ai.PreviewPointCount - 1) - _ai.GetPreviewPoint(0)).Normalized;
                Assert.Less(Vec2.Distance(_ai.AimDirection, expected), .0001f);
                Assert.LessOrEqual(_ai.ObservedAt, _ai.Combat.Status.ElapsedTime);
            }
            Time.timeScale = 0f;
            yield return null;
            Assert.Greater(Vec2.Distance(initialPosition, _enemy.State.Position), .1f,
                "Aiming must retain actual aircraft movement.");
            var line = VisibleStreak();
            Assert.IsNotNull(line);
            Vector3 start = line.GetPosition(0), end = line.GetPosition(line.positionCount - 1);
            Vec2 shown = new Vec2(end.x - start.x, end.y - start.y).Normalized;
            Assert.Less(Vec2.Distance(shown, _enemy.State.Forward), .0001f,
                "The live warning must follow the rendered aircraft nose, not an unrelated target line.");
            var rendered = _enemy.GetComponentInChildren<PlayerView>().RenderPosition;
            Vector3 expectedStart = rendered + new Vector3(shown.X, shown.Y, 0f) * .5f;
            Assert.Less(Vector2.Distance(new Vector2(start.x, start.y), new Vector2(expectedStart.x, expectedStart.y)), .0001f,
                "The warning must stay attached to the interpolated aircraft nose.");
            Assert.IsFalse(_ai.IsAimLocked);
            Time.timeScale = 1f;
            for (int i = 0; i < 45 && !_ai.IsAimLocked; i++) yield return new WaitForFixedUpdate();
            Time.timeScale = 0f;
            yield return null;
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            Assert.IsTrue(_ai.IsAimLocked);
            Assert.That(_ai.PhaseRemaining, Is.InRange(PursuitChargeInputSource.AimCommitSeconds - .04f,
                PursuitChargeInputSource.AimCommitSeconds + .0201f));
            Vec2 committed = _ai.LockedDirection;
            Vec2 launchPosition = _ai.GetPreviewPoint(0);
            line = VisibleStreak();
            Assert.IsNotNull(line);
            Assert.Less(Vec2.Distance(ShownDirection(line), committed), .0001f);
            Vector3 committedStart = line.GetPosition(0);
            Vector3 committedEnd = line.GetPosition(line.positionCount - 1);
            Vec2 expectedLaunchStart = launchPosition + committed * .5f;
            Assert.Less(Vec2.Distance(new Vec2(committedStart.x, committedStart.y), expectedLaunchStart), .0001f,
                "Commitment marks the fixed future launch lane, while the aircraft finishes its planned bank.");

            // Reverse the target after commitment. The warning and the ensuing charge
            // must retain their promised direction instead of correcting after the dodge.
            FlightState target = _player.State;
            target.Velocity = -target.Velocity;
            target.Heading += Mathf.PI;
            SetState(_player, target);
            for (int i = 0; i < 4; i++)
            {
                Time.timeScale = 1f;
                yield return new WaitForFixedUpdate();
                Time.timeScale = 0f;
                yield return null;
                Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
                Assert.Less(Vec2.Distance(_ai.GetPreviewPoint(0), launchPosition), .0001f);
                Assert.Less(Vec2.Distance(_ai.LockedDirection, committed), .0001f);
                line = VisibleStreak();
                Assert.Less(Vector3.Distance(line.GetPosition(0), committedStart), .0001f,
                    "A committed launch lane must stay fixed in world space while the enemy moves into it.");
                Assert.Less(Vector3.Distance(line.GetPosition(line.positionCount - 1), committedEnd), .0001f);
            }
            Time.timeScale = 1f;
            for (int i = 0; i < 50 && _ai.Phase == AttackRunPhase.Windup; i++) yield return new WaitForFixedUpdate();
            Time.timeScale = 0f;
            Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
            Assert.IsTrue(_ai.IsAimLocked);
            Assert.Less(Vec2.Distance(_ai.LockedDirection, committed), .0001f);
            Assert.Less(Vec2.Distance(_enemy.State.Velocity.Normalized, committed), .0001f,
                "The actual first charge movement must follow the committed warning axis.");
            Assert.LessOrEqual(Vec2.Distance(_enemy.PreviousState.Position, launchPosition),
                _ai.CruiseSpeed * Time.fixedDeltaTime + .02f,
                "Charge must start at the promised launch point, within one actual approach step.");
            yield return null;
            Assert.IsFalse(_enemy.GetComponent<AttackCueView>().IsShowingCue);
            Assert.AreEqual(0, _ai.PreviewPointCount);
            foreach (var marker in _enemy.GetComponentsInChildren<LineRenderer>())
                if (marker.name.StartsWith("AttackCue_"))
                    Assert.IsFalse(marker.enabled, "Charge must hide all forward warning geometry.");
            var wake = _enemy.transform.Find("EnemyChargeWake").GetComponent<LineRenderer>();
            Assert.IsTrue(wake.enabled, "Charge should be readable through a restrained rear wake.");
            Vector3 enemyPosition = _enemy.GetComponentInChildren<PlayerView>().RenderPosition;
            for (int i = 0; i < wake.positionCount; i++)
            {
                Vector3 offset = wake.GetPosition(i) - enemyPosition;
                Assert.Less(offset.x * committed.X + offset.y * committed.Y, 0f,
                    "The charge wake must stay behind the enemy, never become a forward impact marker.");
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DisablingAiAlsoHidesAnyYellowAimMarker()
        {
            Assert.IsNotNull(VisibleStreak());
            _ai.enabled = false;
            yield return null;
            Assert.IsFalse(_enemy.GetComponent<AttackCueView>().IsShowingCue);
            foreach (var line in _enemy.GetComponentsInChildren<LineRenderer>()) Assert.IsFalse(line.enabled);
            LogAssert.NoUnexpectedReceived();
        }

        LineRenderer VisibleStreak()
        {
            foreach (var line in _enemy.GetComponentsInChildren<LineRenderer>())
                if (line.enabled && line.widthMultiplier >= .10f && line.widthMultiplier <= .18f) return line;
            return null;
        }

        static Vec2 ShownDirection(LineRenderer line)
        {
            Vector3 delta = line.GetPosition(line.positionCount - 1) - line.GetPosition(0);
            return new Vec2(delta.x, delta.y).Normalized;
        }

        static float PathLength(LineRenderer line)
        {
            float length = 0f;
            for (int i = 1; i < line.positionCount; i++) length += Vector3.Distance(line.GetPosition(i - 1), line.GetPosition(i));
            return length;
        }

        static void AssertNoRepeatedKnots(LineRenderer line)
        {
            Assert.GreaterOrEqual(line.positionCount, 2);
            for (int i = 1; i < line.positionCount; i++)
                Assert.Greater(Vector3.Distance(line.GetPosition(i - 1), line.GetPosition(i)), .0001f);
        }

        static void SetState(PlayerController controller, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(controller, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(controller, state);
            controller.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }
    }
}
