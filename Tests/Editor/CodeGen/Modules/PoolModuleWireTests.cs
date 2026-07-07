using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // PoolModule.Wire() against a real compiled root in an isolated temp scene. Unlike
    // Singleton/Construct/Factory, PoolModule's slot-field-not-found path only skips the
    // *field assignment* (Debug.LogWarning, no `continue`) - it still creates the "Pool"
    // container and instantiates every prefab regardless. That makes the scene-mutation
    // half of Wire() fully testable here always; the slot-field assignment itself
    // additionally runs for real whenever CodeGenSandbox.Bootstrap() has been applied (see
    // run-codegen-sandbox-tests.ps1 and CODEGEN_TESTING_PLAN.md Part 4.5), and is
    // Assert.Ignore()'d otherwise. Phase G4.10.
    public class PoolModuleWireTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/PoolWirePrefab.prefab";
        private static readonly Type InfoType = PrivateFieldAccess.NestedType(typeof(PoolModule), "PoolTypeInfo");

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

        private StateManager CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            go.AddComponent<StateManager>();
            var prefabGo = PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("PoolWirePrefab", name));
            return prefabGo.GetComponent<StateManager>();
        }

        private static PoolModule BuildModule(StateManager prefab, int totalSlots)
            => BuildModule(prefab, totalSlots, "StateManager");

        // typeName drives both the generated slot field name (_pool_{typeName}_{i}) and the
        // child GameObject name - it does NOT need to match prefab's actual component type
        // (Wire() separately uses prefabComponent.GetType() for the real GetComponent() call),
        // so tests that specifically need a slot field guaranteed to never exist on the real
        // compiled type (e.g. when this project's CodeGenSandbox bootstrap is active and has
        // created real "StateManager" pool fields) can pass a type name the sandbox never uses.
        private static PoolModule BuildModule(StateManager prefab, int totalSlots, string typeName)
        {
            var info = PrivateFieldAccess.BuildEntry(InfoType,
                ("Prefab", prefab), ("TypeName", typeName), ("TypeNamespace", "Tsvrc.StateMachine"),
                ("ExternalCount", 0), ("InternalDeps", new Dictionary<string, int>(StringComparer.Ordinal)),
                ("TotalSlots", totalSlots));
            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), InfoType);
            var dict = (IDictionary)Activator.CreateInstance(dictType, StringComparer.Ordinal);
            dict[typeName] = info;

            var module = new PoolModule();
            PrivateFieldAccess.SetField(module, "_poolEntries", new List<(Component, string)> { (prefab, typeName) });
            PrivateFieldAccess.SetField(module, "_poolTypeInfos", dict);
            return module;
        }

        [Test]
        public void Wire_NoEntries_ExistingPoolContainerDestroyed()
        {
            // Fixed CODEGEN_TESTING_PLAN.md Part 4 item 1: this used to only clean up the
            // stale "Pool" container when no pool config had ever existed at all. Now the
            // (removed) _hasAnyConfigured distinction is gone entirely - an empty
            // _poolEntries always triggers cleanup, whether nothing was ever configured or
            // everything configured became invalid (e.g. all prefabs deleted).
            var stray = _scope.CreateGameObject("Pool");
            stray.transform.SetParent(_root.transform, false);

            var module = new PoolModule();
            PrivateFieldAccess.SetField(module, "_poolEntries", new List<(Component, string)>());
            PrivateFieldAccess.SetField(module, "_poolTypeInfos", Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), InfoType), StringComparer.Ordinal));

            module.Wire();

            Assert.IsNull(_root.transform.Find("Pool"));
        }

        [Test]
        public void Wire_ThreeSlots_CreatesContainerWithThreeCorrectlyNamedInstances()
        {
            var prefab = CreateScratchPrefab("Widget");
            var module = BuildModule(prefab, totalSlots: 3);

            module.Wire();

            var container = _root.transform.Find("Pool");
            Assert.IsNotNull(container);
            Assert.AreEqual(3, container.childCount);
            Assert.IsNotNull(container.Find("StateManager_0"));
            Assert.IsNotNull(container.Find("StateManager_1"));
            Assert.IsNotNull(container.Find("StateManager_2"));
            foreach (Transform child in container)
                Assert.IsNotNull(child.GetComponent<StateManager>());
        }

        [Test]
        public void Wire_SlotFieldNotFoundOnRoot_StillCreatesInstanceAndWarns()
        {
            // Uses a type name guaranteed to have no real compiled field (unlike
            // "StateManager", which CodeGenSandbox's bootstrap - see
            // run-codegen-sandbox-tests recipe in CodeGenSandbox.cs - legitimately creates
            // real "_pool_StateManager_0"/"_1" fields for), so this always exercises the
            // genuinely-missing-field path regardless of whether the sandbox is active.
            const string fakeTypeName = "PoolModuleWireTestsNeverRealType";
            var prefab = CreateScratchPrefab("Widget2");
            var module = BuildModule(prefab, totalSlots: 1, fakeTypeName);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex($".*Field '_pool_{fakeTypeName}_0' not found.*"));

            module.Wire();

            var container = _root.transform.Find("Pool");
            Assert.IsNotNull(container);
            Assert.IsNotNull(container.Find($"{fakeTypeName}_0"), "Instance must still be created even when the slot field is missing.");
        }

        [Test]
        public void Wire_ExternalWirePoolTarget_FieldGetsAssignedToNewInstance()
        {
            // Phase 2 of Wire() assigns [WirePool] fields on the TARGET behaviour's own
            // SerializedObject, independent of whether the TsvrcGenerated slot field exists -
            // this is the actual "did wiring work" behavior a world author cares about, and
            // it's fully testable without any bootstrap.
            var prefab = CreateScratchPrefab("Widget3");
            var target = _scope.CreateGameObject("Target").AddComponent<PoolWireTargetDouble>();
            var module = BuildModule(prefab, totalSlots: 1);

            module.Wire();

            var instance = _root.transform.Find("Pool").Find("StateManager_0").GetComponent<StateManager>();
            Assert.AreEqual(instance, target.PublicField);
        }

        [Test]
        public void Wire_MoreExternalTargetsThanSlots_LogsStaleReferenceWarning()
        {
            // Fixed CODEGEN_TESTING_PLAN.md Part 4 item 5: CollectWireTargetsByType now
            // reuses IsWirePoolField, the same public/[SerializeField] filter
            // ScanExternalRefs uses for slot-count math - so PoolWireTargetDouble's
            // _nonSerializedField no longer counts as a target here either. Only
            // PublicField and _serializedField count - 2 total, against 1 slot.
            var prefab = CreateScratchPrefab("Widget4");
            _scope.CreateGameObject("Target").AddComponent<PoolWireTargetDouble>();
            var module = BuildModule(prefab, totalSlots: 1);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*2 \\[WirePool\\] target\\(s\\), 1 slot\\(s\\) — 1 component\\(s\\) will keep stale references.*"));

            module.Wire();
        }

        [Test]
        public void Wire_NonSerializedWirePoolField_SilentlyExcludedFromTargetsLikeItIsFromSlotCounts()
        {
            // Fixed CODEGEN_TESTING_PLAN.md Part 4 item 5: CollectWireTargetsByType and
            // ScanExternalRefs now agree on what counts as a wireable [WirePool] field, so a
            // non-serialized private field is silently excluded from both - no "not
            // serialized" warning fires anymore, since the field is never treated as a
            // target for assignment in the first place. As a direct consequence, the slot now
            // has zero targets, so it logs the ordinary "unassigned" mismatch warning instead
            // (same as any other under-targeted slot). The unrelated "slot field not found on
            // TsvrcGenerated" warning is expected too - this project's compiled type has no
            // real "_pool_StateManager_0" field outside CodeGenSandbox.Bootstrap(), same as
            // every other non-bootstrapped test in this fixture.
            var prefab = CreateScratchPrefab("Widget5b");
            _scope.CreateGameObject("Target").AddComponent<PoolWireOnlyNonSerializedFieldDouble>();
            var module = BuildModule(prefab, totalSlots: 1);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field '_pool_StateManager_0' not found.*"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*1 slot\\(s\\), 0 \\[WirePool\\] target\\(s\\) — 1 slot\\(s\\) unassigned.*"));

            module.Wire();

            LogAssert.NoUnexpectedReceived();
        }

        // IsPoolAlreadyWired() is `private` (instance) - only its "not wired" (false)
        // outcomes are verifiable without a bootstrap (see FactoryModuleWireTests for the
        // same reasoning); all of them are checked before the slot-field lookup that would
        // require a real bootstrapped field.
        private static readonly Type WireTargetsDictType =
            typeof(Dictionary<,>).MakeGenericType(typeof(string), typeof(List<(MonoBehaviour, string)>));

        private static bool IsAlreadyWired(PoolModule module, Component root, Transform existingContainer)
        {
            var emptyTargets = Activator.CreateInstance(WireTargetsDictType, StringComparer.Ordinal);
            return (bool)PrivateFieldAccess.InvokeInstance(module, "IsPoolAlreadyWired", root, existingContainer, emptyTargets);
        }

        // Real [WirePool] target collection (CollectWireTargetsByType is `private static`) -
        // used to exercise IsPoolAlreadyWired's inner per-slot target-field-match check with
        // genuine data, rather than an always-absent empty dict.
        private static bool IsAlreadyWiredWithRealTargets(PoolModule module, Component root, Transform existingContainer)
        {
            var realTargets = PrivateFieldAccess.InvokeStatic(typeof(PoolModule), "CollectWireTargetsByType", root.gameObject.scene);
            return (bool)PrivateFieldAccess.InvokeInstance(module, "IsPoolAlreadyWired", root, existingContainer, realTargets);
        }

        [Test]
        public void IsPoolAlreadyWired_NoExistingContainer_ReturnsFalse()
        {
            var prefab = CreateScratchPrefab("Widget5");
            var module = BuildModule(prefab, totalSlots: 1);

            Assert.IsFalse(IsAlreadyWired(module, _root, null));
        }

        [Test]
        public void IsPoolAlreadyWired_ChildCountMismatch_ReturnsFalse()
        {
            var prefab = CreateScratchPrefab("Widget6");
            var module = BuildModule(prefab, totalSlots: 2); // expects 2 slots
            var container = _scope.CreateGameObject("Pool");
            container.transform.SetParent(_root.transform, false);
            var only = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, container.transform);
            only.name = "StateManager_0"; // only one child, not two

            Assert.IsFalse(IsAlreadyWired(module, _root, container.transform));
        }

        [Test]
        public void IsPoolAlreadyWired_ChildFromWrongPrefabSource_ReturnsFalse()
        {
            var prefab = CreateScratchPrefab("Widget7");
            var otherPrefab = CreateScratchPrefab("OtherWidget2");
            var module = BuildModule(prefab, totalSlots: 1);
            var container = _scope.CreateGameObject("Pool");
            container.transform.SetParent(_root.transform, false);
            var wrongInstance = (GameObject)PrefabUtility.InstantiatePrefab(otherPrefab.gameObject, container.transform);
            wrongInstance.name = "StateManager_0";

            Assert.IsFalse(IsAlreadyWired(module, _root, container.transform));
        }

        [Test]
        public void Wire_RealSlotField_IsAssignedToTheNewInstance()
        {
            // Runs for real once CodeGenSandbox.Bootstrap() has produced real
            // "_pool_StateManager_N" fields on TsvrcGenerated; Assert.Ignore()s otherwise.
            // See run-codegen-sandbox-tests.ps1.
            SandboxGate.RequireField(_root, "_pool_" + CodeGenSandbox.PoolTypeName + "_0");

            var prefab = CreateScratchPrefab("Widget8");
            var module = BuildModule(prefab, totalSlots: 1);

            module.Wire();

            var instance = _root.transform.Find("Pool").Find("StateManager_0").GetComponent<StateManager>();
            var fieldValue = new SerializedObject(_root).FindProperty("_pool_StateManager_0").objectReferenceValue;
            Assert.AreEqual(instance, fieldValue);
        }

        [Test]
        public void IsPoolAlreadyWired_RealSlotFieldMatchesExistingChild_ReturnsTrue()
        {
            SandboxGate.RequireField(_root, "_pool_" + CodeGenSandbox.PoolTypeName + "_0");

            var prefab = CreateScratchPrefab("Widget9");
            var module = BuildModule(prefab, totalSlots: 1);
            module.Wire(); // establishes real wiring, including the slot field

            var container = _root.transform.Find("Pool");
            Assert.IsTrue(IsAlreadyWired(module, _root, container), "An unchanged, correctly-wired container/field must be recognized as already wired.");
        }

        [Test]
        public void IsPoolAlreadyWired_RealWirePoolTargetFieldMatches_ReturnsTrue()
        {
            // Exercises IsPoolAlreadyWired's inner per-slot check
            // (`targetProp.objectReferenceValue == childComp`) with a genuine [WirePool]
            // target, rather than the always-empty targets dict IsAlreadyWired() passes.
            SandboxGate.RequireField(_root, "_pool_" + CodeGenSandbox.PoolTypeName + "_0");

            var prefab = CreateScratchPrefab("Widget10");
            var target = _scope.CreateGameObject("RealTarget").AddComponent<PoolWireTargetDouble>();
            var module = BuildModule(prefab, totalSlots: 1);
            module.Wire(); // Phase 2 assigns target.PublicField/_serializedField to the new instance for real

            Assert.IsNotNull(target.PublicField, "Sanity check: Wire() must have actually assigned the target field.");

            var container = _root.transform.Find("Pool");
            Assert.IsTrue(IsAlreadyWiredWithRealTargets(module, _root, container),
                "An unchanged container, slot field, AND matching real [WirePool] target field must all be recognized as already wired.");
        }

        [Test]
        public void IsPoolAlreadyWired_RealWirePoolTargetFieldManuallyCleared_ReturnsFalse()
        {
            // The mirror image of the test above: once Wire() has run, if a user (or another
            // system) clears the target's field back to null by hand, the next Wire() pass
            // must detect the mismatch and re-wire rather than treating it as already wired.
            SandboxGate.RequireField(_root, "_pool_" + CodeGenSandbox.PoolTypeName + "_0");

            var prefab = CreateScratchPrefab("Widget11");
            var target = _scope.CreateGameObject("RealTarget").AddComponent<PoolWireTargetDouble>();
            var module = BuildModule(prefab, totalSlots: 1);
            module.Wire();

            // Only PublicField needs clearing: CollectWireTargetsByType enumerates fields in
            // declaration order, so PublicField is targets[0] for this double's single slot.
            target.PublicField = null;

            var container = _root.transform.Find("Pool");
            Assert.IsFalse(IsAlreadyWiredWithRealTargets(module, _root, container),
                "A manually-cleared [WirePool] target field must invalidate the already-wired check.");
        }
    }
}
