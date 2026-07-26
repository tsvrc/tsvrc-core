using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsConfigInspector - only verifies the editor is actually registered/constructible for
    // TsConfig. OnInspectorGUI itself is IMGUI (GUILayout calls require a real repaint/layout
    // event) and is not exercised here, matching this suite's existing convention of only
    // testing pure/reflectable logic for editor windows (see MeshCombinerWindowLogicTests).
    //
    // UnityEditor.Editor is referenced by its fully-qualified name throughout: this file's own
    // "using Tsvrc.Editor;" makes the bare name "Editor" ambiguous with that namespace itself.
    public class TsConfigInspectorTests
    {
        private GameObject _go;
        private TsConfig _config;
        private UnityEditor.Editor _editor;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TsConfigInspectorTests_TsConfig");
            _config = _go.AddComponent<TsConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_editor != null) Object.DestroyImmediate(_editor);
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void CreateEditor_ForTsConfig_ReturnsTsConfigInspector()
        {
            _editor = UnityEditor.Editor.CreateEditor(_config);

            Assert.IsInstanceOf<TsConfigInspector>(_editor);
        }
    }
}
