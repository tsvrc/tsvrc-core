using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using Tsvrc.Utils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // GlobalModule.LoadConfig()'s tree-shaking gate (TsModule.ApplyTreeShaking). Off by
    // default - every test here explicitly opts a TsConfig into TreeShakeUnused, so the
    // default-off behavior already covered by GlobalModuleLoadConfigTests is never silently
    // changed by this feature.
    public class GlobalModuleTreeShakingTests
    {
        private const string SnapshotKey = "GlobalModule";
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/GlobalTreeShakingScratch";
            TsPaths.ScriptCompilationFailedOverride = false;
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear(SnapshotKey);
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_sourceTexts", null);
            _scope.Dispose(); // also resets TsPaths, including ScriptCompilationFailedOverride
            ScratchAssets.DeleteAll();
        }

        // Seeds ScriptIndex's whole-project source-text scan directly, the same way
        // TsModuleHelpersTests/ScriptIndexTests seed _baseByClass, instead of depending on real
        // MonoScript assets on disk - keeps "is this entry referenced" deterministic and isolated
        // from whatever this repository's own real scripts happen to say.
        private static void SeedProjectSource(params string[] sourceFiles) =>
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_sourceTexts", new List<string>(sourceFiles));

        private TsConfig AddConfigWithGlobal(string goName, bool treeShake, params string[] forceIncludeNames)
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var target = _scope.CreateGameObject(goName).AddComponent<TsvrcMemory>();
            config.GlobalEntries = new[] { new TsGroupedEntry { Value = target, GroupId = 0 } };
            config.TreeShakeUnused = treeShake;
            config.ForceIncludeNames = forceIncludeNames;
            return config;
        }

        [Test]
        public void LoadConfig_TreeShakingOff_KeepsEntryEvenWithNoReferencesAnywhere()
        {
            AddConfigWithGlobal("SomeMemory", treeShake: false);
            SeedProjectSource("public class Unrelated { }");

            var module = new GlobalModule();
            module.LoadConfig();

            StringAssert.Contains("TsvrcMemory", module.GenerateCode());
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndReferenced_KeepsEntry()
        {
            AddConfigWithGlobal("SomeMemory", treeShake: true);
            SeedProjectSource("public class Foo : TsvrcBehaviour { void Bar() { _ts.TsvrcMemory.DoThing(); } }");

            var module = new GlobalModule();
            module.LoadConfig();

            StringAssert.Contains("TsvrcMemory", module.GenerateCode());
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
        }

        // The first time a registered Global resolves as unreferenced, it's kept via the
        // one-pass grace period (TsModule.ConsumeGracePeriod), not excluded outright, since a
        // developer who just registered it would otherwise have no field to write code against.
        [Test]
        public void LoadConfig_TreeShakingOnAndNotReferencedForTheFirstTime_KeepsEntryViaGracePeriod()
        {
            AddConfigWithGlobal("SomeMemory", treeShake: true);
            SeedProjectSource("public class Unrelated { }");

            LogAssert.Expect(LogType.Log, new Regex(@"\[GlobalModule\] 1 entry isn't referenced"));
            var module = new GlobalModule();
            module.LoadConfig();

            StringAssert.Contains("TsvrcMemory", module.GenerateCode(), "Must still be generated during its grace period.");
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
            CollectionAssert.AreEqual(new[] { "TsvrcMemory" }, module.LastTreeShakingGraceIncluded);
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndNotReferencedForASecondConsecutivePass_ExcludesEntryAndLogsIt()
        {
            AddConfigWithGlobal("SomeMemory", treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            // First pass consumes the grace period.
            new GlobalModule().LoadConfig();

            LogAssert.Expect(LogType.Log, new Regex(@"\[GlobalModule\] Excluded 'TsvrcMemory' - not referenced.*two regenerates"));
            var module = new GlobalModule();
            module.LoadConfig();

            StringAssert.DoesNotContain("TsvrcMemory", module.GenerateCode());
            CollectionAssert.AreEqual(new[] { "TsvrcMemory" }, module.LastTreeShakingExclusions);
            CollectionAssert.IsEmpty(module.LastTreeShakingGraceIncluded);
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndForceIncluded_KeepsEntryEvenWithoutAnyReference()
        {
            AddConfigWithGlobal("SomeMemory", treeShake: true, "TsvrcMemory");
            SeedProjectSource("public class Unrelated { }");

            var module = new GlobalModule();
            module.LoadConfig();

            StringAssert.Contains("TsvrcMemory", module.GenerateCode());
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
            CollectionAssert.IsEmpty(module.LastTreeShakingGraceIncluded, "Force-included entries skip grace-period bookkeeping entirely.");
        }

        [Test]
        public void LoadConfig_ExcludedEntry_DoesNotTriggerLastKnownGoodWarning()
        {
            // First pass: tree-shaking off, one real entry, establishes a last-known-good count
            // of 1 (see TsModule.ApplySnapshotFallback/WarnIfBelowLastKnownGood).
            var config = AddConfigWithGlobal("SomeMemory", treeShake: false);
            new GlobalModule().LoadConfig();

            // Second pass: tree-shaking now on, unreferenced for the first time - kept via grace,
            // count stays at 1, nothing to warn about yet.
            config.TreeShakeUnused = true;
            SeedProjectSource("public class Unrelated { }");
            new GlobalModule().LoadConfig();

            // Third pass: still unreferenced (second consecutive miss) - now actually excluded,
            // dropping the count from 1 to 0. This drop is fully explained by this pass's own
            // tree-shaking exclusion, so WarnIfBelowLastKnownGood must stay silent. Any unexpected
            // Warning here fails the test via LogAssert's own default behavior.
            LogAssert.Expect(LogType.Log, new Regex(@"\[GlobalModule\] Excluded"));
            new GlobalModule().LoadConfig();
        }
    }
}
