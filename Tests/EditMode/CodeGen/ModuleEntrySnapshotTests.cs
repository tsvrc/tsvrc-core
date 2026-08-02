using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // ModuleEntrySnapshot is the last known good cache SingletonModule and FactoryModule fall
    // back to when EditorUtility.scriptCompilationFailed is true and the current live
    // resolution came back empty or reduced.
    public class ModuleEntrySnapshotTests
    {
        private const string Key = "TestModuleEntrySnapshot";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            // TempSceneScope isn't strictly needed for scene state here, but its Dispose()
            // resets TsPaths.GeneratedFolder. Redirecting it to scratch keeps this test from
            // ever writing under a real project's Assets/TsGenerated/.cache.
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/SnapshotScratch";
        }

        [TearDown]
        public void TearDown()
        {
            ModuleEntrySnapshot.Clear(Key);
            _scope.Dispose();
            ScratchAssets.DeleteAll();
        }

        private static List<ModuleEntrySnapshot.Entry> Sample() => new List<ModuleEntrySnapshot.Entry>
        {
            new ModuleEntrySnapshot.Entry { Name = "SettingsManager", TypeName = "SettingsManager", Namespace = "MoL.Core" },
            new ModuleEntrySnapshot.Entry { Name = "GameManager", TypeName = "GameManager", Namespace = "MoL.Game" },
        };

        [Test]
        public void Load_NeverSaved_ReturnsNull()
        {
            Assert.IsNull(ModuleEntrySnapshot.Load(Key));
        }

        [Test]
        public void Save_ThenLoad_RoundTripsAllFields()
        {
            ModuleEntrySnapshot.Save(Key, Sample());

            var loaded = ModuleEntrySnapshot.Load(Key);

            Assert.AreEqual(2, loaded.Count);
            Assert.AreEqual("SettingsManager", loaded[0].Name);
            Assert.AreEqual("SettingsManager", loaded[0].TypeName);
            Assert.AreEqual("MoL.Core", loaded[0].Namespace);
            Assert.AreEqual("GameManager", loaded[1].Name);
        }

        [Test]
        public void Save_EmptyList_RoundTripsAsEmpty_NotNull()
        {
            ModuleEntrySnapshot.Save(Key, new List<ModuleEntrySnapshot.Entry>());

            var loaded = ModuleEntrySnapshot.Load(Key);

            Assert.IsNotNull(loaded);
            Assert.IsEmpty(loaded);
        }

        [Test]
        public void Save_CalledTwice_SecondSaveOverwritesTheFirst()
        {
            ModuleEntrySnapshot.Save(Key, Sample());
            ModuleEntrySnapshot.Save(Key, new List<ModuleEntrySnapshot.Entry>
            {
                new ModuleEntrySnapshot.Entry { Name = "Solo", TypeName = "Solo", Namespace = "" },
            });

            var loaded = ModuleEntrySnapshot.Load(Key);

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual("Solo", loaded[0].Name);
        }

        [Test]
        public void Clear_RemovesTheSnapshot_SubsequentLoadReturnsNull()
        {
            ModuleEntrySnapshot.Save(Key, Sample());

            ModuleEntrySnapshot.Clear(Key);

            Assert.IsNull(ModuleEntrySnapshot.Load(Key));
        }

        [Test]
        public void Clear_NeverSaved_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ModuleEntrySnapshot.Clear(Key));
        }

        [Test]
        public void DifferentModuleKeys_AreIndependent()
        {
            ModuleEntrySnapshot.Save(Key, Sample());
            ModuleEntrySnapshot.Save(Key + "Other", new List<ModuleEntrySnapshot.Entry>());

            Assert.AreEqual(2, ModuleEntrySnapshot.Load(Key).Count);
            Assert.IsEmpty(ModuleEntrySnapshot.Load(Key + "Other"));

            ModuleEntrySnapshot.Clear(Key + "Other");
        }

        [Test]
        public void Save_WriteFails_LogsWarningAndDoesNotThrow()
        {
            // Forces Directory.CreateDirectory to fail by making an intermediate path segment a
            // file instead of a directory, proving a real I/O failure, such as a read-only
            // folder or a full disk, degrades to "not saved this pass" instead of throwing out
            // of LoadConfig().
            string blockerPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath),
                (ScratchAssets.Folder + "/NotADirectory").Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(blockerPath));
            System.IO.File.WriteAllText(blockerPath, "blocker");
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/NotADirectory/Sub";

            Assert.DoesNotThrow(() => ModuleEntrySnapshot.Save(Key, Sample()));
            Assert.IsNull(ModuleEntrySnapshot.Load(Key), "A failed save must not leave a partial/corrupt snapshot behind.");
        }

        // SaveCount/LoadCount: the dedicated count-only store TsModule's "last known good"
        // high-water mark (WarnIfBelowLastKnownGood) uses, kept separate from Save/Load(List of
        // Entry) above since it only ever needs one integer, not a full entry list.
        private const string CountKey = "TestModuleEntrySnapshotCount";

        [TearDown]
        public void TearDownCount() => ModuleEntrySnapshot.Clear(CountKey);

        [Test]
        public void LoadCount_NeverSaved_ReturnsNull()
        {
            Assert.IsNull(ModuleEntrySnapshot.LoadCount(CountKey));
        }

        [Test]
        public void SaveCount_ThenLoadCount_RoundTrips()
        {
            ModuleEntrySnapshot.SaveCount(CountKey, 5);

            Assert.AreEqual(5, ModuleEntrySnapshot.LoadCount(CountKey));
        }

        [Test]
        public void SaveCount_Zero_RoundTripsAsZero_NotNull()
        {
            ModuleEntrySnapshot.SaveCount(CountKey, 0);

            Assert.AreEqual(0, ModuleEntrySnapshot.LoadCount(CountKey));
        }

        [Test]
        public void SaveCount_CalledTwice_SecondSaveOverwritesTheFirst()
        {
            ModuleEntrySnapshot.SaveCount(CountKey, 5);
            ModuleEntrySnapshot.SaveCount(CountKey, 2);

            Assert.AreEqual(2, ModuleEntrySnapshot.LoadCount(CountKey));
        }

        [Test]
        public void SaveCount_AndSave_UseIndependentKeysEvenWhenSharingAModuleKey()
        {
            // Both persist under the same {moduleKey}.json path convention, so this pins that a
            // count-only key and a full-entry-list key never collide as long as callers (as
            // TsModule.LastKnownGoodSnapshotKey does) suffix the count key distinctly.
            ModuleEntrySnapshot.Save(Key, Sample());
            ModuleEntrySnapshot.SaveCount(CountKey, 7);

            Assert.AreEqual(2, ModuleEntrySnapshot.Load(Key).Count);
            Assert.AreEqual(7, ModuleEntrySnapshot.LoadCount(CountKey));
        }

        [Test]
        public void Load_CorruptedFile_DoesNotThrow()
        {
            // The important guarantee is safety, not a specific return value: JsonUtility's own
            // tolerance for malformed input isn't part of this class's contract, so this only
            // pins "never throws", not "must return null" (Unity's parser may recover a
            // default-constructed, empty result instead of throwing for some malformed inputs).
            ModuleEntrySnapshot.Save(Key, Sample()); // ensures the .cache directory exists
            string path = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath),
                (TsPaths.GeneratedFolder + "/.cache/" + Key + ".json").Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.File.WriteAllText(path, "{ not valid json ]]]");

            List<ModuleEntrySnapshot.Entry> result = null;
            Assert.DoesNotThrow(() => result = ModuleEntrySnapshot.Load(Key));
            if (result != null) Assert.IsEmpty(result, "If parsing recovers rather than failing outright, it must not fabricate entries.");
        }

        // SaveNames/LoadNames: the dedicated name-set store TsModule's tree-shaking grace period
        // (ConsumeGracePeriod) uses to persist which names were unreferenced last pass, kept
        // separate from Save/Load(List of Entry) since it only needs bare names, not full entries.
        private const string NamesKey = "TestModuleEntrySnapshotNames";

        [TearDown]
        public void TearDownNames() => ModuleEntrySnapshot.Clear(NamesKey);

        [Test]
        public void LoadNames_NeverSaved_ReturnsEmptySetNotNull()
        {
            var loaded = ModuleEntrySnapshot.LoadNames(NamesKey);

            Assert.IsNotNull(loaded, "Callers use this directly as a Contains() lookup - it must never require a null check.");
            Assert.IsEmpty(loaded);
        }

        [Test]
        public void SaveNames_ThenLoadNames_RoundTrips()
        {
            ModuleEntrySnapshot.SaveNames(NamesKey, new[] { "A", "B" });

            var loaded = ModuleEntrySnapshot.LoadNames(NamesKey);

            CollectionAssert.AreEquivalent(new[] { "A", "B" }, loaded);
        }

        [Test]
        public void SaveNames_EmptySet_RoundTripsAsEmptyNotNull()
        {
            ModuleEntrySnapshot.SaveNames(NamesKey, new string[0]);

            var loaded = ModuleEntrySnapshot.LoadNames(NamesKey);

            Assert.IsNotNull(loaded);
            Assert.IsEmpty(loaded);
        }

        [Test]
        public void SaveNames_CalledTwice_SecondSaveOverwritesTheFirst()
        {
            ModuleEntrySnapshot.SaveNames(NamesKey, new[] { "A", "B" });
            ModuleEntrySnapshot.SaveNames(NamesKey, new[] { "Solo" });

            var loaded = ModuleEntrySnapshot.LoadNames(NamesKey);

            CollectionAssert.AreEquivalent(new[] { "Solo" }, loaded);
        }

        [Test]
        public void SaveNames_AndSave_UseIndependentKeysEvenWhenSharingAModuleKey()
        {
            // Both persist under the same {moduleKey}.json path convention as Save/SaveCount, so
            // this pins that a names-only key never collides with a full-entry-list key as long as
            // callers (as TsModule's TreeShakeGraceKey does) suffix the names key distinctly.
            ModuleEntrySnapshot.Save(Key, Sample());
            ModuleEntrySnapshot.SaveNames(NamesKey, new[] { "A" });

            Assert.AreEqual(2, ModuleEntrySnapshot.Load(Key).Count);
            CollectionAssert.AreEquivalent(new[] { "A" }, ModuleEntrySnapshot.LoadNames(NamesKey));
        }

        [Test]
        public void LoadNames_CorruptedFile_ReturnsEmptySetWithoutThrowing()
        {
            ModuleEntrySnapshot.SaveNames(NamesKey, new[] { "A" }); // ensures the .cache directory exists
            string path = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath),
                (TsPaths.GeneratedFolder + "/.cache/" + NamesKey + ".json").Replace('/', System.IO.Path.DirectorySeparatorChar));
            System.IO.File.WriteAllText(path, "{ not valid json ]]]");

            HashSet<string> result = null;
            Assert.DoesNotThrow(() => result = ModuleEntrySnapshot.LoadNames(NamesKey));
            Assert.IsNotNull(result);
            Assert.IsEmpty(result);
        }
    }
}
