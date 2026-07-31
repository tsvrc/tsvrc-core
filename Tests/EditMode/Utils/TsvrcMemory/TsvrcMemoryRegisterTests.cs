using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Utils;
using UnityEngine.TestTools;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsMemoryRegisterTests
    {
        private readonly System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private TsvrcMemory CreateMemory()
        {
            var go = new GameObject(nameof(TsvrcMemory));
            _spawned.Add(go);
            return go.AddComponent<TsvrcMemory>();
        }

        private static DataDictionary GetRegistry(TsvrcMemory memory) => PrivateFieldAccess.GetField<DataDictionary>(memory, "_registry");

        [Test]
        public void Register_PersistOnly_RecordsPersistFlag()
        {
            TsvrcMemory memory = CreateMemory();

            memory.Register("k", true, false);

            Assert.AreEqual(1d, GetRegistry(memory)["k"].Double);
        }

        [Test]
        public void Register_SyncedOnly_RecordsSyncedFlag()
        {
            TsvrcMemory memory = CreateMemory();

            memory.Register("k", false, true);

            Assert.AreEqual(2d, GetRegistry(memory)["k"].Double);
        }

        [Test]
        public void Register_AlreadyRegistered_LogsErrorAndKeepsOriginalTier()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", true, false);

            LogAssert.Expect(LogType.Error, "[TsVRC] [TsvrcMemory] Key 'k' is already registered.");
            memory.Register("k", false, true);

            Assert.AreEqual(1d, GetRegistry(memory)["k"].Double, "Second Register call must not overwrite the original tier.");
        }

        [Test]
        public void Register_PersistAndSyncedBothTrue_LogsErrorAndDoesNotRegister()
        {
            TsvrcMemory memory = CreateMemory();

            LogAssert.Expect(LogType.Error, "[TsVRC] [TsvrcMemory] Key 'k': persist and synced are mutually exclusive.");
            memory.Register("k", true, true);

            Assert.IsFalse(GetRegistry(memory).ContainsKey("k"));
        }

        [Test]
        public void Register_NeitherPersistNorSynced_LogsErrorAndDoesNotRegister()
        {
            TsvrcMemory memory = CreateMemory();

            LogAssert.Expect(LogType.Error, "[TsVRC] [TsvrcMemory] Key 'k': Register called with no tier. Unregistered keys are already ephemeral.");
            memory.Register("k", false, false);

            Assert.IsFalse(GetRegistry(memory).ContainsKey("k"));
        }

        [Test]
        public void Register_TwoDifferentKeys_BothRecordedIndependently()
        {
            TsvrcMemory memory = CreateMemory();

            memory.Register("a", true, false);
            memory.Register("b", false, true);

            Assert.AreEqual(1d, GetRegistry(memory)["a"].Double);
            Assert.AreEqual(2d, GetRegistry(memory)["b"].Double);
        }

        [Test]
        public void Register_UnregisteredKey_SetRoutesToEphemeralStore()
        {
            TsvrcMemory memory = CreateMemory();

            memory.Set("k", new DataToken("v"));

            var store = PrivateFieldAccess.GetField<DataDictionary>(memory, "_store");
            Assert.IsTrue(store.ContainsKey("k"));
        }

        [Test]
        public void Register_AfterEphemeralDataAlreadySet_OrphansOldValue()
        {
            // Documents current behavior: Register only guards against double-registration,
            // not against a key that already has ephemeral data from before Register was
            // called. The class's own doc comment requires Register before Add/Set.
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken("ephemeral-value"));

            memory.Register("k", true, false);

            Assert.IsFalse(memory.Has("k"), "The persist store has no 'k' entry; the ephemeral value is orphaned, not migrated.");
        }
    }
}
