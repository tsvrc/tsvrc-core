using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // TsSingleComponentModule's tree-shaking gate: only the GenerateCode() fragment and the
    // scene child's existence are gated - TsvrcLogger's/TsvrcMemory's own permanent .cs/.asset
    // are never touched, exactly like PoolModule never touches a registered prefab's own
    // script/asset. Exercised via LogModule for most cases, since it also covers
    // AdditionalUsageMethodNames (LogInfo/LogWarning/LogError); one MemoryModule test confirms
    // the base-only path works and that Log-specific wording doesn't leak into Memory's detection.
    public class TsSingleComponentModuleTreeShakingTests
    {
        private TempSceneScope _scope;
        private Component _root;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _root = CompiledRootFixture.AddTo(_scope);
            ScratchAssets.EnsureFolder();
            // Required now that LoadConfig() persists grace-period state via ModuleEntrySnapshot
            // (see TsModule.SaveGraceState) - without this redirect, that write would land in the
            // real project's Assets/TsGenerated/.cache/ folder instead of an isolated scratch one.
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/TsSingleComponentTreeShakingScratch";
        }

        [TearDown]
        public void TearDown()
        {
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_sourceTexts", null);
            _scope.Dispose(); // also resets TsPaths, including GeneratedFolder
            ScratchAssets.DeleteAll();
        }

        private static void SeedProjectSource(params string[] sourceFiles) =>
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_sourceTexts", new List<string>(sourceFiles));

        private TsConfig AddConfig(bool treeShake, params string[] forceIncludeNames)
        {
            var config = _scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();
            config.TreeShakeUnused = treeShake;
            config.ForceIncludeNames = forceIncludeNames;
            return config;
        }

        [Test]
        public void LoadConfig_TreeShakingOff_GenerateCodeStillEmitsFieldEvenWithNoReferencesAnywhere()
        {
            AddConfig(treeShake: false);
            SeedProjectSource("public class Unrelated { }");
            var module = new LogModule();

            module.LoadConfig();

            StringAssert.Contains("public override TsvrcLogger Log =>", module.GenerateCode());
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndReferencedViaDirectMemberAccess_KeepsField()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Foo : TsvrcBehaviour { void Bar() { _ts.Log.Info(\"x\"); } }");
            var module = new LogModule();

            module.LoadConfig();

            StringAssert.Contains("public override TsvrcLogger Log =>", module.GenerateCode());
            CollectionAssert.IsEmpty(module.LastTreeShakingGraceIncluded);
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndReferencedOnlyViaLogInfoWrapper_KeepsField()
        {
            // LogInfo/LogWarning/LogError never textually mention "_ts.Log" - this pins the
            // AdditionalUsageMethodNames override as the thing that catches the single most common
            // real-world way Log actually gets used.
            AddConfig(treeShake: true);
            SeedProjectSource("public class Foo : TsvrcBehaviour { void Bar() { LogInfo(\"hello\"); } }");
            var module = new LogModule();

            module.LoadConfig();

            StringAssert.Contains("public override TsvrcLogger Log =>", module.GenerateCode());
        }

        // First unreferenced observation is kept via the one-pass grace period
        // (TsModule.ConsumeGracePeriod), not excluded outright, so a developer who hasn't written
        // a LogInfo/_ts.Log call yet still sees the field to write it against.
        [Test]
        public void LoadConfig_TreeShakingOnAndNeverReferencedForTheFirstTime_KeepsFieldViaGracePeriod()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            var module = new LogModule();

            LogAssert.Expect(LogType.Log, new Regex(@"\[LogModule\] 1 entry isn't referenced"));
            module.LoadConfig();

            StringAssert.Contains("public override TsvrcLogger Log =>", module.GenerateCode(), "Must still be generated during its grace period.");
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
            CollectionAssert.AreEqual(new[] { "Log" }, module.LastTreeShakingGraceIncluded);
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndNeverReferencedForASecondConsecutivePass_ExcludesFieldAndLogsIt()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            // First pass consumes "Log"'s grace period.
            new LogModule().LoadConfig();

            var module = new LogModule();
            LogAssert.Expect(LogType.Log, new Regex(@"\[LogModule\] Excluded 'Log' - not referenced.*two regenerates"));
            module.LoadConfig();

            string generated = module.GenerateCode();
            StringAssert.DoesNotContain("[ReadOnly]", generated);
            StringAssert.DoesNotContain("public override TsvrcLogger Log", generated);
            StringAssert.Contains("_TsLogStart", generated, "The stub must still declare the Start hook Scaffold's Start() unconditionally calls.");
            CollectionAssert.AreEqual(new[] { "Log" }, module.LastTreeShakingExclusions);
            CollectionAssert.IsEmpty(module.LastTreeShakingGraceIncluded);
        }

        [Test]
        public void LoadConfig_TreeShakingOnAndForceIncluded_KeepsFieldEvenWithoutAnyReference()
        {
            AddConfig(treeShake: true, "Log");
            SeedProjectSource("public class Unrelated { }");
            var module = new LogModule();

            module.LoadConfig();

            StringAssert.Contains("public override TsvrcLogger Log =>", module.GenerateCode());
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
            CollectionAssert.IsEmpty(module.LastTreeShakingGraceIncluded, "Force-included entries skip grace-period bookkeeping entirely.");
        }

        [Test]
        public void AfterFilesStable_ExcludedForRealAndChildAlreadyExists_DestroysChildButNeverTouchesTheProgramAsset()
        {
            var existing = _scope.CreateGameObject("TsLogger");
            existing.transform.SetParent(_root.transform, false);
            existing.AddComponent<TsvrcLogger>();
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            // First pass consumes the grace period (the child survives this pass).
            new LogModule().LoadConfig();
            var module = new LogModule();
            module.LoadConfig();

            bool programAssetMissing = module.AfterFilesStable();

            Assert.IsNull(_root.transform.Find("TsLogger"), "The scene instance must be torn down once actually excluded.");
            Assert.IsFalse(programAssetMissing,
                "TsvrcLogger.asset is a permanent package resource - excluding the subsystem must never report it missing/needing recreation.");
        }

        [Test]
        public void AfterFilesStable_GraceIncludedAndChildAbsent_StillCreatesItNormally()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            var module = new LogModule();
            module.LoadConfig(); // first pass: grace period, _isUsed stays true

            module.AfterFilesStable();

            Assert.IsNotNull(_root.transform.Find("TsLogger"), "A grace-included (not yet excluded) entry must still be wired normally.");
        }

        [Test]
        public void AfterFilesStable_ExcludedForRealAndNoChildExists_StaysAbsentWithoutError()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            new LogModule().LoadConfig();
            var module = new LogModule();
            module.LoadConfig();

            Assert.DoesNotThrow(() => module.AfterFilesStable());
            Assert.IsNull(_root.transform.Find("TsLogger"));
        }

        [Test]
        public void Wire_ExcludedForReal_NeverLogsFieldNotFoundEvenThoughNoFieldExistsOnTheRealCompiledType()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            new LogModule().LoadConfig();
            var module = new LogModule();
            module.LoadConfig();

            // No LogAssert.Expect here - TryFindField's warning must never fire for a field this
            // pass deliberately, permanently omitted; any unexpected log fails the test on its own.
            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void OnSceneHierarchyChanged_ExcludedForRealAndChildStillPresent_ReturnsTrueToScheduleTeardown()
        {
            var existing = _scope.CreateGameObject("TsLogger");
            existing.transform.SetParent(_root.transform, false);
            existing.AddComponent<TsvrcLogger>();
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            new LogModule().LoadConfig();
            var module = new LogModule();
            module.LoadConfig();

            Assert.IsTrue(module.OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_ExcludedForRealAndChildAlreadyAbsent_ReturnsFalse()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            new LogModule().LoadConfig();
            var module = new LogModule();
            module.LoadConfig();

            Assert.IsFalse(module.OnSceneHierarchyChanged());
        }

        // MemoryModule has no AdditionalUsageMethodNames override - confirms the base-only path
        // works and that Log's own wrapper-method wording doesn't accidentally leak into Memory's
        // detection (a "LogInfo" mention in source must not count as a Memory reference).
        [Test]
        public void LoadConfig_MemoryModule_TreeShakingOnAndOnlyLogWrapperMentionedForASecondPass_StillExcludesMemory()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Foo : TsvrcBehaviour { void Bar() { LogInfo(\"x\"); } }");
            new MemoryModule().LoadConfig();

            var module = new MemoryModule();
            LogAssert.Expect(LogType.Log, new Regex(@"\[MemoryModule\] Excluded 'Memory' - not referenced"));
            module.LoadConfig();

            StringAssert.DoesNotContain("public override TsvrcMemory Memory", module.GenerateCode());
        }

        [Test]
        public void LoadConfig_MemoryModule_TreeShakingOnAndDirectlyReferenced_KeepsField()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Foo : TsvrcBehaviour { void Bar() { _ts.Memory.Set(\"k\", 1); } }");
            var module = new MemoryModule();

            module.LoadConfig();

            StringAssert.Contains("public override TsvrcMemory Memory =>", module.GenerateCode());
        }

        // Referencing something after a missed pass must reset its miss-streak, not just survive
        // that one pass - mirrors TsModuleHelpersTests' equivalent generic-helper coverage, pinned
        // here too since TsSingleComponentModule reimplements the grace calls directly rather than
        // going through ApplyTreeShaking.
        [Test]
        public void LoadConfig_ReferencedAfterAMissedPass_ResetsTheGracePeriodInsteadOfExcludingLater()
        {
            AddConfig(treeShake: true);
            SeedProjectSource("public class Unrelated { }");
            new LogModule().LoadConfig(); // first pass: unreferenced, consumes grace

            SeedProjectSource("public class Foo : TsvrcBehaviour { void Bar() { _ts.Log.Info(\"x\"); } }");
            new LogModule().LoadConfig(); // second pass: referenced, must clear the miss-streak

            SeedProjectSource("public class Unrelated { }");
            var module = new LogModule();
            module.LoadConfig(); // third pass: unreferenced again - must restart the grace period

            StringAssert.Contains("public override TsvrcLogger Log =>", module.GenerateCode());
            CollectionAssert.IsEmpty(module.LastTreeShakingExclusions);
            CollectionAssert.AreEqual(new[] { "Log" }, module.LastTreeShakingGraceIncluded);
        }
    }
}
