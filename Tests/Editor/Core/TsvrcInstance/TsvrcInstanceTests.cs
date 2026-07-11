using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // OnInstanceStart() is invoked by generated code's _TsInstanceStart(), never
    // automatically by TsConstruct.
    //
    // No test double subclasses TsvrcInstance anywhere in this project: InstanceModule
    // scans the whole AppDomain for a non-abstract TsvrcInstance subclass with no
    // test-exclusion mechanism, so a real subclass compiled anywhere (including a
    // test-only assembly) gets permanently adopted as "the" project's instance type
    // and wired into the currently open scene on every domain reload. Virtual-dispatch
    // coverage below is proven via reflection (IsVirtual) instead of an actual override.
    public class TsvrcInstanceTests
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

        private T Create<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void OnInstanceStart_DirectCall_IsNoOpAndDoesNotThrow()
        {
            var instance = Create<TsvrcInstance>();

            Assert.DoesNotThrow(() => instance.OnInstanceStart());
        }

        [Test]
        public void OnInstanceStart_IsVirtual_OverridableByWorldAuthors()
        {
            MethodInfo method = typeof(TsvrcInstance).GetMethod(nameof(TsvrcInstance.OnInstanceStart));

            Assert.IsNotNull(method);
            Assert.IsTrue(method.IsVirtual);
        }

        [Test]
        public void IsTsMaster_IsVirtual_OverridableByWorldAuthors()
        {
            // Proves the "TODO: implement custom master logic" extension point in
            // TsvrcInstance's own doc comment is real and reachable via ordinary
            // C# virtual dispatch, without instantiating a subclass anywhere.
            PropertyInfo property = typeof(TsvrcInstance).GetProperty(nameof(TsvrcInstance.IsTsMaster));

            Assert.IsNotNull(property);
            Assert.IsTrue(property.GetGetMethod().IsVirtual);
        }

        [Test]
        public void TsStart_IsNotOverriddenByTsvrcInstance()
        {
            // TsConstruct only ever calls TsStart() (TsvrcBehaviour.cs). TsvrcInstance
            // does not override it, so no call path from TsConstruct alone can ever
            // reach OnInstanceStart() - only generated code's explicit two-line
            // _TsInstanceStart() wrapper (TsConstruct(this); OnInstanceStart();) does.
            MethodInfo tsStart = typeof(TsvrcInstance).GetMethod(
                "TsStart", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNotNull(tsStart);
            Assert.AreEqual(typeof(TsvrcBehaviour), tsStart.DeclaringType,
                "TsvrcInstance must not override TsStart() - if it ever does, TsConstruct " +
                "alone would start invoking OnInstanceStart() automatically, silently changing " +
                "the documented generated-code contract (TsConstruct then OnInstanceStart, as " +
                "two explicit separate calls).");
        }

        [Test]
        public void IsTsMaster_BaseImplementation_DoesNotThrowOutsidePlayMode()
        {
            var instance = Create<TsvrcInstance>();

            bool _ = false;
            Assert.DoesNotThrow(() => _ = instance.IsTsMaster);
        }

        // The Networking.IsMaster-vs-IsTsMaster equality check itself lives in
        // Tests/PlayMode/Core/TsvrcInstance/TsvrcInstancePlayModeTests.cs instead of
        // here: VRC.SDKBase.Networking is defined in VRCSDKBase.dll, which
        // Tsvrc.Tests.Editor.asmdef does not reference (only VRCSDKBase-Editor.dll).
        // Referencing the bare Networking type name here is a compile error, not a
        // runtime one.
    }
}
