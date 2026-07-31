using NUnit.Framework;
using System.Collections.Generic;
using Tsvrc.Testing.Framework;
using Tsvrc.Utils;
using UnityEngine.TestTools;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsMemoryRemoveClearTests
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

        private TsvrcMemory CreateMemory()
        {
            var go = new GameObject(nameof(TsvrcMemory));
            _spawned.Add(go);
            return go.AddComponent<TsvrcMemory>();
        }

        [Test]
        public void Remove_EphemeralKeyPresent_RemovesValue()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken("v"));

            memory.Remove("k");

            Assert.IsFalse(memory.Has("k"));
        }

        [Test]
        public void Remove_EphemeralKeyAbsent_DoesNotThrowOrLog()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.DoesNotThrow(() => memory.Remove("k"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Remove_EphemeralKey_CanAddAgainWithoutError()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken("v"));
            memory.Remove("k");

            Assert.DoesNotThrow(() => memory.Add("k", new DataToken("v2")));
            Assert.AreEqual("v2", memory.GetString("k"));
        }

        [Test]
        public void Remove_PersistKeyPresent_LogsWarningAndClearsLocalCache()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", true, false);
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] Set('k') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("k", new DataToken("v"));

            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] 'k' is persistent. PlayerData cannot be deleted; local cache cleared.");
            memory.Remove("k");

            Assert.IsFalse(memory.Has("k"));
        }

        [Test]
        public void Remove_PersistKeyNeverSet_DoesNotLogWarning()
        {
            // Remove must not claim a cache was cleared when the key was only registered,
            // never actually set.
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", true, false);

            Assert.DoesNotThrow(() => memory.Remove("k"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Remove_PersistKey_PreservesRegistrationForFutureAdd()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", true, false);
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] Set('k') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("k", new DataToken("v"));
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] 'k' is persistent. PlayerData cannot be deleted; local cache cleared.");
            memory.Remove("k");

            Assert.DoesNotThrow(() => memory.Add("k", new DataToken("v2")));
            Assert.AreEqual("v2", memory.GetString("k"));
        }

        [Test]
        public void Remove_SyncedKeyPresent_RemovesAndDoesNotThrow()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", false, true);
            memory.Set("k", new DataToken("v"));

            Assert.DoesNotThrow(() => memory.Remove("k"));
            Assert.IsFalse(memory.Has("k"));
        }

        [Test]
        public void Remove_SyncedKey_CanAddAgainWithoutError()
        {
            // Symmetry check with the ephemeral/persist equivalents above: Remove preserves
            // registration for every tier, not just ephemeral/persist.
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", false, true);
            memory.Set("k", new DataToken("v"));
            memory.Remove("k");

            Assert.DoesNotThrow(() => memory.Add("k", new DataToken("v2")));
            Assert.AreEqual("v2", memory.GetString("k"));
        }

        [Test]
        public void Remove_SyncedKeyAbsent_NoOpWithoutThrowingOrLogging()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", false, true);

            Assert.DoesNotThrow(() => memory.Remove("k"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Remove_SyncedKeyPresent_UpdatesSerializedJsonField()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", false, true);
            memory.Set("k", new DataToken("v"));

            memory.Remove("k");

            string json = PrivateFieldAccess.GetField<string>(memory, "_syncedJson");
            StringAssert.DoesNotContain("\"k\"", json);
        }

        [Test]
        public void Clear_RemovesValuesFromAllThreeTiers()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("ephemeralKey", new DataToken("e"));
            memory.Register("persistKey", true, false);
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] Set('persistKey') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("persistKey", new DataToken("p"));
            memory.Register("syncedKey", false, true);
            memory.Set("syncedKey", new DataToken("s"));

            memory.Clear();

            Assert.IsFalse(memory.Has("ephemeralKey"));
            Assert.IsFalse(memory.Has("persistKey"));
            Assert.IsFalse(memory.Has("syncedKey"));
        }

        [Test]
        public void Clear_PreservesRegistrations()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("persistKey", true, false);
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] Set('persistKey') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("persistKey", new DataToken("p"));

            memory.Clear();

            // If the registration survived, re-adding without Register succeeds and the
            // value routes to the persist tier again (proven by the before-restore warning
            // firing on Set, which only fires for persist-tier keys).
            LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] Set('persistKey') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("persistKey", new DataToken("p2"));
            Assert.AreEqual("p2", memory.GetString("persistKey"));
        }

        [Test]
        public void Clear_SyncedStoreAlreadyEmpty_DoesNotChangeSerializedJson()
        {
            TsvrcMemory memory = CreateMemory();
            string jsonBefore = PrivateFieldAccess.GetField<string>(memory, "_syncedJson");

            memory.Clear();

            string jsonAfter = PrivateFieldAccess.GetField<string>(memory, "_syncedJson");
            Assert.AreEqual(jsonBefore, jsonAfter);
        }

        [Test]
        public void Clear_SyncedStoreNonEmpty_ClearsAndUpdatesSerializedJson()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", false, true);
            memory.Set("k", new DataToken("v"));

            memory.Clear();

            string json = PrivateFieldAccess.GetField<string>(memory, "_syncedJson");
            StringAssert.DoesNotContain("\"k\"", json);
        }

        [Test]
        public void Clear_EmptyMemory_DoesNotThrow()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.DoesNotThrow(() => memory.Clear());
        }
    }
}
