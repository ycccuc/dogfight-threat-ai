using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.UI.Battle;
using DogfightThreatAI.UI.Navigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DogfightThreatAI.Tests.PlayMode
{
    public sealed class CannonBattleTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const string DifficultyKey = "Dogfight.GunnerDifficulty";
        BattleCombatController _battle;
        BattleMenuView _menu;
        bool _hadPreference;
        int _oldPreference;

        [UnitySetUp] public IEnumerator Setup()
        {
            _hadPreference = PlayerPrefs.HasKey(DifficultyKey); _oldPreference = PlayerPrefs.GetInt(DifficultyKey);
            Time.timeScale = 0;
            yield return SceneManager.LoadSceneAsync(SceneNavigation.GameScene);
            yield return null;
            _battle = Object.FindObjectOfType<BattleCombatController>();
            _menu = Object.FindObjectOfType<BattleMenuView>();
            Assert.That(_battle.UsesProjectileCombat, Is.True); Assert.That(_menu, Is.Not.Null);
        }
        [UnityTest] public IEnumerator SceneUsesOneCannonForBothAircraftAndSweptRoundDamagesEnemy()
        {
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(5, 0), Mathf.PI);
            SetState(_battle.Player, p); SetState(_battle.Enemy, e);
            _battle.Enemy.GetComponent<ChaseAiInputSource>().enabled = false;
            var input = _battle.Player.GetComponent<WeaponInputSource>();
            input.Capture(true, true, false);
            Step(); input.Capture(false, false, false);
            Assert.That(_battle.Simulation.Projectiles.Count, Is.EqualTo(1));
            for (int i = 0; i < 7; i++) Step();
            Assert.That(_battle.Simulation.EnemyHealth, Is.EqualTo(4));
            Assert.That(_battle.Status.PlayerHealth, Is.EqualTo(5));
            yield return null;
        }
        [UnityTest] public IEnumerator EnemyUsesBehaviorTreeToFireAndFiveSegmentHudTracksHealth()
        {
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(10, 0), Mathf.PI);
            SetState(_battle.Player, p); SetState(_battle.Enemy, e);
            var ai = _battle.Enemy.GetComponent<ChaseAiInputSource>(); ai.SetDifficulty(4);
            for (int i = 0; i < 25; i++) { ai.ReadInput(.02f); Step(); }
            Assert.That(ai.Gunner, Is.Not.Null);
            Assert.That(_battle.Status.PlayerHealth, Is.EqualTo(4));
            yield return null;
            Assert.That(Object.FindObjectOfType<BattleHudView>().Ring.FilledSegments, Is.EqualTo(4));
            Canvas.ForceUpdateCanvases();
            var ring = Object.FindObjectOfType<BattleHudView>().Ring;
            Assert.That(ring.GetComponent<CanvasRenderer>(), Is.Not.Null, "A health value without a renderer is not a visible HUD.");
            var mesh = ring.canvasRenderer.GetMesh();
            Assert.That(mesh, Is.Not.Null); Assert.That(mesh.vertexCount, Is.GreaterThan(0));
        }
        [UnityTest] public IEnumerator PauseFreezesBattleAndDifficultyAppliesThenResumeRestoresClock()
        {
            Time.timeScale = 1; _menu.OpenPause();
            Assert.That(Time.timeScale, Is.Zero); float before = _battle.Status.ElapsedTime;
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(_battle.Status.ElapsedTime, Is.EqualTo(before));
            _menu.OpenDifficulty(); _menu.SelectDifficulty(2);
            Assert.That(_battle.Enemy.GetComponent<ChaseAiInputSource>().DifficultyOverride, Is.EqualTo(3));
            Assert.That(PlayerPrefs.GetInt(DifficultyKey), Is.EqualTo(2));
            yield return Capture("cannon-difficulty.png");
            _menu.OpenPause(); yield return Capture("cannon-pause.png");
            _menu.Resume(); Assert.That(Time.timeScale, Is.EqualTo(1)); Assert.That(_menu.IsOpen, Is.False);
        }
        [UnityTest] public IEnumerator QuitConfirmationUsesNativeMenuAndCancelReturnsToPause()
        {
            _menu.OpenPause(); _menu.OpenQuit();
            Assert.That(_menu.GetComponentsInChildren<Text>().Any(t => t.text == "退出游戏？"), Is.True);
            yield return Capture("cannon-quit.png");
            _menu.GetComponentsInChildren<Button>().Single(b => b.name == "CancelQuit").onClick.Invoke();
            Assert.That(_menu.GetComponentsInChildren<Button>().Any(b => b.name == "Resume"), Is.True);
        }
        [UnityTest] public IEnumerator MenuPointerInputCannotLeakIntoNextShot()
        {
            var input = _battle.Player.GetComponent<WeaponInputSource>();
            input.Capture(true, true, true); input.Capture(true, false, false);
            Assert.That(input.Consume(), Is.False);
            input.Capture(false, false, false); input.Capture(true, true, false);
            Assert.That(input.Consume(), Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator HudDisplaysFourSegmentsAndBothPhysicalRounds()
        {
            var p = FlightState.At(new Vec2(-3, 0)); var e = FlightState.At(new Vec2(0, 0), Mathf.PI);
            var sim = _battle.Simulation;
            sim.Tick(.02f, p, p, e, e, .5f, .5f, false, true);
            sim.Tick(.08f, p, p, e, e, .5f, .5f, false, false);
            e = FlightState.At(new Vec2(4, 3), Mathf.PI);
            SetState(_battle.Player, p); SetState(_battle.Enemy, e);
            sim.Tick(.8f, p, p, e, e, .5f, .5f, false, false);
            sim.Tick(.02f, p, p, e, e, .5f, .5f, true, true);
            sim.Tick(.04f, p, p, e, e, .5f, .5f, false, false);
            yield return null;
            Assert.That(Object.FindObjectOfType<BattleHudView>().Ring.FilledSegments, Is.EqualTo(4));
            Assert.That(_battle.GetComponentsInChildren<LineRenderer>().Count(l => l.enabled && l.name.StartsWith("Cannon round")), Is.EqualTo(2));
            yield return Capture("cannon-hud.png");
        }
        [UnityTest] public IEnumerator DestroyingEnemyOpensResultAndStopsBothAircraft()
        {
            var p = FlightState.At(Vec2.Zero); var e = FlightState.At(new Vec2(3, 0));
            SetState(_battle.Player, p); SetState(_battle.Enemy, e);
            _battle.Enemy.GetComponent<ChaseAiInputSource>().enabled = false;
            var sim = _battle.Simulation;
            for (int i = 0; i < 5; i++)
            {
                sim.Tick(.8f, p, p, e, e, .5f, .5f, true, false);
                sim.Tick(.1f, p, p, e, e, .5f, .5f, false, false);
            }
            Step(); yield return null;
            Assert.That(sim.IsFinished, Is.True); Assert.That(sim.EnemyHealth, Is.Zero);
            Assert.That(_battle.Player.enabled || _battle.Enemy.enabled, Is.False);
            Assert.That(_menu.IsOpen, Is.True);
            Assert.That(_menu.GetComponentsInChildren<Text>().Any(t => t.text == "空域已清除"), Is.True);
        }
        void Step() => typeof(BattleCombatController).GetMethod("FixedUpdate", Private).Invoke(_battle, null);
        static void SetState(PlayerController aircraft, FlightState state)
        {
            typeof(PlayerController).GetField("_state", Private).SetValue(aircraft, state);
            typeof(PlayerController).GetField("_previous", Private).SetValue(aircraft, state);
            aircraft.transform.position = new Vector3(state.Position.X, state.Position.Y);
            aircraft.GetComponent<Rigidbody2D>().position = new Vector2(state.Position.X, state.Position.Y);
        }
        static IEnumerator Capture(string filename)
        {
            string directory = Environment.GetEnvironmentVariable("DOGFIGHT_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) yield break;
            Directory.CreateDirectory(directory);
            yield return new WaitForSecondsRealtime(.3f);
            // Batch mode 不执行屏幕截图请求；用真实相机和同一份原生 Canvas 离屏渲染。
            var camera = Camera.main;
            var canvases = Object.FindObjectsOfType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24);
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(directory, filename), texture.EncodeToPNG());
            }
            finally
            {
                foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                target.Release(); Object.Destroy(target); Object.Destroy(texture);
            }
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (_hadPreference) PlayerPrefs.SetInt(DifficultyKey, _oldPreference); else PlayerPrefs.DeleteKey(DifficultyKey);
            Time.timeScale = 1;
            var old = SceneManager.GetActiveScene(); var empty = SceneManager.CreateScene("Cannon cleanup");
            SceneManager.SetActiveScene(empty); if (old.isLoaded) yield return SceneManager.UnloadSceneAsync(old);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
