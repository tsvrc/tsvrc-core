using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // FactoryModule.LoadConfig()'s tree-shaking gate (TsModule.ApplyTreeShaking). A Factory
    // entry's usage signature is its generated Create{Name}(...) call site
    // (TsUsageScanner.IsMethodCallReferenced), not a bare member access, so this gets its own
    // file rather than sharing GlobalModuleTreeShakingTests' fixture.
    //
    // Tsvrc ships its own real Assets/Tsvrc/Runtime/Config/TsBuiltinConfig.asset with real
    // builtin Factory prefabs. Those flow through the same tree-shaking filter as this file's own
    // scratch prefab, so assertions here only check for the presence/absence of this file's own
    // entry rather than exact totals, matching FactoryModuleLoadConfigTests' own style.
    public class FactoryModuleTreeShakingTests
    {
        private const string SnapshotKey = "FactoryModule";
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/FactoryTreeShakingPrefab.prefab";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/FactoryTreeShakingScratch";
            TsPaths.ScriptCompilationFailedOverride = false;
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear(SnapshotKey);
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_sourceTexts", null);
            ScratchAssets.DeleteAll();
            _scope.Dispose();
        }

        private static void SeedProjectSource(params string[] sourceFiles) =>
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_sourceTexts", new List<string>(sourceFiles));

        private GameObject CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            return PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("FactoryTreeShakingPrefab", name));
        }

        private TsConfig AddConfigWithFactoryPrefab(string prefabName, bool treeShake, params string[] forceIncludeNames)
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var prefab = CreateScratchPrefab(prefabName);
            config.FactoryEntries = new[] { new TsGroupedEntry { Value = prefab, GroupId = 0 } };
            config.TreeShakeUnused = treeShake;
            config.ForceIncludeNames = forceIncludeNames;
            return config;
        }

        [Test]
        public void LoadConfig_TreeShakingOff_KeepsEntryEvenWithNoCallSiteAnywhere()
        {
            AddConfigWithFactoryPrefab("Widget", treeShake: false);
            SeedProjectSource("public class Unrelated { }");

            var module = new FactoryModule();
            module.LoadConfig();

            StringAssert.Contains("CreateWidget", module.GenerateCode());
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndCalled_KeepsEntry()
        {
            AddConfigWithFactoryPrefab("Widget", treeShake: true);
            SeedProjectSource("public class Foo : TsvrcBehaviour { void Bar() { _ts.CreateWidget(transform); } }");

            var module = new FactoryModule();
            module.LoadConfig();

            StringAssert.Contains("CreateWidget", module.GenerateCode());
            CollectionAssert.DoesNotContain(module.LastTreeShakingExclusions, "Widget");
        }

        // First unreferenced observation is kept via the one-pass grace period
        // (TsModule.ConsumeGracePeriod), not excluded outright.
        [Test]
        public void LoadConfig_TreeShakingOnAndNeverCalledForTheFirstTime_KeepsEntryViaGracePeriod()
        {
            AddConfigWithFactoryPrefab("Widget", treeShake: true);
            SeedProjectSource("public class Unrelated { }");

            var module = new FactoryModule();
            module.LoadConfig();

            StringAssert.Contains("CreateWidget", module.GenerateCode(), "Must still be generated during its grace period.");
            CollectionAssert.DoesNotContain(module.LastTreeShakingExclusions, "Widget");
            CollectionAssert.Contains(module.LastTreeShakingGraceIncluded, "Widget");
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndNeverCalledForASecondConsecutivePass_ExcludesEntryAndLogsIt()
        {
            AddConfigWithFactoryPrefab("Widget", treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            // First pass consumes "Widget"'s grace period.
            new FactoryModule().LoadConfig();

            LogAssert.Expect(LogType.Log, new Regex(@"\[FactoryModule\] Excluded 'Widget' - not referenced.*two regenerates"));
            var module = new FactoryModule();
            module.LoadConfig();

            StringAssert.DoesNotContain("CreateWidget", module.GenerateCode());
            CollectionAssert.Contains(module.LastTreeShakingExclusions, "Widget");
            CollectionAssert.DoesNotContain(module.LastTreeShakingGraceIncluded, "Widget");
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndForceIncluded_KeepsEntryEvenWithoutAnyCallSite()
        {
            AddConfigWithFactoryPrefab("Widget", treeShake: true, "Widget");
            SeedProjectSource("public class Unrelated { }");

            var module = new FactoryModule();
            module.LoadConfig();

            StringAssert.Contains("CreateWidget", module.GenerateCode());
            CollectionAssert.DoesNotContain(module.LastTreeShakingExclusions, "Widget");
        }
    }
}
