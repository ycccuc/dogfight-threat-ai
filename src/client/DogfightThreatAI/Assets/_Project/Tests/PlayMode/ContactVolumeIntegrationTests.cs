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
using DogfightThreatAI.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DogfightThreatAI.Tests.PlayMode
{
    [Category("LegacyRam")]
    public sealed class ContactVolumeIntegrationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        PlayerController _player, _enemy;
        ChaseAiInputSource _ai;
        BattleCombatController _combat;
        float _originalScale;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            _originalScale = Time.timeScale;
            Time.timeScale = 0f;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.GameScene, LoadSceneMode.Single);
            LegacyRamFixture.Activate();
            _combat = Object.FindObjectOfType<BattleCombatController>();
            _ai = Object.FindObjectOfType<ChaseAiInputSource>();
            _player = _combat.Player;
            _enemy = _combat.Enemy;
            SetState(_player, FlightState.At(Vec2.Zero));
            SetState(_enemy, FlightState.At(new Vec2(4f, 0f), Mathf.PI));
            SetInput(_player, new FlightInput(-1f, 0f));
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
        }

        [UnityTearDown]
        public IEnumerator RestoreScale()
        {
            Time.timeScale = _originalScale;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContactOutsideChargeImmediatelyDamagesThePlayer()
        {
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            Assert.IsFalse(_ai.AttackWindow.IsActive);
            SetState(_enemy, FlightState.At(Vec2.Zero));
            TickCombat();
            Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth,
                "Aircraft volume contact must cause damage during aiming too; it is no longer gated by a charge window.");
            Assert.AreEqual(1, _combat.Status.HitSequence);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CollisionInvulnerabilityLastsPointSevenSeconds()
        {
            EnterCharge();
            SetState(_enemy, FlightState.At(Vec2.Zero));
            TickCombat();
            Assert.AreEqual(1, _combat.Status.HitSequence);
            Assert.That(_combat.Status.InvulnerabilityRemaining, Is.InRange(.67f, .701f),
                "A collision grants 0.7 seconds of protection, rather than the old 2 seconds.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChargeDoesNotRenderAnyPathStreakArrowOrDangerMarker()
        {
            EnterCharge();
            Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
            yield return null;
            var lines = _enemy.GetComponentsInChildren<LineRenderer>();
            foreach (var line in lines)
                if (line.name.StartsWith("AttackCue_"))
                    Assert.IsFalse(line.enabled, $"The charge marker must be removed: {line.name} was still visible.");
            Assert.IsFalse(_enemy.GetComponent<AttackCueView>().IsShowingCue);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SavedBattleContainsBothRealRootContactVolumes()
        {
            foreach (var plane in new[] { _player, _enemy })
            {
                var hitbox = plane.GetComponent<AircraftHitbox>();
                Assert.IsNotNull(hitbox, "The saved scene must contain contact volumes, not only add them from a test.");
                Assert.IsNotNull(hitbox.Collider);
                Assert.AreSame(plane.gameObject, hitbox.Collider.gameObject);
                Assert.AreEqual(Vector2.zero, hitbox.Collider.offset);
                Assert.IsTrue(hitbox.Collider.isTrigger);
                Assert.IsTrue(hitbox.IsActive);
                Assert.AreEqual(.5f, hitbox.WorldRadius, .001f);
                Assert.AreEqual(RigidbodyType2D.Kinematic, plane.GetComponent<Rigidbody2D>().bodyType);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CollisionUsesTheCurrentTwoColliderRadiiInsteadOfAnOldFixedDistance()
        {
            var playerHitbox = _player.GetComponent<AircraftHitbox>();
            var enemyHitbox = _enemy.GetComponent<AircraftHitbox>();
            playerHitbox.Collider.radius = .2f;
            enemyHitbox.Collider.radius = .2f;
            SetState(_enemy, FlightState.At(new Vec2(.9f, 0f)));
            TickCombat();
            Assert.AreEqual(5, _combat.Status.PlayerHealth,
                "Centers only .9 apart must not collide when the two real radii total .4.");
            enemyHitbox.Collider.radius = .8f;
            TickCombat();
            Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth,
                "Increasing the actual collider must change contact immediately; a separate fixed damage radius is not allowed.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledColliderOrHitboxStopsDamageWhileTheCombatClockContinues()
        {
            var hitbox = _enemy.GetComponent<AircraftHitbox>();
            SetState(_enemy, FlightState.At(Vec2.Zero));
            hitbox.Collider.enabled = false;
            float before = _combat.Status.ElapsedTime;
            for (int i = 0; i < 5; i++) TickCombat();
            Assert.AreEqual(5, _combat.Status.PlayerHealth);
            Assert.Greater(_combat.Status.ElapsedTime, before + .09f);
            hitbox.Collider.enabled = true;
            hitbox.enabled = false;
            TickCombat();
            Assert.AreEqual(5, _combat.Status.PlayerHealth);
            hitbox.enabled = true;
            TickCombat();
            Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ContinuousContactCannotRepeatDamageBeforeProtectionExpires()
        {
            _ai.ReadInput(Time.fixedDeltaTime);
            int aimingId = _ai.AttackId;
            SetState(_enemy, FlightState.At(Vec2.Zero));
            TickCombat();
            Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth);
            int protectedSteps = 0;
            while (_combat.Status.InvulnerabilityRemaining > Time.fixedDeltaTime + .0001f && protectedSteps++ < 100)
            {
                TickCombat();
                Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth);
                Assert.AreEqual(1, _combat.Status.HitSequence);
            }
            Assert.Greater(protectedSteps, 30);
            TickCombat();
            TickCombat();
            Assert.AreEqual(Mathf.Max(0, 5 - 2 * _ai.CollisionDamage), _combat.Status.PlayerHealth);
            Assert.AreEqual(2, _combat.Status.HitSequence);
            Assert.AreEqual(aimingId, _ai.AttackId,
                "Contact damage after invulnerability is independent of attack IDs; the actor was held in the same aim.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecoveryContactCanDamageOnceProtectionHasExpired()
        {
            _ai.ReadInput(Time.fixedDeltaTime);
            var model = Model();
            var player = _player.State;
            var far = FlightState.At(new Vec2(100f, 0f));
            // A missed attack has no active protection. Its shorter recovery must still
            // allow a first real contact, without assuming recovery outlasts 0.7 seconds.
            model.Step(player, player, far, far, .8f);
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
            model.Step(player, player, far, far, .9f);
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Recover, _ai.Phase);
            Assert.AreEqual(0f, _combat.Status.InvulnerabilityRemaining);
            Assert.AreEqual(0, _combat.Status.HitSequence);
            SetState(_enemy, FlightState.At(Vec2.Zero));
            TickCombat();
            Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth,
                "Recovery is no longer an arbitrary immunity phase when the two aircraft volumes touch.");
            Assert.AreEqual(1, _combat.Status.HitSequence);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FortyFiveSpeedSweepDetectsCrossingEvenWhenBothEndpointsMiss()
        {
            _player.GetComponent<AircraftHitbox>().Collider.radius = .1f;
            _enemy.GetComponent<AircraftHitbox>().Collider.radius = .1f;
            var previous = FlightState.At(new Vec2(-.45f, 0f));
            var current = new FlightState { Position = new Vec2(.45f, 0f), Heading = 0f, Velocity = new Vec2(45f, 0f) };
            SetState(_enemy, current);
            typeof(PlayerController).GetField("_previous", Private).SetValue(_enemy, previous);
            typeof(PlayerController).GetField("_hasPrevious", Private).SetValue(_enemy, true);
            float radius = _player.GetComponent<AircraftHitbox>().WorldRadius + _enemy.GetComponent<AircraftHitbox>().WorldRadius;
            Assert.Greater(Vec2.Distance(previous.Position, _player.State.Position), radius);
            Assert.Greater(Vec2.Distance(current.Position, _player.State.Position), radius);
            Assert.AreEqual(45f, Vec2.Distance(previous.Position, current.Position) / Time.fixedDeltaTime, .001f);
            TickCombat();
            Assert.AreEqual(5 - _ai.CollisionDamage, _combat.Status.PlayerHealth, "The new dash must not tunnel through a real contact volume.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator HitShakeMovesOnlyArtAndReturnsToItsLogicalPosition()
        {
            SetState(_enemy, FlightState.At(Vec2.Zero));
            TickCombat();
            var view = _player.GetComponentInChildren<PlayerView>();
            Assert.IsNotNull(view);
            Assert.IsTrue(view.IsHitShaking);
            FlightState logical = _player.State;
            Vector2 rootPosition = _player.GetComponent<Rigidbody2D>().position;
            Vector2 colliderOffset = _player.GetComponent<AircraftHitbox>().Collider.offset;
            view.Render(logical, logical, 1f, _player.Ship.BodySprite);
            // Freeze simulation without disabling the independent Art feedback component.
            _player.enabled = false;
            Time.timeScale = 1f;
            yield return null;
            Assert.Greater(Vector2.Distance(new Vector2(view.transform.position.x, view.transform.position.y),
                new Vector2(logical.Position.X, logical.Position.Y)), .005f);
            Assert.AreEqual(logical, _player.State);
            Assert.AreEqual(rootPosition, _player.GetComponent<Rigidbody2D>().position);
            Assert.AreEqual(colliderOffset, _player.GetComponent<AircraftHitbox>().Collider.offset);
            yield return new WaitForSeconds(.25f);
            Assert.IsFalse(view.IsHitShaking);
            Assert.Less(Vector2.Distance(new Vector2(view.transform.position.x, view.transform.position.y),
                new Vector2(logical.Position.X, logical.Position.Y)), .0001f);
            Assert.AreEqual(logical, _player.State);
            Assert.AreEqual(rootPosition, _player.GetComponent<Rigidbody2D>().position);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AimKeepsProfileSpeedAndChargeMovesAtAbsoluteFortyFiveThenRestores()
        {
            foreach (var hitbox in Object.FindObjectsOfType<AircraftHitbox>()) hitbox.Collider.enabled = false;
            SetState(_enemy, new FlightState { Position = new Vec2(4f, 0f), Heading = Mathf.PI, Velocity = new Vec2(-9f, 0f) });
            FlightInput aim = _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            Assert.AreEqual(1f, aim.Throttle, "Aiming may turn but must not intentionally throttle down.");
            Assert.AreEqual(1f, ((IFlightSpeedSource)_ai).SpeedMultiplier);
            TickController(_enemy);
            float normal = _enemy.State.Speed;
            Assert.AreEqual(_ai.CruiseSpeed, normal, .01f);
            EnterCharge();
            Assert.AreEqual(5f, ((IFlightSpeedSource)_ai).SpeedMultiplier);
            TickController(_enemy);
            float dash = _enemy.State.Speed;
            Assert.That(dash, Is.InRange(44.8f, 45.01f));
            Assert.AreEqual(45f, dash, .01f, "Higher cruise profiles must not multiply the charge above 45.");
            for (int tick = 0; tick < 100 && _ai.Phase == AttackRunPhase.Charge; tick++)
            {
                TickCombat();
                TickController(_enemy);
            }
            Assert.AreEqual(AttackRunPhase.Recover, _ai.Phase);
            Assert.AreEqual(1f, ((IFlightSpeedSource)_ai).SpeedMultiplier);
            Assert.LessOrEqual(_enemy.State.Speed, _ai.CruiseSpeed + .01f, "Ending the dash must not leave an overspeed recovery state.");
            Assert.AreEqual(9f, _player.Ship.MaxSpeed);
            yield return null;
        }

        void EnterCharge()
        {
            typeof(ChaseAiInputSource).GetField("_source", Private).SetValue(_ai, null);
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Windup, _ai.Phase);
            var model = Model();
            var p = _player.State;
            var e = _enemy.State;
            model.Step(p, p, e, e, .8f);
            _ai.ReadInput(Time.fixedDeltaTime);
            Assert.AreEqual(AttackRunPhase.Charge, _ai.Phase);
        }

        static void SetState(PlayerController controller, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(controller, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(controller, state);
            controller.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }

        static void SetInput(PlayerController controller, FlightInput input)
        {
            var source = controller.gameObject.AddComponent<BattleTestInputSource>();
            source.DesiredInput = input;
            typeof(PlayerController).GetField("inputSource", Private).SetValue(controller, source);
        }

        void TickCombat() => typeof(BattleCombatController).GetMethod("FixedUpdate", Private).Invoke(_combat, null);
        ContactCombatModel Model() => (ContactCombatModel)typeof(BattleCombatController).GetField("_model", Private).GetValue(_combat);
        static void TickController(PlayerController controller)
            => typeof(PlayerController).GetMethod("FixedUpdate", Private).Invoke(controller, null);
    }
}
