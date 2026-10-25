using System.Collections;
using System.Linq;
using System.Reflection;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.Gameplay.View;
using DogfightThreatAI.Gameplay.World;
using DogfightThreatAI.UI.Menu;
using DogfightThreatAI.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace DogfightThreatAI.Tests.Menu
{
    public sealed class MenuToBattleTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator StartGameAndReturnMenuPreserveBothScenesAndResetAircraft()
        {
            Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.StartMenuScene, LoadSceneMode.Single);
            var menu = Object.FindObjectOfType<MainMenuController>();
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.Phase, Is.EqualTo(MainMenuController.MenuPhase.AwaitInput));
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.StartMenuScene), "Splash must wait for user input");
            Assert.That(Object.FindObjectOfType<PlayerController>(), Is.Null);

            menu.RevealMenu();
            yield return new WaitForSecondsRealtime(1.15f);
            Assert.That(menu.Phase, Is.EqualTo(MainMenuController.MenuPhase.Menu));
            var start = menu.GetComponentsInChildren<Button>().Single(button => button.name == "StartGame");
            Assert.That(start.IsInteractable(), Is.True);
            // Invoke the real Button pointer event, including its scene-bound menu action.
            ExecuteEvents.Execute(start.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (SceneManager.GetActiveScene().name != SceneNavigation.GameScene && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.GameScene));
            yield return null;

            Assert.That(Object.FindObjectOfType<MainMenuController>(), Is.Null, "Menu overlay must be unloaded");
            AssertRiverValleyMap();
            var player = AssertSingleBattle().Player;
            Assert.That(player.isActiveAndEnabled, Is.True);
            Assert.That(player.Ship, Is.Not.Null);
            Assert.That(player.GetComponentInChildren<SpriteRenderer>().sprite, Is.EqualTo(player.Ship.BodySprite));
            Assert.That(Camera.main, Is.Not.Null);
            Assert.That(Camera.main.GetComponent<CameraFollow>().isActiveAndEnabled, Is.True);
            var input = player.gameObject.AddComponent<IntegrationFlightInput>();
            typeof(PlayerController).GetField("inputSource", Private).SetValue(player, input);
            Vector3 initial = player.transform.position;
            yield return new WaitForSeconds(.6f);
            Assert.That(Vector3.Distance(initial, player.transform.position), Is.GreaterThan(.05f),
                "Flight simulation must continue after leaving the menu");

            var navigation = Object.FindObjectOfType<SceneNavigation>();
            Assert.That(navigation, Is.Not.Null);
            var back = navigation.GetComponentsInChildren<Button>().Single(button => button.name == "ReturnToMainMenu");
            ExecuteEvents.Execute(back.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
            deadline = Time.realtimeSinceStartup + 15f;
            while (SceneManager.GetActiveScene().name != SceneNavigation.StartMenuScene && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.StartMenuScene));
            Assert.That(Object.FindObjectOfType<PlayerController>(), Is.Null);
            Assert.That(Object.FindObjectOfType<BattleMapView>(), Is.Null);
            menu = Object.FindObjectOfType<MainMenuController>();
            Assert.That(menu.Phase, Is.EqualTo(MainMenuController.MenuPhase.AwaitInput));
            menu.RevealMenu();
            yield return new WaitForSecondsRealtime(1.15f);
            menu.GetComponentsInChildren<Button>().Single(button => button.name == "StartGame").onClick.Invoke();
            deadline = Time.realtimeSinceStartup + 15f;
            while (SceneManager.GetActiveScene().name != SceneNavigation.GameScene && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.GameScene));
            Assert.That(AssertSingleBattle().Player.transform.position.y, Is.EqualTo(0).Within(.1f),
                "Starting again must create a fresh aircraft at spawn");
        }

        [UnityTest]
        public IEnumerator ReturningToMenuAndReenteringKeepsRiverValleyAndResetsDamagedBattle()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.StartMenuScene, LoadSceneMode.Single);
            yield return StartGameFromMenu();
            Time.timeScale = 0f;

            var firstBattle = AssertSingleBattle();
            var firstMap = AssertRiverValleyMap();
            var mapAsset = firstMap.Map;
            var firstPlayer = firstBattle.Player;
            var firstEnemy = firstBattle.Enemy;
            AssertFreshBattle(firstBattle);

            // Resolve a real contact, then spend a dash on the player's actual boost model.
            // Pausing makes the fixture independent of attack timing and physical keyboard input.
            SetAircraftState(firstEnemy, firstPlayer.State);
            typeof(BattleCombatController).GetMethod("FixedUpdate", Private).Invoke(firstBattle, null);
            Assert.That(firstBattle.Status.PlayerHealth, Is.EqualTo(4));
            Assert.That(firstBattle.Status.HitSequence, Is.EqualTo(1));
            Assert.That(firstBattle.Status.InvulnerabilityRemaining, Is.GreaterThan(0f));
            var keyboard = firstPlayer.GetComponent<KeyboardInputSource>();
            var boost = (PlayerBoostModel)typeof(KeyboardInputSource).GetField("_boost", Private).GetValue(keyboard);
            boost.Step(true, false, .02f, 9f);
            Assert.That(keyboard.Energy, Is.EqualTo(35f).Within(.001f));
            Assert.That(keyboard.DashCooldownRemaining, Is.GreaterThan(0f));

            var navigation = Object.FindObjectsOfType<SceneNavigation>().Single();
            Click(navigation.GetComponentsInChildren<Button>().Single(button => button.name == "ReturnToMainMenu"));
            yield return WaitForScene(SceneNavigation.StartMenuScene);
            Assert.That(Time.timeScale, Is.EqualTo(1f), "Returning from a paused battle must restore the menu clock");
            Assert.That(Object.FindObjectsOfType<PlayerController>(), Is.Empty);
            Assert.That(Object.FindObjectsOfType<ChaseAiInputSource>(), Is.Empty);
            Assert.That(Object.FindObjectsOfType<BattleCombatController>(), Is.Empty);
            Assert.That(Object.FindObjectsOfType<BattleMapView>(), Is.Empty);
            Assert.That(firstBattle == null && firstPlayer == null && firstEnemy == null && firstMap == null, Is.True,
                "Returning must unload every instance from the previous battle");

            yield return StartGameFromMenu();
            Time.timeScale = 0f;
            var freshBattle = AssertSingleBattle();
            var freshMap = AssertRiverValleyMap();
            Assert.That(freshMap.Map, Is.SameAs(mapAsset), "Reentry must keep the authored river-valley map asset");
            AssertFreshBattle(freshBattle);
            Assert.That(Object.FindObjectOfType<MainMenuController>(), Is.Null);
            Assert.That(Object.FindObjectsOfType<SceneNavigation>().Length, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        static IEnumerator StartGameFromMenu()
        {
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.StartMenuScene));
            var menu = Object.FindObjectsOfType<MainMenuController>().Single();
            Assert.That(menu.Phase, Is.EqualTo(MainMenuController.MenuPhase.AwaitInput));
            menu.RevealMenu();
            yield return new WaitForSecondsRealtime(1.15f);
            Assert.That(menu.Phase, Is.EqualTo(MainMenuController.MenuPhase.Menu));
            Click(menu.GetComponentsInChildren<Button>().Single(button => button.name == "StartGame"));
            yield return WaitForScene(SceneNavigation.GameScene);
        }

        static IEnumerator WaitForScene(string sceneName)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (SceneManager.GetActiveScene().name != sceneName && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName));
            yield return null;
        }

        static void Click(Button button)
        {
            Assert.That(button.IsInteractable(), Is.True);
            Assert.That(EventSystem.current, Is.Not.Null);
            ExecuteEvents.Execute(button.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                ExecuteEvents.pointerClickHandler);
        }

        static BattleCombatController AssertSingleBattle()
        {
            var battles = Object.FindObjectsOfType<BattleCombatController>();
            Assert.That(battles.Length, Is.EqualTo(1), "Each game scene must have one combat authority");
            var battle = battles[0];
            Assert.That(Object.FindObjectsOfType<PlayerController>().Length, Is.EqualTo(2));
            Assert.That(battle.Player, Is.Not.Null);
            Assert.That(battle.Enemy, Is.Not.Null);
            Assert.That(battle.Enemy, Is.Not.SameAs(battle.Player));
            var enemies = Object.FindObjectsOfType<ChaseAiInputSource>();
            Assert.That(enemies.Length, Is.EqualTo(1));
            Assert.That(enemies[0].GetComponent<PlayerController>(), Is.SameAs(battle.Enemy));
            Assert.That(enemies[0].Target, Is.SameAs(battle.Player));
            Assert.That(battle.Player.gameObject.scene.name, Is.EqualTo(SceneNavigation.GameScene));
            Assert.That(battle.Enemy.gameObject.scene.name, Is.EqualTo(SceneNavigation.GameScene));
            return battle;
        }

        static BattleMapView AssertRiverValleyMap()
        {
            var maps = Object.FindObjectsOfType<BattleMapView>();
            Assert.That(maps.Length, Is.EqualTo(1));
            var map = maps[0];
            Assert.That(map.Map, Is.Not.Null);
            Assert.That(map.Map.IsValid, Is.True);
            Assert.That(map.Map.DisplayName, Is.EqualTo("河谷农田"));
            Assert.That(map.LoadedChunkCount, Is.GreaterThan(0));
            Assert.That(map.GetComponentsInChildren<Tilemap>().Any(chunk => chunk.GetUsedTilesCount() > 0), Is.True);
            Assert.That(Object.FindObjectOfType<DebugStarfield>(), Is.Null);
            return map;
        }

        static void AssertFreshBattle(BattleCombatController battle)
        {
            Assert.That(battle.Player.isActiveAndEnabled && battle.Enemy.isActiveAndEnabled, Is.True);
            Assert.That(battle.Status.MaxPlayerHealth, Is.EqualTo(5));
            Assert.That(battle.Status.PlayerHealth, Is.EqualTo(5), "A new battle must discard the previous hull damage");
            Assert.That(battle.Status.HitSequence, Is.EqualTo(0));
            Assert.That(battle.Status.InvulnerabilityRemaining, Is.EqualTo(0f));
            var keyboard = battle.Player.GetComponent<KeyboardInputSource>();
            Assert.That(keyboard, Is.Not.Null);
            Assert.That(keyboard.Energy, Is.EqualTo(60f).Within(.001f));
            Assert.That(keyboard.DashCooldownRemaining, Is.EqualTo(0f));
            Assert.That(keyboard.IsOverheated, Is.False);
            Assert.That(battle.Enemy.GetComponent<ChaseAiInputSource>().BoostEnergy, Is.EqualTo(60f).Within(.001f));
        }

        static void SetAircraftState(PlayerController aircraft, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(aircraft, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(aircraft, state);
            aircraft.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            var previous = SceneManager.GetActiveScene();
            var empty = SceneManager.CreateScene("Menu integration cleanup");
            SceneManager.SetActiveScene(empty);
            if (previous.IsValid() && previous.isLoaded) yield return SceneManager.UnloadSceneAsync(previous);
            Time.timeScale = 1;
        }
    }
}
