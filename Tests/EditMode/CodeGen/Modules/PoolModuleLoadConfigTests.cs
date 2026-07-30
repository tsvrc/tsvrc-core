using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using Tsvrc.Testing.Framework;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Tests PoolModule.LoadConfig()'s Layer A snapshot fallback, the same mechanism as Singleton,
    // Construct, and Factory, but PoolModule also snapshots each type's TotalSlots (Entry.SlotCount)
    // since that number is itself derived from a live, scene-wide [WirePool] reflection scan,
    // just as fragile to a broken compile as the entry list itself. Wire()'s destructive early
    // exit (tearing down "Pool" when _poolEntries looks empty) is guarded separately, see
    // Wire_DuringSnapshotFallback_DoesNotDestroyExistingPoolContainer.
    public class PoolModuleLoadConfigTests
    {
        private const string SnapshotKey = "PoolModule";
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/PoolLoadConfigPrefab.prefab";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            // Without this redirect, Save/Load(SnapshotKey) would hit the real
            // Assets/TsGenerated/.cache/PoolModule.json, the same file the real, production
            // PoolModule reads and writes whenever an actual domain reload runs TsGenerator against
            // this project's real scene, which can happen interleaved with this exact test suite
            // run and contaminate these assertions with real project data.
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/PoolLoadConfigScratch";
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear(SnapshotKey);
            ScratchAssets.DeleteAll();
            _scope.Dispose(); // also resets TsPaths, including ScriptCompilationFailedOverride
        }

        private StateManager CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            go.AddComponent<StateManager>();
            var prefabGo = PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("PoolLoadConfigPrefab", name));
            return prefabGo.GetComponent<StateManager>();
        }

        // Gives the pooled StateManager type a real, nonzero ExternalCount/TotalSlots via the
        // live [WirePool] reflection scan. Otherwise every entry here would compute
        // TotalSlots == 0 and GenerateCode() would exclude it regardless of the snapshot
        // fallback under test. PoolWireTargetDouble carries two countable [WirePool] StateManager
        // fields (PublicField and the serialized-private one; the non-serialized-private and
        // array/generic fields are excluded by IsWirePoolField/ScanExternalRefs), so one instance
        // contributes ExternalCount == 2, not 1.
        private void AddWireTarget() => _scope.CreateGameObject("WireTarget").AddComponent<PoolWireTargetDouble>();
        private const int WireTargetSlotContribution = 2;

        private void AddConfigWithPooledPrefab(string name)
        {
            var config = _scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();
            config.PooledObjects = new UdonSharpBehaviour[] { CreateScratchPrefab(name) };
        }

        // Tsvrc ships its own real Assets/Tsvrc/Runtime/Config/TsBuiltinConfig.asset with real
        // builtin pool prefabs configured (currently 6 across a handful of distinct types).
        // BuiltinConfigPath isn't a test-redirectable seam like TsPaths.GeneratedFolder is, so
        // every live resolution in this file legitimately includes them. Computed dynamically
        // (via the real, private ResolveConfig + the same by-typeName dedup LoadConfig applies)
        // rather than hardcoded, so this file keeps working if that asset's contents ever change.
        private static int RealBuiltinPoolTypeCount()
        {
            var builtin = AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>("Assets/Tsvrc/Runtime/Config/TsBuiltinConfig.asset");
            var resolved = (List<(Component prefab, string typeName)>)PrivateFieldAccess.InvokeStatic(
                typeof(PoolModule), "ResolveConfig", null, builtin);
            return resolved.Select(e => e.typeName).Distinct().Count();
        }

        [Test]
        public void LoadConfig_CompilesClean_SavesASnapshotOfTheLiveResultIncludingSlotCount()
        {
            AddConfigWithPooledPrefab("Prefab1");
            AddWireTarget();
            TsPaths.ScriptCompilationFailedOverride = false;

            new PoolModule().LoadConfig();

            var snapshot = ModuleEntrySnapshot.Load(SnapshotKey);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(RealBuiltinPoolTypeCount() + 1, snapshot.Count);
            var stateManagerEntry = snapshot.Find(e => e.TypeName == "StateManager");
            Assert.IsNotNull(stateManagerEntry);
            Assert.AreEqual(WireTargetSlotContribution, stateManagerEntry.SlotCount);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndLiveResultEmpty_FallsBackToSnapshotIncludingSlotCount()
        {
            // The cached count must exceed live (builtins only, since no user TsConfig or prefab exists
            // this pass) by at least one for the fallback to trigger. This pads with the real
            // builtin type count itself rather than a hardcoded number, so it keeps working if
            // the real TsBuiltinConfig asset's contents ever change.
            var cached = new List<ModuleEntrySnapshot.Entry>();
            for (int i = 0; i < RealBuiltinPoolTypeCount(); i++)
                cached.Add(new ModuleEntrySnapshot.Entry { Name = $"Padding{i}", TypeName = $"Padding{i}", Namespace = "", SlotCount = 1 });
            cached.Add(new ModuleEntrySnapshot.Entry { Name = "StateManager", TypeName = "StateManager", Namespace = "Tsvrc.StateMachine", SlotCount = 2 });
            ModuleEntrySnapshot.Save(SnapshotKey, cached);
            // There is no user TsConfig or prefab at all this pass, so live resolution finds only the real
            // builtins, simulating every user pool prefab reference having gone null because the
            // assembly declaring the pooled type failed to compile.
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new PoolModule();
            module.LoadConfig();
            string generated = module.GenerateCode();

            StringAssert.Contains("_pool_StateManager_0", generated);
            StringAssert.Contains("_pool_StateManager_1", generated);
            StringAssert.DoesNotContain("_pool_StateManager_2", generated);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentButLiveResultAtLeastAsLarge_UsesLiveNotStaleSnapshot()
        {
            ModuleEntrySnapshot.Save(SnapshotKey, new List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "OldEntry", TypeName = "OldEntry", Namespace = "", SlotCount = 5 },
            });
            AddConfigWithPooledPrefab("Prefab1");
            AddWireTarget();
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new PoolModule();
            module.LoadConfig();
            string generated = module.GenerateCode();

            StringAssert.Contains("StateManager", generated);
            StringAssert.DoesNotContain("OldEntry", generated);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndNoSnapshotEverSaved_ResolvesEmptyWithoutThrowing()
        {
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new PoolModule();

            Assert.DoesNotThrow(() => module.LoadConfig());
            StringAssert.DoesNotContain("SerializeField", module.GenerateCode());
        }

        [Test]
        public void Wire_DuringSnapshotFallback_DoesNotDestroyExistingPoolContainer()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var poolContainer = new GameObject("Pool");
            poolContainer.transform.SetParent(root.transform, false);

            ModuleEntrySnapshot.Save(SnapshotKey, new List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "StateManager", TypeName = "StateManager", Namespace = "Tsvrc.StateMachine", SlotCount = 1 },
            });
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new PoolModule();
            module.LoadConfig();
            module.Wire();

            Assert.IsNotNull(root.transform.Find("Pool"),
                "Wire() must not tear down an existing Pool container while operating on a compile-broken, snapshot-restored result.");
        }
    }
}
