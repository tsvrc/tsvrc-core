#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Row-rendering helpers shared by TsGroupTreeGUI's grouped-entry rows - every grouped module
    // (Globals, Pool, Constructs, Factories) draws its entries through TsGroupTreeGUI, which
    // reuses DeleteButton/DrawEntryHints/IsSceneInstance/IsDuplicate from here.
    internal static class ObjectListGUI
    {
        internal static bool DeleteButton() => GUILayout.Button("✕", GUILayout.Width(22));

        // True for a scene instance (invalid wherever a prefab asset is required - e.g. Pool),
        // false for a persisted asset or null. Pure so it's directly unit-testable.
        internal static bool IsSceneInstance(Object obj) => obj != null && !EditorUtility.IsPersistent(obj);

        // True if obj is non-null and already in seen (added as a side effect otherwise). Pure
        // aside from the seen-set mutation, so it's testable without a real SerializedProperty/array.
        internal static bool IsDuplicate(Object obj, HashSet<Object> seen) => obj != null && !seen.Add(obj);

        // Unity's PropertyField widget can only display a component's real type while the
        // project compiles - a "Missing (Mono Script)" reference always renders as a blank type,
        // with no way to tell what it points at. Resolves it anyway via
        // TsModule.TryResolveObjectType, the same ScriptIndex-backed fallback Wire()/GenerateCode()
        // already use. No-op when the live type already resolves fine on its own.
        private static void DrawResolvedTypeHint(Object obj)
        {
            if (!(obj is Component component)) return;
            if (component.GetType() != typeof(MonoBehaviour)) return;

            string hint = TsModule.TryResolveObjectType(obj, out string typeName, out _)
                ? $"detected as {typeName} (compile currently broken)"
                : "could not detect a type for this reference (compile currently broken)";
            EditorGUILayout.LabelField("   ↳ " + hint, EditorStyles.miniLabel);
        }

        // A null slot renders as a bare, empty ObjectField with no other signal - this flags it
        // needs attention, since SerializedProperty carries no history to say why it's empty.
        private static void DrawNullSlotHint(Object obj)
        {
            if (obj != null) return;
            EditorGUILayout.LabelField(
                "   ↳ empty - nothing assigned here (if this used to point at something, it may have been deleted)",
                EditorStyles.miniLabel);
        }

        // Per-row hint/warning block: resolved-type hint, empty-slot hint, scene-instance warning
        // (assetsOnly), duplicate-reference warning (warnDuplicates). isDuplicate is precomputed
        // by the caller across the whole filtered set, not just the current page - a caller that
        // only scans part of the set would get a wrong answer from a fresh, partial seen set.
        internal static void DrawEntryHints(Object obj, bool assetsOnly, bool warnDuplicates, bool isDuplicate)
        {
            DrawResolvedTypeHint(obj);
            DrawNullSlotHint(obj);

            if (assetsOnly && IsSceneInstance(obj))
                TsEditorGUI.DrawStatusBox(
                    $"'{obj.name}' is a scene object, not a prefab asset - drag one in from the Project window instead.",
                    MessageType.Warning);

            if (warnDuplicates && isDuplicate)
                TsEditorGUI.DrawStatusBox(
                    $"'{obj.name}' is already listed above - the duplicate will be dropped at regenerate.",
                    MessageType.Warning);
        }
    }
}
#endif
