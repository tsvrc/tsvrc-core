using NUnit.Framework;
using System.Collections.Generic;
using System.Collections;
using System;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // PoolModule.Wire() against a real compiled root in an isolated temp scene. Unlike
    // Singleton/Construct/Factory, PoolModule's slot-field-not-found path only skips the
    // *field assignment* (Debug.LogWarning, no `continue`) - it still creates the "Pool"
    // container and instantiates every prefab regardless. That makes the scene-mutation half
    // of Wire() fully testable here always. The slot-field assignment mechanism itself is the
    // same generic SerializedProperty scalar assignment proven end-to-end by
    // SingletonModuleWireTests.Wire_FieldFound_DirectReferenceIsAssigned. IsPoolAlreadyWired's
    // per-slot field-match checks below are instance methods that take `root` as an explicit
    // parameter rather than looking it up themselves, so their "true" outcomes are tested
    // directly against a plain test double carrying a literal "_pool_StateManager_0" field,
    // without needing this project's real compiled type to have one.
    public class PoolModuleWireTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/PoolWirePrefab.prefab";
        private static readonly Type InfoType = CodeGenModuleReflection.NestedType(typeof(PoolModule), "PoolTypeInfo");

        // A stand-in for the compiled root exposing only the one literal field name
        // IsPoolAlreadyWired needs to find a match against - IsPoolAlreadyWired takes `root`
        // as a parameter rather than looking up the real compiled type itself, so any
        // Component with the right field works.
        private class PoolSlotFieldDouble : MonoBehaviour
        {
            public StateManager _pool_StateManager_0;
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
        // compiled type can pass an arbitrary type name.
        private static PoolModule BuildModule(StateManager prefab, int totalSlots, string typeName)
        {
            var info = CodeGenModuleReflection.BuildEntry(InfoType,
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
            // An empty _poolEntries always triggers cleanup of a stale "Pool" container,
            // whether nothing was ever configured or everything configured became invalid
            // (e.g. all prefabs deleted).
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
            // Uses a type name guaranteed to have no real compiled field, so this always
            // exercises the genuinely-missing-field path.
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
            // SerializedObject, independent of whether the TsGenerated slot field exists -
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
            // CollectWireTargetsByType reuses IsWirePoolField, the same public/[SerializeField]
            // filter ScanExternalRefs uses for slot-count math - so PoolWireTargetDouble's
            // _nonSerializedField does not count as a target here either. Only PublicField
            // and _serializedField count - 2 total, against 1 slot.
            var prefab = CreateScratchPrefab("Widget4");
            _scope.CreateGameObject("Target").AddComponent<PoolWireTargetDouble>();
            var module = BuildModule(prefab, totalSlots: 1);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*2 \\[WirePool\\] target\\(s\\), 1 slot\\(s\\) — 1 component\\(s\\) will keep stale references.*"));

            module.Wire();
        }

        [Test]
        public void Wire_NonSerializedWirePoolField_SilentlyExcludedFromTargetsLikeItIsFromSlotCounts()
        {
            // CollectWireTargetsByType and ScanExternalRefs agree on what counts as a wireable
            // [WirePool] field, so a non-serialized private field is silently excluded from
            // both - no "not serialized" warning fires, since the field is never treated as a
            // target for assignment in the first place. As a consequence the slot has zero
            // targets, so it logs the ordinary "unassigned" mismatch warning instead (same as
            // any other under-targeted slot). The unrelated "slot field not found on
            // TsGenerated" warning is expected too - this project's compiled type never has
            // a real "_pool_StateManager_0" field, same as every other test in this fixture.
            var prefab = CreateScratchPrefab("Widget5b");
            _scope.CreateGameObject("Target").AddComponent<PoolWireOnlyNonSerializedFieldDouble>();
            var module = BuildModule(prefab, totalSlots: 1);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field '_pool_StateManager_0' not found.*"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*1 slot\\(s\\), 0 \\[WirePool\\] target\\(s\\) — 1 slot\\(s\\) unassigned.*"));

            module.Wire();

            LogAssert.NoUnexpectedReceived();
        }

        // IsPoolAlreadyWired() is `private` (instance) - reachable via reflection, passing
        // whatever root/container/targets the test constructs.
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

        private (Component root, Transform container, StateManager child) BuildWiredState(StateManager prefab)
        {
            var root = _scope.CreateGameObject("FakeRoot").AddComponent<PoolSlotFieldDouble>();
            var container = _scope.CreateGameObject("Pool");
            container.transform.SetParent(root.transform, false);
            var childGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, container.transform);
            childGo.name = "StateManager_0";
            var child = childGo.GetComponent<StateManager>();
            root._pool_StateManager_0 = child;
            return (root, container.transform, child);
        }

        [Test]
        public void IsPoolAlreadyWired_SlotFieldMatchesExistingChild_ReturnsTrue()
        {
            var prefab = CreateScratchPrefab("Widget9");
            var module = BuildModule(prefab, totalSlots: 1);
            var (root, container, _) = BuildWiredState(prefab);

            Assert.IsTrue(IsAlreadyWired(module, root, container), "An unchanged, correctly-wired container/field must be recognized as already wired.");
        }

        [Test]
        public void IsPoolAlreadyWired_WirePoolTargetFieldMatches_ReturnsTrue()
        {
            // Exercises IsPoolAlreadyWired's inner per-slot check
            // (`targetProp.objectReferenceValue == childComp`) with a genuine [WirePool]
            // target, rather than the always-empty targets dict IsAlreadyWired() passes.
            var prefab = CreateScratchPrefab("Widget10");
            var (root, container, child) = BuildWiredState(prefab);
            var target = _scope.CreateGameObject("RealTarget").AddComponent<PoolWireTargetDouble>();
            target.PublicField = child; // simulates Wire()'s own [WirePool] target assignment
            var module = BuildModule(prefab, totalSlots: 1);

            Assert.IsTrue(IsAlreadyWiredWithRealTargets(module, root, container),
                "An unchanged container, slot field, AND matching real [WirePool] target field must all be recognized as already wired.");
        }

        [Test]
        public void IsPoolAlreadyWired_WirePoolTargetFieldManuallyCleared_ReturnsFalse()
        {
            // The mirror image of the test above: if a user (or another system) clears the
            // target's field back to null by hand, the next Wire() pass must detect the
            // mismatch and re-wire rather than treating it as already wired. Left at its
            // default null: CollectWireTargetsByType enumerates fields in declaration order,
            // so PublicField is targets[0] for this double's single slot.
            var prefab = CreateScratchPrefab("Widget11");
            var (root, container, _) = BuildWiredState(prefab);
            var target = _scope.CreateGameObject("RealTarget").AddComponent<PoolWireTargetDouble>();
            var module = BuildModule(prefab, totalSlots: 1);

            Assert.IsFalse(IsAlreadyWiredWithRealTargets(module, root, container),
                "A manually-cleared [WirePool] target field must invalidate the already-wired check.");
        }
    }
}
