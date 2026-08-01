#if UNITY_EDITOR
using System;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates _log and _TsLogStart() on TsGenerated so every system reaches logging
    // only through TsGenerated.Log (or TsvrcBehaviour.LogInfo/LogWarning/LogError). Mirrors MemoryModule.
    internal class LogModule : TsSingleComponentModule
    {
        // Package-relative, not a literal, see PackagePaths.
        private static string LogScriptPath => $"{PackagePaths.Root}/Runtime/Utils/TsvrcLogger.cs";
        private static string LogAssetPath => $"{PackagePaths.Root}/Runtime/Utils/TsvrcLogger.asset";

        internal override string FileName => "TsGeneratedLog.cs";

        protected override Type ComponentType => typeof(TsvrcLogger);
        protected override string FieldName => "_log";
        protected override string PublicPropertyName => "Log";
        protected override string StartMethodName => "_TsLogStart";
        protected override string ChildGameObjectName => "TsLogger";
        protected override string ScriptPath => LogScriptPath;
        protected override string AssetPath => LogAssetPath;
        protected override string ModuleTag => "LogModule";

        internal override string TabLabel => "Logging";
        internal override string TabDescription =>
            "Configure the scene's TsvrcLogger: the optional project tag shown after the always-present [TsVRC] framework tag, and which log levels are shown, independently for Tsvrc's own internal diagnostics and your world's own scripts. All levels are shown by default.";

        // Unlike other tabs, this data lives on the scene's TsvrcLogger component, not TsConfig,
        // so it builds its own SerializedObject instead of using the TsConfig-bound `so` param.
        internal override void DrawTab(SerializedObject so)
        {
            var logger = (TsvrcLogger)UnityEngine.Object.FindObjectOfType(typeof(TsvrcLogger), true);
            if (logger == null)
            {
                EditorGUILayout.HelpBox(
                    "No TsvrcLogger found in the scene yet. Press Force Regenerate below to create it.",
                    MessageType.Info);
                return;
            }

            var logSo = new SerializedObject(logger);
            logSo.Update();

            EditorGUILayout.PropertyField(logSo.FindProperty("_prefix"),
                new GUIContent("World Prefix", "Optional. Shown after the always-present [TsVRC] tag, e.g. \"SomeWorld\" -> \"[TsVRC] [SomeWorld] [ClassName] message\". Leave empty to omit."));

            EditorGUILayout.Space(8);
            // No manual "Tsvrc Internal" label: _internalInfoEnabled carries [Header("Tsvrc Internal")]
            // in TsvrcLogger.cs, which PropertyField renders automatically.
            EditorGUILayout.PropertyField(logSo.FindProperty("_internalInfoEnabled"), new GUIContent("Info"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_internalWarningEnabled"), new GUIContent("Warning"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_internalErrorEnabled"), new GUIContent("Error"));

            // Same reasoning: _worldInfoEnabled carries [Header("Your World")].
            EditorGUILayout.PropertyField(logSo.FindProperty("_worldInfoEnabled"), new GUIContent("Info"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_worldWarningEnabled"), new GUIContent("Warning"));
            EditorGUILayout.PropertyField(logSo.FindProperty("_worldErrorEnabled"), new GUIContent("Error"));

            logSo.ApplyModifiedProperties();
        }
    }
}
#endif
