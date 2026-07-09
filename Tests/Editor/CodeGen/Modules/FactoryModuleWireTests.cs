using System;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // FactoryModule.Wire() against a real compiled root. Unlike PoolModule, Factory's
    // slot-field-not-found path `continue`s BEFORE creating the "Factories" container or
    // instantiating anything - so the empty-entries branches and the "field missing -> zero
    // mutation" case are always testable against this project's real (unbootstrapped)
    // compiled root. The "field found -> instantiated and assigned" sequence lives in
    // FactoryModule.CreateAndAssignInstance(), a private static method reachable via
    // reflection, tested directly against a plain test double instead of needing a real
    // bootstrapped field on the compiled type.
    public class FactoryModuleWireTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/FactoryWirePrefab.prefab";
        private static readonly Type EntryType = CodeGenModuleReflection.NestedType(typeof(FactoryModule), "FactoryEntry");

        // A stand-in for the compiled root exposing only the one literal field name
        // CreateAndAssignInstance/IsFactoriesAlreadyWired need to find a match against - both
        // take their target as an explicit parameter rather than looking up the real compiled
        // type themselves, so any Component with the right field works.
        private class FactoryFieldDouble : MonoBehaviour
        {
            public GameObject _factoryWidget;
        }

        private TempSceneScope _scope;
        private Component _root;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _root = CompiledRootFixture.AddTo(_scope);
            ScratchAssets.EnsureFolder();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            ScratchAssets.DeleteAll();
        }

        private GameObject CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            return PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("FactoryWirePrefab", name));
        }

        [Test]
        public void Wire_EmptyEntries_ExistingFactoriesChildDestroyed()
        {
            var stray = _scope.CreateGameObject("Factories");
            stray.transform.SetParent(_root.transform, false);

            new FactoryModule().Wire();

            Assert.IsNull(_root.transform.Find("Factories"));
        }

        [Test]
        public void Wire_EmptyEntries_NoExistingChild_IsNoOp()
        {
            Assert.DoesNotThrow(() => new FactoryModule().Wire());
            Assert.IsNull(_root.transform.Find("Factories"));
        }

        [Test]
        public void Wire_FieldNotFoundOnRoot_NoContainerOrInstanceCreated()
        {
            var prefab = CreateScratchPrefab("Widget");
            var entry = CodeGenModuleReflection.BuildEntry(EntryType,
                ("Name", "DefinitelyNotReal"), ("TypeName", "GameObject"), ("TypeNamespace", ""),
                ("IsTsvrcBehaviour", false), ("PrefabAsset", prefab));
            var module = new FactoryModule();
            PrivateFieldAccess.SetField(module, "_entries", CodeGenModuleReflection.BuildList(EntryType, new object[] { entry }));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field '_factoryDefinitelyNotReal' not found.*"));

            module.Wire();

            Assert.IsNull(_root.transform.Find("Factories"), "Unlike PoolModule, Factory must not create the container at all when the field is missing.");
        }

        // IsFactoriesAlreadyWired() is `private` (instance) - reachable via reflection,
        // passing whatever root/existing container the test constructs.
        private static bool IsAlreadyWired(FactoryModule module, Component root, Transform existing)
            => (bool)PrivateFieldAccess.InvokeInstance(module, "IsFactoriesAlreadyWired", root, existing);

        private static FactoryModule ModuleWithOneEntry(string name, GameObject prefab)
        {
            var entry = CodeGenModuleReflection.BuildEntry(EntryType,
                ("Name", name), ("TypeName", "GameObject"), ("TypeNamespace", ""),
                ("IsTsvrcBehaviour", false), ("PrefabAsset", prefab));
            var module = new FactoryModule();
            PrivateFieldAccess.SetField(module, "_entries", CodeGenModuleReflection.BuildList(EntryType, new object[] { entry }));
            return module;
        }

        [Test]
        public void IsFactoriesAlreadyWired_NoExistingContainer_ReturnsFalse()
        {
            var prefab = CreateScratchPrefab("Widget");
            var module = ModuleWithOneEntry("Widget", prefab);

            Assert.IsFalse(IsAlreadyWired(module, _root, null));
        }

        [Test]
        public void IsFactoriesAlreadyWired_ChildCountMismatch_ReturnsFalse()
        {
            var prefab = CreateScratchPrefab("Widget");
            var module = ModuleWithOneEntry("Widget", prefab);
            var container = _scope.CreateGameObject("Factories");
            container.transform.SetParent(_root.transform, false);
            // Two children exist but the module expects exactly one entry.
            var a = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.transform);
            a.name = "Widget";
            var b = _scope.CreateGameObject("Extra");
            b.transform.SetParent(container.transform, false);

            Assert.IsFalse(IsAlreadyWired(module, _root, container.transform));
        }

        [Test]
        public void IsFactoriesAlreadyWired_ChildActiveInsteadOfInactive_ReturnsFalse()
        {
            var prefab = CreateScratchPrefab("Widget");
            var module = ModuleWithOneEntry("Widget", prefab);
            var container = _scope.CreateGameObject("Factories");
            container.transform.SetParent(_root.transform, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.transform);
            instance.name = "Widget";
            instance.SetActive(true); // must be inactive to count as "already wired"

            Assert.IsFalse(IsAlreadyWired(module, _root, container.transform));
        }

        [Test]
        public void IsFactoriesAlreadyWired_ChildFromWrongPrefabSource_ReturnsFalse()
        {
            var prefab = CreateScratchPrefab("Widget");
            var otherPrefab = CreateScratchPrefab("OtherWidget");
            var module = ModuleWithOneEntry("Widget", prefab);
            var container = _scope.CreateGameObject("Factories");
            container.transform.SetParent(_root.transform, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(otherPrefab, container.transform);
            instance.name = "Widget";
            instance.SetActive(false);

            Assert.IsFalse(IsAlreadyWired(module, _root, container.transform));
        }

        [Test]
        public void CreateAndAssignInstance_FieldFound_CreatesContainerInstantiatesInactiveAndAssignsField()
        {
            var prefab = CreateScratchPrefab("Widget");
            var fakeRoot = _scope.CreateGameObject("FakeRoot").AddComponent<FactoryFieldDouble>();
            var prop = new SerializedObject(fakeRoot).FindProperty(nameof(FactoryFieldDouble._factoryWidget));

            var (container, instance) = ((GameObject container, GameObject instance))PrivateFieldAccess.InvokeStatic(
                typeof(FactoryModule), "CreateAndAssignInstance", prop, prefab, "Widget", null, fakeRoot.transform);
            prop.serializedObject.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsNotNull(container);
            Assert.AreEqual("Factories", container.name);
            Assert.AreEqual(fakeRoot.transform, container.transform.parent);
            Assert.IsNotNull(instance);
            Assert.IsFalse(instance.activeSelf, "Factory instances must be created inactive.");
            Assert.AreEqual(instance, fakeRoot._factoryWidget);
        }

        [Test]
        public void CreateAndAssignInstance_ExistingContainerPassedIn_ReusedRatherThanRecreated()
        {
            var prefab = CreateScratchPrefab("Widget");
            var fakeRoot = _scope.CreateGameObject("FakeRoot").AddComponent<FactoryFieldDouble>();
            var existingContainer = _scope.CreateGameObject("Factories");
            existingContainer.transform.SetParent(fakeRoot.transform, false);
            var prop = new SerializedObject(fakeRoot).FindProperty(nameof(FactoryFieldDouble._factoryWidget));

            var (container, _) = ((GameObject container, GameObject instance))PrivateFieldAccess.InvokeStatic(
                typeof(FactoryModule), "CreateAndAssignInstance", prop, prefab, "Widget", existingContainer, fakeRoot.transform);

            Assert.AreEqual(existingContainer, container, "An existing container must be reused, not recreated.");
        }

        [Test]
        public void IsFactoriesAlreadyWired_FieldMatchesExistingChild_ReturnsTrue()
        {
            var prefab = CreateScratchPrefab("Widget");
            var module = ModuleWithOneEntry("Widget", prefab);

            var fakeRoot = _scope.CreateGameObject("FakeRoot").AddComponent<FactoryFieldDouble>();
            var container = _scope.CreateGameObject("Factories");
            container.transform.SetParent(fakeRoot.transform, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.transform);
            instance.name = "Widget";
            instance.SetActive(false);
            fakeRoot._factoryWidget = instance;

            Assert.IsTrue(IsAlreadyWired(module, fakeRoot, container.transform));
        }
    }
}
