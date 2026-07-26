using System.Reflection;
using NUnit.Framework;
using Tsvrc.Core.Generated;
using Tsvrc.Editor;
using UnityEditor;

namespace Tsvrc.Tests.EditMode
{
    // TsRootInspector - verifies the [CustomEditor] registration itself (target type = TsRoot,
    // editorForChildClasses = true) via the same private-field reflection UdonSharp's own
    // UdonSharpCustomEditorManager uses to discover it (see UdonSharpBehaviourEditor.cs). Not
    // exercised end-to-end against a real compiled UdonSharpBehaviour instance here - that would
    // require a full UdonSharpProgramAsset + backing UdonBehaviour setup (see CompiledRootFixture)
    // and would really be testing UdonSharp's wrapping behavior, not this class's own logic.
    public class TsRootInspectorTests
    {
        [Test]
        public void CustomEditorAttribute_TargetsTsRoot()
        {
            var attribute = typeof(TsRootInspector).GetCustomAttribute<CustomEditor>();
            Assert.IsNotNull(attribute, "TsRootInspector must have a [CustomEditor] attribute.");

            var inspectedTypeField = typeof(CustomEditor).GetField("m_InspectedType", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(inspectedTypeField, "CustomEditor.m_InspectedType field changed or was removed.");

            Assert.AreEqual(typeof(TsRoot), inspectedTypeField.GetValue(attribute));
        }

        [Test]
        public void CustomEditorAttribute_EditorForChildClassesIsTrue()
        {
            // UdonSharpCustomEditorManager checks this same flag to decide whether the inspector
            // applies to concrete subclasses of TsRoot (the actual compiled TsGenerated type),
            // not just TsRoot itself, which is abstract and never directly instantiated.
            var attribute = typeof(TsRootInspector).GetCustomAttribute<CustomEditor>();
            var editorForChildClassesField = typeof(CustomEditor).GetField("m_EditorForChildClasses", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(editorForChildClassesField, "CustomEditor.m_EditorForChildClasses field changed or was removed.");

            Assert.IsTrue((bool)editorForChildClassesField.GetValue(attribute));
        }
    }
}
