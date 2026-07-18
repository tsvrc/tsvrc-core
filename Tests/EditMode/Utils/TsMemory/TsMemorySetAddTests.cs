using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Utils;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsMemorySetAddTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

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

        private static DataDictionary GetTypes(TsMemory memory) => PrivateFieldAccess.GetField<DataDictionary>(memory, "_types");

        // _types is only ever populated for persist-tier keys (the class's own doc comment:
        // "Populated for all persist-registered keys") - the three tests below verify that
        // directly rather than only by inference from _WriteToPd's persist-only call site.

        [Test]
        public void Set_EphemeralKey_DoesNotPopulateTypesMap()
        {
            TsMemory memory = CreateMemory();

            memory.Set("k", new DataToken("v"));

            Assert.IsFalse(GetTypes(memory).ContainsKey("k"));
        }

        [Test]
        public void Set_SyncedKey_DoesNotPopulateTypesMap()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", false, true);

            memory.Set("k", new DataToken("v"));

            Assert.IsFalse(GetTypes(memory).ContainsKey("k"));
        }

        [Test]
        public void Set_PersistKey_PopulatesTypesMapWithMatchingTypeCode()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", true, false);

            LogAssert.Expect(LogType.Warning, "[TsMemory] Set('k') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("k", new DataToken("v"));

            const int TYPE_STRING = 1;
            Assert.AreEqual((double)TYPE_STRING, GetTypes(memory)["k"].Double);
        }

        [Test]
        public void Set_EphemeralKey_StoresValue()
        {
            TsMemory memory = CreateMemory();

            memory.Set("k", new DataToken("v"));

            Assert.IsTrue(memory.Has("k"));
            Assert.AreEqual("v", memory.GetString("k"));
        }

        [Test]
        public void Set_EphemeralKey_CalledTwice_OverwritesPreviousValue()
        {
            TsMemory memory = CreateMemory();
            memory.Set("k", new DataToken("first"));

            memory.Set("k", new DataToken("second"));

            Assert.AreEqual("second", memory.GetString("k"));
        }

        [Test]
        public void Set_EphemeralKey_DoesNotLogAnything()
        {
            TsMemory memory = CreateMemory();

            Assert.DoesNotThrow(() => memory.Set("k", new DataToken("v")));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Set_NullKey_ThrowsArgumentNullException()
        {
            // The underlying DataDictionary indexer rejects a null key outright; TsMemory
            // does not guard against it, matching every other caller-contract violation
            // this class leaves undefended.
            TsMemory memory = CreateMemory();

            Assert.Throws<System.ArgumentNullException>(() => memory.Set(null, new DataToken("v")));
        }

        [Test]
        public void Set_PersistKeyWithUnsupportedTokenType_ThrowsWritingToPlayerData()
        {
            // _TypeCode falls through to TYPE_DOUBLE for any token type outside the class's
            // documented supported set (String/Bool/Float/Int/Double/Dict/List). A default
            // (Null-typed) DataToken then makes _WriteToPd call PlayerData.SetDouble(key,
            // value.Double), and .Double throws for a Null-typed token.
            TsMemory memory = CreateMemory();
            memory.Register("k", true, false);
            PrivateFieldAccess.SetField(memory, "_playerRestored", true);

            Assert.Throws<System.InvalidOperationException>(() => memory.Set("k", default));
        }

        [Test]
        public void Set_PersistKeyBeforeRestore_LogsWarningAndStillUpdatesLocalCache()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", true, false);

            LogAssert.Expect(LogType.Warning, "[TsMemory] Set('k') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("k", new DataToken("v"));

            Assert.IsTrue(memory.Has("k"));
            Assert.AreEqual("v", memory.GetString("k"));
        }

        [Test]
        public void Set_PersistKey_DoesNotThrow()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", true, false);

            Assert.DoesNotThrow(() => memory.Set("k", new DataToken("v")));
        }

        [Test]
        public void Set_SyncedKey_StoresValueAndDoesNotThrow()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", false, true);

            Assert.DoesNotThrow(() => memory.Set("k", new DataToken("v")));
            Assert.IsTrue(memory.Has("k"));
            Assert.AreEqual("v", memory.GetString("k"));
        }

        [Test]
        public void Set_SyncedKey_UpdatesSerializedJsonField()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", false, true);

            memory.Set("k", new DataToken("v"));

            string json = PrivateFieldAccess.GetField<string>(memory, "_syncedJson");
            StringAssert.Contains("\"k\"", json);
            StringAssert.Contains("\"v\"", json);
        }

        [Test]
        public void Set_SyncedKey_DoesNotAffectEphemeralOrPersistStores()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", false, true);

            memory.Set("k", new DataToken("v"));

            var store = PrivateFieldAccess.GetField<DataDictionary>(memory, "_store");
            var persistStore = PrivateFieldAccess.GetField<DataDictionary>(memory, "_persistStore");
            Assert.IsFalse(store.ContainsKey("k"));
            Assert.IsFalse(persistStore.ContainsKey("k"));
        }

        [Test]
        public void Add_EphemeralKey_NoPriorValue_Stores()
        {
            TsMemory memory = CreateMemory();

            memory.Add("k", new DataToken("v"));

            Assert.IsTrue(memory.Has("k"));
            Assert.AreEqual("v", memory.GetString("k"));
        }

        [Test]
        public void Add_EphemeralKeyAlreadyExists_LogsErrorAndDoesNotOverwrite()
        {
            TsMemory memory = CreateMemory();
            memory.Add("k", new DataToken("first"));

            LogAssert.Expect(LogType.Error, "[TsMemory] Key 'k' already exists. Use Set to overwrite.");
            memory.Add("k", new DataToken("second"));

            Assert.AreEqual("first", memory.GetString("k"));
        }

        [Test]
        public void Add_PersistKeyAlreadyExists_LogsErrorAndDoesNotOverwrite()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", true, false);
            LogAssert.Expect(LogType.Warning, "[TsMemory] Set('k') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("k", new DataToken("first"));

            LogAssert.Expect(LogType.Error, "[TsMemory] Key 'k' already exists. Use Set to overwrite.");
            memory.Add("k", new DataToken("second"));

            Assert.AreEqual("first", memory.GetString("k"));
        }

        [Test]
        public void Add_PersistKey_DoesNotLogTheBeforeRestoreWarning()
        {
            // Add never calls _WriteToPd (only Set does), so it must not emit the
            // "before OnPlayerRestored" warning even though _playerRestored is false.
            TsMemory memory = CreateMemory();
            memory.Register("k", true, false);

            Assert.DoesNotThrow(() => memory.Add("k", new DataToken("v")));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Add_SyncedKeyAlreadyExists_SilentlySkipsWithoutError()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", false, true);
            memory.Set("k", new DataToken("first"));

            Assert.DoesNotThrow(() => memory.Add("k", new DataToken("second")));
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual("first", memory.GetString("k"), "Add on an already-populated synced key must not overwrite.");
        }

        [Test]
        public void Add_SyncedKeyNoPriorValue_Stores()
        {
            TsMemory memory = CreateMemory();
            memory.Register("k", false, true);

            memory.Add("k", new DataToken("v"));

            Assert.AreEqual("v", memory.GetString("k"));
        }

        [Test]
        public void Add_UnregisteredKey_TreatedAsEphemeral()
        {
            TsMemory memory = CreateMemory();

            memory.Add("k", new DataToken("v"));

            var store = PrivateFieldAccess.GetField<DataDictionary>(memory, "_store");
            Assert.IsTrue(store.ContainsKey("k"));
        }
    }
}
