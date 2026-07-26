#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Shared drawing helpers used by every Tsvrc editor window (Configure, Translation, Mesh
    // Combiner) so they present a single consistent look: one header style, one status-box
    // convention, one primary-button style with a built-in disabled/tooltip path.
    internal static class TsEditorGUI
    {
        internal static void DrawStatusBox(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
            EditorGUILayout.Space(4);
        }

        // enabled: false shows disabledTooltip (if provided) instead of letting the click through -
        // used so windows can explain *why* the main action is unavailable rather than only
        // reacting after the user tries it anyway.
        internal static bool PrimaryButton(string label, bool enabled = true, string disabledTooltip = null)
        {
            var content = enabled || string.IsNullOrEmpty(disabledTooltip)
                ? new GUIContent(label)
                : new GUIContent(label, disabledTooltip);

            using (new EditorGUI.DisabledScope(!enabled))
                return GUILayout.Button(content);
        }

        // Used by inspectors for objects that are primarily meant to be edited through a Tsvrc
        // window (e.g. TsConfig) - so a user who selects the object directly in the Hierarchy still
        // finds their way to the friendlier tabbed UI instead of only ever seeing a bare inspector.
        internal static void DrawManagedByConfigureBanner(string message)
        {
            DrawStatusBox(message, MessageType.Info);
            if (GUILayout.Button("Open Tsvrc > Configure"))
                EditorApplication.ExecuteMenuItem("Tsvrc/Configure");
            EditorGUILayout.Space(8);
        }
    }
}
#endif
