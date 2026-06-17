#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    internal class ConstructModule : TsvrcModule
    {
        private List<TsvrcField> _currentFields = new List<TsvrcField>();

        internal override string FileName => "TsvrcConstructBehaviour.cs";

        internal override void LoadConfig()
        {
            var sceneConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            _currentFields = TsvrcResolver.Resolve(sceneConfig?.Constructs);
        }

        internal override string GenerateCode()
        {
            if (_currentFields.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var field in _currentFields)
                if (!string.IsNullOrEmpty(field.Namespace) && !usings.Contains(field.Namespace))
                    usings.Add(field.Namespace);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.ConstructClassName} : UdonSharpBehaviour"))
            {
                foreach (var field in _currentFields.OrderBy(f => f.Name))
                    w.Line($"[HideInInspector] [SerializeField] public {field.Type} {field.Name};");

                using (w.Method("void Start()")) { }
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.ConstructClassName} : UdonSharpBehaviour"))
            using (w.Method("void Start()"))
            { }
            return w.ToString();
        }

        internal override void Wire()
        {
            var constructType = ScaffoldModule.FindConstructType();
            if (constructType == null) return;

            var constructComp = (Component)UnityEngine.Object.FindObjectOfType(constructType, true);
            if (constructComp == null) return;

            var so = new SerializedObject(constructComp);
            foreach (var field in _currentFields)
            {
                if (field.SourceObject == null)
                {
                    Debug.LogWarning($"[ConstructModule] Construct '{field.Name}' source object is null. Remove the missing entry from TsvrcConfig.");
                    continue;
                }

                var prop = so.FindProperty(field.Name);
                if (prop == null)
                {
                    Debug.LogWarning($"[ConstructModule] Field '{field.Name}' not found on {ScaffoldModule.ConstructClassName}. Force compile to regenerate.");
                    continue;
                }

                prop.objectReferenceValue = field.SourceObject;
            }

            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(constructComp.gameObject.scene);
        }
    }
}
#endif
