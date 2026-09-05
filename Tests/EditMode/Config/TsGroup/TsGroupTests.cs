using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsGroup and TsGroupedEntry are pure [Serializable] data classes, zero methods, the shared
    // tree/entry shape behind both Globals and Factories. Pins the [Serializable] attribute
    // itself (losing it silently breaks the Inspector with no compile error), default field
    // values, and that both round-trip as nested array elements through Unity's serializer the
    // way TsConfig.GlobalGroups/GlobalEntries/FactoryGroups/FactoryEntries actually use them.
    public class TsGroupTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void TsGroup_HasSerializableAttribute()
        {
            Assert.IsTrue(Attribute.IsDefined(typeof(TsGroup), typeof(SerializableAttribute)),
                "TsGroup must stay [Serializable] - losing it silently breaks the Inspector for " +
                "TsConfig.GlobalGroups/FactoryGroups with no compile error.");
        }

        [Test]
        public void TsGroupedEntry_HasSerializableAttribute()
        {
            Assert.IsTrue(Attribute.IsDefined(typeof(TsGroupedEntry), typeof(SerializableAttribute)),
                "TsGroupedEntry must stay [Serializable] - losing it silently breaks the Inspector " +
                "for TsConfig.GlobalEntries/FactoryEntries with no compile error.");
        }

        [Test]
        public void TsGroup_FreshInstance_FieldsDefaultToZeroAndNull()
        {
            var group = new TsGroup();

            Assert.AreEqual(0, group.Id);
            Assert.AreEqual(0, group.ParentId);
            Assert.IsNull(group.Name);
        }

        [Test]
        public void TsGroupedEntry_FreshInstance_FieldsDefaultToNullAndZero()
        {
            var entry = new TsGroupedEntry();

            Assert.IsNull(entry.Value);
            Assert.AreEqual(0, entry.GroupId);
        }

        [Test]
        public void NestedInsideTsConfigGlobalGroups_SerializedObjectWalksIntoFields()
        {
            var go = new GameObject(nameof(TsConfig));
            _spawned.Add(go);
            TsConfig config = go.AddComponent<TsConfig>();
            config.GlobalGroups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "Enemies" } };

            var so = new SerializedObject(config);
            SerializedProperty groups = so.FindProperty(nameof(TsConfig.GlobalGroups));
            Assert.IsNotNull(groups);
            Assert.AreEqual(1, groups.arraySize);

            SerializedProperty first = groups.GetArrayElementAtIndex(0);
            Assert.IsNotNull(first.FindPropertyRelative(nameof(TsGroup.Id)),
                "SerializedObject must walk into TsGroup's own fields as a nested array element - " +
                "this is exactly the scenario the [Serializable] attribute exists to support.");
            Assert.AreEqual(1, first.FindPropertyRelative(nameof(TsGroup.Id)).intValue);
            Assert.AreEqual(0, first.FindPropertyRelative(nameof(TsGroup.ParentId)).intValue);
            Assert.AreEqual("Enemies", first.FindPropertyRelative(nameof(TsGroup.Name)).stringValue);
        }

        [Test]
        public void NestedInsideTsConfigGlobalEntries_SerializedObjectWalksIntoFields()
        {
            var go = new GameObject(nameof(TsConfig));
            _spawned.Add(go);
            var marker = new GameObject("Marker");
            _spawned.Add(marker);
            TsConfig config = go.AddComponent<TsConfig>();
            config.GlobalEntries = new[] { new TsGroupedEntry { Value = marker, GroupId = 3 } };

            var so = new SerializedObject(config);
            SerializedProperty entries = so.FindProperty(nameof(TsConfig.GlobalEntries));
            Assert.IsNotNull(entries);
            Assert.AreEqual(1, entries.arraySize);

            SerializedProperty first = entries.GetArrayElementAtIndex(0);
            Assert.IsNotNull(first.FindPropertyRelative(nameof(TsGroupedEntry.Value)));
            Assert.AreSame(marker, first.FindPropertyRelative(nameof(TsGroupedEntry.Value)).objectReferenceValue);
            Assert.AreEqual(3, first.FindPropertyRelative(nameof(TsGroupedEntry.GroupId)).intValue);
        }

        [Test]
        public void NestedInsideTsConfigGlobalGroups_MultipleEntries_IndicesStayIndependent()
        {
            // Two entries with distinct Name values, read back by index, prove each array index
            // gets its own independent SerializedProperty rather than aliasing shared backing
            // data - same contract TsFactoryGroupTests pinned for the type this replaced.
            var go = new GameObject(nameof(TsConfig));
            _spawned.Add(go);
            TsConfig config = go.AddComponent<TsConfig>();
            config.GlobalGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Dungeon" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Arena" },
            };

            var so = new SerializedObject(config);
            SerializedProperty groups = so.FindProperty(nameof(TsConfig.GlobalGroups));
            Assert.AreEqual(2, groups.arraySize);

            string firstName = groups.GetArrayElementAtIndex(0).FindPropertyRelative(nameof(TsGroup.Name)).stringValue;
            string secondName = groups.GetArrayElementAtIndex(1).FindPropertyRelative(nameof(TsGroup.Name)).stringValue;

            Assert.AreEqual("Dungeon", firstName);
            Assert.AreEqual("Arena", secondName);
        }
    }
}
