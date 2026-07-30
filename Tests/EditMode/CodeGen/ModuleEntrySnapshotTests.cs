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
    }
}
