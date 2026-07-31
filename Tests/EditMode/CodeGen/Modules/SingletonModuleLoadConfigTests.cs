using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.Utils;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Tests SingletonModule.LoadConfig()'s Layer A snapshot fallback (see ModuleEntrySnapshot and
    // TsModule.ApplySnapshotFallback). This is the exact mechanism that would have prevented
    // the real regression that motivated it: a broken Assembly-CSharp compile nulling out
    // every scene reference to a Singleton, silently regenerating an empty stub over 9 real
    // entries. Resolve()'s own name-derivation, dedup, and alias logic is covered separately in
    // SingletonModuleResolveTests; this file is only about the snapshot save/restore wiring.
    public class SingletonModuleLoadConfigTests
    {
        private const string SnapshotKey = "SingletonModule";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            // Without this redirect, Save/Load(SnapshotKey) would hit the real
            // Assets/TsGenerated/.cache/SingletonModule.json, the same file the real,
            // production SingletonModule reads and writes whenever an actual domain reload runs
            // TsGenerator against this project's real scene, which can happen interleaved with
            // this exact test suite run and contaminate these assertions with real project data.
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/SingletonLoadConfigScratch";
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear(SnapshotKey);
            _scope.Dispose(); // also resets TsPaths, including ScriptCompilationFailedOverride
            ScratchAssets.DeleteAll();
        }

        private TsConfig AddConfigWithSingleton(string goName)
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var target = _scope.CreateGameObject(goName).AddComponent<TsvrcMemory>();
            config.Singletons = new Object[] { target };
            return config;
        }

        [Test]
        public void LoadConfig_CompilesClean_SavesASnapshotOfTheLiveResult()
        {
            AddConfigWithSingleton("SomeMemory");
            TsPaths.ScriptCompilationFailedOverride = false;

            new SingletonModule().LoadConfig();

            var snapshot = ModuleEntrySnapshot.Load(SnapshotKey);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual("TsvrcMemory", snapshot[0].Name);
        }

        [Test]
        public void LoadConfig_CompilesCleanWithNoConfig_SavesAnEmptySnapshot_NotStale()
        {
            // Seed a stale snapshot from an earlier (different) scene state first.
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "Stale", TypeName = "Stale", Namespace = "" },
            });
            TsPaths.ScriptCompilationFailedOverride = false;

            var module = new SingletonModule();
            module.LoadConfig();

            StringAssert.DoesNotContain("Stale", module.GenerateCode(),
                "A clean compile with a genuinely empty config must overwrite the stale snapshot, not preserve it.");
            Assert.IsEmpty(ModuleEntrySnapshot.Load(SnapshotKey));
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndLiveResultEmpty_FallsBackToSnapshot()
        {
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "SettingsManager", TypeName = "SettingsManager", Namespace = "MoL.Core" },
                new ModuleEntrySnapshot.Entry { Name = "GameManager", TypeName = "GameManager", Namespace = "MoL.Game" },
            });
            // There is no TsConfig in this scene at all, so live resolution finds nothing, simulating every
            // Singleton reference having gone null because the assembly that declares them
            // failed to compile.
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new SingletonModule();
            module.LoadConfig();
            string generated = module.GenerateCode();

            StringAssert.Contains("SettingsManager", generated);
            StringAssert.Contains("GameManager", generated);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentButLiveResultAtLeastAsLarge_UsesLiveNotStaleSnapshot()
        {
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "OldEntry", TypeName = "OldEntry", Namespace = "" },
            });
            AddConfigWithSingleton("CurrentMemory");
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new SingletonModule();
            module.LoadConfig();
            string generated = module.GenerateCode();

            StringAssert.Contains("TsvrcMemory", generated);
            StringAssert.DoesNotContain("OldEntry", generated);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndNoSnapshotEverSaved_ResolvesEmptyWithoutThrowing()
        {
            // There is nothing to fall back to, so it must degrade to the same empty stub a clean-but-empty
            // config would produce, not throw.
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new SingletonModule();

            Assert.DoesNotThrow(() => module.LoadConfig());
            StringAssert.DoesNotContain("[SerializeField]", module.GenerateCode());
        }
    }
}
