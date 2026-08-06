using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Core;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsConfig is a pure data container, zero methods. Tests here pin the class's documented
    // contracts: plain MonoBehaviour (never compiled to Udon), not living under an Editor/
    // folder, default field values, and that the fields are genuinely serialized.
    public class TsConfigTests
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

        private TsConfig CreateConfig()
        {
            var go = new GameObject(nameof(TsConfig));
            _spawned.Add(go);
            return go.AddComponent<TsConfig>();
        }

        [Test]
        public void AddComponent_FromThisAssembly_Succeeds()
        {
            // AddComponent silently returns null for a script under a folder
            // literally named "Editor", since such scripts are excluded from the
            // runtime assembly. TsConfig deliberately isn't under one.
            TsConfig config = CreateConfig();

            Assert.IsNotNull(config);
        }

        [Test]
        public void TsConfig_IsPlainMonoBehaviour_NotUdonSharpBehaviour()
        {
            Assert.IsTrue(typeof(MonoBehaviour).IsAssignableFrom(typeof(TsConfig)));
            Assert.IsFalse(typeof(UdonSharpBehaviour).IsAssignableFrom(typeof(TsConfig)),
                "TsConfig must never become an UdonSharpBehaviour - it's read only by editor " +
                "tooling and is never compiled to Udon, per the class's own doc comment.");
        }

        [Test]
        public void FreshInstance_ArrayFields_DefaultToNull()
        {
            TsConfig config = CreateConfig();

            Assert.IsNull(config.GlobalEntries);
            Assert.IsNull(config.GlobalGroups);
            Assert.IsNull(config.PooledObjects);
            Assert.IsNull(config.Constructs);
            Assert.IsNull(config.FactoryEntries);
            Assert.IsNull(config.FactoryGroups);
        }

        [Test]
        public void GlobalEntries_AssignAndReadBack_RoundTrips()
        {
            TsConfig config = CreateConfig();
            var marker = new GameObject("GlobalMarker");
            _spawned.Add(marker);
            var value = new[] { new TsGroupedEntry { Value = marker, GroupId = 0 } };

            config.GlobalEntries = value;

            Assert.AreSame(value, config.GlobalEntries);
            Assert.AreSame(marker, config.GlobalEntries[0].Value);
        }

        [Test]
        public void PooledObjects_AssignAndReadBack_RoundTrips()
        {
            TsConfig config = CreateConfig();
            var value = new UdonSharpBehaviour[0];

            config.PooledObjects = value;

            Assert.AreSame(value, config.PooledObjects);
        }

        [Test]
        public void Constructs_AssignAndReadBack_RoundTrips()
        {
            TsConfig config = CreateConfig();
            var behaviourGo = new GameObject("ConstructEntry");
            _spawned.Add(behaviourGo);
            var behaviour = behaviourGo.AddComponent<TsvrcBehaviour>();
            var value = new[] { behaviour };

            config.Constructs = value;

            Assert.AreSame(value, config.Constructs);
            Assert.AreSame(behaviour, config.Constructs[0]);
        }

        [Test]
        public void FactoryEntries_AssignAndReadBack_RoundTrips()
        {
            TsConfig config = CreateConfig();
            var prefabGo = new GameObject("FactoryPrefabMarker");
            _spawned.Add(prefabGo);
            var value = new[] { new TsGroupedEntry { Value = prefabGo, GroupId = 0 } };

            config.FactoryEntries = value;

            Assert.AreSame(value, config.FactoryEntries);
            Assert.AreSame(prefabGo, config.FactoryEntries[0].Value);
        }

        [Test]
        public void SerializedObject_FindsEveryFieldByName()
        {
            TsConfig config = CreateConfig();
            var so = new SerializedObject(config);

            Assert.IsNotNull(so.FindProperty(nameof(TsConfig.GlobalEntries)),
                "GlobalEntries must be a genuinely serialized field for the generator/Inspector to read it.");
            Assert.IsNotNull(so.FindProperty(nameof(TsConfig.GlobalGroups)));
            Assert.IsNotNull(so.FindProperty(nameof(TsConfig.PooledObjects)));
            Assert.IsNotNull(so.FindProperty(nameof(TsConfig.Constructs)));
            Assert.IsNotNull(so.FindProperty(nameof(TsConfig.FactoryEntries)));
            Assert.IsNotNull(so.FindProperty(nameof(TsConfig.FactoryGroups)));
        }

        [Test]
        public void GlobalEntries_TooltipText_NamesTheRealGeneratedClass_NotAStaleApiShape()
        {
            // GlobalModule.GenerateCode() generates Globals entries directly
            // as fields on the TsGenerated partial class, with no nested wrapper
            // type - the tooltip must describe that shape.
            FieldInfo field = typeof(TsConfig).GetField(nameof(TsConfig.GlobalEntries));
            var tooltip = field.GetCustomAttribute<TooltipAttribute>();

            Assert.IsNotNull(tooltip);
            StringAssert.DoesNotContain("_ts.Global", tooltip.tooltip);
            StringAssert.Contains("TsGenerated", tooltip.tooltip);
        }
    }
}
