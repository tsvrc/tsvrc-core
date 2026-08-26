#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Unlike TsConfig/TsRoot, this package-shipped, every-world-shares-it asset had no custom
    // inspector: a bare default Inspector gives no signal that dragging a project-specific prefab
    // in here (an easy mistake, since it sits next to the package's own real config with a
    // near-identical shape) makes that registration a library-wide "builtin" instead of a
    // per-world one, shared with every teammate on the next pull. Mirrors TsConfigInspector's
    // own banner pattern.
    //
    // Globals/Factories are drawn via the same TsGroupTreeGUI widget Tsvrc > Configure uses for
    // the per-world TsConfig, as two separate trees rather than merged with the world's own
    // config - builtin and per-world registrations are conceptually distinct, so browsing or
    // searching them together would blur that distinction.
    [CustomEditor(typeof(TsBuiltinConfig))]
    internal class TsBuiltinConfigInspector : UnityEditor.Editor
    {
        private readonly TsGroupTreeGUI.State _globalTreeState = new TsGroupTreeGUI.State();
        private readonly TsGroupTreeGUI.State _factoryTreeState = new TsGroupTreeGUI.State();
        private readonly TsGroupTreeGUI.State _poolTreeState = new TsGroupTreeGUI.State();

        // This asset isn't watched by TsGenerator (only TsConfig is - see TsGenerator.cs's
        // watchedTypes set), so there is no automatic regenerate to hold back here the way
        // TsWindow needs to. TsPendingConfigEdit is still used for the same Apply/Discard UX
        // consistency: this is the designated editor for TsBuiltinConfig, not a bypass of one,
        // so it gets the same batched-edit contract.
        private readonly TsPendingConfigEdit _pending = new TsPendingConfigEdit();

        // The live group fields are drawn explicitly above via TsGroupTreeGUI instead of
        // DrawDefaultInspector's raw array view, so they're excluded here to avoid showing the
        // same data twice.
        private static readonly string[] ManuallyDrawnProperties =
        {
            "m_Script",
            "GlobalEntries", "GlobalGroups", "GlobalNextGroupId",
            "FactoryEntries", "FactoryGroups", "FactoryNextGroupId",
            "PoolEntries", "PoolGroups", "PoolNextGroupId",
        };

        private void OnEnable() => _pending.BeginTracking(target);

        // Not OnDisable: that also fires around a domain reload (see TsWindow.OnDestroy's own
        // comment for why). OnDestroy only fires when this Editor instance is actually being
        // torn down for good.
        private void OnDestroy() => _pending.Cleanup();

        public override void OnInspectorGUI()
        {
            TsEditorGUI.DrawStatusBox(
                "This is Tsvrc's own package-level builtin config, shared by every world using this " +
                "copy of Tsvrc - anything registered here becomes available to every world, not just " +
                "this one. Register your own world's globals/pool prefabs/factories via " +
                "Tsvrc > Configure instead, unless you specifically intend to add a library-wide builtin.",
                MessageType.Info);

            bool regeneratePending = TsGenerator.IsRegeneratePending;
            if (regeneratePending)
                TsEditorGUI.DrawStatusBox(
                    "Waiting for a regenerate triggered by your last Apply to finish compiling - " +
                    "further edits are disabled until it settles.",
                    MessageType.Info);

            serializedObject.Update();
            _pending.BeginFrame();
            EditorGUI.BeginChangeCheck();

            // Disabled while a regenerate this inspector's own Apply triggered is still waiting
            // on a recompile - see TsWindow.OnGUI's matching comment for why.
            bool didReparent;
            using (new EditorGUI.DisabledScope(regeneratePending))
            {
                EditorGUILayout.LabelField("Globals", EditorStyles.boldLabel);
                didReparent = TsGroupTreeGUI.Draw(serializedObject, "GlobalGroups", "GlobalEntries", _globalTreeState,
                    "No builtin globals registered yet.", warnDuplicates: true, memberPrefix: "_ts.");

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Factories", EditorStyles.boldLabel);
                didReparent |= TsGroupTreeGUI.Draw(serializedObject, "FactoryGroups", "FactoryEntries", _factoryTreeState,
                    "No builtin factory prefabs registered yet.", assetsOnly: true,
                    memberPrefix: "Create", memberSuffix: "(parent)", prefixRespectsToggle: false);

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Pool", EditorStyles.boldLabel);
                didReparent |= TsGroupTreeGUI.Draw(serializedObject, "PoolGroups", "PoolEntries", _poolTreeState,
                    "No builtin pool prefabs registered yet.", assetsOnly: true);

                EditorGUILayout.Space(10);
                DrawPropertiesExcluding(serializedObject, ManuallyDrawnProperties);
            }

            // EndChangeCheck() catches a widget-driven edit even if TsGroupTreeGUI's own drag-
            // and-drop reparenting already flushed it via its own ApplyModifiedProperties() call
            // above (reported back via Draw's own return value) - see TsWindow.OnGUI's matching
            // comment for why this gate exists at all.
            bool anyWidgetEdit = EditorGUI.EndChangeCheck();
            bool anyChangesApplied = serializedObject.ApplyModifiedProperties() || anyWidgetEdit || didReparent;
            _pending.NotifyAppliedToSerializedObject(anyChangesApplied);
            DrawPendingChangesFooter();
        }

        private void DrawPendingChangesFooter()
        {
            if (!_pending.HasPendingChanges) return;

            EditorGUILayout.Space(8);
            TsEditorGUI.DrawStatusBox(
                "You have unapplied changes. Apply them, or discard them to revert.",
                MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                _pending.Apply();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Discard"))
            {
                _pending.Discard(serializedObject);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
