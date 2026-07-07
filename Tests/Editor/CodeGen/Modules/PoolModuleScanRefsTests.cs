using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // PoolModule.ScanExternalRefs()/ScanInternalDeps(), driven against a real temp scene
    // (TempSceneScope makes it the active scene, which is what SceneManager.GetActiveScene()
    // inside these methods reads) containing the PoolWireTargetDouble test doubles.
    public class PoolModuleScanRefsTests
    {
        private static readonly Type InfoType = PrivateFieldAccess.NestedType(typeof(PoolModule), "PoolTypeInfo");

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static PoolModule BuildModuleWithStateManagerPoolType()
        {
            var info = PrivateFieldAccess.BuildEntry(InfoType,
                ("Prefab", null), ("TypeName", "StateManager"), ("TypeNamespace", "Tsvrc.StateMachine"),
                ("ExternalCount", 0), ("InternalDeps", new Dictionary<string, int>(StringComparer.Ordinal)),
                ("TotalSlots", 0));

            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), InfoType);
            var dict = (IDictionary)Activator.CreateInstance(dictType, StringComparer.Ordinal);
            dict["StateManager"] = info;

            var module = new PoolModule();
            PrivateFieldAccess.SetField(module, "_poolTypeInfos", dict);
            return module;
        }

        private static int ExternalCountOf(PoolModule module)
        {
            var dict = PrivateFieldAccess.GetField<IDictionary>(module, "_poolTypeInfos");
            var info = dict["StateManager"];
            return PrivateFieldAccess.GetField<int>(info, "ExternalCount");
        }

        [Test]
        public void ScanExternalRefs_ArrayWirePoolField_IsExcluded()
        {
            var module = BuildModuleWithStateManagerPoolType();
            var behaviour = _scope.CreateGameObject("Target").AddComponent<PoolWireTargetDouble>();
            behaviour.ArrayField = new Tsvrc.StateMachine.StateManager[0];

            PrivateFieldAccess.InvokeInstance(module, "ScanExternalRefs");

            // PublicField/_serializedField (both StateManager, not array/generic) still count,
            // so assert the array/generic fields specifically did NOT each add one more on top
            // of the two valid fields - i.e. total is exactly 2, not 4.
            Assert.AreEqual(2, ExternalCountOf(module));
        }

        [Test]
        public void ScanExternalRefs_NonSerializedPrivateField_IsExcluded()
        {
            var module = BuildModuleWithStateManagerPoolType();
            _scope.CreateGameObject("Target").AddComponent<PoolWireTargetDouble>();

            PrivateFieldAccess.InvokeInstance(module, "ScanExternalRefs");

            // Only PublicField + _serializedField qualify; _nonSerializedField must not.
            Assert.AreEqual(2, ExternalCountOf(module));
        }

        [Test]
        public void ScanExternalRefs_FieldDeclaredOnBaseClass_IsDiscovered()
        {
            var module = BuildModuleWithStateManagerPoolType();
            _scope.CreateGameObject("Target").AddComponent<PoolWireTargetDoubleDerived>();

            PrivateFieldAccess.InvokeInstance(module, "ScanExternalRefs");

            Assert.AreEqual(1, ExternalCountOf(module));
        }

        [Test]
        public void ScanExternalRefs_FieldOnAnotherPoolType_IsNotDoubleCountedAsExternal()
        {
            // A [WirePool] field whose declaring behaviour is itself a configured pool type
            // must be handled by ScanInternalDeps, never counted again by ScanExternalRefs.
            var info = PrivateFieldAccess.BuildEntry(InfoType,
                ("Prefab", null), ("TypeName", nameof(PoolWireTargetDouble)), ("TypeNamespace", "Tsvrc.Tests.Editor"),
                ("ExternalCount", 0), ("InternalDeps", new Dictionary<string, int>(StringComparer.Ordinal)),
                ("TotalSlots", 0));
            var stateManagerInfo = PrivateFieldAccess.BuildEntry(InfoType,
                ("Prefab", null), ("TypeName", "StateManager"), ("TypeNamespace", "Tsvrc.StateMachine"),
                ("ExternalCount", 0), ("InternalDeps", new Dictionary<string, int>(StringComparer.Ordinal)),
                ("TotalSlots", 0));
            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), InfoType);
            var dict = (IDictionary)Activator.CreateInstance(dictType, StringComparer.Ordinal);
            dict[nameof(PoolWireTargetDouble)] = info;
            dict["StateManager"] = stateManagerInfo;

            var module = new PoolModule();
            PrivateFieldAccess.SetField(module, "_poolTypeInfos", dict);

            // PoolWireTargetDouble is itself now a configured pool type, so its own
            // StateManager-typed fields must be excluded from ScanExternalRefs (they belong
            // to ScanInternalDeps instead).
            _scope.CreateGameObject("Target").AddComponent<PoolWireTargetDouble>();

            PrivateFieldAccess.InvokeInstance(module, "ScanExternalRefs");

            Assert.AreEqual(0, ExternalCountOf(module), "Fields on a scene instance of a configured pool type must not be counted as external refs.");
        }
    }
}
