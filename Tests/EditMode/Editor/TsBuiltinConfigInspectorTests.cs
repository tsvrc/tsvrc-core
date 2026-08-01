using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsBuiltinConfigInspector - mirrors TsConfigInspectorTests: only verifies the editor is
    // actually registered/constructible for TsBuiltinConfig. OnInspectorGUI itself is IMGUI and
    // not exercised here, matching this suite's existing convention (see TsConfigInspectorTests'
    // own comment for why).
    public class TsBuiltinConfigInspectorTests
    {
        private TsBuiltinConfig _config;
        private UnityEditor.Editor _editor;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<TsBuiltinConfig>();

        [TearDown]
        public void TearDown()
        {
            if (_editor != null) Object.DestroyImmediate(_editor);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void CreateEditor_ForTsBuiltinConfig_ReturnsTsBuiltinConfigInspector()
        {
            _editor = UnityEditor.Editor.CreateEditor(_config);

            Assert.IsInstanceOf<TsBuiltinConfigInspector>(_editor);
        }
    }
}
