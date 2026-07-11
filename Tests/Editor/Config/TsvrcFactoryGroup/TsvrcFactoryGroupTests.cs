using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // TsvrcFactoryGroup is a pure [Serializable] data class - two fields, zero
    // methods. Tests here pin the [Serializable] attribute itself (losing it
    // silently breaks the Inspector for TsvrcConfig.Factories/
    // TsvrcBuiltinConfig.Factories with no compile error), default field values,
    // and that it round-trips as a nested array element through Unity's serializer.
    public class TsvrcFactoryGroupTests
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
        public void TsvrcFactoryGroup_HasSerializableAttribute()
        {
            bool isSerializable = Attribute.IsDefined(typeof(TsvrcFactoryGroup), typeof(SerializableAttribute));

            Assert.IsTrue(isSerializable,
                "TsvrcFactoryGroup must stay [Serializable] - losing it silently breaks the " +
                "Inspector for TsvrcConfig.Factories/TsvrcBuiltinConfig.Factories with no compile error.");
        }

        [Test]
        public void FreshInstance_FieldsDefaultToNull()
        {
            var group = new TsvrcFactoryGroup();

            Assert.IsNull(group.GroupName);
            Assert.IsNull(group.Prefabs);
        }

        [Test]
        public void GroupName_AssignAndReadBack_RoundTrips()
        {
            var group = new TsvrcFactoryGroup { GroupName = "Maze" };

            Assert.AreEqual("Maze", group.GroupName);
        }

        [Test]
        public void Prefabs_AssignAndReadBack_RoundTrips()
        {
            var marker = new GameObject("PrefabMarker");
            _spawned.Add(marker);
            var value = new UnityEngine.Object[] { marker };

            var group = new TsvrcFactoryGroup { Prefabs = value };

            Assert.AreSame(value, group.Prefabs);
            Assert.AreSame(marker, group.Prefabs[0]);
        }

        [Test]
        public void NestedInsideTsvrcConfigFactories_SerializedObjectWalksIntoFields()
        {
            var go = new GameObject(nameof(TsvrcConfig));
            _spawned.Add(go);
            TsvrcConfig config = go.AddComponent<TsvrcConfig>();
            config.Factories = new[] { new TsvrcFactoryGroup { GroupName = "Maze" } };

            var so = new SerializedObject(config);
            SerializedProperty factories = so.FindProperty(nameof(TsvrcConfig.Factories));
            Assert.IsNotNull(factories);
            Assert.AreEqual(1, factories.arraySize);

            SerializedProperty firstEntry = factories.GetArrayElementAtIndex(0);
            SerializedProperty groupName = firstEntry.FindPropertyRelative(nameof(TsvrcFactoryGroup.GroupName));
            SerializedProperty prefabs = firstEntry.FindPropertyRelative(nameof(TsvrcFactoryGroup.Prefabs));

            Assert.IsNotNull(groupName,
                "SerializedObject must walk into TsvrcFactoryGroup's own fields as a nested " +
                "array element - this is exactly the scenario the [Serializable] attribute exists to support.");
            Assert.AreEqual("Maze", groupName.stringValue);
            Assert.IsNotNull(prefabs);
        }

        [Test]
        public void NestedInsideTsvrcConfigFactories_MultipleEntries_IndicesStayIndependent()
        {
            // Two entries with distinct GroupName values, read back by index, prove
            // each array index gets its own independent SerializedProperty rather
            // than aliasing shared backing data.
            var go = new GameObject(nameof(TsvrcConfig));
            _spawned.Add(go);
            TsvrcConfig config = go.AddComponent<TsvrcConfig>();
            config.Factories = new[]
            {
                new TsvrcFactoryGroup { GroupName = "Maze" },
                new TsvrcFactoryGroup { GroupName = "Arena" },
            };

            var so = new SerializedObject(config);
            SerializedProperty factories = so.FindProperty(nameof(TsvrcConfig.Factories));
            Assert.AreEqual(2, factories.arraySize);

            string firstName = factories.GetArrayElementAtIndex(0)
                .FindPropertyRelative(nameof(TsvrcFactoryGroup.GroupName)).stringValue;
            string secondName = factories.GetArrayElementAtIndex(1)
                .FindPropertyRelative(nameof(TsvrcFactoryGroup.GroupName)).stringValue;

            Assert.AreEqual("Maze", firstName);
            Assert.AreEqual("Arena", secondName);
        }

        [Test]
        public void Prefabs_TooltipText_NamesTheRealGeneratedClass_NotAStaleTypo()
        {
            // FactoryModule.GenerateCode() emits Create{Group}{Name}(Transform parent)
            // methods directly on TsvrcGenerated (ScaffoldModule.CompiledClassName) -
            // the tooltip must name that class.
            FieldInfo field = typeof(TsvrcFactoryGroup).GetField(nameof(TsvrcFactoryGroup.Prefabs));
            var tooltip = field.GetCustomAttribute<TooltipAttribute>();

            Assert.IsNotNull(tooltip);
            StringAssert.DoesNotContain("CompiledTsvrc", tooltip.tooltip);
            StringAssert.Contains("TsvrcGenerated", tooltip.tooltip);
        }
    }
}
