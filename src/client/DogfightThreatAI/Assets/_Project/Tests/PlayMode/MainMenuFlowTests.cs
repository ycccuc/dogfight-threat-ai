using System.Collections;
using DogfightThreatAI.UI.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DogfightThreatAI.Tests.Menu
{
    /// <summary>Regression coverage for input gating, modal focus and unapplied settings.</summary>
    public sealed class MainMenuFlowTests
    {
        GameObject _root;
        MainMenuController _menu;
        GameObject _previousSelection;

        [SetUp]
        public void SetUp()
        {
            _previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            _root = new GameObject("Menu Flow Test", typeof(RectTransform));
            _menu = _root.AddComponent<MainMenuController>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_previousSelection);
            Object.Destroy(_root);
            yield return null;
        }

        IEnumerator Reveal()
        {
            _menu.RevealMenu();
            yield return new WaitForSecondsRealtime(1.15f);
            Assert.AreEqual(MainMenuController.MenuPhase.Menu, _menu.Phase);
        }

        [UnityTest]
        public IEnumerator SplashAndTransitionCannotLaunchBattle()
        {
            _menu.StartBattle();
            Assert.AreEqual(MainMenuController.MenuPhase.AwaitInput, _menu.Phase);
            _menu.RevealMenu();
            _menu.RevealMenu();
            _menu.StartBattle();
            Assert.AreEqual(MainMenuController.MenuPhase.Revealing, _menu.Phase);
            yield return new WaitForSecondsRealtime(1.15f);
            Assert.AreEqual(MainMenuController.MenuPhase.Menu, _menu.Phase);
            Assert.AreEqual("StartGame", EventSystem.current.currentSelectedGameObject.name);
        }

        [UnityTest]
        public IEnumerator ModalBlocksMenuAndRestoresItsOriginFocus()
        {
            yield return Reveal();
            _menu.OpenSettings();
            yield return new WaitForSecondsRealtime(.3f);
            _menu.StartBattle();
            _menu.OpenCredits();
            Assert.AreEqual(MainMenuController.MenuPhase.Panel, _menu.Phase);
            Assert.AreEqual(1, _root.GetComponentsInChildren<Slider>().Length);
            _menu.ClosePanel();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual(MainMenuController.MenuPhase.Menu, _menu.Phase);
            Assert.AreEqual("Settings", EventSystem.current.currentSelectedGameObject.name);
        }

        [UnityTest]
        public IEnumerator ClosingSettingsDiscardsDraftWithoutChangingAudio()
        {
            yield return Reveal();
            float audio = AudioListener.volume;
            _menu.OpenSettings();
            yield return new WaitForSecondsRealtime(.3f);
            var slider = _root.GetComponentInChildren<Slider>();
            float saved = slider.value;
            slider.value = saved > .5f ? .2f : .8f;
            _menu.ClosePanel();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual(audio, AudioListener.volume);
            _menu.OpenSettings();
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(saved, _root.GetComponentInChildren<Slider>().value);
        }

        [UnityTest]
        public IEnumerator ControlsAndCreditsHaveWorkingReturnPaths()
        {
            yield return Reveal();
            _menu.OpenControls();
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual("Back", EventSystem.current.currentSelectedGameObject.name);
            EventSystem.current.currentSelectedGameObject.GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual("Controls", EventSystem.current.currentSelectedGameObject.name);
            _menu.OpenCredits();
            yield return new WaitForSecondsRealtime(.3f);
            EventSystem.current.currentSelectedGameObject.GetComponent<Button>().onClick.Invoke();
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual("Credits", EventSystem.current.currentSelectedGameObject.name);
        }
    }
}
