using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // Drives the real, static TsGenerator.Run() - the one place every trigger converges - rather
    // than SingletonModule.LoadConfig() directly, so a pass caused only by a reactive trigger
    // unrelated to the entry being evaluated (countsForGracePeriod: false) is proven not to
    // consume or reset that entry's grace-period miss-streak. Going through Run() means RunCore's
    // own ScriptIndex.Rebuild() re-scans this real project's actual scripts every pass, which would
    // overwrite any seeded fake source text a test tried to inject - so instead these tests use a
    // Singleton name (via the __alias__ convention) that cannot possibly appear anywhere in this
    // repository's real source, and only assert it stays consistently unreferenced across passes.
    public class TsGeneratorGracePeriodAttributionTests
    {
        // Astronomically unlikely to textually appear anywhere in this repository's real source,
        // unlike a real component type name (e.g. "TsvrcMemory") which the actual project may
        // already reference somewhere, making a real ScriptIndex.Rebuild() scan see it as used.
        private const string EntryName = "ZzZGracePeriodAttributionProbe987";

        private TsGeneratorTestHarness _harness;
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _harness = new TsGeneratorTestHarness();
            _scope = _harness.Scope;
            TsPaths.ScriptCompilationFailedOverride = false;
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear("SingletonModule");
            _harness.Dispose(); // also resets TsPaths, including ScriptCompilationFailedOverride
        }

        private void AddUnreferencedSingleton()
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var target = _scope.CreateGameObject($"__{EntryName}__");
            config.Singletons = new Object[] { target };
            config.TreeShakeUnused = true;
        }

        [Test]
        public void Run_ReactiveTriggerPassBetweenTwoRealPasses_DoesNotConsumeOrExcludeAnUnrelatedEntrysGrace()
        {
            AddUnreferencedSingleton();

            // Pass 1: a real recompile (AfterDomainReload's own shape) - consumes the first miss.
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true, countsForGracePeriod: true);
            CollectionAssert.IsEmpty(TsGenerator.LastTreeShakingExclusions);
            CollectionAssert.Contains(TsGenerator.LastTreeShakingGraceIncluded, EntryName);

            // Pass 2: simulates a reactive trigger from an entirely unrelated module. Would be the
            // excluding second miss if this pass counted, but it must not exclude.
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true, countsForGracePeriod: false);
            CollectionAssert.IsEmpty(TsGenerator.LastTreeShakingExclusions,
                "A reactive pass unrelated to this entry must never consume its grace period.");
            CollectionAssert.Contains(TsGenerator.LastTreeShakingGraceIncluded, EntryName);

            // Pass 3: the next real pass must see this as the genuine second consecutive real
            // miss and exclude - proving pass 2 neither consumed nor reset the streak.
            LogAssert.Expect(LogType.Log, new Regex($@"\[SingletonModule\] Excluded '{EntryName}'"));
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true, countsForGracePeriod: true);
            CollectionAssert.Contains(TsGenerator.LastTreeShakingExclusions, EntryName);
        }

        [Test]
        public void Run_ScriptCompilationFailedDuringAReactivePass_AlsoDoesNotConsumeGrace()
        {
            AddUnreferencedSingleton();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true, countsForGracePeriod: true);
            CollectionAssert.IsEmpty(TsGenerator.LastTreeShakingExclusions);

            // A domain reload landing while some unrelated part of the project doesn't compile:
            // still "counts" per the trigger (AfterDomainReload's own shape), but must not decide
            // anything, mirroring ApplySnapshotFallback's own compile-health gate.
            TsPaths.ScriptCompilationFailedOverride = true;
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true, countsForGracePeriod: true);
            CollectionAssert.IsEmpty(TsGenerator.LastTreeShakingExclusions,
                "A broken compile anywhere in the project must never consume this entry's grace period.");

            TsPaths.ScriptCompilationFailedOverride = false;
            LogAssert.Expect(LogType.Log, new Regex($@"\[SingletonModule\] Excluded '{EntryName}'"));
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true, countsForGracePeriod: true);
            CollectionAssert.Contains(TsGenerator.LastTreeShakingExclusions, EntryName);
        }
    }
}
