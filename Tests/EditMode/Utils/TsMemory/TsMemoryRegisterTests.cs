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

        private TsMemory CreateMemory()
        {
            var go = new GameObject(nameof(TsMemory));
            _spawned.Add(go);
            return go.AddComponent<TsMemory>();
        }

        private static DataDictionary GetRegistry(TsMemory memory) => PrivateFieldAccess.GetField<DataDictionary>(memory, "_registry");

        [Test]
        public void Register_PersistOnly_RecordsPersistFlag()
        {
            TsMemory memory = CreateMemory();

            memory.Register("k", true, false);

            Assert.AreEqual(1d, GetRegistry(memory)["k"].Double);
        }

        [Test]
        public void Register_SyncedOnly_RecordsSyncedFlag()
        {
            TsMemory memory = CreateMemory();

            memory.Register("k", false, true);

            Assert.AreEqual(2d, GetRegistry(memory)["k"].Double);
        }

        [Test]
        public void Register_AlreadyRegistered_LogsErrorAndKeepsOriginalTier()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", true, false);

            LogAssert.Expect(LogType.Error, "[TsVRC] [TsMemory] Key 'k' is already registered.");
            memory.Register("k", false, true);

            Assert.AreEqual(1d, GetRegistry(memory)["k"].Double, "Second Register call must not overwrite the original tier.");
        }

        [Test]
        public void Register_PersistAndSyncedBothTrue_LogsErrorAndDoesNotRegister()
        {
            TsMemory memory = CreateMemory();

            LogAssert.Expect(LogType.Error, "[TsVRC] [TsMemory] Key 'k': persist and synced are mutually exclusive.");
            memory.Register("k", true, true);

            Assert.IsFalse(GetRegistry(memory).ContainsKey("k"));
        }

        [Test]
        public void Register_NeitherPersistNorSynced_LogsErrorAndDoesNotRegister()
        {
            TsMemory memory = CreateMemory();

            LogAssert.Expect(LogType.Error, "[TsVRC] [TsMemory] Key 'k': Register called with no tier. Unregistered keys are already ephemeral.");
            memory.Register("k", false, false);

            Assert.IsFalse(GetRegistry(memory).ContainsKey("k"));
        }

        [Test]
        public void Register_TwoDifferentKeys_BothRecordedIndependently()
        {
            TsMemory memory = CreateMemory();

            memory.Register("a", true, false);
            memory.Register("b", false, true);

            Assert.AreEqual(1d, GetRegistry(memory)["a"].Double);
            Assert.AreEqual(2d, GetRegistry(memory)["b"].Double);
        }

        [Test]
        public void Register_UnregisteredKey_SetRoutesToEphemeralStore()
        {
            TsMemory memory = CreateMemory();

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
            TsMemory memory = CreateMemory();
            memory.Set("k", new DataToken("ephemeral-value"));

            memory.Register("k", true, false);

            Assert.IsFalse(memory.Has("k"), "The persist store has no 'k' entry; the ephemeral value is orphaned, not migrated.");
        }
    }
}
