using NUnit.Framework;
using System.Collections.Generic;
using Tsvrc.UdonTestKit;
using Tsvrc.Utils;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsMemorySyncTests
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

        private static void SetSyncedJson(TsvrcMemory memory, string json) =>
            PrivateFieldAccess.SetField(memory, "_syncedJson", json);

        private static DataDictionary GetSyncedStore(TsvrcMemory memory) =>
            PrivateFieldAccess.GetField<DataDictionary>(memory, "_syncedStore");

        [Test]
        public void OnDeserialization_ValidDictJson_UpdatesSyncedStore()
        {
            TsvrcMemory memory = CreateMemory();
            SetSyncedJson(memory, "{\"k\":\"v\"}");

            memory.OnDeserialization();

            Assert.AreEqual("v", GetSyncedStore(memory)["k"].String);
        }

        [Test]
        public void OnDeserialization_ValidDictJson_EmitsSyncedChangedEvent()
        {
            using var calls = new CallLog();
            TsvrcMemory memory = CreateMemory();
            memory.TsSubscribe(calls.CreateSpy("Listener"), TsvrcMemory.OnSyncedChangedEvent, nameof(CallbackSpy.CallbackA));
            SetSyncedJson(memory, "{\"k\":\"v\"}");

            memory.OnDeserialization();

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
        }

        [Test]
        public void OnDeserialization_EmptyDictJson_UpdatesToEmptyStoreAndEmits()
        {
            using var calls = new CallLog();
            TsvrcMemory memory = CreateMemory();
            memory.TsSubscribe(calls.CreateSpy("Listener"), TsvrcMemory.OnSyncedChangedEvent, nameof(CallbackSpy.CallbackA));
            SetSyncedJson(memory, "{}");

            memory.OnDeserialization();

            Assert.AreEqual(0, GetSyncedStore(memory).Count);
            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
        }

        [Test]
        public void OnDeserialization_MalformedJson_ReturnsEarlyWithoutEmitting()
        {
            using var calls = new CallLog();
            TsvrcMemory memory = CreateMemory();
            memory.TsSubscribe(calls.CreateSpy("Listener"), TsvrcMemory.OnSyncedChangedEvent, nameof(CallbackSpy.CallbackA));
            DataDictionary storeBefore = GetSyncedStore(memory);
            SetSyncedJson(memory, "{not valid json");

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[TsVRC] [TsJson] Failed to deserialize JSON string.");
            Assert.DoesNotThrow(() => memory.OnDeserialization());

            CollectionAssert.IsEmpty(calls);
            Assert.AreSame(storeBefore, GetSyncedStore(memory), "Malformed JSON must leave the existing synced store untouched.");
        }

        [Test]
        public void OnDeserialization_ArrayJson_ReturnsEarlyWithoutEmitting()
        {
            using var calls = new CallLog();
            TsvrcMemory memory = CreateMemory();
            memory.TsSubscribe(calls.CreateSpy("Listener"), TsvrcMemory.OnSyncedChangedEvent, nameof(CallbackSpy.CallbackA));
            SetSyncedJson(memory, "[1,2,3]");

            Assert.DoesNotThrow(() => memory.OnDeserialization());

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void OnDeserialization_PrimitiveStringJson_ReturnsEarlyWithoutEmitting()
        {
            using var calls = new CallLog();
            TsvrcMemory memory = CreateMemory();
            memory.TsSubscribe(calls.CreateSpy("Listener"), TsvrcMemory.OnSyncedChangedEvent, nameof(CallbackSpy.CallbackA));
            SetSyncedJson(memory, "\"just a string\"");

            // VRCJson's underlying parser rejects a bare JSON scalar at the top level (only
            // object/array root values deserialize successfully), so this fails the same way
            // malformed JSON does.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[TsVRC] [TsJson] Failed to deserialize JSON string.");
            Assert.DoesNotThrow(() => memory.OnDeserialization());

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void OnDeserialization_EmptyStringJson_ReturnsEarlyWithoutEmitting()
        {
            using var calls = new CallLog();
            TsvrcMemory memory = CreateMemory();
            memory.TsSubscribe(calls.CreateSpy("Listener"), TsvrcMemory.OnSyncedChangedEvent, nameof(CallbackSpy.CallbackA));
            SetSyncedJson(memory, "");

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[TsVRC] [TsJson] Cannot deserialize null or empty JSON string.");
            Assert.DoesNotThrow(() => memory.OnDeserialization());

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void OnDeserialization_DefaultSyncedJsonValue_ParsesToEmptyDictAndEmits()
        {
            using var calls = new CallLog();
            // "{}" is the field's own default initializer - a fresh TsvrcMemory (or one whose
            // OnDeserialization fires before any real network write ever arrives) must
            // handle it identically to any other valid empty dict.
            TsvrcMemory memory = CreateMemory();
            memory.TsSubscribe(calls.CreateSpy("Listener"), TsvrcMemory.OnSyncedChangedEvent, nameof(CallbackSpy.CallbackA));

            memory.OnDeserialization();

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
        }

        [Test]
        public void SetOnSyncedKey_ThenOnDeserializationOfResultingJson_RoundTrips()
        {
            TsvrcMemory writer = CreateMemory();
            writer.Register("k", false, true);
            writer.Set("k", new DataToken("v"));
            string json = PrivateFieldAccess.GetField<string>(writer, "_syncedJson");

            TsvrcMemory reader = CreateMemory();
            SetSyncedJson(reader, json);
            reader.OnDeserialization();

            Assert.AreEqual("v", GetSyncedStore(reader)["k"].String);
        }
    }
}
