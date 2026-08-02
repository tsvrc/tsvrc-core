#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates _log and _TsLogStart() on TsGenerated so every system reaches logging
    // only through TsGenerated.Log (or TsvrcBehaviour.LogInfo/LogWarning/LogError). Mirrors MemoryModule.
    internal class LogModule : TsSingleComponentModule
    {
        // LogInfo/LogWarning/LogError (TsvrcBehaviour.cs) reach _ts.Log indirectly - a script
        // calling LogInfo(...) never textually mentions "_ts.Log", so the base class's plain
        // member-access check alone would miss the most common way Log actually gets used.
        protected override IEnumerable<string> AdditionalUsageMethodNames() =>
            new[] { "LogInfo", "LogWarning", "LogError" };

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
            // Scoped to the linked scene, same as Wire(), so this preview can't show a
            // TsvrcLogger from an unrelated additively-loaded scene.
            var logger = (TsvrcLogger)TsLinkedScene.FindType(typeof(TsvrcLogger));
            if (logger == null)
            {
                string message = IsUsed
                    ? "No TsvrcLogger found in the scene yet. Press Force Regenerate below to create it."
                    : "Tree-Shake Unused is on and no project script currently calls _ts.Log/LogInfo/LogWarning/" +
                      "LogError, so TsvrcLogger isn't generated right now. Reference it from a TsvrcBehaviour, or " +
                      "add \"Log\" to Force Include Names above, to bring it back.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
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
