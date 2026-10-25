using System.Collections;
using System.Reflection;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DogfightThreatAI.Tests.PlayMode
{
    public sealed class CameraHitShakeIsolationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject _art, _stableTarget, _followingCamera, _referenceCamera;
        float _originalScale;

        [UnitySetUp]
        public IEnumerator PrepareFrame()
        {
            _originalScale = Time.timeScale;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Object.Destroy(_art);
            Object.Destroy(_stableTarget);
            Object.Destroy(_followingCamera);
            Object.Destroy(_referenceCamera);
            Time.timeScale = _originalScale;
            yield return null;
        }

        [Test]
        public void FollowingArtAfterHitShakeMatchesFollowingTheUnshakenInterpolatedPosition()
        {
            _art = new GameObject("CameraShakeTest_Art");
            var view = _art.AddComponent<PlayerView>();
            view.enabled = false;
            var previous = FlightState.At(new Vec2(1f, 2f));
            var current = FlightState.At(new Vec2(5f, 6f));
            view.Render(current, previous, .25f, null);
            Vector3 stablePosition = _art.transform.position;

            _stableTarget = new GameObject("CameraShakeTest_StableTarget");
            _stableTarget.transform.position = stablePosition;
            var follow = CreateCamera("CameraShakeTest_Follow", _art.transform, out _followingCamera);
            var reference = CreateCamera("CameraShakeTest_Reference", _stableTarget.transform, out _referenceCamera);
            Tick(follow);
            Tick(reference);

            // Force the adverse LateUpdate order, instead of depending on Unity's unspecified component order.
            // A long duration keeps this isolation check independent of a slow editor frame.
            view.PlayHitShake(10f);
            typeof(PlayerView).GetMethod("LateUpdate", Private).Invoke(view, null);
            Assert.Greater(Vector3.Distance(stablePosition, _art.transform.position), .01f,
                "The Art must actually shake before checking whether the camera follows that visual offset.");
            Tick(follow);
            Tick(reference);

            Assert.Less(Vector3.Distance(_followingCamera.transform.position, _referenceCamera.transform.position), .00001f,
                "Hit feedback must move only Art: a camera following Art must use its unshaken interpolated position.");
            LogAssert.NoUnexpectedReceived();
        }

        static CameraFollow CreateCamera(string name, Transform target, out GameObject root)
        {
            root = new GameObject(name);
            root.transform.position = new Vector3(-4f, -4f, -10f);
            root.AddComponent<Camera>().enabled = false;
            var follow = root.AddComponent<CameraFollow>();
            follow.enabled = false;
            typeof(CameraFollow).GetField("clampToWorld", Private).SetValue(follow, false);
            follow.SetTarget(target);
            return follow;
        }

        static void Tick(CameraFollow follow)
            => typeof(CameraFollow).GetMethod("LateUpdate", Private).Invoke(follow, null);
    }
}
