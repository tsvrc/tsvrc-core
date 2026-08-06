using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Core;
using Tsvrc.Editor;
using Tsvrc.Utils;

namespace Tsvrc.Tests.EditMode
{
    // Tests ConstructModule.LoadConfig()'s Layer A snapshot fallback (see ModuleEntrySnapshot
    // and TsModule.ApplySnapshotFallback). This uses the same mechanism and the same test shape
    // as GlobalModuleLoadConfigTests, applied to TsConfig.Constructs instead of Globals.
    public class ConstructModuleLoadConfigTests
    {
        private const string SnapshotKey = "ConstructModule";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            // Without this redirect, Save/Load(SnapshotKey) would hit the real
            // Assets/TsGenerated/.cache/ConstructModule.json, the same file the real,
            // production ConstructModule reads and writes whenever an actual domain reload runs
            // TsGenerator against this project's real scene, which can happen interleaved with
            // this exact test suite run and contaminate these assertions with real project data.
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/ConstructLoadConfigScratch";
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear(SnapshotKey);
            _scope.Dispose(); // also resets TsPaths, including ScriptCompilationFailedOverride
            ScratchAssets.DeleteAll();
        }

        private TsConfig AddConfigWithConstruct(string goName)
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var target = _scope.CreateGameObject(goName).AddComponent<TsvrcMemory>();
            config.Constructs = new TsvrcBehaviour[] { target };
            return config;
        }

        [Test]
        public void LoadConfig_CompilesClean_SavesASnapshotOfTheLiveResult()
        {
            AddConfigWithConstruct("SomeMemory");
            TsPaths.ScriptCompilationFailedOverride = false;

            new ConstructModule().LoadConfig();

            var snapshot = ModuleEntrySnapshot.Load(SnapshotKey);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual("TsvrcMemory", snapshot[0].Name);
        }

        [Test]
        public void LoadConfig_CompilesCleanWithNoConfig_SavesAnEmptySnapshot_NotStale()
        {
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "Stale", TypeName = "Stale", Namespace = "" },
            });
            TsPaths.ScriptCompilationFailedOverride = false;

            var module = new ConstructModule();
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
                new ModuleEntrySnapshot.Entry { Name = "HudManager", TypeName = "HudManager", Namespace = "MoL.UI" },
            });
            // There is no TsConfig in this scene at all, so live resolution finds nothing, simulating the
            // real construct having gone null because the assembly that declares it failed to
            // compile.
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new ConstructModule();
            module.LoadConfig();

            StringAssert.Contains("HudManager", module.GenerateCode());
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentButLiveResultAtLeastAsLarge_UsesLiveNotStaleSnapshot()
        {
            ModuleEntrySnapshot.Save(SnapshotKey, new System.Collections.Generic.List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "OldEntry", TypeName = "OldEntry", Namespace = "" },
            });
            AddConfigWithConstruct("CurrentMemory");
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new ConstructModule();
            module.LoadConfig();
            string generated = module.GenerateCode();

            StringAssert.Contains("TsvrcMemory", generated);
            StringAssert.DoesNotContain("OldEntry", generated);
        }

        [Test]
        public void LoadConfig_CompileErrorsPresentAndNoSnapshotEverSaved_ResolvesEmptyWithoutThrowing()
        {
            TsPaths.ScriptCompilationFailedOverride = true;

            var module = new ConstructModule();

            Assert.DoesNotThrow(() => module.LoadConfig());
            StringAssert.DoesNotContain("[SerializeField]", module.GenerateCode());
        }
    }
}
