using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Utils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // Covers TsBehaviour's LogInfo/LogWarning/LogError wrappers: the tag is always the
    // concrete runtime type name (via GetUdonTypeName()), always led by TsLogger.FrameworkTag
    // ("TsVRC") with no project-specific Prefix (the fallback path has no TsLogger instance to
    // read one from), and the message routes through _ts.Log when it's wired up, falling back
    // to Debug.Log directly (same tag/format) when _ts or _ts.Log is not yet set - e.g. before
    // TsConstruct.
    public class TsBehaviourLoggingTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private T CreateBehaviour<T>(string name = null) where T : UdonSharp.UdonSharpBehaviour
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void LogWarning_BeforeTsConstruct_FallsBackToDebugLogWithAutoTag()
        {
            var behaviour = CreateBehaviour<TsBehaviourTestSubclass>();

            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsBehaviourTestSubclass] fallback path");
            behaviour.InvokeLogWarning("fallback path");
        }

        [Test]
        public void LogError_BeforeTsConstruct_FallsBackToDebugLogWithAutoTag()
        {
            var behaviour = CreateBehaviour<TsBehaviourTestSubclass>();

            LogAssert.Expect(LogType.Error, "[TsVRC] [TsBehaviourTestSubclass] fallback path");
            behaviour.InvokeLogError("fallback path");
        }

        [Test]
        public void LogWarning_TsConstructedWithRootThatHasNoLogger_StillFallsBackToDebugLog()
        {
            var behaviour = CreateBehaviour<TsBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRoot>();
            behaviour.TsConstruct(root);

            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsBehaviourTestSubclass] still falls back");
            behaviour.InvokeLogWarning("still falls back");
        }

        [Test]
        public void LogWarning_TsConstructedWithRealLogger_DelegatesToTsLogger()
        {
            var behaviour = CreateBehaviour<TsBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRootWithLogger>();
            root.LogOverride = CreateBehaviour<TsLogger>();
            behaviour.TsConstruct(root);

            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsBehaviourTestSubclass] delegated");
            behaviour.InvokeLogWarning("delegated");
        }

        [Test]
        public void LogInfo_TsConstructedWithRealLogger_WorldInfoDisabled_DoesNotLog()
        {
            var behaviour = CreateBehaviour<TsBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRootWithLogger>();
            root.LogOverride = CreateBehaviour<TsLogger>();
            // TsBehaviourTestSubclass does not override IsTsvrcInternal, so it reports false
            // (world) - disabling WorldInfoEnabled (not InternalInfoEnabled) must suppress it.
            root.LogOverride.WorldInfoEnabled = false;
            behaviour.TsConstruct(root);

            int callCount = 0;
            void Handler(string condition, string stackTrace, LogType type) => callCount++;
            Application.logMessageReceived += Handler;
            try
            {
                behaviour.InvokeLogInfo("suppressed");
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void LogWarning_WorldBehaviour_RoutesThroughWorldToggleNotInternal()
        {
            var behaviour = CreateBehaviour<TsBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRootWithLogger>();
            root.LogOverride = CreateBehaviour<TsLogger>();
            root.LogOverride.InternalWarningEnabled = false;
            behaviour.TsConstruct(root);

            // A plain TsBehaviour subclass is "world", so disabling only InternalWarningEnabled
            // must not suppress it.
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsBehaviourTestSubclass] still world");
            behaviour.InvokeLogWarning("still world");
        }

        [Test]
        public void LogWarning_InternalBehaviour_RoutesThroughInternalToggleNotWorld()
        {
            var behaviour = CreateBehaviour<TsInternalBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRootWithLogger>();
            root.LogOverride = CreateBehaviour<TsLogger>();
            root.LogOverride.WorldWarningEnabled = false;
            behaviour.TsConstruct(root);

            // TsInternalBehaviourTestSubclass overrides IsTsvrcInternal to true, so disabling
            // only WorldWarningEnabled must not suppress it.
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsInternalBehaviourTestSubclass] still internal");
            behaviour.InvokeLogWarning("still internal");
        }

        [Test]
        public void LogWarning_InternalBehaviour_InternalToggleDisabled_Suppressed()
        {
            var behaviour = CreateBehaviour<TsInternalBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRootWithLogger>();
            root.LogOverride = CreateBehaviour<TsLogger>();
            root.LogOverride.InternalWarningEnabled = false;
            behaviour.TsConstruct(root);

            int callCount = 0;
            void Handler(string condition, string stackTrace, LogType type) => callCount++;
            Application.logMessageReceived += Handler;
            try
            {
                behaviour.InvokeLogWarning("suppressed");
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }

            Assert.AreEqual(0, callCount);
        }
    }
}
