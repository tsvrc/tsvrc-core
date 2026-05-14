#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Tsvrc.Core;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Handles the single TsvrcInstance for the world.
    // If config.Instance is assigned it is used directly. Otherwise the module scans user
    // source files for a TsvrcInstance subclass, creates a child GameObject under CompiledTsvrc,
    // and wires it automatically.
    internal class InstanceModule : TsvrcModule
    {
        private TsvrcField _field;
        private bool _autoDetected;
        private string _scriptAssetPath;

        internal override void Scan(TsvrcConfig config)
        {
            if (config.Instance != null)
            {
                _field = FieldFromInstance(config.Instance);
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
                Namespace = found.Value.Namespace,
            };
            _scriptAssetPath = found.Value.AssetPath;
            _autoDetected = true;
        }

        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            // Config-assigned path: same fast type extraction as Scan, no file scan needed.
            if (config.Instance != null)
            {
                _field = FieldFromInstance(config.Instance);
                _autoDetected = false;
                return;
            }

            // Auto-detected path: check if _coreTsvrcInstance exists in the compiled type via reflection
            // so we avoid a full source file scan.
            var fieldInfo = compiledType.GetField("_coreTsvrcInstance",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo == null)
            {
                _field = null;
                return;
            }

            var instanceType = fieldInfo.FieldType;

            // Locate the script asset using Unity's indexed asset database (fast, no file read).
            _scriptAssetPath = string.Empty;
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {instanceType.Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == instanceType.Name)
                {
                    _scriptAssetPath = path;
                    break;
                }
            }

            _field = new TsvrcField
            {
                Type = instanceType.Name,
                Namespace = instanceType.Namespace ?? string.Empty,
            };
            _autoDetected = true;
        }

        internal override IEnumerable<string> GetUsings()
        {
            yield return "Tsvrc.Core"; // for public TsvrcInstance Instance property
            if (_field != null && !string.IsNullOrEmpty(_field.Namespace))
                yield return _field.Namespace;
        }

        internal override void WriteFields(CsWriter w)
        {
            if (_field == null) return;
            w.Line($"[ReadOnly] [SerializeField] private {_field.Type} _coreTsvrcInstance;");
            w.Line("public TsvrcInstance Instance => _coreTsvrcInstance;");
        }

        internal override void WriteStartBody(CsWriter w)
        {
            if (_field == null) return;
            w.Line("_coreTsvrcInstance.TsConstruct(this);");
            w.Line("_coreTsvrcInstance.OnInstanceStart();");
        }

        internal override void Wire(SerializedObject target)
        {
            if (_field == null) return;

            var prop = target.FindProperty("_coreTsvrcInstance");
            if (prop == null)
            {
                Debug.LogWarning("[TsvrcWirer] '_coreTsvrcInstance' property not found on CompiledTsvrc.");
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

            if (!TsvrcCompiler.EnsureUdonSharpProgramAsset(_scriptAssetPath, programAssetPath))
            {
                Debug.LogWarning($"[TsvrcWirer] Script not found at '{_scriptAssetPath}'. Cannot create program asset for '{_field.Type}'.");
                return;
            }

            var compiledGo = ((Component)target.targetObject).gameObject;

            // Destroy any existing auto-detected instance child so repeated wire passes don't stack duplicates.
            var existingChild = compiledGo.transform.Find(_field.Type);
            if (existingChild != null)
                Undo.DestroyObjectImmediate(existingChild.gameObject);

            var go = new GameObject(_field.Type);
            go.transform.SetParent(compiledGo.transform, false);
            Undo.RegisterCreatedObjectUndo(go, $"Create {_field.Type} instance");

            prop.objectReferenceValue = UdonSharpUndo.AddComponent(go, instanceType);
        }

        private static TsvrcField FieldFromInstance(TsvrcInstance instance)
        {
            var type = instance.GetType();
            return new TsvrcField
            {
                Type = type.Name,
                Namespace = type.Namespace ?? string.Empty,
                SourceObject = instance,
            };
        }
    }
}
#endif
