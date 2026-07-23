using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.Core;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// VRCSDK's VRC.Core.UnityEventFilter strips any persistent UnityEvent listener whose
    /// target type isn't in its private allowlist dictionary - and Unity Test Framework's own
    /// PlayMode reporting/termination chain is wired entirely through persistent UnityEvent
    /// listeners on PlaymodeTestsController (see RuntimeTestLauncherBase.SetListeners in
    /// com.unity.test-framework), targeting four internal types: PlayModeRunnerCallback (the
    /// interactive Test Runner window), TestRunnerCallback (whose RunFinished sets
    /// EditorApplication.isPlaying = false - the real interactive auto-exit),
    /// CallbacksDelegatorListener (forwards into CallbacksDelegator.instance, which invokes
    /// every ICallbacks registered via TestRunnerApi.RegisterCallbacks - including
    /// ExitCallbacks, whose RunFinished calls EditorApplication.Exit with the real NUnit-derived
    /// exit code), and TestRunCallbackListener (invokes any project's own [TestRunCallback]
    /// listeners). None of the four are on VRCSDK's allowlist, so all four get stripped the
    /// instant Play Mode starts, breaking Unity's entire native reporting/termination chain in
    /// one shot.
    ///
    /// This fixup reflectively widens VRC.Core.UnityEventFilter's allowlist to include all four
    /// types (permitting their TestStarted/TestFinished/RunStarted/RunFinished methods), so the
    /// filter's own normal pass simply never strips them - restoring Unity's native results.xml
    /// writing, Test Runner window display, and real process exit codes (confirmed via a real
    /// batch-mode run: a genuine results.xml with a correctly-captured Assert failure message
    /// and the real NUnit-derived process exit code). Runs once via [InitializeOnLoad], at
    /// domain-load time - strictly before any Play Mode session's first EnteredPlayMode filter
    /// pass, and long before any [OneTimeSetUp] could matter. Fails silently (logs one warning,
    /// does nothing further) if any of the reflected members can't be found - e.g. after a Unity
    /// Test Framework or VRCSDK update changes their internals.
    /// </summary>
    [InitializeOnLoad]
    public sealed class UnityEventFilterAllowlistFixup : IPlayModeEnvironmentFixup
    {
        private static readonly string[] ListenerTypeNames =
        {
            "UnityEngine.TestTools.TestRunner.Callbacks.PlayModeRunnerCallback",
            "UnityEditor.TestTools.TestRunner.TestRunnerCallback",
            "UnityEditor.TestTools.TestRunner.Api.CallbacksDelegatorListener",
            "UnityEngine.TestRunner.Utils.TestRunCallbackListener",
        };

        private static readonly List<string> AllowedMethodNames = new List<string>
        {
            "TestStarted", "TestFinished", "RunStarted", "RunFinished",
        };

        static UnityEventFilterAllowlistFixup()
        {
            try
            {
                Allowlist();
            }
            catch (Exception e)
            {
                Warn("threw an exception while widening the allowlist: " + e);
            }
        }

        public void OnBeforeAnyTests()
        {
        }

        public void OnUnityTearDown()
        {
        }

        private static void Allowlist()
        {
            FieldInfo lazyField = typeof(UnityEventFilter).GetField("_allowedUnityEventTargetTypes", BindingFlags.NonPublic | BindingFlags.Static);
            if (lazyField == null)
            {
                Warn("could not find UnityEventFilter._allowedUnityEventTargetTypes.");
                return;
            }

            object lazy = lazyField.GetValue(null);
            PropertyInfo valueProperty = lazy?.GetType().GetProperty("Value");
            if (!(valueProperty?.GetValue(lazy) is IDictionary allowlist))
            {
                Warn("could not read UnityEventFilter's allowlist dictionary.");
                return;
            }

            Type filterEntryType = typeof(UnityEventFilter).GetNestedType("AllowedMethodFilter", BindingFlags.NonPublic);
            ConstructorInfo ctor = filterEntryType?.GetConstructor(new[] { typeof(List<string>), typeof(List<string>) });
            if (ctor == null)
            {
                Warn("could not find UnityEventFilter.AllowedMethodFilter's constructor.");
                return;
            }

            foreach (string typeName in ListenerTypeNames)
            {
                Type listenerType = FindType(typeName);
                if (listenerType == null)
                {
                    Warn("could not find " + typeName + ".");
                    continue;
                }

                if (allowlist.Contains(listenerType))
                    continue;

                object filterEntry = ctor.Invoke(new object[] { AllowedMethodNames, new List<string>() });
                allowlist.Add(listenerType, filterEntry);
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static void Warn(string reason)
        {
            Debug.LogWarning("[Tsvrc] UnityEventFilterAllowlistFixup: " + reason +
                " Unity Test Framework's native PlayMode reporting/termination stays broken.");
        }
    }
}
