using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.Core;

namespace Tsvrc.Tests.PlayMode.Testing.Framework
{
    // Proves UnityEventFilterAllowlistFixup actually widens VRC.Core.UnityEventFilter's real
    // allowlist to cover Unity Test Framework's four PlayMode listener types, and that removing
    // one of its entries reproduces the real denial UnityEventFilterRootCauseTests demonstrates
    // generically - tying that root cause directly to the specific types this fixup exists for.
    //
    // Lives under PlayMode, not EditMode, for the same reason as UnityEventFilterRootCauseTests:
    // UnityEventFilter.IsTargetPermitted touches VRC.Udon.UdonManager.Instance, which calls
    // Object.DontDestroyOnLoad and throws InvalidOperationException outside Play Mode.
    public class UnityEventFilterAllowlistFixupTests
    {
        private static readonly (string TypeName, string MethodName)[] RealListeners =
        {
            ("UnityEngine.TestTools.TestRunner.Callbacks.PlayModeRunnerCallback", "RunFinished"),
            ("UnityEditor.TestTools.TestRunner.TestRunnerCallback", "RunFinished"),
            ("UnityEditor.TestTools.TestRunner.Api.CallbacksDelegatorListener", "RunFinished"),
            ("UnityEngine.TestRunner.Utils.TestRunCallbackListener", "RunFinished"),
        };

        private readonly List<UnityEngine.Object> _createdInstances = new List<UnityEngine.Object>();
        private GameObject _hostForComponents;

        [TearDown]
        public void TearDown()
        {
            foreach (var instance in _createdInstances)
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
            _createdInstances.Clear();

            if (_hostForComponents != null)
                UnityEngine.Object.DestroyImmediate(_hostForComponents);
            _hostForComponents = null;
        }

        [UnityTest]
        public IEnumerator IsTargetPermitted_ForEachRealListenerType_ReturnsTrueForRunFinished()
        {
            yield return null;

            foreach (var (typeName, methodName) in RealListeners)
            {
                Type listenerType = FindType(typeName);
                Assert.IsNotNull(listenerType, $"{typeName} not found - has Unity Test Framework's internal layout changed?");

                UnityEngine.Object instance = CreateInstance(listenerType);
                bool permitted = (bool)PrivateFieldAccess.InvokeStatic(typeof(UnityEventFilter), "IsTargetPermitted", instance, methodName);

                Assert.IsTrue(permitted,
                    $"{typeName}.{methodName} is not permitted by UnityEventFilter - " +
                    "UnityEventFilterAllowlistFixup did not widen the allowlist for this type.");
            }
        }

        [UnityTest]
        public IEnumerator WithoutItsAllowlistEntry_ARealListenerTypeIsDeniedJustLikeAnyOther()
        {
            yield return null;

            Type listenerType = FindType("UnityEngine.TestTools.TestRunner.Callbacks.PlayModeRunnerCallback");
            Assert.IsNotNull(listenerType, "PlayModeRunnerCallback not found - has Unity Test Framework's internal layout changed?");
            UnityEngine.Object instance = CreateInstance(listenerType);

            IDictionary allowlist = GetAllowlist();
            object savedEntry = allowlist[listenerType];
            Assert.IsNotNull(savedEntry, "PlayModeRunnerCallback was not on the allowlist to begin with - the fixup did not run.");

            try
            {
                allowlist.Remove(listenerType);

                bool permitted = (bool)PrivateFieldAccess.InvokeStatic(typeof(UnityEventFilter), "IsTargetPermitted", instance, "RunFinished");

                Assert.IsFalse(permitted,
                    "PlayModeRunnerCallback.RunFinished was still permitted after removing its allowlist entry - " +
                    "UnityEventFilter's default-deny behavior (the reason this fixup exists) did not reproduce.");
            }
            finally
            {
                allowlist[listenerType] = savedEntry;
            }
        }

        private static IDictionary GetAllowlist()
        {
            FieldInfo lazyField = typeof(UnityEventFilter).GetField("_allowedUnityEventTargetTypes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(lazyField, "UnityEventFilter._allowedUnityEventTargetTypes not found - has VRCSDK's internal layout changed?");

            object lazy = lazyField.GetValue(null);
            PropertyInfo valueProperty = lazy.GetType().GetProperty("Value");
            var allowlist = valueProperty.GetValue(lazy) as IDictionary;
            Assert.IsNotNull(allowlist, "Could not read UnityEventFilter's allowlist dictionary.");
            return allowlist;
        }

        private UnityEngine.Object CreateInstance(Type type)
        {
            UnityEngine.Object instance;
            if (typeof(Component).IsAssignableFrom(type))
            {
                if (_hostForComponents == null)
                    _hostForComponents = new GameObject(nameof(UnityEventFilterAllowlistFixupTests));
                instance = _hostForComponents.AddComponent(type);
            }
            else
            {
                instance = ScriptableObject.CreateInstance(type);
                _createdInstances.Add(instance);
            }

            return instance;
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
    }
}
