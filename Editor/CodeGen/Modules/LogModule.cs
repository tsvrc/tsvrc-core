#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates _log and _TsLogStart() on TsGenerated so every system reaches logging
    // only through TsGenerated.Log (or TsBehaviour.LogInfo/LogWarning/LogError). Mirrors MemoryModule.
    internal class LogModule : TsModule
    {
        // Package-relative, not a literal - see PackagePaths.
        private static string LogScriptPath => $"{PackagePaths.Root}/Runtime/Utils/TsLogger.cs";
        private static string LogAssetPath => $"{PackagePaths.Root}/Runtime/Utils/TsLogger.asset";

        internal override string FileName => "TsGeneratedLog.cs";

        internal override string TabLabel => "Logging";
        internal override string TabDescription =>
            "Configure the scene's TsLogger: the optional project tag shown after the always-present [TsVRC] framework tag, and which log levels are shown - independently for Tsvrc's own internal diagnostics and your world's own scripts. All levels are shown by default.";

        // Unlike other tabs, this data lives on the scene's TsLogger component, not TsConfig,
        // so it builds its own SerializedObject instead of using the TsConfig-bound `so` param.
        internal override void DrawTab(SerializedObject so)
        {
            var logger = (TsLogger)UnityEngine.Object.FindObjectOfType(typeof(TsLogger), true);
            if (logger == null)
            {
                EditorGUILayout.HelpBox(
                    "No TsLogger found in the scene yet. Press Force Regenerate below to create it.",
                    MessageType.Info);
                return;
            }

            var logSo = new SerializedObject(logger);
            logSo.Update();

            EditorGUILayout.PropertyField(logSo.FindProperty("_prefix"),
                new GUIContent("World Prefix", "Optional. Shown after the always-present [TsVRC] tag, e.g. \"SomeWorld\" -> \"[TsVRC] [SomeWorld] [ClassName] message\". Leave empty to omit."));

            EditorGUILayout.Space(8);
            // No manual "Tsvrc Internal" label: _internalInfoEnabled carries [Header("Tsvrc Internal")]
            // in TsLogger.cs, which PropertyField renders automatically.
            EditorGUILayout.PropertyField(logSo.FindProperty("_internalInfoEnabled"), new GUIContent("Info"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_internalWarningEnabled"), new GUIContent("Warning"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_internalErrorEnabled"), new GUIContent("Error"));

            // Same reasoning: _worldInfoEnabled carries [Header("Your World")].
            EditorGUILayout.PropertyField(logSo.FindProperty("_worldInfoEnabled"), new GUIContent("Info"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_worldWarningEnabled"), new GUIContent("Warning"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_worldErrorEnabled"), new GUIContent("Error"));

            logSo.ApplyModifiedProperties();
        }

        internal override IEnumerable<string> WatchedAssets() => new[] { LogAssetPath };

        internal override void LoadConfig() { }

        internal override string GenerateCode()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "Tsvrc.Utils", "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                w.Line("[ReadOnly] [SerializeField] private TsLogger _log;");
                w.BlankLine();
                w.Line("public override TsLogger Log => _log;");
                w.BlankLine();
                using (w.Method("public void _TsLogStart()"))
                    w.Line("_log.TsConstruct(this);");
            }
            return w.ToString();
        }

        internal override bool AfterFilesStable()
        {
            bool programAssetMissing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LogAssetPath) == null;
            ScaffoldModule.EnsureUdonSharpProgramAsset(LogScriptPath, LogAssetPath);

            var root = FindRoot();
            if (root == null) return programAssetMissing;

            ScaffoldModule.EnsureChildSceneObject("TsLogger", typeof(TsLogger), root);
            return programAssetMissing;
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var log = (Component)UnityEngine.Object.FindObjectOfType(typeof(TsLogger), true);

            var so = new SerializedObject(root);
            var prop = so.FindProperty("_log");
            if (prop == null)
            {
                Debug.LogWarning($"[LogModule] Field '_log' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                return;
            }
            if (prop.objectReferenceValue == (UnityEngine.Object)log) return;
            prop.objectReferenceValue = log;
            ApplyAndMarkDirty(so, root);
        }

        internal override bool OnSceneHierarchyChanged()
        {
            var root = FindRoot();
            if (root == null) return false;
            return root.transform.Find("TsLogger") == null;
        }
    }
}
#endif
