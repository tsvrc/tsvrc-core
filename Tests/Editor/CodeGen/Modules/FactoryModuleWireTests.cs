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
    // mutation" regression are always testable against this project's real (unbootstrapped)
    // compiled root; the "field found -> instantiated and assigned" happy path additionally
    // runs for real whenever CodeGenSandbox.Bootstrap() has been applied (see
    // run-codegen-sandbox-tests.ps1 and CODEGEN_TESTING_PLAN.md Part 4.5), and is
    // Assert.Ignore()'d otherwise. Phase G4.9.
    public class FactoryModuleWireTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/FactoryWirePrefab.prefab";
        private static readonly Type EntryType = PrivateFieldAccess.NestedType(typeof(FactoryModule), "FactoryEntry");

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
            var entry = PrivateFieldAccess.BuildEntry(EntryType,
                ("Name", "DefinitelyNotReal"), ("TypeName", "GameObject"), ("TypeNamespace", ""),
                ("IsTsvrcBehaviour", false), ("PrefabAsset", prefab));
            var module = new FactoryModule();
            PrivateFieldAccess.SetField(module, "_entries", PrivateFieldAccess.BuildList(EntryType, new object[] { entry }));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field '_factoryDefinitelyNotReal' not found.*"));

            module.Wire();

            Assert.IsNull(_root.transform.Find("Factories"), "Unlike PoolModule, Factory must not create the container at all when the field is missing.");
        }

        // IsFactoriesAlreadyWired() is `private` (instance) - reachable via reflection. Only
        // its "not wired" (false) outcomes are verifiable without a bootstrap, since the
        // "wired" (true) branch requires a real `_factory{Name}` field reference match on
        // the compiled root (see class-level comment / Part 4.5).
        private static bool IsAlreadyWired(FactoryModule module, Component root, Transform existing)
            => (bool)PrivateFieldAccess.InvokeInstance(module, "IsFactoriesAlreadyWired", root, existing);

        private static FactoryModule ModuleWithOneEntry(string name, GameObject prefab)
        {
            var entry = PrivateFieldAccess.BuildEntry(EntryType,
                ("Name", name), ("TypeName", "GameObject"), ("TypeNamespace", ""),
                ("IsTsvrcBehaviour", false), ("PrefabAsset", prefab));
            var module = new FactoryModule();
            PrivateFieldAccess.SetField(module, "_entries", PrivateFieldAccess.BuildList(EntryType, new object[] { entry }));
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
        public void Wire_RealFactoryField_CreatesContainerInstantiatesInactiveAndAssignsField()
        {
            // Runs for real once CodeGenSandbox.Bootstrap() has produced a real
            // "_factorySampleFactoryPrefab" field on TsvrcGenerated; Assert.Ignore()s
            // otherwise. See run-codegen-sandbox-tests.ps1.
            SandboxGate.RequireField(_root, CodeGenSandbox.FactoryFieldName);

            var prefab = CreateScratchPrefab(CodeGenSandbox.FactoryEntryName);
            var module = ModuleWithOneEntry(CodeGenSandbox.FactoryEntryName, prefab);

            module.Wire();

            var container = _root.transform.Find("Factories");
            Assert.IsNotNull(container);
            var instance = container.Find(CodeGenSandbox.FactoryEntryName);
            Assert.IsNotNull(instance);
            Assert.IsFalse(instance.gameObject.activeSelf, "Factory instances must be created inactive.");
            var fieldValue = new SerializedObject(_root).FindProperty(CodeGenSandbox.FactoryFieldName).objectReferenceValue;
            Assert.AreEqual(instance.gameObject, fieldValue);
        }

        [Test]
        public void Wire_RealFactoryField_SecondWireWithUnchangedConfigIsIdempotent()
        {
            SandboxGate.RequireField(_root, CodeGenSandbox.FactoryFieldName);

            var prefab = CreateScratchPrefab(CodeGenSandbox.FactoryEntryName);
            var module = ModuleWithOneEntry(CodeGenSandbox.FactoryEntryName, prefab);
            module.Wire();
            var firstInstance = _root.transform.Find("Factories").Find(CodeGenSandbox.FactoryEntryName).gameObject;

            var secondModule = ModuleWithOneEntry(CodeGenSandbox.FactoryEntryName, prefab);
            secondModule.Wire();

            var container = _root.transform.Find("Factories");
            Assert.AreEqual(1, container.childCount, "Re-wiring identical config must not recreate the instance.");
            Assert.AreEqual(firstInstance, container.Find(CodeGenSandbox.FactoryEntryName).gameObject);
        }
    }
}
