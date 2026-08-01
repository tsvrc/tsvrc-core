using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // MeshCombinerWindow.ComputePathError/GetValid are private instance methods on an
    // EditorWindow — created via ScriptableObject.CreateInstance (never Show()n, so no
    // actual window appears) and driven entirely through reflection.
    public class MeshCombinerWindowLogicTests
    {
        private static readonly System.Type WindowType = typeof(MeshCombinerWindow);
        private static readonly FieldInfo SavePathField = WindowType.GetField("_savePath", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SourcesField = WindowType.GetField("_sources", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ComputePathErrorMethod = WindowType.GetMethod("ComputePathError", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo GetValidMethod = WindowType.GetMethod("GetValid", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawnedGameObjects = new List<GameObject>();
        private MeshCombinerWindow _window;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SavePathField, "MeshCombinerWindow._savePath field changed or was removed.");
            Assert.IsNotNull(SourcesField, "MeshCombinerWindow._sources field changed or was removed.");
            Assert.IsNotNull(ComputePathErrorMethod, "MeshCombinerWindow.ComputePathError method changed or was removed.");
            Assert.IsNotNull(GetValidMethod, "MeshCombinerWindow.GetValid method changed or was removed.");

            _window = ScriptableObject.CreateInstance<MeshCombinerWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
                Object.DestroyImmediate(_window);

            foreach (GameObject go in _spawnedGameObjects)
                Object.DestroyImmediate(go);
            _spawnedGameObjects.Clear();
        }

        private string ComputePathError(string savePath)
        {
            SavePathField.SetValue(_window, savePath);
            return (string)ComputePathErrorMethod.Invoke(_window, null);
        }

        private MeshFilter CreateMeshFilter(string name)
        {
            var go = new GameObject(name);
            _spawnedGameObjects.Add(go);
            return go.AddComponent<MeshFilter>();
        }

        private List<MeshFilter> GetValid(List<MeshFilter> sources)
        {
            SourcesField.SetValue(_window, sources);
            return (List<MeshFilter>)GetValidMethod.Invoke(_window, null);
        }

        [Test]
        public void ComputePathError_EmptyPath_ReturnsEmptyPathError()
        {
            Assert.AreEqual("Save path is empty.", ComputePathError(""));
        }

        [Test]
        public void ComputePathError_WhitespaceOnlyPath_ReturnsEmptyPathError()
        {
            Assert.AreEqual("Save path is empty.", ComputePathError("   "));
        }

        [Test]
        public void ComputePathError_MissingAssetsPrefix_ReturnsPrefixError()
        {
            Assert.AreEqual("Save path must start with \"Assets/\".", ComputePathError("Foo/Bar.asset"));
        }

        [Test]
        public void ComputePathError_MissingAssetSuffix_ReturnsSuffixError()
        {
            Assert.AreEqual("Save path must end with \".asset\".", ComputePathError("Assets/Foo.txt"));
        }

        [Test]
        public void ComputePathError_ValidPath_ReturnsNull()
        {
            Assert.IsNull(ComputePathError("Assets/Foo.asset"));
        }

        [Test]
        public void GetValid_EmptySources_ReturnsEmptyList()
        {
            List<MeshFilter> valid = GetValid(new List<MeshFilter>());

            Assert.AreEqual(0, valid.Count);
        }

        [Test]
        public void GetValid_FiltersNullEntries()
        {
            MeshFilter mf = CreateMeshFilter("A");

            List<MeshFilter> valid = GetValid(new List<MeshFilter> { null, mf, null });

            Assert.AreEqual(new[] { mf }, valid.ToArray());
        }

        [Test]
        public void GetValid_DedupesDuplicateReferences_KeepingFirstOccurrenceOrder()
        {
            MeshFilter mf1 = CreateMeshFilter("A");
            MeshFilter mf2 = CreateMeshFilter("B");

            List<MeshFilter> valid = GetValid(new List<MeshFilter> { mf1, mf1, mf2 });

            Assert.AreEqual(new[] { mf1, mf2 }, valid.ToArray());
        }

        // DetermineCombineDisabledReason is internal static and pure, called directly (no
        // reflection needed, unlike ComputePathError/GetValid above).

        [Test]
        public void DetermineCombineDisabledReason_NoValidSources_ReturnsAddSourceReason()
        {
            Assert.AreEqual("Add at least one valid MeshFilter source before combining.",
                MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 0, pathError: null, rootOverlapError: null, isPlayMode: false));
        }

        [Test]
        public void DetermineCombineDisabledReason_ValidSourcesButPathError_ReturnsPathError()
        {
            Assert.AreEqual("Save path is empty.",
                MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 1, pathError: "Save path is empty.", rootOverlapError: null, isPlayMode: false));
        }

        [Test]
        public void DetermineCombineDisabledReason_ValidSourcesAndNoPathError_ReturnsNull()
        {
            Assert.IsNull(MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 1, pathError: null, rootOverlapError: null, isPlayMode: false));
        }

        [Test]
        public void DetermineCombineDisabledReason_NoValidSourcesTakesPrecedenceOverPathError()
        {
            Assert.AreEqual("Add at least one valid MeshFilter source before combining.",
                MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 0, pathError: "Save path is empty.", rootOverlapError: null, isPlayMode: false));
        }

        [Test]
        public void DetermineCombineDisabledReason_PlayMode_TakesPrecedenceOverEverythingElse()
        {
            string reason = MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 1, pathError: null, rootOverlapError: null, isPlayMode: true);

            StringAssert.Contains("Play Mode", reason);
        }

        [Test]
        public void DetermineCombineDisabledReason_NotPlayModeWithValidSourcesAndPath_ReturnsNull()
        {
            Assert.IsNull(MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 1, pathError: null, rootOverlapError: null, isPlayMode: false));
        }

        // A Root Transform that is, or is inside, one of the sources being combined must block
        // the combine: deactivating sources afterward would also deactivate the just-created
        // output (activeInHierarchy cascades to children), making a "successful" combine
        // silently invisible with no error.
        [Test]
        public void DetermineCombineDisabledReason_RootOverlapError_ReturnsIt()
        {
            Assert.AreEqual("root overlap",
                MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 1, pathError: null, rootOverlapError: "root overlap", isPlayMode: false));
        }

        [Test]
        public void DetermineCombineDisabledReason_PathErrorTakesPrecedenceOverRootOverlap()
        {
            Assert.AreEqual("path error",
                MeshCombinerWindow.DetermineCombineDisabledReason(validCount: 1, pathError: "path error", rootOverlapError: "root overlap", isPlayMode: false));
        }

        [Test]
        public void DetermineRootOverlapError_RootNull_ReturnsNull()
        {
            var mf = CreateMeshFilter("Source");

            Assert.IsNull(MeshCombinerWindow.DetermineRootOverlapError(null, new List<MeshFilter> { mf }, new List<GameObject>()));
        }

        [Test]
        public void DetermineRootOverlapError_RootUnrelatedToSources_ReturnsNull()
        {
            var mf = CreateMeshFilter("Source");
            var rootGo = new GameObject("Root");
            _spawnedGameObjects.Add(rootGo);

            Assert.IsNull(MeshCombinerWindow.DetermineRootOverlapError(rootGo.transform, new List<MeshFilter> { mf }, new List<GameObject>()));
        }

        [Test]
        public void DetermineRootOverlapError_RootIsSourceItself_ReturnsError()
        {
            var mf = CreateMeshFilter("Source");

            string error = MeshCombinerWindow.DetermineRootOverlapError(mf.transform, new List<MeshFilter> { mf }, new List<GameObject>());

            StringAssert.Contains("Source", error);
        }

        [Test]
        public void DetermineRootOverlapError_RootIsDescendantOfSource_ReturnsError()
        {
            var mf = CreateMeshFilter("Source");
            var child = new GameObject("Child");
            _spawnedGameObjects.Add(child);
            child.transform.SetParent(mf.transform);

            string error = MeshCombinerWindow.DetermineRootOverlapError(child.transform, new List<MeshFilter> { mf }, new List<GameObject>());

            Assert.IsNotNull(error, "A root nested under a source is exactly the trigger this validates against.");
        }

        [Test]
        public void DetermineRootOverlapError_RootIsColliderOnlySourceItself_ReturnsError()
        {
            var colliderOnly = new GameObject("ColliderOnly");
            _spawnedGameObjects.Add(colliderOnly);

            string error = MeshCombinerWindow.DetermineRootOverlapError(
                colliderOnly.transform, new List<MeshFilter>(), new List<GameObject> { colliderOnly });

            StringAssert.Contains("ColliderOnly", error);
        }

        [Test]
        public void DetermineRootOverlapError_RootIsParentOfSource_ReturnsNull()
        {
            // The reverse relationship (root is an ancestor, not a descendant) is the ordinary,
            // intended usage - "combine these children into their shared parent" - and must never
            // be flagged.
            var parentGo = new GameObject("Parent");
            _spawnedGameObjects.Add(parentGo);
            var mf = CreateMeshFilter("Source");
            mf.transform.SetParent(parentGo.transform);

            Assert.IsNull(MeshCombinerWindow.DetermineRootOverlapError(parentGo.transform, new List<MeshFilter> { mf }, new List<GameObject>()));
        }
    }
}
