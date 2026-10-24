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
    public sealed class BattleChaseIntegrationTests
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        PlayerController _player, _enemy;
        ChaseAiInputSource _ai;
        BattleCombatController _combat;
        float _originalTimeScale;

        [UnitySetUp]
        public IEnumerator LoadBattle()
        {
            _originalTimeScale = Time.timeScale;
            yield return LoadFreshBattle(0f);
        }

        [UnityTearDown]
        public IEnumerator RestoreTimeScale()
        {
            Time.timeScale = _originalTimeScale;
            yield return null;
        }

        IEnumerator LoadFreshBattle(float elapsed)
        {
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            FindSceneAircraft();
            Assert.IsTrue(_player.IsInitialized);
            Assert.IsTrue(_enemy.IsInitialized);
            if (elapsed > 0f) SeedBattleClock(elapsed);
            SetPlayerInput(FlightInput.Neutral);
            Time.timeScale = 5f;
            yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator BattleScene_HasFairAircraftCombatAuthorityAndRuntimeCues()
        {
            // Inspect the saved spawn and wiring before automatic flight. A fast enemy
            // can legitimately leave view during a variable number of setup frames.
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            FindSceneAircraft();
            Assert.AreEqual(2, Object.FindObjectsOfType<PlayerController>().Length);
            Assert.AreEqual(1, Object.FindObjectsOfType<BattleCombatController>().Length);
            Assert.AreSame(_player, _ai.Target);
            Assert.AreSame(_combat, _ai.Combat);
            Assert.AreSame(_player, _combat.Player);
            Assert.AreSame(_enemy, _combat.Enemy);
            Assert.IsNotNull(_player.GetComponent<KeyboardInputSource>());
            Assert.IsNull(_enemy.GetComponent<KeyboardInputSource>());
            Assert.AreNotSame(_player.Ship, _enemy.Ship);
            Assert.AreEqual(9f, _player.Ship.MaxSpeed, 1e-5f);
            Assert.AreEqual(9f, _enemy.Ship.MaxSpeed, 1e-5f);
            Assert.AreEqual(210f, _enemy.Ship.TurnRateDegPerSec, 1e-5f);
            Assert.Less(_enemy.Ship.TurnRateDegPerSec, _player.Ship.TurnRateDegPerSec);
            Assert.AreSame(_ai, typeof(PlayerController).GetField("inputSource", PrivateInstance).GetValue(_enemy));
            Assert.IsNotNull(_enemy.GetComponent<AttackCueView>());
            Assert.AreEqual(5, _combat.Status.PlayerHealth);
            var playerRenderer = _player.GetComponentInChildren<SpriteRenderer>();
            var enemyRenderer = _enemy.GetComponentInChildren<SpriteRenderer>();
            Assert.IsNotNull(playerRenderer.sprite);
            Assert.IsNotNull(enemyRenderer.sprite);
            Assert.AreNotEqual(playerRenderer.color, enemyRenderer.color);
            AssertInCamera(playerRenderer.bounds.center);
            AssertInCamera(enemyRenderer.bounds.center);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MovingWarningShowsTheNoseThenCommitsItsWorldLaunchLaneBeforeCharge()
        {
            Time.timeScale = 0f;
            var input = SetPlayerInput(new FlightInput(1f, 0f));
            SetAircraftState(_player, new FlightState
            {
                Position = Vec2.Zero, Heading = Mathf.PI * .5f, Velocity = new Vec2(0f, 9f)
            });
            SetAircraftState(_enemy, FlightState.At(new Vec2(3f, 0f), Mathf.PI));
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            // This scenario verifies tracking and commitment timing; contact cancellation has separate coverage.
            foreach (var hitbox in Object.FindObjectsOfType<AircraftHitbox>()) hitbox.Collider.enabled = false;
            BeginFreshAim();
            Vec2 initialPrediction = _ai.PredictionPoint;
            float aimStarted = _combat.Status.ElapsedTime;
            int attackId = _ai.AttackId;
            Time.timeScale = 1f;
            // Lv1 now samples on .08-second intervals with .06-second observation delay.
            // Exercise several usable updates while remaining before the .50 commitment.
            for (int tick = 0; tick < 20; tick++)
            {
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
                Assert.IsFalse(_ai.AttackWindow.IsActive);
                Assert.AreEqual(attackId, _ai.AttackId);
                Vec2 expectedAim = _enemy.PreviousState.Forward;
                Assert.Less(Vec2.Distance(_ai.AimDirection, expectedAim), .0001f,
                    "The Core warning records the real nose before this flight step, rather than a separate predicted-target line.");
                Assert.IsFalse(_ai.IsAimLocked);
                Assert.LessOrEqual(_ai.ObservedAt, _combat.Status.ElapsedTime);
            }
            Assert.Greater(Vec2.Distance(initialPrediction, _ai.PredictionPoint), .2f,
                "The tracking segment must still update its observed target before commitment.");
            bool announced = false;
            Vec2 announcedAxis = Vec2.Zero, announcedOrigin = Vec2.Zero;
            for (int tick = 0; tick < 40 && _ai.Phase == AttackRunPhase.Windup; tick++)
            {
                if (_ai.IsAimLocked)
                {
                    if (!announced)
                    {
                        announced = true;
                        announcedAxis = _ai.LockedDirection;
                        announcedOrigin = _ai.GetPreviewPoint(0);
                    }
                    Assert.Less(Vec2.Distance(announcedAxis, _ai.LockedDirection), .0001f);
                    Assert.Less(Vec2.Distance(announcedOrigin, _ai.GetPreviewPoint(0)), .0001f);
                }
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
            Assert.IsTrue(announced, "The launch lane must be visible before the aircraft charges.");
            Assert.That(_combat.Status.ElapsedTime - aimStarted, Is.InRange(.76f, .86f));
            Vec2 chargeDirection = _ai.LockedDirection;
            Assert.Less(Vec2.Distance(announcedAxis, chargeDirection), .0001f);
            Assert.Less(Mathf.Abs(Vec2.Cross(_enemy.State.Position - announcedOrigin, announcedAxis)), .001f,
                "The real charge must run on the promised world lane.");
            input.DesiredInput = new FlightInput(1f, -1f);
            for (int tick = 0; tick < 50 && _ai.Phase == AttackRunPhase.Charge; tick++)
            {
                Assert.AreEqual(chargeDirection, _ai.LockedDirection,
                    "The announced charge must not home after launch.");
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual(AttackRunPhase.Recover, _ai.Phase);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AutomaticDifficulty_ReflectsFourRealLevelsAcrossBattleClocks()
        {
            foreach (float elapsed in new[] { 0f, 20f, 40f, 60f, 80f })
            {
                yield return LoadFreshBattle(elapsed);
                for (int tick = 0; tick < 3; tick++) yield return new WaitForFixedUpdate();
                int expectedLevel = Mathf.Min(4, 1 + Mathf.FloorToInt(elapsed / 20f));
                Assert.AreEqual(expectedLevel, _ai.Level);
                Assert.AreEqual((expectedLevel - 1) / 3f, _ai.Aggression01, .001f);
                if (expectedLevel == 1) Assert.AreEqual(AttackTactic.Pursuit, _ai.Tactic);
                else Assert.AreNotEqual(AttackTactic.Pursuit, _ai.Tactic);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AimCountdownAtNormalSpeedThenChargeCommitsAndRecovers()
        {
            Time.timeScale = 0f;
            SetPlayerInput(new FlightInput(-1f, 0f));
            foreach (var hitbox in Object.FindObjectsOfType<AircraftHitbox>()) hitbox.Collider.enabled = false;
            // This countdown fixture needs a known visible entry; setup frames are not
            // part of its clock and the stronger enemy can already have left its spawn.
            SetAircraftState(_player, FlightState.At(Vec2.Zero, Mathf.PI * .5f));
            SetAircraftState(_enemy, FlightState.At(new Vec2(-4f, 0f), Mathf.PI * .5f));
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            Camera.main.aspect = 16f / 9f;
            BeginFreshAim();
            Vec2 aimStartedAt = _enemy.State.Position;
            int attackId = _ai.AttackId;
            float windupStarted = _combat.Status.ElapsedTime;
            Assert.Greater(_ai.PreviewPointCount, 1);
            Assert.That(_ai.PhaseRemaining, Is.InRange(0.75f, 0.81f));
            Assert.IsFalse(_ai.AttackWindow.IsActive);
            yield return null;
            var cue = _enemy.GetComponent<AttackCueView>();
            Assert.IsTrue(cue.IsShowingCue, "The saved scene must render the live aiming line.");
            Time.timeScale = 1f;

            for (int tick = 0; tick < 80 && _ai.Phase == AttackRunPhase.Windup; tick++)
            {
                Assert.AreEqual(attackId, _ai.AttackId);
                Assert.IsFalse(_ai.AttackWindow.IsActive);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(_combat.Status.ElapsedTime - windupStarted, Is.InRange(0.76f, 0.86f));
            Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
            Assert.Greater(Vec2.Distance(aimStartedAt, _enemy.State.Position), .1f,
                "Aiming must keep flying; contact cancellation is isolated in its own collision tests.");
            Vec2 locked = _ai.LockedDirection;
            float chargeStarted = _combat.Status.ElapsedTime;
            for (int tick = 0; tick < 60 && _ai.Phase == AttackRunPhase.Charge; tick++)
            {
                Assert.AreEqual(locked, _ai.LockedDirection);
                Assert.AreEqual(attackId, _ai.AttackId);
                yield return new WaitForFixedUpdate();
            }
            Assert.LessOrEqual(_combat.Status.ElapsedTime - chargeStarted, 0.94f);
            Assert.AreEqual(AttackRunPhase.Recover, _ai.Phase);
            float recoverySeconds = _ai.RecoverySeconds;
            Assert.That(_ai.PhaseRemaining, Is.InRange(recoverySeconds - .06f, recoverySeconds + .02f));
            float recoveryStarted = _combat.Status.ElapsedTime;
            for (int tick = 0; tick < Mathf.CeilToInt(_ai.RecoverySeconds / Time.fixedDeltaTime) + 8 && _ai.Phase == AttackRunPhase.Recover; tick++)
            {
                Assert.IsFalse(_ai.AttackWindow.IsActive);
                yield return new WaitForFixedUpdate();
            }
            Assert.AreNotEqual(AttackRunPhase.Recover, _ai.Phase,
                "Recovery must finish on its fixed timer rather than wait forever for a positioning gate.");
            Assert.That(_combat.Status.ElapsedTime - recoveryStarted, Is.InRange(recoverySeconds - .06f, recoverySeconds + .06f));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NonChargeOverlapDamagesOnceAndProtectionPreventsImmediateRepeats()
        {
            SetPlayerInput(new FlightInput(-1f, 0f));
            var touching = FlightState.At(_enemy.State.Position, _player.State.Heading);
            SetAircraftState(_player, touching);
            for (int tick = 0; tick < 15; tick++)
            {
                yield return new WaitForFixedUpdate();
                Assert.AreNotEqual(AttackRunPhase.Charge, _ai.Phase);
                Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth, "Aircraft contact is harmful during aiming and recovery, with protection preventing repeated damage.");
            }
            Assert.AreEqual(1, _combat.Status.HitSequence);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StationaryPlayerReceivesContactHitsAtProtectedIntervalsAndEventuallyDies()
        {
            SetPlayerInput(FlightInput.Neutral);
            Vec2 playerPosition = _player.State.Position;
            int lastHit = 0, lastHealth = 5;
            float lastHitTime = -10f;
            bool separated = false, recovered = false;
            for (int tick = 0; tick < 6000 && _combat.Status.IsPlayerAlive; tick++)
            {
                yield return new WaitForFixedUpdate();
                var status = _combat.Status;
                if (lastHit > 0 && DistanceBetweenAircraft() >= 3.5f) separated = true;
                if (lastHit > 0 && _ai.Phase == AttackRunPhase.Recover) recovered = true;
                if (status.HitSequence == lastHit)
                {
                    Assert.AreEqual(lastHealth, status.PlayerHealth);
                    continue;
                }
                Assert.AreEqual(lastHit + 1, status.HitSequence);
                Assert.AreEqual(Mathf.Max(0, lastHealth - _ai.CollisionDamage), status.PlayerHealth);
                Assert.GreaterOrEqual(status.ElapsedTime - lastHitTime, .679f,
                    "At fixed-step resolution, two contacts must be separated by the 0.7 second protection period.");
                lastHit = status.HitSequence;
                lastHealth = status.PlayerHealth;
                lastHitTime = status.ElapsedTime;
            }
            Assert.AreEqual(0, _combat.Status.PlayerHealth, "A non-responsive player must not be protected by an AI that never rearms. " + FlightDiagnostic());
            Assert.AreEqual(5, _combat.Status.HitSequence, "Five hull points require five real contacts at the requested one-point damage.");
            Assert.IsTrue(separated, "The attack cycle must include actual flight away from the player.");
            Assert.IsTrue(recovered);
            Assert.Less(Vec2.Distance(playerPosition, _player.State.Position), 1e-3f);
            Assert.IsFalse(_player.enabled);
            for (int tick = 0; tick < 50; tick++) yield return new WaitForFixedUpdate();
            Assert.Less(_enemy.State.Speed, 0.1f);
            AssertBrake(_ai.ReadInput(Time.fixedDeltaTime));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RecoveryFinishesOnTimeWithoutResettingProtectionAndEnemyCanReaim()
        {
            SetPlayerInput(new FlightInput(-1f, 0f));
            for (int tick = 0; tick < 3000 && _combat.Status.HitSequence == 0; tick++)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, _combat.Status.HitSequence);
            int firstAttack = _ai.AttackId;
            yield return WaitForPhase(AttackRunPhase.Recover, 20);
            Time.timeScale = 1f;
            float recoveryStarted = _combat.Status.ElapsedTime;
            float frozenRecoverySeconds = _ai.RecoverySeconds;
            float protectionAtStart = _combat.Status.InvulnerabilityRemaining;
            int hitsAtStart = _combat.Status.HitSequence;
            for (int tick = 0; tick < Mathf.CeilToInt(frozenRecoverySeconds / Time.fixedDeltaTime) + 8 && _ai.Phase == AttackRunPhase.Recover; tick++)
                yield return new WaitForFixedUpdate();
            Assert.AreNotEqual(AttackRunPhase.Recover, _ai.Phase);
            Assert.That(_combat.Status.ElapsedTime - recoveryStarted, Is.InRange(frozenRecoverySeconds - .1f, frozenRecoverySeconds + .08f));
            Assert.AreEqual(hitsAtStart, _combat.Status.HitSequence);
            float expectedProtection = Mathf.Max(0f, protectionAtStart - (_combat.Status.ElapsedTime - recoveryStarted));
            Assert.AreEqual(expectedProtection, _combat.Status.InvulnerabilityRemaining, .025f,
                "Recovery uses its own timer and must neither extend nor clear contact protection.");
            Time.timeScale = 5f;
            yield return WaitForPhase(AttackRunPhase.Windup, 1000);
            Assert.Greater(_ai.AttackId, firstAttack, "The next complete aiming cycle must resume after recovery, including while contact protection remains.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OutsideAimRange_EnemyKeepsApproachingUntilThePlayerIsVisibleAndInRange()
        {
            Time.timeScale = 0f;
            SetPlayerInput(new FlightInput(-1f, 0f));
            SetAircraftState(_player, FlightState.At(Vec2.Zero));
            SetAircraftState(_enemy, FlightState.At(new Vec2(12f, 0f), Mathf.PI));
            typeof(ChaseAiInputSource).GetField("_source", PrivateInstance).SetValue(_ai, null);
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            float originalDistance = DistanceBetweenAircraft();
            bool observedPursuit = false;
            Time.timeScale = 5f;
            for (int tick = 0; tick < 500 && _ai.Phase != AttackRunPhase.Windup; tick++)
            {
                yield return new WaitForFixedUpdate();
                if (_ai.Phase == AttackRunPhase.Windup) break;
                observedPursuit |= _ai.Phase == AttackRunPhase.Pursuit;
                Assert.AreNotEqual(AttackRunPhase.Positioning, _ai.Phase,
                    "This baseline follows the player directly, without looking for preparation points.");
                Assert.IsFalse(_ai.AttackWindow.IsActive);
            }
            Assert.IsTrue(observedPursuit);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            Assert.Less(DistanceBetweenAircraft(), originalDistance - 3f,
                "Being off-screen must not make the pursuer park outside its aiming range.");
            Assert.LessOrEqual(DistanceBetweenAircraft(), 8.2f);
            Assert.That(_ai.PhaseRemaining, Is.InRange(.75f, .81f));
            Assert.AreEqual(9f, _enemy.Ship.MaxSpeed);
            Assert.AreEqual(210f, _enemy.Ship.TurnRateDegPerSec);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MovingAndTurningPlayerStops_EnemyAttacksTheNewPosition()
        {
            var input = SetPlayerInput(new FlightInput(1f, 0f));
            Vec2 initialPosition = _player.State.Position;
            for (int tick = 0; tick < 80; tick++) yield return new WaitForFixedUpdate();
            input.DesiredInput = new FlightInput(1f, 0.5f);
            for (int tick = 0; tick < 20; tick++) yield return new WaitForFixedUpdate();
            input.DesiredInput = new FlightInput(-1f, 0f);
            for (int tick = 0; tick < 40; tick++) yield return new WaitForFixedUpdate();
            Assert.Greater(Vec2.Distance(initialPosition, _player.State.Position), 5f);
            int hits = _combat.Status.HitSequence;
            for (int tick = 0; tick < 3000 && _combat.Status.HitSequence == hits; tick++)
                yield return new WaitForFixedUpdate();
            Assert.Greater(_combat.Status.HitSequence, hits, "Stopping after moving must allow the enemy to re-establish an attack. " + FlightDiagnostic());
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator CornerPlayer_EnemyFindsVisibleAttackAndCanRecoverInsideArena()
        {
            SetPlayerInput(FlightInput.Neutral);
            SetAircraftState(_player, FlightState.At(new Vec2(199.5f, 149.5f)));
            SetAircraftState(_enemy, FlightState.At(new Vec2(194f, 146f)));
            yield return null;
            for (int tick = 0; tick < 4000 && _combat.Status.HitSequence < 2; tick++)
            {
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(_enemy.ArenaBounds.Value.Contains(_enemy.State.Position));
                Assert.IsTrue(_player.ArenaBounds.Value.Contains(_player.State.Position));
            }
            Assert.GreaterOrEqual(_combat.Status.HitSequence, 2, "Boundary checks must reposition for another fair attack, not abandon a stationary corner target.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DisabledThenDestroyedTarget_EnemyBrakesSafely()
        {
            SetPlayerInput(FlightInput.Neutral);
            for (int tick = 0; tick < 20; tick++) yield return new WaitForFixedUpdate();
            _player.enabled = false;
            AssertBrake(_ai.ReadInput(Time.fixedDeltaTime));
            for (int tick = 0; tick < 40; tick++) yield return new WaitForFixedUpdate();
            Assert.Less(_enemy.State.Speed, 0.1f);
            Object.Destroy(_player.gameObject);
            yield return null;
            AssertBrake(_ai.ReadInput(Time.fixedDeltaTime));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RestartBattle_ResetsHealthAndRestartsTheFirstFullAim()
        {
            SetPlayerInput(FlightInput.Neutral);
            for (int tick = 0; tick < 3000 && (_ai.AttackId < 2 || _combat.Status.HitSequence == 0); tick++)
                yield return new WaitForFixedUpdate();
            Assert.GreaterOrEqual(_ai.AttackId, 2, "First establish an old attack sequence before verifying restart resets it.");
            Assert.Less(_combat.Status.PlayerHealth, 5);
            _combat.RestartBattle();
            yield return null;
            FindSceneAircraft();
            Assert.AreEqual(5, _combat.Status.PlayerHealth);
            Assert.AreEqual(0, _combat.Status.HitSequence);
            Assert.AreEqual(1, _ai.Level);
            Assert.Less(_combat.Status.ElapsedTime, 0.5f);
            Assert.IsTrue(_player.enabled);
            Assert.LessOrEqual(_ai.AttackId, 1,
                "The in-range spawn may begin its first aim immediately, but may not retain an old attack number.");
            if (_ai.Phase == AttackRunPhase.Windup)
                Assert.Greater(_ai.PhaseRemaining, .3f, "Restarting must start a new countdown rather than resume a previous charge.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator FirstSimulationStep_InterpolationUsesEnemySpawnState()
        {
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync("Battle", LoadSceneMode.Single);
            FindSceneAircraft();
            FlightState spawn = _enemy.State;
            typeof(PlayerController).GetMethod("FixedUpdate", PrivateInstance).Invoke(_enemy, null);
            var previous = (FlightState)typeof(PlayerController).GetField("_previous", PrivateInstance).GetValue(_enemy);
            Assert.AreEqual(spawn, previous);
            var view = _enemy.GetComponentInChildren<PlayerView>();
            view.Render(_enemy.State, previous, 0f, _enemy.Ship.BodySprite);
            Assert.AreEqual(spawn.Position.X, view.transform.position.x, 1e-4f);
            Assert.AreEqual(spawn.Position.Y, view.transform.position.y, 1e-4f);
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator WaitForPhase(AttackRunPhase phase, int ticks)
        {
            for (int tick = 0; tick < ticks && _ai.Phase != phase; tick++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(phase, _ai.Phase, $"AI failed to enter {phase}; enemy={_enemy.State.Position}, player={_player.State.Position}.");
        }

        void SeedBattleClock(float elapsed)
        {
            var model = new ContactCombatModel(5, 1, .7f);
            var p = FlightState.At(Vec2.Zero);
            var e = FlightState.At(new Vec2(100f, 0f));
            model.Step(p, p, e, e, elapsed);
            typeof(BattleCombatController).GetField("_model", PrivateInstance).SetValue(_combat, model);
        }

        static void SetAircraftState(PlayerController controller, FlightState state)
        {
            typeof(PlayerController).GetField("_state", PrivateInstance).SetValue(controller, state);
            typeof(PlayerController).GetField("_previous", PrivateInstance).SetValue(controller, state);
            controller.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }

        void FindSceneAircraft()
        {
            var adapters = Object.FindObjectsOfType<ChaseAiInputSource>();
            Assert.AreEqual(1, adapters.Length);
            _ai = adapters[0];
            _enemy = _ai.GetComponent<PlayerController>();
            _player = _ai.Target;
            _combat = Object.FindObjectOfType<BattleCombatController>();
            Assert.IsNotNull(_enemy);
            Assert.IsNotNull(_player);
            Assert.IsNotNull(_combat);
        }

        BattleTestInputSource SetPlayerInput(FlightInput desiredInput)
        {
            var input = _player.GetComponent<BattleTestInputSource>();
            if (input == null) input = _player.gameObject.AddComponent<BattleTestInputSource>();
            input.DesiredInput = desiredInput;
            typeof(PlayerController).GetField("inputSource", PrivateInstance).SetValue(_player, input);
            return input;
        }

        void BeginFreshAim()
        {
            Assert.AreEqual(0f, Time.timeScale, "Start the timing fixture while simulation is paused.");
            typeof(ChaseAiInputSource).GetField("_source", PrivateInstance).SetValue(_ai, null);
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            Assert.That(_ai.PhaseRemaining, Is.EqualTo(.8f).Within(.0001f),
                "Capture the actual beginning of a fresh countdown, not an aiming phase already advanced by setup frames.");
        }

        float DistanceBetweenAircraft() => Vec2.Distance(_enemy.State.Position, _player.State.Position);

        string FlightDiagnostic() => $"t={_combat.Status.ElapsedTime:0.00}, level={_ai.Level}, phase={_ai.Phase}, attacks={_ai.AttackId}, " +
            $"player=({_player.State.Position.X:0.00},{_player.State.Position.Y:0.00}), enemy=({_enemy.State.Position.X:0.00},{_enemy.State.Position.Y:0.00}), " +
            $"enemySpeed={_enemy.State.Speed:0.00}, aspect={Camera.main.aspect:0.000}, screen={Screen.width}x{Screen.height}";

        static void AssertInCamera(Vector3 position)
        {
            Vector3 viewport = Camera.main.WorldToViewportPoint(position);
            Assert.Greater(viewport.z, 0f);
            Assert.That(viewport.x, Is.InRange(0f, 1f));
            Assert.That(viewport.y, Is.InRange(0f, 1f));
        }

        static void AssertBrake(FlightInput input)
        {
            Assert.AreEqual(-1f, input.Throttle);
            Assert.AreEqual(0f, input.Steer);
        }
    }
}
