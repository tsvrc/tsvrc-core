#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Handles the single TsvrcInstance for the world.
    /// If config.Instance is assigned, uses it directly.
    /// If not, auto-detects any TsvrcInstance subclass in user code, creates a child GameObject
    /// under CompiledTsvrc, and wires it as the instance.
    /// </summary>
    internal class InstanceModule : TsvrcModule
    {
        private TsvrcField _field;
        private bool _autoDetected;
        private string _scriptAssetPath;

        internal override void Scan(TsvrcConfig config)
        {
            if (config.Instance != null)
            {
                var resolved = TsvrcResolver.Resolve(
                    new HashSet<UnityEngine.Object> { config.Instance },
                    new HashSet<string>());
                _field = resolved.FirstOrDefault();
                _autoDetected = false;
                return;
            }

            var found = SourceScanner.FindSubclass("TsvrcInstance");
            if (found == null)
            {
                _field = null;
                return;
            }

            _field = new TsvrcField
            {
                Type = found.Value.TypeName,
                Name = found.Value.TypeName,
                Namespace = found.Value.Namespace,
            };
            _scriptAssetPath = found.Value.AssetPath;
            _autoDetected = true;
        }

        internal override IEnumerable<string> GetUsings()
        {
            if (_field != null && !string.IsNullOrEmpty(_field.Namespace))
                yield return _field.Namespace;
        }

        internal override void WriteFields(CsWriter w)
        {
            if (_field == null) return;
            w.Line($"[SerializeField] private {_field.Type} _core_tsvrc_instance;");
        }

        internal override void WriteStartBody(CsWriter w)
        {
            if (_field == null) return;
            w.Line("_core_tsvrc_instance.TsConstruct(this);");
            w.Line("_core_tsvrc_instance.OnInstanceStart();");
        }

        internal override void Wire(SerializedObject target)
        {
            if (_field == null) return;

            var prop = target.FindProperty("_core_tsvrc_instance");
            if (prop == null)
            {
                Debug.LogWarning("[TsvrcWirer] '_core_tsvrc_instance' property not found on CompiledTsvrc.");
                return;
            }

            if (!_autoDetected)
            {
                prop.objectReferenceValue = _field.SourceObject;
                return;
            }

            string fullTypeName = string.IsNullOrEmpty(_field.Namespace)
                ? $"{_field.Type}, Assembly-CSharp"
                : $"{_field.Namespace}.{_field.Type}, Assembly-CSharp";

            var instanceType = Type.GetType(fullTypeName);
            if (instanceType == null)
            {
                Debug.LogWarning($"[TsvrcWirer] Could not resolve type '{fullTypeName}'. Skipping auto-instance creation.");
                return;
            }

            string programAssetPath = TsvrcCompiler.GeneratedFolder + $"/{_field.Type}.asset";
            EnsureProgramAsset(_scriptAssetPath, programAssetPath);

            var compiledGo = ((Component)target.targetObject).gameObject;
            var go = new GameObject(_field.Type);
            go.transform.SetParent(compiledGo.transform, false);
            Undo.RegisterCreatedObjectUndo(go, $"Create {_field.Type} instance");

            prop.objectReferenceValue = UdonSharpUndo.AddComponent(go, instanceType);
        }

        private static void EnsureProgramAsset(string scriptAssetPath, string programAssetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programAssetPath) != null)
                return;

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptAssetPath);
            if (monoScript == null)
            {
                Debug.LogWarning($"[TsvrcWirer] Script not found at '{scriptAssetPath}' — cannot create program asset.");
                return;
            }

            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = monoScript;
            AssetDatabase.CreateAsset(programAsset, programAssetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
