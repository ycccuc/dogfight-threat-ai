using System.Collections;
using System.Linq;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.Lobby;
using DogfightThreatAI.UI.Lobby;
using DogfightThreatAI.UI.Menu;
using DogfightThreatAI.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace DogfightThreatAI.Tests.Menu
{
    public sealed class LobbyFlowTests
    {
        [UnityTest]
        public IEnumerator MenuLobbyAndReturnHaveValidLocalStateAndNoCombat()
        {
            Time.timeScale = 1f;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.StartMenuScene);
            var menu = Object.FindObjectOfType<MainMenuController>();
            menu.RevealMenu();
            yield return new WaitForSecondsRealtime(1.2f);
            menu.GetComponentsInChildren<Button>().Single(button => button.name == "Lobby").onClick.Invoke();
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.LobbyScene));
            var lobby = Object.FindObjectOfType<LobbyController>();
            Assert.That(lobby, Is.Not.Null);
            Assert.That(lobby.Data.IsValid, Is.True);
            Assert.That(lobby.Simulation, Is.Not.Null);
            Assert.That(Object.FindObjectOfType<LobbyHudView>().PlayerCountText, Is.EqualTo("1 / 4"));
            Assert.That(Object.FindObjectOfType<Tilemap>(), Is.Not.Null);
            Assert.That(Object.FindObjectOfType<BattleCombatController>(), Is.Null);
            Object.FindObjectOfType<LobbyHudView>().RequestExit();
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.StartMenuScene));
            Assert.That(Object.FindObjectOfType<LobbyController>(), Is.Null);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
