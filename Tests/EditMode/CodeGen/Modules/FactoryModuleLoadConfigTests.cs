using System.Linq;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Tests FactoryModule.LoadConfig()'s Layer A snapshot fallback (see ModuleEntrySnapshot and
    // TsModule.ApplySnapshotFallback), using the same mechanism as GlobalModuleLoadConfigTests,
    // for the module that also generates the Create{Group}{Name} methods MoL's own world
    // scripts were calling. BuildEntries()'s own group/prefix/dedup logic is covered separately
    // in FactoryModuleBuildEntriesTests; this file is only about the snapshot save/restore
    // wiring, including that the restored IsTsvrcBehaviour flag is recomputed correctly.
    public class FactoryModuleLoadConfigTests
    {
        private const string SnapshotKey = "FactoryModule";
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/FactoryLoadConfigPrefab.prefab";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            // Without this redirect, Save/Load(SnapshotKey) would hit the real
            // Assets/TsGenerated/.cache/FactoryModule.json, the same file the real, production
            // FactoryModule reads and writes whenever an actual domain reload runs TsGenerator
            // against this project's real scene, which can happen interleaved with this exact
            // test suite run and contaminate these assertions with real project data.
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/FactoryLoadConfigScratch";
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear(SnapshotKey);
            ScratchAssets.DeleteAll();
            _scope.Dispose(); // also resets TsPaths, including ScriptCompilationFailedOverride
        }

        private GameObject CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            return PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("FactoryLoadConfigPrefab", name));
        }

        private TsConfig AddConfigWithFactoryPrefab(string prefabName)
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var prefab = CreateScratchPrefab(prefabName);
            config.FactoryEntries = new[] { new TsGroupedEntry { Value = prefab, GroupId = 0 } };
            return config;
        }

        // Tsvrc ships its own real Assets/Tsvrc/Runtime/Config/TsBuiltinConfig.asset with real
        // builtin Factory prefabs configured (currently 2). BuiltinConfigPath isn't a
        // test-redirectable seam like TsPaths.GeneratedFolder is, so every live resolution in
        // this file legitimately includes them alongside whatever the test itself configures.
        // Computed dynamically (not hardcoded) so this file keeps working if that asset's
        // contents ever change.
        private static int RealBuiltinFactoryCount()
        {
            var builtin = AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>("Assets/Tsvrc/Runtime/Config/TsBuiltinConfig.asset");
            if (builtin?.FactoryEntries == null) return 0;
            return builtin.FactoryEntries.Count(e => e?.Value != null);
        }

        [Test]
        public void LoadConfig_CompilesClean_SavesASnapshotOfTheLiveResult()
        {
            AddConfigWithFactoryPrefab("Widget");
            TsPaths.ScriptCompilationFailedOverride = false;

            new FactoryModule().LoadConfig();

            var snapshot = ModuleEntrySnapshot.Load(SnapshotKey);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(RealBuiltinFactoryCount() + 1, snapshot.Count);
            Assert.IsTrue(snapshot.Exists(e => e.Name == "Widget"));
        }

        [Test]
        public void LoadConfig_CompilesCleanWithNoConfig_SavesTheRealBuiltinOnlySnapshot_NotStale()
        {
            // Seed a stale snapshot from an earlier (different) scene state first.
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "Stale", TypeName = "GameObject", Namespace = "" },
            });
            TsPaths.ScriptCompilationFailedOverride = false;

            var module = new FactoryModule();
            module.LoadConfig();

            StringAssert.DoesNotContain("Stale", module.GenerateCode(),
                "A clean compile with no user config must overwrite the stale snapshot, not preserve it.");
            // Not necessarily empty, since the real builtin config's own Factories still resolve live.
            Assert.AreEqual(RealBuiltinFactoryCount(), ModuleEntrySnapshot.Load(SnapshotKey).Count);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndNoSnapshotEverSaved_ResolvesLiveBuiltinsWithoutThrowing()
        {
            // There is nothing to fall back to, so it must resolve whatever's live (the real
            // builtins, since no user TsConfig exists in this scene) without throwing, never a bare empty stub,
            // since ApplySnapshotFallback always trusts live when there's no cached snapshot.
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new FactoryModule();

            Assert.DoesNotThrow(() => module.LoadConfig());
            int createCount = System.Text.RegularExpressions.Regex.Matches(module.GenerateCode(), @"public \S+ Create\w+\(Transform parent\)").Count;
            Assert.AreEqual(RealBuiltinFactoryCount(), createCount);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndLiveResultSmallerThanSnapshot_FallsBackToSnapshot()
        {
            // The cached count must exceed live (builtins only, since no user TsConfig exists this
            // pass) by at least one for the fallback to trigger. This pads with the real builtin
            // count itself rather than a hardcoded number, so it keeps working if the real
            // TsBuiltinConfig asset's contents ever change.
            var cached = new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>();
            for (int i = 0; i < RealBuiltinFactoryCount(); i++)
                cached.Add(new ModuleEntrySnapshot.Entry { Name = $"Padding{i}", TypeName = "GameObject", Namespace = "" });
            cached.Add(new ModuleEntrySnapshot.Entry { Name = "MazeWallStoneFantasy", TypeName = "GameObject", Namespace = "" });
            ModuleEntrySnapshot.Save(SnapshotKey, cached);
            // There is no user TsConfig in this scene, simulating every Factories prefab reference having
            // gone null because the assembly declaring it (or the scene itself) is unreadable.
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new FactoryModule();
            module.LoadConfig();

            StringAssert.Contains("CreateMazeWallStoneFantasy", module.GenerateCode());
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndLiveResultEmpty_RestoredEntryIsTsvrcBehaviourRecomputedFromScriptIndex()
        {
            // Same fallback path, but the cached entry names a type that is a TsvrcBehaviour
            // subclass per source, proving the restored entry's IsTsvrcBehaviour flag (which the
            // snapshot itself does not persist) is correctly recomputed, not just defaulted to
            // false, by going through IsTsvrcBehaviourType's ScriptIndex fallback.
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "StateManager", TypeName = "StateManager", Namespace = "Tsvrc.StateMachine" },
            });
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new FactoryModule();
            module.LoadConfig();
            string generated = module.GenerateCode();

            // IsTsvrcBehaviour == true means GenerateCode() emits the TsConstruct(this) call.
            StringAssert.Contains("instance.TsConstruct(this)", generated);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentButLiveResultAtLeastAsLarge_UsesLiveNotStaleSnapshot()
        {
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "OldEntry", TypeName = "GameObject", Namespace = "" },
            });
            AddConfigWithFactoryPrefab("CurrentWidget");
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new FactoryModule();
            module.LoadConfig();
            string generated = module.GenerateCode();

            StringAssert.Contains("CreateCurrentWidget", generated);
            StringAssert.DoesNotContain("OldEntry", generated);
        }
    }
}
