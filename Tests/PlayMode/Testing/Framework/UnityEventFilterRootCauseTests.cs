using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using VRC.Core;
#if UNITY_EDITOR
using UnityEditor.Events;
#endif

namespace Tsvrc.Tests.PlayMode.Testing.Framework
{
    // Not a fixup test - this proves the actual VRCSDK mechanism that BatchModeTerminationFixup
    // and ConsoleLogVerdictSink exist to work around is real, not hypothetical: VRCSDK's
    // VRC.Core.UnityEventFilter strips Unity Test Framework's own
    // TestStarted/TestFinished/RunStarted/RunFinished event wiring the instant Play Mode
    // starts, because that wiring is exactly a persistent UnityEvent listener targeting a type
    // not on UnityEventFilter's allowlist - reproduced directly here against the real,
    // unmodified VRC.Core.UnityEventFilter.FilterUnityEvents API.
    //
    // Lives under PlayMode, not EditMode: UnityEventFilter.IsTargetPermitted touches
    // VRC.Udon.UdonManager.Instance, which calls Object.DontDestroyOnLoad - Unity throws
    // InvalidOperationException if that runs outside Play Mode (confirmed by trying this in
    // EditMode first). This class deliberately does not extend TsPlayModeTestBase - it doesn't
    // need ClientSim, and TsPlayModeTestBase's OneTimeTearDown calls
    // EditorApplication.Exit()/isPlaying = false per-fixture, so a second class extending it
    // would risk exiting before TsPlayerPlayModeTests' own tests finish.
    public class UnityEventFilterRootCauseTests
    {
        private GameObject _gameObject;
        private TargetComponent _target;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(UnityEventFilterRootCauseTests));
            _target = _gameObject.AddComponent<TargetComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [UnityTest]
        public IEnumerator FilterUnityEvents_ListenerTargetsTypeNotOnAllowlist_StripsTheListener()
        {
            yield return null;

#if UNITY_EDITOR
            UnityEventTools.AddPersistentListener(_target.OnSomething, _target.NotOnAllowlist);
#endif
            Assert.AreEqual(1, _target.OnSomething.GetPersistentEventCount(),
                "Setup sanity check failed: listener wasn't actually added.");

            UnityEventFilter.FilterUnityEvents(_gameObject);

            Assert.AreEqual(0, _target.OnSomething.GetPersistentEventCount(),
                "UnityEventFilter did not strip a listener targeting a type absent from its " +
                "allowlist - if this now passes because VRCSDK's allowlist behavior changed, " +
                "BatchModeTerminationFixup and ConsoleLogVerdictSink's justification needs " +
                "re-checking against real Unity Test Framework listeners, not just this proxy.");
        }

        [UnityTest]
        public IEnumerator FilterUnityEvents_ListenerTargetsAllowlistedAudioSourceMethod_LeavesItIntact()
        {
            yield return null;

            var audioSource = _gameObject.AddComponent<AudioSource>();
#if UNITY_EDITOR
            UnityEventTools.AddPersistentListener(_target.OnSomething, audioSource.Play);
#endif

            UnityEventFilter.FilterUnityEvents(_gameObject);

            Assert.AreEqual(1, _target.OnSomething.GetPersistentEventCount(),
                "UnityEventFilter stripped a listener targeting an explicitly allowlisted " +
                "AudioSource method - filtering is supposed to be selective, not a blanket clear.");
        }

        private class TargetComponent : MonoBehaviour
        {
            public UnityEvent OnSomething = new UnityEvent();

            public void NotOnAllowlist()
            {
            }
        }
    }
}
